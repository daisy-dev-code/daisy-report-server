using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface ISchedulerRepository
{
    Task<ScheduleJob?> GetByIdAsync(long id);
    Task<(List<ScheduleJob> Jobs, int Total)> ListAsync(int page, int pageSize, string? status);
    Task<long> CreateAsync(ScheduleJob job);
    Task<bool> UpdateAsync(ScheduleJob job);
    Task<bool> DeleteAsync(long id);

    Task<List<ScheduleJob>> GetDueJobsAsync(DateTime now);
    Task<bool> AcquireLockAsync(long jobId, string instanceId);
    Task ReleaseLockAsync(long jobId);
    Task UpdateHeartbeatAsync(long jobId);
    Task UpdateStatusAsync(long jobId, string status, DateTime? nextFireTime);
    Task<List<ScheduleJob>> GetStaleJobsAsync(TimeSpan threshold);

    Task<(List<JobExecution> Executions, int Total)> GetExecutionsAsync(long jobId, int page, int pageSize);
    Task<long> CreateExecutionAsync(JobExecution execution);
    Task UpdateExecutionAsync(JobExecution execution);

    Task<List<JobAction>> GetActionsAsync(long jobId);
}
