public class AppointmentData
{
    private string GenerateId()
    {
        return $"{this.userid}-{this.aptname}";
    }

    // Note that "id" must be lower case for the Cosmos APIs to work
    // and for consistency, all keys are lower case
    public string id { get { return GenerateId(); } }

    public string userid { get; set; } = string.Empty;

    public string useremail { get; set; } = string.Empty;
    public string aptname { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public DateTime datetime { get; set; } = DateTime.MinValue;

    public override string ToString()
    {
        return $"id: {id}, userid: {userid}, useremail: {useremail}, aptname: {aptname}, desc: {description}, datetime: {datetime}";
    }
}
