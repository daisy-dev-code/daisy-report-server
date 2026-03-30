namespace DaisyReport.Api.Models;

public class Report
{
    public long Id { get; set; }
    public long? FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? KeyField { get; set; }
    public string EngineType { get; set; } = string.Empty;
    public long? DatasourceId { get; set; }
    public string? QueryText { get; set; }
    public string? Config { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
