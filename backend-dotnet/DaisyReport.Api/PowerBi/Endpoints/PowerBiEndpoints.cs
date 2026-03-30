using DaisyReport.Api.PowerBi.Models;
using DaisyReport.Api.PowerBi.Services;

namespace DaisyReport.Api.PowerBi.Endpoints;

public static class PowerBiEndpoints
{
    public static void MapPowerBiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/powerbi").RequireAuthorization();

        // Configuration
        group.MapGet("/config", GetConfig);
        group.MapPost("/config", SaveConfig);
        group.MapPost("/config/test", TestConnection);

        // Workspaces
        group.MapGet("/workspaces", ListWorkspaces);
        group.MapGet("/workspaces/{id}", GetWorkspace);

        // Reports
        group.MapGet("/reports", ListReports);
        group.MapGet("/workspaces/{workspaceId}/reports", ListWorkspaceReports);
        group.MapGet("/workspaces/{workspaceId}/reports/{reportId}", GetReport);
        group.MapGet("/workspaces/{workspaceId}/reports/{reportId}/pages", GetReportPages);
        group.MapPost("/workspaces/{workspaceId}/reports/{reportId}/embed", GetEmbedToken);
        group.MapPost("/workspaces/{workspaceId}/reports/{reportId}/export", ExportReport);
        group.MapPost("/workspaces/{workspaceId}/reports/{reportId}/import", ImportReport);

        // Datasets
        group.MapGet("/datasets", ListDatasets);
        group.MapGet("/workspaces/{workspaceId}/datasets", ListWorkspaceDatasets);
        group.MapGet("/workspaces/{workspaceId}/datasets/{datasetId}/datasources", GetDatasetDatasources);
        group.MapGet("/workspaces/{workspaceId}/datasets/{datasetId}/refreshes", GetRefreshHistory);
        group.MapPost("/workspaces/{workspaceId}/datasets/{datasetId}/refresh", TriggerRefresh);
        group.MapPost("/workspaces/{workspaceId}/datasets/{datasetId}/query", ExecuteDaxQuery);

        // Dashboards
        group.MapGet("/dashboards", ListDashboards);
        group.MapGet("/workspaces/{workspaceId}/dashboards/{dashboardId}/tiles", GetDashboardTiles);

        // Gateways
        group.MapGet("/gateways", ListGateways);

