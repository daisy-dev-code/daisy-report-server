namespace DaisyReport.Api.Models;

public class Dashboard
{
    public long Id { get; set; }
    public long? FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Layout { get; set; } = "SINGLE";
    public int Columns { get; set; } = 1;
    public int ReloadInterval { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsConfigProtected { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public List<Dadget> Dadgets { get; set; } = new();
}
