using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;
using Telemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Cosmos;
using System.Diagnostics;
using Microsoft.Azure.Cosmos.Linq;

using Telemetry.Trace;
class Program
{
    static void Main(String[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Instead of hardcoding the values for various strings, we are going to put them into
        // a configuration file to make it easier to read. This is especially useful when you
        // want to have the container act one way in release vs debug, or locally vs in Azure.
        // https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0
        // TODO: Go into appsettings.json and update for your service configuration endpoints
        IConfiguration configuration = builder.Configuration;

        string serviceName = configuration["Logging:ServiceName"]!;
        string serviceVersion = configuration["Logging:ServiceVersion"]!;

        // Configure important OpenTelemetry settings, the console exporter, and instrumentation library
        builder.Services.AddOpenTelemetry().WithTracing(tcb =>
        {
            tcb
            .AddSource(serviceName)
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .AddAspNetCoreInstrumentation() // Automatically generate log lines for HTTP requests
            .AddJsonConsoleExporter(); // Output log lines to the console
        });

        AppointmentHandler instance = new AppointmentHandler(configuration);

        WebApplication app = builder.Build();
 
        // app.MapGet("/healthcheck", instance.HealthCheckDelegate);
        app.MapGet("/listAppointments", instance.ListAptsDelegate);
        app.MapGet("/cancelAppointment", instance.CancelAptDelegate);
        app.MapGet("/appointmentInfo", instance.DetailAptDelegate);
        
        // ONLY TO BE USED BY OTHER API
        app.MapGet("/api/getAllAppointments", (Delegate)instance.GetAppointmentsDelegate);
               
        app.MapPost("/addAppointment", instance.AddAppointmentDelegate);

        app.Run();
    }
}