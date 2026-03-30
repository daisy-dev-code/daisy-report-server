namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiSyncLog
{
    public long Id { get; set; }
    public string SyncType { get; set; } = "";
    public string Status { get; set; } = "";
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsDeleted { get; set; }
    public int ItemsErrored { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
