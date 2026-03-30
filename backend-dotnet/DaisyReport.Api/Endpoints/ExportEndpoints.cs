using DaisyReport.Api.Middleware;
using DaisyReport.Api.ReportEngine;

namespace DaisyReport.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization();

        group.MapGet("/{id:long}/export", ExportReport);
    }

    private static async Task<IResult> ExportReport(
        long id,
        IReportExecutionPipeline pipeline,
        HttpContext httpContext,
        string format = "csv",
        int? pageSize = null,
        int? page = null)
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
            OutputFormat = format.ToUpperInvariant(),
            PageSize = pageSize,
            Page = page
        };

        var result = await pipeline.ExecuteAsync(request);

        if (!result.Success)
        {
            return Results.Problem(result.ErrorMessage ?? "Report execution failed.", statusCode: 500);
        }

        if (result.Data == null || result.Data.Length == 0)
        {
            return Results.NoContent();
        }

        // Determine file extension
        var extension = format.ToUpperInvariant() switch
        {
            "CSV" => "csv",
            "EXCEL" => "xlsx",
            "PDF" => "pdf",
            "HTML" => "html",
            "JSON" => "json",
            _ => "dat"
        };

        var fileName = $"report_{id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{extension}";

        return Results.File(result.Data, result.ContentType, fileName);
    }
}
