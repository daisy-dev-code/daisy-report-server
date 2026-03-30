namespace DaisyReport.Api.Models;

public class ReportFolder
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? ParentId { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
