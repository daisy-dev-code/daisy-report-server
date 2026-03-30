using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization();

        group.MapGet("/", ListReports);
        group.MapGet("/{id:long}", GetReport);
        group.MapPost("/", CreateReport);
        group.MapPut("/{id:long}", UpdateReport);
        group.MapDelete("/{id:long}", DeleteReport);
        group.MapGet("/{id:long}/parameters", GetParameters);
    }

    private static async Task<IResult> ListReports(
        IReportRepository repo,
        int page = 1,
        int pageSize = 25,
        long? folderId = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (reports, total) = await repo.ListAsync(page, pageSize, folderId);

        return Results.Ok(new
        {
            data = reports.Select(r => new
            {
                r.Id,
                r.FolderId,
                r.Name,
                r.Description,
                r.KeyField,
                r.EngineType,
                r.DatasourceId,
                r.CreatedBy,
                r.CreatedAt,
                r.UpdatedAt
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

    private static async Task<IResult> GetReport(long id, IReportRepository repo)
    {
        var report = await repo.GetByIdAsync(id);
        if (report == null) return Results.NotFound(new { error = "Report not found." });

        return Results.Ok(new
        {
            report.Id,
            report.FolderId,
            report.Name,
            report.Description,
            report.KeyField,
            report.EngineType,
            report.DatasourceId,
            report.QueryText,
            report.Config,
            report.CreatedBy,
            report.CreatedAt,
            report.UpdatedAt
        });
    }

    private static async Task<IResult> CreateReport(CreateReportRequest request, IReportRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required." });
        }

        var report = new Report
        {
            FolderId = request.FolderId,
            Name = request.Name,
            Description = request.Description,
            KeyField = request.KeyField,
            EngineType = request.EngineType ?? "sql",
            DatasourceId = request.DatasourceId,
            QueryText = request.QueryText,
            Config = request.Config,
            CreatedBy = request.CreatedBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var id = await repo.CreateAsync(report);

        return Results.Created($"/api/reports/{id}", new { id, report.Name });
    }

    private static async Task<IResult> UpdateReport(long id, UpdateReportRequest request, IReportRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Report not found." });

        if (request.FolderId.HasValue) existing.FolderId = request.FolderId;
        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.KeyField != null) existing.KeyField = request.KeyField;
        if (request.EngineType != null) existing.EngineType = request.EngineType;
        if (request.DatasourceId.HasValue) existing.DatasourceId = request.DatasourceId;
        if (request.QueryText != null) existing.QueryText = request.QueryText;
        if (request.Config != null) existing.Config = request.Config;
        existing.UpdatedAt = DateTime.UtcNow;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update report.");

        return Results.Ok(new { message = "Report updated successfully." });
    }

    private static async Task<IResult> DeleteReport(long id, IReportRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Report not found." });

        return Results.Ok(new { message = "Report deleted successfully." });
    }

    private static async Task<IResult> GetParameters(long id, IReportRepository repo)
    {
        var report = await repo.GetByIdAsync(id);
        if (report == null) return Results.NotFound(new { error = "Report not found." });

        var parameters = await repo.GetParametersAsync(id);

        return Results.Ok(new
        {
            data = parameters.Select(p => new
            {
                p.Id,
                p.ReportId,
                p.Name,
                p.Label,
                p.ParamType,
                p.DefaultValue,
                p.Required,
                p.SortOrder
            })
        });
    }
}

public record CreateReportRequest(
    long? FolderId,
    string Name,
    string? Description,
    string? KeyField,
    string? EngineType,
    long? DatasourceId,
    string? QueryText,
    string? Config,
    long? CreatedBy);

public record UpdateReportRequest(
    long? FolderId,
    string? Name,
    string? Description,
    string? KeyField,
    string? EngineType,
    long? DatasourceId,
    string? QueryText,
    string? Config);
