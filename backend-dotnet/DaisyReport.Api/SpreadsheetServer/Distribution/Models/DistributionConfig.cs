namespace DaisyReport.Api.SpreadsheetServer.Distribution.Models;

public class DistributionConfig
{
    public long TemplateId { get; set; }
    public string OutputFormat { get; set; } = "EXCEL"; // EXCEL, CSV, HTML
    public bool BurstEnabled { get; set; }
    public string? BurstParameterName { get; set; }
    public List<string>? BurstValues { get; set; }
    public long? BurstQueryConnectionId { get; set; }
    public string? BurstQuery { get; set; }
    public string ChannelType { get; set; } = "FILESYSTEM";
    public Dictionary<string, string> ChannelSettings { get; set; } = new();
    public List<string> Recipients { get; set; } = new();
}

public class DistributionResult
{
    public bool Success { get; set; }
    public int ReportsGenerated { get; set; }
    public int ReportsDelivered { get; set; }
    public List<string> Errors { get; set; } = new();
    public long DurationMs { get; set; }
}
