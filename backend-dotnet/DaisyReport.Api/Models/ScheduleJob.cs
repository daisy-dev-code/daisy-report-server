namespace DaisyReport.Api.Models;

public class ScheduleJob
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long ReportId { get; set; }
    public long OwnerId { get; set; }
    public string Status { get; set; } = "WAITING";
    public string ScheduleType { get; set; } = "ONCE";
    public string ScheduleExpression { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public DateTime? NextFireTime { get; set; }
    public DateTime? LastFireTime { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int OccurrenceCount { get; set; }
    public int? MaxOccurrences { get; set; }
    public string? LockOwner { get; set; }
    public DateTime? LockAcquiredAt { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<JobAction> Actions { get; set; } = new();
}
