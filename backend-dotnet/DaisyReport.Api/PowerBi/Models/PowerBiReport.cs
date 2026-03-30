namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiReport
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? WebUrl { get; set; }
    public string? EmbedUrl { get; set; }
    public string? DatasetId { get; set; }
    public string? WorkspaceId { get; set; }
    public string? ReportType { get; set; }
    public DateTime? CreatedDateTime { get; set; }
    public DateTime? ModifiedDateTime { get; set; }
}
