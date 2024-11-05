using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text.Json;
using AppointmentManager.Azure;
using AppointmentManager.Utils;
using Microsoft.Extensions.Primitives;
using Microsoft.Azure.Cosmos.Linq;

public class AppointmentHandler
{
    private readonly IConfiguration _configuration;
    private readonly Logger _logger;
    private readonly CosmosDbWrapper _cosmosDbWrapper;

    public AppointmentHandler(IConfiguration configuration)
    {
        _configuration = configuration;
        if (null == _configuration)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        string serviceName = configuration["Logging:ServiceName"]!;
        _logger = new Logger(serviceName);

        _cosmosDbWrapper = new CosmosDbWrapper(configuration);
    }

    // private static string GetParameterFromList(string parameterName, HttpRequest request, MethodLogger log)
    // {
    //     // Obtain the parameter from the caller
    //     if (request.Query.TryGetValue(parameterName, out StringValues items))
    //     {
    //         if (items.Count > 1)
    //         {
    //             throw new UserErrorException($"Multiple {parameterName} found");
    //         }

    //         log.SetAttribute($"request.{parameterName}", items[0]!);
    //     }
    //     else
    //     {
    //         throw new UserErrorException($"No {parameterName} found");
    //     }

    //     return items[0]!;
    // }

    private string GetParameterFromList(string parameterName, HttpRequest request, MethodLogger log)
{
    if (request.HasFormContentType && request.Form.ContainsKey(parameterName))
    {
        log.SetAttribute($"request.{parameterName}", request.Form[parameterName]!);
        return request.Form[parameterName]!;
    }
    else
    {
        throw new UserErrorException($"No {parameterName} found");
    }
}

    public async Task AddAppointmentDelegate(HttpContext context)
    {
        using(var log = _logger.StartMethod(nameof(AddAppointmentDelegate), context))
        {
            try
            {
                HttpRequest request = context.Request;

                AppointmentData m = new AppointmentData();
                m.userid = GetParameterFromList("userid", request, log);
                m.aptname = GetParameterFromList("aptname", request, log);
                m.description = GetParameterFromList("desc", request, log);

                log.SetAttribute("request.userid", m.userid);
                log.SetAttribute("request.aptname", m.aptname);
                log.SetAttribute("request.desc", m.description);
                // Parse human readable date time input (Format: MM/dd/yyyy h:mm tt) tt being AM/PM
                string dtUnparsed = GetParameterFromList("datetime", request, log);
                        if (DateTime.TryParseExact(dtUnparsed, 
                                   "MM/dd/yyyy h:mm tt", 
                                   CultureInfo.InvariantCulture, 
                                   DateTimeStyles.None, 
                                   out DateTime parsedDate))
                {
                    // Successfully parsed the date
                    m.datetime = parsedDate;
                    log.SetAttribute("request.datetime", m.datetime.ToString());
                }
                else
                {
                    throw new UserErrorException($"Invalid datetime format: {dtUnparsed}");
                }

                // First step is we will write the metadata to CosmosDB
                // Here we are using Type mapping to convert our data structure
                // to a JSON document that can be stored in CosmosDB.
                if (await _cosmosDbWrapper.GetItemAsync<AppointmentData>(m.id, m.userid) != null)
                {
                    await _cosmosDbWrapper.UpdateItemAsync(m.id, m.userid, m);
                }
                else
                {
                    await _cosmosDbWrapper.AddItemAsync(m, m.userid);
                }

                // The POST has no response body, so we just return and the system
                // will return a 200 OK to the caller.
            }
            catch (UserErrorException e)
            {
                log.LogUserError(e.Message);
            }
            catch(Exception e)
            {
                log.HandleException(e);
            }
        }
    }

    public async Task ListAptsDelegate(HttpContext context)
    {
        using(var log = _logger.StartMethod(nameof(ListAptsDelegate), context))
        {
            try
            {
                HttpRequest request = context.Request;

                AppointmentData m = new AppointmentData();
                m.userid = GetParameterFromList("userid", request, log);

                string query = $"SELECT * FROM c WHERE c.userid = '{m.userid}'"; 

                var apts = await _cosmosDbWrapper.GetItemsAsync<AppointmentData>(query); // Use with text entry

                // Convert results into list.
                var aptnames = apts.Select(apt => apt.aptname).ToList();

                // Convert list to JSON
                var jsonResponse = JsonSerializer.Serialize(aptnames);
                
                // Set response headers and write the JSON response
                context.Response.ContentLength = jsonResponse.Length;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(jsonResponse);
                
            }
            catch(Exception e)
            {
                log.HandleException(e);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }

    public async Task DetailAptDelegate(HttpContext context)
    {
        using(var log = _logger.StartMethod(nameof(DetailAptDelegate), context))
        {
            try
            {
                HttpRequest request = context.Request;

                AppointmentData m = new AppointmentData();
                m.userid = GetParameterFromList("userid", request, log);
                m.aptname = GetParameterFromList("aptname", request, log);

                string query = $"SELECT * FROM c WHERE c.userid = '{m.userid}' AND c.aptname = '{m.aptname}'"; 

                var apts = await _cosmosDbWrapper.GetItemsAsync<AppointmentData>(query); // Use with text entry

                if (apts.FirstOrDefault() == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    throw new UserErrorException("No appointment found with that name.");
                }

                // Convert results into list.
                string[] aptnames = { $"Appointment Name: {apts.FirstOrDefault()!.aptname}", $"Appointment Description: {apts.FirstOrDefault()!.description}", $"Appointment Date/Time (MM/DD/YYYY hh:mm:ms): {apts.FirstOrDefault()!.datetime.ToString()}" };

                // Convert list to JSON
                var jsonResponse = JsonSerializer.Serialize(aptnames);
                
                // Set response headers and write the JSON response
                context.Response.ContentLength = jsonResponse.Length;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(jsonResponse);
                
            }
            catch (UserErrorException e)
            {
                log.LogUserError(e.Message);
            }
            catch(Exception e)
            {
                log.HandleException(e);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }

    public async Task CancelAptDelegate(HttpContext context)
    {
        using(var log = _logger.StartMethod(nameof(CancelAptDelegate), context))
        {
            try
            {
                HttpRequest request = context.Request;

                AppointmentData m = new AppointmentData();
                m.userid = GetParameterFromList("userid", request, log);
                m.aptname = GetParameterFromList("aptname", request, log);

                // Retrieve the appointment data from CosmosDB
                string query = $"SELECT * FROM c WHERE c.userid = '{m.userid}' AND c.aptname = '{m.aptname}'";
                var fMD = await _cosmosDbWrapper.GetItemsAsync<AppointmentData>(query);
                if (fMD.FirstOrDefault() != null)
                {
                    // Delete data
                    await _cosmosDbWrapper.DeleteItemAsync(fMD.FirstOrDefault()!.id, fMD.FirstOrDefault()!.userid);      
                    // await context.Response.WriteAsync("Appointment Success.");
                }
            }
            catch(Exception e)
            {
                log.HandleException(e);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }

}
