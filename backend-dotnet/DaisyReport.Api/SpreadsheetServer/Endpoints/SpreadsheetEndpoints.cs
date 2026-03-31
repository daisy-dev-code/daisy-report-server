using System.Reflection;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.SpreadsheetServer.Models;
using DaisyReport.Api.SpreadsheetServer.Services;

namespace DaisyReport.Api.SpreadsheetServer.Endpoints;

public static class SpreadsheetEndpoints
{
    public static void MapSpreadsheetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/spreadsheet").RequireAuthorization();

        // Query execution
        group.MapPost("/query", ExecuteQuery);
        group.MapPost("/aggregate", ExecuteAggregate);
        group.MapPost("/lookup", ExecuteLookup);

        // GL formulas
        group.MapPost("/gl/balance", GetGlBalance);
        group.MapPost("/gl/detail", GetGlDetail);
        group.MapPost("/gl/range", GetGlRange);

        // Drilldown
        group.MapPost("/drilldown", ExecuteDrilldown);

        // Saved queries
        group.MapGet("/queries", ListQueries);
        group.MapGet("/queries/{id:long}", GetQuery);
        group.MapPost("/queries", CreateQuery);
        group.MapPut("/queries/{id:long}", UpdateQuery);
        group.MapDelete("/queries/{id:long}", DeleteQuery);

        // Connections (for Excel add-in)
        group.MapGet("/connections", ListConnections);

        // Version check (for auto-update)
        group.MapGet("/version", GetVersion);
    }

    // ── Query Execution ───────────────────────────────────────────────────────

    private static async Task<IResult> ExecuteQuery(
        QueryRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.ExecuteQueryAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    private static async Task<IResult> ExecuteAggregate(
        AggregateRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.ExecuteAggregateAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    private static async Task<IResult> ExecuteLookup(
        LookupRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.ExecuteLookupAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    // ── GL Formulas ───────────────────────────────────────────────────────────

    private static async Task<IResult> GetGlBalance(
        GlBalanceRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.GetGlBalanceAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    private static async Task<IResult> GetGlDetail(
        GlDetailRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.GetGlDetailAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    private static async Task<IResult> GetGlRange(
        GlRangeRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.GetGlRangeAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    // ── Drilldown ─────────────────────────────────────────────────────────────

    private static async Task<IResult> ExecuteDrilldown(
        DrilldownRequest request,
        HttpContext context,
        ISpreadsheetQueryService service)
    {
        var userId = GetUserId(context);
        var result = await service.ExecuteDrilldownAsync(request, userId);

        if (result.Error != null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result);
    }

    // ── Saved Queries ─────────────────────────────────────────────────────────

    private static async Task<IResult> ListQueries(
        ISavedQueryService service,
        long? datasourceId = null)
    {
        var queries = await service.ListAsync(datasourceId);
        return Results.Ok(new { data = queries });
    }

    private static async Task<IResult> GetQuery(
        long id,
        ISavedQueryService service)
    {
        var query = await service.GetByIdAsync(id);
        if (query == null)
            return Results.NotFound(new { error = "Saved query not found." });

        return Results.Ok(query);
    }

    private static async Task<IResult> CreateQuery(
        SavedQuery request,
        HttpContext context,
        ISavedQueryService service)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });

        if (request.DatasourceId <= 0)
            return Results.BadRequest(new { error = "DatasourceId is required." });

        request.CreatedBy = GetUserId(context);
        var id = await service.CreateAsync(request);

        return Results.Created($"/api/spreadsheet/queries/{id}", new { id, request.Name });
    }

    private static async Task<IResult> UpdateQuery(
        long id,
        SavedQuery request,
        ISavedQueryService service)
    {
        var existing = await service.GetByIdAsync(id);
        if (existing == null)
            return Results.NotFound(new { error = "Saved query not found." });

        request.Id = id;
        var result = await service.UpdateAsync(request);

        return result
            ? Results.Ok(new { message = "Saved query updated successfully." })
            : Results.Problem("Failed to update saved query.");
    }

    private static async Task<IResult> DeleteQuery(
        long id,
        ISavedQueryService service)
    {
        var result = await service.DeleteAsync(id);
        return result
            ? Results.Ok(new { message = "Saved query deleted successfully." })
            : Results.NotFound(new { error = "Saved query not found." });
    }

    // ── Connections ───────────────────────────────────────────────────────────

    private static async Task<IResult> ListConnections(
        IDatasourceRepository repo)
    {
        var datasources = await repo.ListAsync();

        // Return a simplified view for the Excel add-in
        var connections = datasources
            .Where(d => d.Dtype == "database")
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                d.DriverClass,
                Host = ExtractHost(d.JdbcUrl)
            });

        return Results.Ok(new { data = connections });
    }

    // ── Version ───────────────────────────────────────────────────────────────

    private static IResult GetVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0";

        return Results.Ok(new
        {
            version,
            api = "DaisyReport SpreadsheetServer",
            minClientVersion = "1.0.0"
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static long GetUserId(HttpContext context)
    {
        if (context.Items.TryGetValue("UserId", out var uid) && uid is long userId)
            return userId;
        return 0;
    }

    private static string? ExtractHost(string? jdbcUrl)
    {
        if (string.IsNullOrWhiteSpace(jdbcUrl))
            return null;

        try
        {
            // Strip jdbc: prefix variants
            var url = jdbcUrl;
            foreach (var prefix in new[] { "jdbc:mysql://", "jdbc:sqlserver://", "jdbc:postgresql://" })
            {
                if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    url = url[prefix.Length..];
                    break;
                }
            }

            var slashIdx = url.IndexOf('/');
            var hostPort = slashIdx >= 0 ? url[..slashIdx] : url;
            return hostPort.Split(':')[0];
        }
        catch
        {
            return null;
        }
    }
}
