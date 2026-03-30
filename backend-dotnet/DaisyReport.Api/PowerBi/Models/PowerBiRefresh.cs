namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiRefresh
{
    public string? RequestId { get; set; }
    public string? RefreshType { get; set; }
    public string? Status { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ServiceExceptionJson { get; set; }
}
