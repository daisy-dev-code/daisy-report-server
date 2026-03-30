namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiDashboard
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? WebUrl { get; set; }
    public string? EmbedUrl { get; set; }
    public bool IsReadOnly { get; set; }
}
