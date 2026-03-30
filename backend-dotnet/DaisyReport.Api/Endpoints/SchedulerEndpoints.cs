using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.Services;

namespace DaisyReport.Api.Endpoints;

public static class SchedulerEndpoints
{
    public static void MapSchedulerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/scheduler/jobs").RequireAuthorization();

        group.MapGet("/", ListJobs);
        group.MapGet("/{id:long}", GetJob);
        group.MapPost("/", CreateJob);
        group.MapPut("/{id:long}", UpdateJob);
        group.MapDelete("/{id:long}", DeleteJob);
        group.MapPost("/{id:long}/execute", TriggerJob);
        group.MapGet("/{id:long}/executions", GetExecutions);
    }

    private static async Task<IResult> ListJobs(
        ISchedulerRepository repo,
        int page = 1,
        int pageSize = 25,
        string? status = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (jobs, total) = await repo.ListAsync(page, pageSize, status);

        return Results.Ok(new
        {
            data = jobs.Select(j => new
            {
                j.Id,
                j.Name,
                j.Description,
                j.ReportId,
                j.OwnerId,
                j.Status,
                j.ScheduleType,
                j.ScheduleExpression,
                j.Timezone,
                j.NextFireTime,
                j.LastFireTime,
                j.RetryCount,
                j.MaxRetries,
                j.OccurrenceCount,
                j.MaxOccurrences,
                j.CreatedAt,
                j.UpdatedAt
            }),
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            }
        });
    }

    private static async Task<IResult> GetJob(long id, ISchedulerRepository repo)
    {
        var job = await repo.GetByIdAsync(id);
        if (job == null) return Results.NotFound(new { error = "Job not found." });

        return Results.Ok(new
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
            job.LastFireTime,
            job.RetryCount,
            job.MaxRetries,
            job.OccurrenceCount,
            job.MaxOccurrences,
            job.CreatedAt,
            job.UpdatedAt,
            actions = job.Actions.Select(a => new
            {
                a.Id,
                a.ActionType,
                a.DatasinkId,
                a.Config,
                a.SortOrder
            })
        });
    }

    private static async Task<IResult> CreateJob(
        CreateJobRequest request,
        ISchedulerRepository repo,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });

        if (request.ReportId <= 0)
            return Results.BadRequest(new { error = "ReportId is required." });

        var userId = (long?)context.Items["UserId"] ?? 0;
        var now = DateTime.UtcNow;

        var job = new ScheduleJob
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            ReportId = request.ReportId,
            OwnerId = userId,
            Status = "WAITING",
            ScheduleType = request.ScheduleType ?? "ONCE",
            ScheduleExpression = request.ScheduleExpression ?? string.Empty,
            Timezone = request.Timezone ?? "UTC",
            NextFireTime = request.NextFireTime,
            MaxRetries = request.MaxRetries ?? 3,
            MaxOccurrences = request.MaxOccurrences,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Calculate next fire time if not explicitly provided
        if (!job.NextFireTime.HasValue && !string.IsNullOrWhiteSpace(job.ScheduleExpression))
        {
            job.NextFireTime = SchedulerService.CalculateNextFireTime(job);
        }

        var id = await repo.CreateAsync(job);

        return Results.Created($"/api/scheduler/jobs/{id}", new { id, job.Name });
    }

    private static async Task<IResult> UpdateJob(
        long id,
        UpdateJobRequest request,
        ISchedulerRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Job not found." });

        if (existing.Status == "EXECUTING")
            return Results.BadRequest(new { error = "Cannot update a running job." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.ReportId.HasValue) existing.ReportId = request.ReportId.Value;
        if (request.ScheduleType != null) existing.ScheduleType = request.ScheduleType;
        if (request.ScheduleExpression != null) existing.ScheduleExpression = request.ScheduleExpression;
        if (request.Timezone != null) existing.Timezone = request.Timezone;
        if (request.NextFireTime.HasValue) existing.NextFireTime = request.NextFireTime;
        if (request.MaxRetries.HasValue) existing.MaxRetries = request.MaxRetries.Value;
        if (request.MaxOccurrences.HasValue) existing.MaxOccurrences = request.MaxOccurrences;
        if (request.Status != null) existing.Status = request.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update job.");

        return Results.Ok(new { message = "Job updated successfully." });
    }

    private static async Task<IResult> DeleteJob(long id, ISchedulerRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Job not found." });

        if (existing.Status == "EXECUTING")
            return Results.BadRequest(new { error = "Cannot delete a running job." });

        var result = await repo.DeleteAsync(id);
        if (!result) return Results.Problem("Failed to delete job.");

        return Results.Ok(new { message = "Job deleted successfully." });
    }

    private static async Task<IResult> TriggerJob(long id, ISchedulerRepository repo)
    {
        var job = await repo.GetByIdAsync(id);
        if (job == null) return Results.NotFound(new { error = "Job not found." });

        if (job.Status == "EXECUTING")
            return Results.BadRequest(new { error = "Job is already executing." });

        // Set next fire time to now so the scheduler picks it up immediately
        await repo.UpdateStatusAsync(id, "WAITING", DateTime.UtcNow);

        return Results.Accepted(value: new { message = "Job triggered for execution." });
    }

    private static async Task<IResult> GetExecutions(
        long id,
        ISchedulerRepository repo,
        int page = 1,
        int pageSize = 25)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (executions, total) = await repo.GetExecutionsAsync(id, page, pageSize);

        return Results.Ok(new
        {
            data = executions.Select(e => new
            {
                e.Id,
                e.JobId,
                e.Status,
                e.StartedAt,
                e.CompletedAt,
                e.DurationMs,
                e.OutputSize,
                e.ErrorMessage,
                e.RetryAttempt
            }),
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            }
        });
    }
}

public record CreateJobRequest(
    string Name,
    string? Description,
    long ReportId,
    string? ScheduleType,
    string? ScheduleExpression,
    string? Timezone,
    DateTime? NextFireTime,
    int? MaxRetries,
    int? MaxOccurrences);

public record UpdateJobRequest(
    string? Name,
    string? Description,
    long? ReportId,
    string? Status,
    string? ScheduleType,
    string? ScheduleExpression,
    string? Timezone,
    DateTime? NextFireTime,
    int? MaxRetries,
    int? MaxOccurrences);
