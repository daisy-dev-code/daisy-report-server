namespace DaisyReport.Api.Models;

public class JobAction
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public long? DatasinkId { get; set; }
    public string Config { get; set; } = "{}";
    public int SortOrder { get; set; }
}
