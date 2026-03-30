namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiTile
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? EmbedUrl { get; set; }
    public string? ReportId { get; set; }
    public string? DatasetId { get; set; }
    public int RowSpan { get; set; }
    public int ColSpan { get; set; }
}
