using DaisyReport.Api.PowerBi.Models;

namespace DaisyReport.Api.PowerBi.Services;

public interface IPowerBiSyncService
{
    Task<SyncResult> SyncAllAsync();
    Task<SyncResult> SyncWorkspacesAsync();
    Task<SyncResult> SyncReportsAsync(string? workspaceId = null);
    Task<SyncResult> SyncDatasetsAsync(string? workspaceId = null);
    Task<SyncResult> SyncDashboardsAsync(string? workspaceId = null);
    Task<PowerBiEmbedToken> GenerateEmbedTokenAsync(string workspaceId, string reportId);
    Task<bool> TriggerRefreshAsync(string workspaceId, string datasetId);
    Task<List<PowerBiRefresh>> GetRefreshHistoryAsync(string workspaceId, string datasetId);
    Task<List<PowerBiPage>> GetReportPagesAsync(string workspaceId, string reportId);
    Task<List<PowerBiTile>> GetDashboardTilesAsync(string workspaceId, string dashboardId);
    Task<List<PowerBiGateway>> GetGatewaysAsync();
    Task<List<PowerBiDatasource>> GetDatasetDatasourcesAsync(string workspaceId, string datasetId);
    Task<object?> ExecuteDaxQueryAsync(string workspaceId, string datasetId, string daxQuery);
}

public class PowerBiSyncService : IPowerBiSyncService
{
    private readonly IPowerBiApiClient _apiClient;
    private readonly IPowerBiRepository _repository;
    private readonly ILogger<PowerBiSyncService> _logger;

