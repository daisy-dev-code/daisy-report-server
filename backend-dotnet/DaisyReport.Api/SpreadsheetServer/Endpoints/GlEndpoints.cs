using DaisyReport.Api.SpreadsheetServer.GlEngine;
using DaisyReport.Api.SpreadsheetServer.Models;

namespace DaisyReport.Api.SpreadsheetServer.Endpoints;

public static class GlEndpoints
{
    public static void MapGlEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/spreadsheet/gl").RequireAuthorization();

        // Config
        group.MapGet("/config/{datasourceId:long}", GetConfig);
        group.MapPost("/config/{datasourceId:long}", SaveConfig);

        // Auto-detect
        group.MapPost("/detect/{datasourceId:long}", DetectTables);

        // Chart of accounts
        group.MapGet("/accounts/{datasourceId:long}", GetAccounts);

        // Fiscal periods
        group.MapGet("/periods/{datasourceId:long}/{year:int}", GetPeriods);
    }

    private static async Task<IResult> GetConfig(
        long datasourceId,
        IGlFormulaProcessor processor)
    {
        try
        {
            var config = await processor.GetConfigAsync(datasourceId);
            if (config == null)
                return Results.NotFound(new { error = "No GL configuration found for this datasource." });

            return Results.Ok(new { data = config });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SaveConfig(
        long datasourceId,
        ErpConnectorConfig config,
        IGlFormulaProcessor processor)
    {
        try
        {
            await processor.SaveConfigAsync(datasourceId, config);
            return Results.Ok(new { message = "GL configuration saved successfully." });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DetectTables(
        long datasourceId,
        IGlFormulaProcessor processor)
    {
        try
        {
            var mapping = await processor.DetectTablesAsync(datasourceId);
            if (mapping == null)
                return Results.Ok(new
                {
                    detected = false,
                    message = "No GL tables could be detected in this database.",
                    mapping = (object?)null
                });

            return Results.Ok(new
            {
                detected = true,
                message = $"GL tables detected with {mapping.Confidence:P0} confidence.",
                mapping
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetAccounts(
        long datasourceId,
        IGlFormulaProcessor processor,
        string? filter = null)
    {
        try
        {
            var accounts = await processor.GetChartOfAccountsAsync(datasourceId, filter);
            return Results.Ok(new { data = accounts, total = accounts.Count });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetPeriods(
        long datasourceId,
        int year,
        IGlFormulaProcessor processor)
    {
        try
        {
            var periods = await processor.GetFiscalPeriodsAsync(datasourceId, year);
            return Results.Ok(new { data = periods, total = periods.Count });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