        // Sync
        group.MapPost("/sync", TriggerSync);
        group.MapPost("/sync/workspaces", SyncWorkspaces);
        group.MapPost("/sync/reports", SyncReports);
        group.MapGet("/sync/status", GetSyncStatus);
        group.MapGet("/sync/history", GetSyncHistory);
    }

    // ── Configuration ─────────────────────────────────────────────

    private static async Task<IResult> GetConfig(IPowerBiRepository repo)
    {
        var config = await repo.GetConfigAsync();
        if (config == null)
            return Results.Ok(new { configured = false });

        return Results.Ok(new
        {
            configured = true,
            config.Id,
            config.TenantId,
            config.ClientId,
            clientSecret = "********", // never expose the secret
            config.Enabled,
            config.LastSyncAt,
            config.CreatedAt,
            config.UpdatedAt
        });
    }

    private static async Task<IResult> SaveConfig(
        SavePowerBiConfigRequest request,
        IPowerBiRepository repo,
        IPowerBiAuthService authService)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
            return Results.BadRequest(new { error = "TenantId is required." });
        if (string.IsNullOrWhiteSpace(request.ClientId))
            return Results.BadRequest(new { error = "ClientId is required." });
        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return Results.BadRequest(new { error = "ClientSecret is required." });

        var existing = await repo.GetConfigAsync();
        var config = existing ?? new PowerBiConfig();

        config.TenantId = request.TenantId;
        config.ClientId = request.ClientId;
        config.ClientSecretEncrypted = request.ClientSecret; // In production, encrypt this
        config.Enabled = request.Enabled ?? true;

        await repo.SaveConfigAsync(config);

        return Results.Ok(new { message = "Power BI configuration saved successfully." });
    }

    private static async Task<IResult> TestConnection(
        TestPowerBiConnectionRequest request,
        IPowerBiAuthService authService)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return Results.BadRequest(new { error = "TenantId, ClientId, and ClientSecret are all required." });
        }

        var success = await authService.TestConnectionAsync(request.TenantId, request.ClientId, request.ClientSecret);

        return success
            ? Results.Ok(new { success = true, message = "Connection to Power BI successful." })
            : Results.Ok(new { success = false, message = "Connection to Power BI failed. Check credentials." });
    }

    // ── Workspaces ────────────────────────────────────────────────

    private static async Task<IResult> ListWorkspaces(IPowerBiRepository repo)
    {
        var workspaces = await repo.GetWorkspacesAsync();
        return Results.Ok(new { data = workspaces });
    }

    private static async Task<IResult> GetWorkspace(string id, IPowerBiRepository repo)
    {
        var workspace = await repo.GetWorkspaceAsync(id);
        if (workspace == null)
            return Results.NotFound(new { error = "Workspace not found." });

        return Results.Ok(workspace);
    }

    // ── Reports ───────────────────────────────────────────────────

    private static async Task<IResult> ListReports(IPowerBiRepository repo)
    {
        var reports = await repo.GetReportsAsync();
        return Results.Ok(new { data = reports });
    }

    private static async Task<IResult> ListWorkspaceReports(string workspaceId, IPowerBiRepository repo)
    {
        var reports = await repo.GetReportsAsync(workspaceId);
        return Results.Ok(new { data = reports });
    }

    private static async Task<IResult> GetReport(
        string workspaceId, string reportId, IPowerBiRepository repo)
    {
        var report = await repo.GetReportAsync(reportId);
        if (report == null)
            return Results.NotFound(new { error = "Report not found." });

        return Results.Ok(report);
    }

    private static async Task<IResult> GetReportPages(
        string workspaceId, string reportId, IPowerBiSyncService syncService)
    {
        try
        {
            var pages = await syncService.GetReportPagesAsync(workspaceId, reportId);
            return Results.Ok(new { data = pages });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get report pages: {ex.Message}");
        }
    }

    private static async Task<IResult> GetEmbedToken(
        string workspaceId, string reportId, IPowerBiSyncService syncService)
    {
        try
        {
            var token = await syncService.GenerateEmbedTokenAsync(workspaceId, reportId);
            return Results.Ok(new
            {
                token.Token,
                token.TokenId,
                token.Expiration
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to generate embed token: {ex.Message}");
        }
    }

    private static async Task<IResult> ExportReport(
        string workspaceId, string reportId, ExportReportRequest request,
        IPowerBiApiClient apiClient)
    {
        try
        {
            var body = new
            {
                format = request.Format ?? "PDF"
            };

            var result = await apiClient.PostAsync<object>(
                $"groups/{workspaceId}/reports/{reportId}/ExportTo", body);

            return Results.Ok(new { message = "Export initiated.", result });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to export report: {ex.Message}");
        }
    }

    private static async Task<IResult> ImportReport(
        string workspaceId, string reportId,
        IPowerBiSyncService syncService)
    {
        // Import syncs this specific report from Power BI into local DB
        try
        {
            var result = await syncService.SyncReportsAsync(workspaceId);
            return Results.Ok(new { message = "Report imported/synced.", result });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to import report: {ex.Message}");
        }
    }

    // ── Datasets ──────────────────────────────────────────────────

    private static async Task<IResult> ListDatasets(IPowerBiRepository repo)
    {
        var datasets = await repo.GetDatasetsAsync();
        return Results.Ok(new { data = datasets });
    }

    private static async Task<IResult> ListWorkspaceDatasets(string workspaceId, IPowerBiRepository repo)
    {
        var datasets = await repo.GetDatasetsAsync(workspaceId);
        return Results.Ok(new { data = datasets });
    }

    private static async Task<IResult> GetDatasetDatasources(
        string workspaceId, string datasetId, IPowerBiSyncService syncService)
    {
        try
        {
            var datasources = await syncService.GetDatasetDatasourcesAsync(workspaceId, datasetId);
            return Results.Ok(new { data = datasources });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get datasources: {ex.Message}");
        }
    }

    private static async Task<IResult> GetRefreshHistory(
        string workspaceId, string datasetId, IPowerBiSyncService syncService)
    {
        try
        {
            var refreshes = await syncService.GetRefreshHistoryAsync(workspaceId, datasetId);
            return Results.Ok(new { data = refreshes });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get refresh history: {ex.Message}");
        }
    }

    private static async Task<IResult> TriggerRefresh(
        string workspaceId, string datasetId, IPowerBiSyncService syncService)
    {
        var success = await syncService.TriggerRefreshAsync(workspaceId, datasetId);

        return success
            ? Results.Ok(new { message = "Refresh triggered successfully." })
            : Results.Problem("Failed to trigger dataset refresh.");
    }

    private static async Task<IResult> ExecuteDaxQuery(
        string workspaceId, string datasetId, DaxQueryRequest request,
        IPowerBiSyncService syncService)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Results.BadRequest(new { error = "DAX query is required." });

        try
        {
            var result = await syncService.ExecuteDaxQueryAsync(workspaceId, datasetId, request.Query);
            return Results.Ok(new { data = result });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to execute DAX query: {ex.Message}");
        }
    }

    // ── Dashboards ────────────────────────────────────────────────

    private static async Task<IResult> ListDashboards(IPowerBiRepository repo)
    {
        var dashboards = await repo.GetDashboardsAsync();
        return Results.Ok(new { data = dashboards });
    }

    private static async Task<IResult> GetDashboardTiles(
        string workspaceId, string dashboardId, IPowerBiSyncService syncService)
    {
        try
        {
            var tiles = await syncService.GetDashboardTilesAsync(workspaceId, dashboardId);
            return Results.Ok(new { data = tiles });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get dashboard tiles: {ex.Message}");
        }
    }

    // ── Gateways ──────────────────────────────────────────────────

    private static async Task<IResult> ListGateways(IPowerBiSyncService syncService)
    {
        try
        {
            var gateways = await syncService.GetGatewaysAsync();
            return Results.Ok(new { data = gateways });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get gateways: {ex.Message}");
        }
    }

    // ── Sync ──────────────────────────────────────────────────────

    private static async Task<IResult> TriggerSync(IPowerBiSyncService syncService)
    {
        try
        {
            var result = await syncService.SyncAllAsync();
            return Results.Ok(new
            {
                message = "Full sync completed.",
                result.Created,
                result.Updated,
                result.Deleted,
                result.Errors,
                result.ErrorMessages,
                result.SyncedAt
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Sync failed: {ex.Message}");
        }
    }

    private static async Task<IResult> SyncWorkspaces(IPowerBiSyncService syncService)
    {
        try
        {
            var result = await syncService.SyncWorkspacesAsync();
            return Results.Ok(new
            {
                message = "Workspace sync completed.",
                result.Created,
                result.Updated,
                result.Deleted,
                result.Errors,
                result.SyncedAt
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Workspace sync failed: {ex.Message}");
        }
    }

    private static async Task<IResult> SyncReports(IPowerBiSyncService syncService)
    {
        try
        {
            var result = await syncService.SyncReportsAsync();
            return Results.Ok(new
            {
                message = "Report sync completed.",
                result.Created,
                result.Updated,
                result.Deleted,
                result.Errors,
                result.SyncedAt
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Report sync failed: {ex.Message}");
        }
    }

    private static async Task<IResult> GetSyncStatus(IPowerBiRepository repo)
    {
        var config = await repo.GetConfigAsync();
        var latestLog = await repo.GetLatestSyncLogAsync();

        return Results.Ok(new
        {
            configured = config != null,
            enabled = config?.Enabled ?? false,
            lastSyncAt = config?.LastSyncAt,
            latestSync = latestLog != null ? new
            {
                latestLog.SyncType,
                latestLog.Status,
                latestLog.ItemsCreated,
                latestLog.ItemsUpdated,
                latestLog.ItemsDeleted,
                latestLog.ItemsErrored,
                latestLog.StartedAt,
                latestLog.CompletedAt
            } : null
        });
    }

    private static async Task<IResult> GetSyncHistory(IPowerBiRepository repo, int limit = 50)
    {
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        var history = await repo.GetSyncHistoryAsync(limit);
        return Results.Ok(new { data = history });
    }
}

// ── Request DTOs ──────────────────────────────────────────────────

public record SavePowerBiConfigRequest(
    string TenantId,
    string ClientId,
    string ClientSecret,
    bool? Enabled);

public record TestPowerBiConnectionRequest(
    string TenantId,
    string ClientId,
    string ClientSecret);

public record ExportReportRequest(string? Format);

public record DaxQueryRequest(string Query);