    public PowerBiSyncService(
        IPowerBiApiClient apiClient,
        IPowerBiRepository repository,
        ILogger<PowerBiSyncService> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAllAsync()
    {
        var overall = new SyncResult();

        _logger.LogInformation("Starting full Power BI sync");

        var wsResult = await SyncWorkspacesAsync();
        MergeResults(overall, wsResult);

        var workspaces = await _repository.GetWorkspacesAsync();
        foreach (var ws in workspaces)
        {
            try
            {
                var rptResult = await SyncReportsAsync(ws.Id);
                MergeResults(overall, rptResult);

                var dsResult = await SyncDatasetsAsync(ws.Id);
                MergeResults(overall, dsResult);

                var dashResult = await SyncDashboardsAsync(ws.Id);
                MergeResults(overall, dashResult);
            }
            catch (Exception ex)
            {
                overall.Errors++;
                overall.ErrorMessages.Add($"Workspace {ws.Id} ({ws.Name}): {ex.Message}");
                _logger.LogError(ex, "Error syncing workspace {WorkspaceId}", ws.Id);
            }
        }

        // Update last sync time
        await _repository.UpdateLastSyncAsync();

        // Log the sync
        await _repository.InsertSyncLogAsync(new PowerBiSyncLog
        {
            SyncType = "FULL",
            Status = overall.Errors > 0 ? "PARTIAL" : "SUCCESS",
            ItemsCreated = overall.Created,
            ItemsUpdated = overall.Updated,
            ItemsDeleted = overall.Deleted,
            ItemsErrored = overall.Errors,
            ErrorMessage = overall.ErrorMessages.Count > 0
                ? string.Join("; ", overall.ErrorMessages.Take(5))
                : null,
            StartedAt = overall.SyncedAt,
            CompletedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Full Power BI sync complete: {Created} created, {Updated} updated, {Deleted} deleted, {Errors} errors",
            overall.Created, overall.Updated, overall.Deleted, overall.Errors);

        return overall;
    }

    public async Task<SyncResult> SyncWorkspacesAsync()
    {
        var result = new SyncResult();
        _logger.LogInformation("Syncing Power BI workspaces");

        try
        {
            var response = await _apiClient.GetAsync<ODataList<PowerBiWorkspace>>("groups");
            var workspaces = response?.Value ?? new List<PowerBiWorkspace>();

            var existingIds = (await _repository.GetWorkspacesAsync()).Select(w => w.Id).ToHashSet();

            foreach (var ws in workspaces)
            {
                await _repository.UpsertWorkspaceAsync(ws);
                if (existingIds.Contains(ws.Id))
                    result.Updated++;
                else
                    result.Created++;
            }

            // Remove workspaces no longer in Power BI
            var remoteIds = workspaces.Select(w => w.Id).ToHashSet();
            foreach (var existingId in existingIds.Where(id => !remoteIds.Contains(id)))
            {
                await _repository.DeleteWorkspaceAsync(existingId);
                result.Deleted++;
            }
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add($"Workspaces: {ex.Message}");
            _logger.LogError(ex, "Error syncing workspaces");
        }

        await LogSync("WORKSPACES", result);
        return result;
    }

    public async Task<SyncResult> SyncReportsAsync(string? workspaceId = null)
    {
        var result = new SyncResult();

        try
        {
            if (workspaceId != null)
            {
                await SyncReportsForWorkspaceAsync(workspaceId, result);
            }
            else
            {
                var workspaces = await _repository.GetWorkspacesAsync();
                foreach (var ws in workspaces)
                {
                    await SyncReportsForWorkspaceAsync(ws.Id, result);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add($"Reports: {ex.Message}");
            _logger.LogError(ex, "Error syncing reports");
        }

        await LogSync("REPORTS", result);
        return result;
    }

    private async Task SyncReportsForWorkspaceAsync(string workspaceId, SyncResult result)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiReport>>($"groups/{workspaceId}/reports");
        var reports = response?.Value ?? new List<PowerBiReport>();

        var existingIds = (await _repository.GetReportsAsync(workspaceId)).Select(r => r.Id).ToHashSet();

        foreach (var report in reports)
        {
            report.WorkspaceId = workspaceId;
            await _repository.UpsertReportAsync(report);
            if (existingIds.Contains(report.Id))
                result.Updated++;
            else
                result.Created++;
        }

        var remoteIds = reports.Select(r => r.Id).ToHashSet();
        foreach (var existingId in existingIds.Where(id => !remoteIds.Contains(id)))
        {
            await _repository.DeleteReportAsync(existingId);
            result.Deleted++;
        }
    }

    public async Task<SyncResult> SyncDatasetsAsync(string? workspaceId = null)
    {
        var result = new SyncResult();

        try
        {
            if (workspaceId != null)
            {
                await SyncDatasetsForWorkspaceAsync(workspaceId, result);
            }
            else
            {
                var workspaces = await _repository.GetWorkspacesAsync();
                foreach (var ws in workspaces)
                {
                    await SyncDatasetsForWorkspaceAsync(ws.Id, result);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add($"Datasets: {ex.Message}");
            _logger.LogError(ex, "Error syncing datasets");
        }

        await LogSync("DATASETS", result);
        return result;
    }

    private async Task SyncDatasetsForWorkspaceAsync(string workspaceId, SyncResult result)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiDataset>>($"groups/{workspaceId}/datasets");
        var datasets = response?.Value ?? new List<PowerBiDataset>();

        var existingIds = (await _repository.GetDatasetsAsync(workspaceId)).Select(d => d.Id).ToHashSet();

        foreach (var dataset in datasets)
        {
            await _repository.UpsertDatasetAsync(dataset, workspaceId);
            if (existingIds.Contains(dataset.Id))
                result.Updated++;
            else
                result.Created++;
        }

        var remoteIds = datasets.Select(d => d.Id).ToHashSet();
        foreach (var existingId in existingIds.Where(id => !remoteIds.Contains(id)))
        {
            await _repository.DeleteDatasetAsync(existingId);
            result.Deleted++;
        }
    }

    public async Task<SyncResult> SyncDashboardsAsync(string? workspaceId = null)
    {
        var result = new SyncResult();

        try
        {
            if (workspaceId != null)
            {
                await SyncDashboardsForWorkspaceAsync(workspaceId, result);
            }
            else
            {
                var workspaces = await _repository.GetWorkspacesAsync();
                foreach (var ws in workspaces)
                {
                    await SyncDashboardsForWorkspaceAsync(ws.Id, result);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add($"Dashboards: {ex.Message}");
            _logger.LogError(ex, "Error syncing dashboards");
        }

        await LogSync("DASHBOARDS", result);
        return result;
    }

    private async Task SyncDashboardsForWorkspaceAsync(string workspaceId, SyncResult result)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiDashboard>>($"groups/{workspaceId}/dashboards");
        var dashboards = response?.Value ?? new List<PowerBiDashboard>();

        var existingIds = (await _repository.GetDashboardsAsync(workspaceId)).Select(d => d.Id).ToHashSet();

        foreach (var dashboard in dashboards)
        {
            await _repository.UpsertDashboardAsync(dashboard, workspaceId);
            if (existingIds.Contains(dashboard.Id))
                result.Updated++;
            else
                result.Created++;
        }

        var remoteIds = dashboards.Select(d => d.Id).ToHashSet();
        foreach (var existingId in existingIds.Where(id => !remoteIds.Contains(id)))
        {
            await _repository.DeleteDashboardAsync(existingId);
            result.Deleted++;
        }
    }

    public async Task<PowerBiEmbedToken> GenerateEmbedTokenAsync(string workspaceId, string reportId)
    {
        _logger.LogInformation("Generating embed token for report {ReportId} in workspace {WorkspaceId}",
            reportId, workspaceId);

        var requestBody = new
        {
            accessLevel = "View",
            allowSaveAs = false
        };

        var token = await _apiClient.PostAsync<PowerBiEmbedToken>(
            $"groups/{workspaceId}/reports/{reportId}/GenerateToken", requestBody);

        return token ?? throw new InvalidOperationException("Failed to generate embed token");
    }

    public async Task<bool> TriggerRefreshAsync(string workspaceId, string datasetId)
    {
        _logger.LogInformation("Triggering refresh for dataset {DatasetId} in workspace {WorkspaceId}",
            datasetId, workspaceId);

        try
        {
            await _apiClient.PostAsync($"groups/{workspaceId}/datasets/{datasetId}/refreshes",
                new { notifyOption = "NoNotification" });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger refresh for dataset {DatasetId}", datasetId);
            return false;
        }
    }

    public async Task<List<PowerBiRefresh>> GetRefreshHistoryAsync(string workspaceId, string datasetId)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiRefresh>>(
            $"groups/{workspaceId}/datasets/{datasetId}/refreshes");
        return response?.Value ?? new List<PowerBiRefresh>();
    }

    public async Task<List<PowerBiPage>> GetReportPagesAsync(string workspaceId, string reportId)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiPage>>(
            $"groups/{workspaceId}/reports/{reportId}/pages");
        return response?.Value ?? new List<PowerBiPage>();
    }

    public async Task<List<PowerBiTile>> GetDashboardTilesAsync(string workspaceId, string dashboardId)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiTile>>(
            $"groups/{workspaceId}/dashboards/{dashboardId}/tiles");
        return response?.Value ?? new List<PowerBiTile>();
    }

    public async Task<List<PowerBiGateway>> GetGatewaysAsync()
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiGateway>>("gateways");
        return response?.Value ?? new List<PowerBiGateway>();
    }

    public async Task<List<PowerBiDatasource>> GetDatasetDatasourcesAsync(string workspaceId, string datasetId)
    {
        var response = await _apiClient.GetAsync<ODataList<PowerBiDatasource>>(
            $"groups/{workspaceId}/datasets/{datasetId}/datasources");
        return response?.Value ?? new List<PowerBiDatasource>();
    }

    public async Task<object?> ExecuteDaxQueryAsync(string workspaceId, string datasetId, string daxQuery)
    {
        _logger.LogInformation("Executing DAX query on dataset {DatasetId}", datasetId);

        var body = new
        {
            queries = new[]
            {
                new { query = daxQuery }
            },
            serializerSettings = new { includeNulls = true }
        };

        return await _apiClient.PostAsync<object>(
            $"groups/{workspaceId}/datasets/{datasetId}/executeQueries", body);
    }

    private async Task LogSync(string syncType, SyncResult result)
    {
        try
        {
            await _repository.InsertSyncLogAsync(new PowerBiSyncLog
            {
                SyncType = syncType,
                Status = result.Errors > 0 ? "PARTIAL" : "SUCCESS",
                ItemsCreated = result.Created,
                ItemsUpdated = result.Updated,
                ItemsDeleted = result.Deleted,
                ItemsErrored = result.Errors,
                ErrorMessage = result.ErrorMessages.Count > 0
                    ? string.Join("; ", result.ErrorMessages.Take(5))
                    : null,
                StartedAt = result.SyncedAt,
                CompletedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log sync result for {SyncType}", syncType);
        }
    }

    private static void MergeResults(SyncResult target, SyncResult source)
    {
        target.Created += source.Created;
        target.Updated += source.Updated;
        target.Deleted += source.Deleted;
        target.Errors += source.Errors;
        target.ErrorMessages.AddRange(source.ErrorMessages);
    }

    private class ODataList<T>
    {
        public List<T> Value { get; set; } = new();
    }
}
