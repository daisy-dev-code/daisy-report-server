using System.Collections.Concurrent;
using Cronos;
using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Services;

public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerService> _logger;
    private readonly string _instanceId;
    private readonly ConcurrentDictionary<long, DateTime> _runningJobs = new();

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(90);

    public SchedulerService(IServiceScopeFactory scopeFactory, ILogger<SchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _instanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler engine started. Instance: {InstanceId}", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueJobs(stoppingToken);
                await UpdateHeartbeats(stoppingToken);
                await DetectStaleJobs(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler main loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("Scheduler engine stopped");
    }

    private async Task ProcessDueJobs(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchedulerRepository>();

        var dueJobs = await repo.GetDueJobsAsync(DateTime.UtcNow);

        foreach (var job in dueJobs)
        {
            if (stoppingToken.IsCancellationRequested) break;

            var acquired = await repo.AcquireLockAsync(job.Id, _instanceId);
            if (!acquired) continue;

            _runningJobs.TryAdd(job.Id, DateTime.UtcNow);

            // Execute in thread pool — fire and forget with error handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteJob(job);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error executing job {JobId}: {JobName}", job.Id, job.Name);
                }
                finally
                {
                    _runningJobs.TryRemove(job.Id, out _);
                }
            }, stoppingToken);
        }
    }

    private async Task ExecuteJob(ScheduleJob job)
    {
        _logger.LogInformation("Executing job {JobId}: {JobName}", job.Id, job.Name);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchedulerRepository>();

        var execution = new JobExecution
        {
            JobId = job.Id,
            Status = "RUNNING",
            StartedAt = DateTime.UtcNow,
            RetryAttempt = job.RetryCount
        };

        var executionId = await repo.CreateExecutionAsync(execution);
        execution.Id = executionId;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Load job actions
            var actions = await repo.GetActionsAsync(job.Id);

            // Execute each action in order
            foreach (var action in actions.OrderBy(a => a.SortOrder))
            {
                await ExecuteAction(job, action);
            }

            sw.Stop();

            // Mark execution as completed
            execution.Status = "COMPLETED";
            execution.CompletedAt = DateTime.UtcNow;
            execution.DurationMs = sw.ElapsedMilliseconds;
            await repo.UpdateExecutionAsync(execution);

            // Calculate next fire time
            var nextFire = CalculateNextFireTime(job);

            // Check max occurrences
            var newOccurrenceCount = job.OccurrenceCount + 1;
            var finalStatus = "WAITING";

            if (job.MaxOccurrences.HasValue && newOccurrenceCount >= job.MaxOccurrences.Value)
            {
                finalStatus = "COMPLETED";
                nextFire = null;
            }
            else if (nextFire == null)
            {
                // ONCE jobs or jobs with no next fire time
                finalStatus = "COMPLETED";
            }

            await repo.UpdateStatusAsync(job.Id, finalStatus, nextFire);
            await repo.ReleaseLockAsync(job.Id);

            // Update occurrence count
            using var conn = await scope.ServiceProvider.GetRequiredService<Infrastructure.IDatabase>().GetConnectionAsync();
            await Dapper.SqlMapper.ExecuteAsync(conn,
                "UPDATE RS_SCHEDULE_JOBS SET occurrence_count = @Count, retry_count = 0 WHERE id = @Id",
                new { Count = newOccurrenceCount, Id = job.Id });

            _logger.LogInformation("Job {JobId} completed in {Duration}ms", job.Id, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();

            execution.Status = "FAILED";
            execution.CompletedAt = DateTime.UtcNow;
            execution.DurationMs = sw.ElapsedMilliseconds;
            execution.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await repo.UpdateExecutionAsync(execution);

            // Handle retry logic
            var newRetryCount = job.RetryCount + 1;
            if (newRetryCount < job.MaxRetries)
            {
                // Exponential backoff: 30s, 60s, 120s...
                var delaySeconds = 30 * Math.Pow(2, newRetryCount - 1);
                var retryTime = DateTime.UtcNow.AddSeconds(delaySeconds);

                await repo.UpdateStatusAsync(job.Id, "FAILED", retryTime);

                using var conn = await scope.ServiceProvider.GetRequiredService<Infrastructure.IDatabase>().GetConnectionAsync();
                await Dapper.SqlMapper.ExecuteAsync(conn,
                    "UPDATE RS_SCHEDULE_JOBS SET retry_count = @Count WHERE id = @Id",
                    new { Count = newRetryCount, Id = job.Id });

                _logger.LogWarning("Job {JobId} failed (attempt {Attempt}/{Max}), retry at {RetryTime}",
                    job.Id, newRetryCount, job.MaxRetries, retryTime);
            }
            else
            {
                await repo.UpdateStatusAsync(job.Id, "FAILED", null);
                _logger.LogError(ex, "Job {JobId} failed permanently after {MaxRetries} retries",
                    job.Id, job.MaxRetries);
            }

            await repo.ReleaseLockAsync(job.Id);
        }
    }

    private Task ExecuteAction(ScheduleJob job, JobAction action)
    {
        _logger.LogInformation("Executing action {ActionId} ({ActionType}) for job {JobId}",
            action.Id, action.ActionType, job.Id);

        // Action execution is a placeholder — each action type would connect
        // to the relevant subsystem (email service, teamspace, datasink, etc.)
        return action.ActionType switch
        {
            "EMAIL" => ExecuteEmailAction(job, action),
            "TEAMSPACE" => ExecuteTeamspaceAction(job, action),
            "DATASINK" => ExecuteDatasinkAction(job, action),
            "TABLE" => ExecuteTableAction(job, action),
            _ => Task.CompletedTask
        };
    }

    private Task ExecuteEmailAction(ScheduleJob job, JobAction action)
    {
        _logger.LogInformation("Email action for job {JobId}, config: {Config}", job.Id, action.Config);
        // TODO: Integrate with email service
        return Task.CompletedTask;
    }

    private Task ExecuteTeamspaceAction(ScheduleJob job, JobAction action)
    {
        _logger.LogInformation("Teamspace action for job {JobId}, config: {Config}", job.Id, action.Config);
        // TODO: Integrate with teamspace service
        return Task.CompletedTask;
    }

    private Task ExecuteDatasinkAction(ScheduleJob job, JobAction action)
    {
        _logger.LogInformation("Datasink action for job {JobId}, datasink: {DatasinkId}", job.Id, action.DatasinkId);
        // TODO: Integrate with datasink service
        return Task.CompletedTask;
    }

    private Task ExecuteTableAction(ScheduleJob job, JobAction action)
    {
        _logger.LogInformation("Table action for job {JobId}, config: {Config}", job.Id, action.Config);
        // TODO: Integrate with table output service
        return Task.CompletedTask;
    }

    private async Task UpdateHeartbeats(CancellationToken stoppingToken)
    {
        if (_runningJobs.IsEmpty) return;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchedulerRepository>();

        foreach (var jobId in _runningJobs.Keys)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await repo.UpdateHeartbeatAsync(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update heartbeat for job {JobId}", jobId);
            }
        }
    }

    private async Task DetectStaleJobs(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchedulerRepository>();

        var staleJobs = await repo.GetStaleJobsAsync(StaleThreshold);

        foreach (var job in staleJobs)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogWarning("Detected stale job {JobId}: {JobName} (lock: {LockOwner}, heartbeat: {Heartbeat})",
                job.Id, job.Name, job.LockOwner, job.HeartbeatAt);

            // Reset the job so it can be picked up again
            await repo.ReleaseLockAsync(job.Id);
            await repo.UpdateStatusAsync(job.Id, "WAITING", job.NextFireTime ?? DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Calculates the next fire time based on the schedule type and expression.
    /// Made static and public so endpoints can use it during job creation.
    /// </summary>
    public static DateTime? CalculateNextFireTime(ScheduleJob job)
    {
        var now = DateTime.UtcNow;

        return job.ScheduleType switch
        {
            "ONCE" => null, // One-time jobs don't recur
            "DAILY" => now.Date.AddDays(1).Add(GetTimeFromExpression(job.ScheduleExpression)),
            "WEEKLY" => CalculateWeeklyNext(now, job.ScheduleExpression),
            "MONTHLY" => CalculateMonthlyNext(now, job.ScheduleExpression),
            "INTERVAL" => CalculateIntervalNext(now, job.ScheduleExpression),
            "CRON" => CalculateCronNext(job.ScheduleExpression),
            _ => null
        };
    }

    private static TimeSpan GetTimeFromExpression(string expression)
    {
        // Expression format: "HH:mm" or "HH:mm:ss"
        if (TimeSpan.TryParse(expression, out var time))
            return time;
        return TimeSpan.Zero;
    }

    private static DateTime? CalculateWeeklyNext(DateTime now, string expression)
    {
        // Expression format: "DayOfWeek HH:mm" e.g., "Monday 09:00"
        var parts = expression.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return now.AddDays(7);

        if (!Enum.TryParse<DayOfWeek>(parts[0], true, out var targetDay))
            return now.AddDays(7);

        var time = TimeSpan.TryParse(parts[1], out var t) ? t : TimeSpan.Zero;
        var daysUntil = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && now.TimeOfDay >= time)
            daysUntil = 7;

        return now.Date.AddDays(daysUntil).Add(time);
    }

    private static DateTime? CalculateMonthlyNext(DateTime now, string expression)
    {
        // Expression format: "DD HH:mm" e.g., "15 09:00"
        var parts = expression.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return now.AddMonths(1);

        if (!int.TryParse(parts[0], out var day)) return now.AddMonths(1);
        var time = parts.Length >= 2 && TimeSpan.TryParse(parts[1], out var t) ? t : TimeSpan.Zero;

        var candidate = new DateTime(now.Year, now.Month,
            Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month)),
            0, 0, 0, DateTimeKind.Utc).Add(time);

        if (candidate <= now)
        {
            var next = now.AddMonths(1);
            candidate = new DateTime(next.Year, next.Month,
                Math.Min(day, DateTime.DaysInMonth(next.Year, next.Month)),
                0, 0, 0, DateTimeKind.Utc).Add(time);
        }

        return candidate;
    }

    private static DateTime? CalculateIntervalNext(DateTime now, string expression)
    {
        // Expression format: interval in seconds, e.g., "3600" for hourly
        if (int.TryParse(expression, out var seconds) && seconds > 0)
            return now.AddSeconds(seconds);
        return null;
    }

    private static DateTime? CalculateCronNext(string cronExpression)
    {
        try
        {
            var cron = CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);
            return cron.GetNextOccurrence(DateTime.UtcNow, inclusive: false);
        }
        catch
        {
            try
            {
                // Try standard 5-field format
                var cron = CronExpression.Parse(cronExpression);
                return cron.GetNextOccurrence(DateTime.UtcNow, inclusive: false);
            }
            catch
            {
                return null;
            }
        }
    }
}
