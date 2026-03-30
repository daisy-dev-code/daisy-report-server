using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class SchedulerRepository : ISchedulerRepository
{
    private readonly IDatabase _database;

    public SchedulerRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<ScheduleJob?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var job = await conn.QuerySingleOrDefaultAsync<ScheduleJob>(
            @"SELECT id AS Id, name AS Name, description AS Description, report_id AS ReportId,
                     owner_id AS OwnerId, status AS Status, schedule_type AS ScheduleType,
                     schedule_expression AS ScheduleExpression, timezone AS Timezone,
                     next_fire_time AS NextFireTime, last_fire_time AS LastFireTime,
                     retry_count AS RetryCount, max_retries AS MaxRetries,
                     occurrence_count AS OccurrenceCount, max_occurrences AS MaxOccurrences,
                     lock_owner AS LockOwner, lock_acquired_at AS LockAcquiredAt,
                     heartbeat_at AS HeartbeatAt, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_SCHEDULE_JOB WHERE id = @Id",
            new { Id = id });

        if (job != null)
        {
            job.Actions = await GetActionsAsync(id);
        }

        return job;
    }

    public async Task<(List<ScheduleJob> Jobs, int Total)> ListAsync(int page, int pageSize, string? status)
    {
        using var conn = await _database.GetConnectionAsync();

        var whereClause = !string.IsNullOrWhiteSpace(status) ? "WHERE status = @Status" : "";
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_SCHEDULE_JOB {whereClause}",
            new { Status = status });

        var jobs = (await conn.QueryAsync<ScheduleJob>(
            $@"SELECT id AS Id, name AS Name, description AS Description, report_id AS ReportId,
                      owner_id AS OwnerId, status AS Status, schedule_type AS ScheduleType,
                      schedule_expression AS ScheduleExpression, timezone AS Timezone,
                      next_fire_time AS NextFireTime, last_fire_time AS LastFireTime,
                      retry_count AS RetryCount, max_retries AS MaxRetries,
                      occurrence_count AS OccurrenceCount, max_occurrences AS MaxOccurrences,
                      lock_owner AS LockOwner, lock_acquired_at AS LockAcquiredAt,
                      heartbeat_at AS HeartbeatAt, created_at AS CreatedAt, updated_at AS UpdatedAt
               FROM RS_SCHEDULE_JOB {whereClause}
               ORDER BY next_fire_time ASC
               LIMIT @PageSize OFFSET @Offset",
            new { Status = status, PageSize = pageSize, Offset = offset })).ToList();

        return (jobs, total);
    }

    public async Task<long> CreateAsync(ScheduleJob job)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_SCHEDULE_JOB (name, description, report_id, owner_id, status,
                     schedule_type, schedule_expression, timezone, next_fire_time, last_fire_time,
                     retry_count, max_retries, occurrence_count, max_occurrences,
                     created_at, updated_at)
              VALUES (@Name, @Description, @ReportId, @OwnerId, @Status,
                     @ScheduleType, @ScheduleExpression, @Timezone, @NextFireTime, @LastFireTime,
                     @RetryCount, @MaxRetries, @OccurrenceCount, @MaxOccurrences,
                     @CreatedAt, @UpdatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                job.Name,
                job.Description,
                job.ReportId,
                job.OwnerId,
                job.Status,
                job.ScheduleType,
                job.ScheduleExpression,
                job.Timezone,
                job.NextFireTime,
                job.LastFireTime,
                job.RetryCount,
                job.MaxRetries,
                job.OccurrenceCount,
                job.MaxOccurrences,
                job.CreatedAt,
                job.UpdatedAt
            });
        return id;
    }

    public async Task<bool> UpdateAsync(ScheduleJob job)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_SCHEDULE_JOB SET
                name = @Name, description = @Description, report_id = @ReportId,
                owner_id = @OwnerId, status = @Status, schedule_type = @ScheduleType,
                schedule_expression = @ScheduleExpression, timezone = @Timezone,
                next_fire_time = @NextFireTime, max_retries = @MaxRetries,
                max_occurrences = @MaxOccurrences, updated_at = @UpdatedAt
              WHERE id = @Id",
            new
            {
                job.Id,
                job.Name,
                job.Description,
                job.ReportId,
                job.OwnerId,
                job.Status,
                job.ScheduleType,
                job.ScheduleExpression,
                job.Timezone,
                job.NextFireTime,
                job.MaxRetries,
                job.MaxOccurrences,
                job.UpdatedAt
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM RS_JOB_ACTION WHERE job_id = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM RS_JOB_EXECUTION WHERE job_id = @Id", new { Id = id });
        var rows = await conn.ExecuteAsync("DELETE FROM RS_SCHEDULE_JOB WHERE id = @Id", new { Id = id });
        return rows > 0;
    }

    public async Task<List<ScheduleJob>> GetDueJobsAsync(DateTime now)
    {
        using var conn = await _database.GetConnectionAsync();
        var jobs = (await conn.QueryAsync<ScheduleJob>(
            @"SELECT id AS Id, name AS Name, description AS Description, report_id AS ReportId,
                     owner_id AS OwnerId, status AS Status, schedule_type AS ScheduleType,
                     schedule_expression AS ScheduleExpression, timezone AS Timezone,
                     next_fire_time AS NextFireTime, last_fire_time AS LastFireTime,
                     retry_count AS RetryCount, max_retries AS MaxRetries,
                     occurrence_count AS OccurrenceCount, max_occurrences AS MaxOccurrences,
                     lock_owner AS LockOwner, lock_acquired_at AS LockAcquiredAt,
                     heartbeat_at AS HeartbeatAt, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_SCHEDULE_JOB
              WHERE status IN ('WAITING', 'FAILED')
                AND next_fire_time <= @Now
                AND lock_owner IS NULL",
            new { Now = now })).ToList();

        return jobs;
    }

    public async Task<bool> AcquireLockAsync(long jobId, string instanceId)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_SCHEDULE_JOB SET
                lock_owner = @InstanceId, lock_acquired_at = @Now, heartbeat_at = @Now,
                status = 'EXECUTING'
              WHERE id = @JobId AND lock_owner IS NULL",
            new { JobId = jobId, InstanceId = instanceId, Now = DateTime.UtcNow });
        return rows > 0;
    }

    public async Task ReleaseLockAsync(long jobId)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"UPDATE RS_SCHEDULE_JOB SET
                lock_owner = NULL, lock_acquired_at = NULL, heartbeat_at = NULL
              WHERE id = @JobId",
            new { JobId = jobId });
    }

    public async Task UpdateHeartbeatAsync(long jobId)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE RS_SCHEDULE_JOB SET heartbeat_at = @Now WHERE id = @JobId",
            new { JobId = jobId, Now = DateTime.UtcNow });
    }

    public async Task UpdateStatusAsync(long jobId, string status, DateTime? nextFireTime)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"UPDATE RS_SCHEDULE_JOB SET
                status = @Status, next_fire_time = @NextFireTime,
                last_fire_time = @Now, updated_at = @Now
              WHERE id = @JobId",
            new { JobId = jobId, Status = status, NextFireTime = nextFireTime, Now = DateTime.UtcNow });
    }

    public async Task<List<ScheduleJob>> GetStaleJobsAsync(TimeSpan threshold)
    {
        using var conn = await _database.GetConnectionAsync();
        var cutoff = DateTime.UtcNow - threshold;
        var jobs = (await conn.QueryAsync<ScheduleJob>(
            @"SELECT id AS Id, name AS Name, description AS Description, report_id AS ReportId,
                     owner_id AS OwnerId, status AS Status, schedule_type AS ScheduleType,
                     schedule_expression AS ScheduleExpression, timezone AS Timezone,
                     next_fire_time AS NextFireTime, last_fire_time AS LastFireTime,
                     retry_count AS RetryCount, max_retries AS MaxRetries,
                     occurrence_count AS OccurrenceCount, max_occurrences AS MaxOccurrences,
                     lock_owner AS LockOwner, lock_acquired_at AS LockAcquiredAt,
                     heartbeat_at AS HeartbeatAt, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_SCHEDULE_JOB
              WHERE status = 'EXECUTING'
                AND heartbeat_at < @Cutoff",
            new { Cutoff = cutoff })).ToList();

        return jobs;
    }

    public async Task<(List<JobExecution> Executions, int Total)> GetExecutionsAsync(long jobId, int page, int pageSize)
    {
        using var conn = await _database.GetConnectionAsync();
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM RS_JOB_EXECUTION WHERE job_id = @JobId",
            new { JobId = jobId });

        var executions = (await conn.QueryAsync<JobExecution>(
            @"SELECT id AS Id, job_id AS JobId, status AS Status,
                     started_at AS StartedAt, completed_at AS CompletedAt,
                     duration_ms AS DurationMs, output_size AS OutputSize,
                     error_message AS ErrorMessage, retry_attempt AS RetryAttempt
              FROM RS_JOB_EXECUTION
              WHERE job_id = @JobId
              ORDER BY started_at DESC
              LIMIT @PageSize OFFSET @Offset",
            new { JobId = jobId, PageSize = pageSize, Offset = offset })).ToList();

        return (executions, total);
    }

    public async Task<long> CreateExecutionAsync(JobExecution execution)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_JOB_EXECUTION (job_id, status, started_at, completed_at,
                     duration_ms, output_size, error_message, retry_attempt)
              VALUES (@JobId, @Status, @StartedAt, @CompletedAt,
                     @DurationMs, @OutputSize, @ErrorMessage, @RetryAttempt);
              SELECT LAST_INSERT_ID();",
            new
            {
                execution.JobId,
                execution.Status,
                execution.StartedAt,
                execution.CompletedAt,
                execution.DurationMs,
                execution.OutputSize,
                execution.ErrorMessage,
                execution.RetryAttempt
            });
        return id;
    }

    public async Task UpdateExecutionAsync(JobExecution execution)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"UPDATE RS_JOB_EXECUTION SET
                status = @Status, completed_at = @CompletedAt,
                duration_ms = @DurationMs, output_size = @OutputSize,
                error_message = @ErrorMessage
              WHERE id = @Id",
            new
            {
                execution.Id,
                execution.Status,
                execution.CompletedAt,
                execution.DurationMs,
                execution.OutputSize,
                execution.ErrorMessage
            });
    }

    public async Task<List<JobAction>> GetActionsAsync(long jobId)
    {
        using var conn = await _database.GetConnectionAsync();
        var actions = (await conn.QueryAsync<JobAction>(
            @"SELECT id AS Id, job_id AS JobId, action_type AS ActionType,
                     datasink_id AS DatasinkId, config AS Config, sort_order AS SortOrder
              FROM RS_JOB_ACTION
              WHERE job_id = @JobId
              ORDER BY sort_order",
            new { JobId = jobId })).ToList();

        return actions;
    }
}
