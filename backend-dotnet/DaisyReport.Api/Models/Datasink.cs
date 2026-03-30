namespace DaisyReport.Api.Models;

public class Datasink
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Dtype { get; set; } = string.Empty;
    public long? FolderId { get; set; }
    public DateTime CreatedAt { get; set; }
}
