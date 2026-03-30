namespace DaisyReport.Api.Models;

public class OrgUnit
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? ParentId { get; set; }
    public DateTime CreatedAt { get; set; }
}
