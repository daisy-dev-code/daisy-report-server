namespace DaisyReport.Api.PowerBi.Models;

public class SyncResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}
