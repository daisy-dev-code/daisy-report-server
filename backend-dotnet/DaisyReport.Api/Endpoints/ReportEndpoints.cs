using DaisyReport.Api.DynamicList;
using DaisyReport.Api.Middleware;
using DaisyReport.Api.Models;
using DaisyReport.Api.ReportEngine;
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
        group.MapPost("/{id:long}/execute", ExecuteReport);
        group.MapGet("/{id:long}/preview", PreviewReport);

        // Dynamic List advanced endpoints
        group.MapPost("/{id:long}/dynamic-list/execute", ExecuteDynamicList);
        group.MapGet("/{id:long}/dynamic-list/preview", PreviewDynamicList);
        group.MapPost("/{id:long}/dynamic-list/export", ExportDynamicList);
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
                p.KeyField,
                p.Type,
                p.DefaultValue,
                p.Mandatory,
                p.Position
            })
        });
    }

    private static async Task<IResult> ExecuteReport(
        long id,
        ExecuteReportRequest body,
        IReportExecutionPipeline pipeline,
        HttpContext httpContext)
    {
        var userId = httpContext.GetUserId();
        if (!userId.HasValue)
            return Results.Unauthorized();

        var request = new ReportExecutionRequest
        {
            ReportId = id,
            UserId = userId.Value,
            Parameters = body.Parameters ?? new Dictionary<string, string>(),
            OutputFormat = body.OutputFormat ?? "JSON",
            PageSize = body.PageSize,
            Page = body.Page
        };

        var result = await pipeline.ExecuteAsync(request);

        if (!result.Success)
        {
            return Results.Problem(result.ErrorMessage ?? "Report execution failed.", statusCode: 500);
        }

        // For JSON format, return the structured data directly
        if ((body.OutputFormat ?? "JSON").Equals("JSON", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                success = true,
                columns = result.Columns,
                rows = result.Rows,
                totalRows = result.TotalRows,
                executionTimeMs = result.ExecutionTimeMs,
                generatedSql = result.GeneratedSql
            });
        }

        // For other formats, return the raw content
        if (result.Data != null)
        {
            return Results.Bytes(result.Data, result.ContentType);
        }

        return Results.Ok(new { success = true, data = result.DataAsString });
    }

    private static async Task<IResult> PreviewReport(
        long id,
        IReportExecutionPipeline pipeline,
        HttpContext httpContext)
    {
        var userId = httpContext.GetUserId();
        if (!userId.HasValue)
            return Results.Unauthorized();

        // Collect query string parameters that start with "p_" as report parameters
        var parameters = new Dictionary<string, string>();
        foreach (var kvp in httpContext.Request.Query)
        {
            if (kvp.Key.StartsWith("p_", StringComparison.OrdinalIgnoreCase) && kvp.Value.Count > 0)
            {
                parameters[kvp.Key[2..]] = kvp.Value.ToString();
            }
        }

        var request = new ReportExecutionRequest
        {
            ReportId = id,
            UserId = userId.Value,
            Parameters = parameters,
            OutputFormat = "JSON",
            PageSize = 50,
            Page = 1
        };

        var result = await pipeline.ExecuteAsync(request);

        if (!result.Success)
        {
            return Results.Problem(result.ErrorMessage ?? "Report execution failed.", statusCode: 500);
        }

        return Results.Ok(new
        {
            success = true,
            columns = result.Columns,
            rows = result.Rows,
            totalRows = result.TotalRows,
            executionTimeMs = result.ExecutionTimeMs
        });
    }
    // ── Dynamic List Advanced Endpoints ────────────────────────────────────

    private static async Task<IResult> ExecuteDynamicList(
        long id,
        DynamicListRequest body,
        IDynamicListEngine engine)
    {
        try
        {
            var result = await engine.ExecuteAsync(id, body);

            return Results.Ok(new
            {
                success = true,
                columns = result.Columns,
                rows = result.Rows,
                totalRows = result.TotalRows,
                page = result.Page,
                pageSize = result.PageSize,
                generatedSql = result.GeneratedSql
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> PreviewDynamicList(
        long id,
        IDynamicListEngine engine,
        int maxRows = 50)
    {
        try
        {
            var result = await engine.PreviewAsync(id, maxRows);

            return Results.Ok(new
            {
                success = true,
                columns = result.Columns,
                rows = result.Rows,
                totalRows = result.TotalRows
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ExportDynamicList(
        long id,
        DynamicListRequest body,
        IDynamicListEngine engine)
    {
        try
        {
            var format = body.Format ?? "CSV";
            var data = await engine.ExportAsync(id, body, format);

            var (contentType, extension) = format.ToUpperInvariant() switch
            {
                "CSV" => ("text/csv", "csv"),
                "JSON" => ("application/json", "json"),
                "HTML" => ("text/html", "html"),
                _ => ("application/octet-stream", "bin")
            };

            return Results.File(data, contentType, $"report_{id}.{extension}");
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
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

public record ExecuteReportRequest(
    Dictionary<string, string>? Parameters,
    string? OutputFormat,
    int? PageSize,
    int? Page);
