namespace DaisyReport.Api.Models;

public class JobExecution
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string Status { get; set; } = "RUNNING";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public long? OutputSize { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryAttempt { get; set; }
}
