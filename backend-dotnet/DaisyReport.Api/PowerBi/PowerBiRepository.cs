using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.PowerBi.Models;

namespace DaisyReport.Api.PowerBi;

public interface IPowerBiRepository
{
    // Workspaces
    Task<List<PowerBiWorkspace>> GetWorkspacesAsync();
    Task<PowerBiWorkspace?> GetWorkspaceAsync(string id);
    Task UpsertWorkspaceAsync(PowerBiWorkspace workspace);
    Task DeleteWorkspaceAsync(string id);

    // Reports
    Task<List<PowerBiReport>> GetReportsAsync(string? workspaceId = null);
    Task<PowerBiReport?> GetReportAsync(string id);
    Task UpsertReportAsync(PowerBiReport report);
    Task DeleteReportAsync(string id);

    // Datasets
    Task<List<PowerBiDataset>> GetDatasetsAsync(string? workspaceId = null);
    Task<PowerBiDataset?> GetDatasetAsync(string id);
    Task UpsertDatasetAsync(PowerBiDataset dataset, string workspaceId);
    Task DeleteDatasetAsync(string id);

    // Dashboards
    Task<List<PowerBiDashboard>> GetDashboardsAsync(string? workspaceId = null);
    Task<PowerBiDashboard?> GetDashboardAsync(string id);
    Task UpsertDashboardAsync(PowerBiDashboard dashboard, string workspaceId);
    Task DeleteDashboardAsync(string id);

    // Config
    Task<PowerBiConfig?> GetConfigAsync();
    Task SaveConfigAsync(PowerBiConfig config);
    Task UpdateLastSyncAsync();

    // Sync Logs
    Task InsertSyncLogAsync(PowerBiSyncLog log);
    Task<List<PowerBiSyncLog>> GetSyncHistoryAsync(int limit = 50);
    Task<PowerBiSyncLog?> GetLatestSyncLogAsync();
}

public class PowerBiRepository : IPowerBiRepository
{
    private readonly IDatabase _database;

    public PowerBiRepository(IDatabase database)
    {
        _database = database;
    }

    // ── Workspaces ────────────────────────────────────────────────

    public async Task<List<PowerBiWorkspace>> GetWorkspacesAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        var results = await conn.QueryAsync<PowerBiWorkspace>(
            @"SELECT id AS Id, name AS Name, description AS Description, type AS Type,
                     state AS State, is_read_only AS IsReadOnly,
                     is_on_dedicated_capacity AS IsOnDedicatedCapacity
              FROM RS_POWERBI_WORKSPACE ORDER BY name");
        return results.ToList();
    }

    public async Task<PowerBiWorkspace?> GetWorkspaceAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PowerBiWorkspace>(
            @"SELECT id AS Id, name AS Name, description AS Description, type AS Type,
                     state AS State, is_read_only AS IsReadOnly,
                     is_on_dedicated_capacity AS IsOnDedicatedCapacity
              FROM RS_POWERBI_WORKSPACE WHERE id = @Id",
            new { Id = id });
    }

    public async Task UpsertWorkspaceAsync(PowerBiWorkspace workspace)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_POWERBI_WORKSPACE (id, name, description, type, state, is_read_only, is_on_dedicated_capacity, synced_at)
              VALUES (@Id, @Name, @Description, @Type, @State, @IsReadOnly, @IsOnDedicatedCapacity, @Now)
              ON DUPLICATE KEY UPDATE
                name = @Name, description = @Description, type = @Type, state = @State,
                is_read_only = @IsReadOnly, is_on_dedicated_capacity = @IsOnDedicatedCapacity, synced_at = @Now",
            new
            {
                workspace.Id,
                workspace.Name,
                workspace.Description,
                workspace.Type,
                workspace.State,
                workspace.IsReadOnly,
                workspace.IsOnDedicatedCapacity,
                Now = DateTime.UtcNow
            });
    }

    public async Task DeleteWorkspaceAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM RS_POWERBI_WORKSPACE WHERE id = @Id", new { Id = id });
    }

    // ── Reports ───────────────────────────────────────────────────

    public async Task<List<PowerBiReport>> GetReportsAsync(string? workspaceId = null)
    {
        using var conn = await _database.GetConnectionAsync();
        var where = workspaceId != null ? "WHERE workspace_id = @WorkspaceId" : "";
        var results = await conn.QueryAsync<PowerBiReport>(
            $@"SELECT id AS Id, workspace_id AS WorkspaceId, name AS Name, description AS Description,
                      web_url AS WebUrl, embed_url AS EmbedUrl, dataset_id AS DatasetId,
                      report_type AS ReportType, created_date_time AS CreatedDateTime,
                      modified_date_time AS ModifiedDateTime
               FROM RS_POWERBI_REPORT {where} ORDER BY name",
            new { WorkspaceId = workspaceId });
        return results.ToList();
    }

    public async Task<PowerBiReport?> GetReportAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PowerBiReport>(
            @"SELECT id AS Id, workspace_id AS WorkspaceId, name AS Name, description AS Description,
                     web_url AS WebUrl, embed_url AS EmbedUrl, dataset_id AS DatasetId,
                     report_type AS ReportType, created_date_time AS CreatedDateTime,
                     modified_date_time AS ModifiedDateTime
              FROM RS_POWERBI_REPORT WHERE id = @Id",
            new { Id = id });
    }

    public async Task UpsertReportAsync(PowerBiReport report)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_POWERBI_REPORT (id, workspace_id, name, description, web_url, embed_url, dataset_id, report_type, created_date_time, modified_date_time, synced_at)
              VALUES (@Id, @WorkspaceId, @Name, @Description, @WebUrl, @EmbedUrl, @DatasetId, @ReportType, @CreatedDateTime, @ModifiedDateTime, @Now)
              ON DUPLICATE KEY UPDATE
                workspace_id = @WorkspaceId, name = @Name, description = @Description,
                web_url = @WebUrl, embed_url = @EmbedUrl, dataset_id = @DatasetId,
                report_type = @ReportType, created_date_time = @CreatedDateTime,
                modified_date_time = @ModifiedDateTime, synced_at = @Now",
            new
            {
                report.Id,
                report.WorkspaceId,
                report.Name,
                report.Description,
                report.WebUrl,
                report.EmbedUrl,
                report.DatasetId,
                report.ReportType,
                report.CreatedDateTime,
                report.ModifiedDateTime,
                Now = DateTime.UtcNow
            });
    }

    public async Task DeleteReportAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM RS_POWERBI_REPORT WHERE id = @Id", new { Id = id });
    }

    // ── Datasets ──────────────────────────────────────────────────

    public async Task<List<PowerBiDataset>> GetDatasetsAsync(string? workspaceId = null)
    {
        using var conn = await _database.GetConnectionAsync();
        var where = workspaceId != null ? "WHERE workspace_id = @WorkspaceId" : "";
        var results = await conn.QueryAsync<PowerBiDataset>(
            $@"SELECT id AS Id, name AS Name, web_url AS WebUrl,
                      is_refreshable AS IsRefreshable,
                      is_effective_identity_required AS IsEffectiveIdentityRequired,
                      configured_by AS ConfiguredBy, created_date AS CreatedDate
               FROM RS_POWERBI_DATASET {where} ORDER BY name",
            new { WorkspaceId = workspaceId });
        return results.ToList();
    }

    public async Task<PowerBiDataset?> GetDatasetAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PowerBiDataset>(
            @"SELECT id AS Id, name AS Name, web_url AS WebUrl,
                     is_refreshable AS IsRefreshable,
                     is_effective_identity_required AS IsEffectiveIdentityRequired,
                     configured_by AS ConfiguredBy, created_date AS CreatedDate
              FROM RS_POWERBI_DATASET WHERE id = @Id",
            new { Id = id });
    }

    public async Task UpsertDatasetAsync(PowerBiDataset dataset, string workspaceId)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_POWERBI_DATASET (id, workspace_id, name, web_url, is_refreshable, is_effective_identity_required, configured_by, created_date, synced_at)
              VALUES (@Id, @WorkspaceId, @Name, @WebUrl, @IsRefreshable, @IsEffectiveIdentityRequired, @ConfiguredBy, @CreatedDate, @Now)
              ON DUPLICATE KEY UPDATE
                workspace_id = @WorkspaceId, name = @Name, web_url = @WebUrl,
                is_refreshable = @IsRefreshable, is_effective_identity_required = @IsEffectiveIdentityRequired,
                configured_by = @ConfiguredBy, created_date = @CreatedDate, synced_at = @Now",
            new
            {
                dataset.Id,
                WorkspaceId = workspaceId,
                dataset.Name,
                dataset.WebUrl,
                dataset.IsRefreshable,
                dataset.IsEffectiveIdentityRequired,
                dataset.ConfiguredBy,
                dataset.CreatedDate,
                Now = DateTime.UtcNow
            });
    }

    public async Task DeleteDatasetAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM RS_POWERBI_DATASET WHERE id = @Id", new { Id = id });
    }

    // ── Dashboards ────────────────────────────────────────────────

    public async Task<List<PowerBiDashboard>> GetDashboardsAsync(string? workspaceId = null)
    {
        using var conn = await _database.GetConnectionAsync();
        var where = workspaceId != null ? "WHERE workspace_id = @WorkspaceId" : "";
        var results = await conn.QueryAsync<PowerBiDashboard>(
            $@"SELECT id AS Id, display_name AS DisplayName, web_url AS WebUrl,
                      embed_url AS EmbedUrl, is_read_only AS IsReadOnly
               FROM RS_POWERBI_DASHBOARD {where} ORDER BY display_name",
            new { WorkspaceId = workspaceId });
        return results.ToList();
    }

    public async Task<PowerBiDashboard?> GetDashboardAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PowerBiDashboard>(
            @"SELECT id AS Id, display_name AS DisplayName, web_url AS WebUrl,
                     embed_url AS EmbedUrl, is_read_only AS IsReadOnly
              FROM RS_POWERBI_DASHBOARD WHERE id = @Id",
            new { Id = id });
    }

    public async Task UpsertDashboardAsync(PowerBiDashboard dashboard, string workspaceId)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_POWERBI_DASHBOARD (id, workspace_id, display_name, web_url, embed_url, is_read_only, synced_at)
              VALUES (@Id, @WorkspaceId, @DisplayName, @WebUrl, @EmbedUrl, @IsReadOnly, @Now)
              ON DUPLICATE KEY UPDATE
                workspace_id = @WorkspaceId, display_name = @DisplayName,
                web_url = @WebUrl, embed_url = @EmbedUrl, is_read_only = @IsReadOnly, synced_at = @Now",
            new
            {
                dashboard.Id,
                WorkspaceId = workspaceId,
                dashboard.DisplayName,
                dashboard.WebUrl,
                dashboard.EmbedUrl,
                dashboard.IsReadOnly,
                Now = DateTime.UtcNow
            });
    }

    public async Task DeleteDashboardAsync(string id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM RS_POWERBI_DASHBOARD WHERE id = @Id", new { Id = id });
    }

    // ── Config ────────────────────────────────────────────────────

    public async Task<PowerBiConfig?> GetConfigAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PowerBiConfig>(
            @"SELECT id AS Id, tenant_id AS TenantId, client_id AS ClientId,
                     client_secret_encrypted AS ClientSecretEncrypted,
                     enabled AS Enabled, last_sync_at AS LastSyncAt,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_POWERBI_CONFIG ORDER BY id LIMIT 1");
    }

    public async Task SaveConfigAsync(PowerBiConfig config)
    {
        using var conn = await _database.GetConnectionAsync();

        if (config.Id > 0)
        {
            await conn.ExecuteAsync(
                @"UPDATE RS_POWERBI_CONFIG SET
                    tenant_id = @TenantId, client_id = @ClientId,
                    client_secret_encrypted = @ClientSecretEncrypted,
                    enabled = @Enabled, updated_at = @Now
                  WHERE id = @Id",
                new
                {
                    config.Id,
                    config.TenantId,
                    config.ClientId,
                    config.ClientSecretEncrypted,
                    config.Enabled,
                    Now = DateTime.UtcNow
                });
        }
        else
        {
            await conn.ExecuteAsync(
                @"INSERT INTO RS_POWERBI_CONFIG (tenant_id, client_id, client_secret_encrypted, enabled, created_at, updated_at)
                  VALUES (@TenantId, @ClientId, @ClientSecretEncrypted, @Enabled, @Now, @Now)",
                new
                {
                    config.TenantId,
                    config.ClientId,
                    config.ClientSecretEncrypted,
                    config.Enabled,
                    Now = DateTime.UtcNow
                });
        }

        // Also store in RS_CONFIG for the auth service
        await conn.ExecuteAsync(
            @"INSERT INTO RS_CONFIG (config_key, config_value, category, description, updated_at)
              VALUES ('powerbi.tenant_id', @TenantId, 'powerbi', 'Power BI Tenant ID', @Now)
              ON DUPLICATE KEY UPDATE config_value = @TenantId, updated_at = @Now",
            new { config.TenantId, Now = DateTime.UtcNow });

        await conn.ExecuteAsync(
            @"INSERT INTO RS_CONFIG (config_key, config_value, category, description, updated_at)
              VALUES ('powerbi.client_id', @ClientId, 'powerbi', 'Power BI Client ID', @Now)
              ON DUPLICATE KEY UPDATE config_value = @ClientId, updated_at = @Now",
            new { config.ClientId, Now = DateTime.UtcNow });

        await conn.ExecuteAsync(
            @"INSERT INTO RS_CONFIG (config_key, config_value, category, description, updated_at)
              VALUES ('powerbi.client_secret', @Secret, 'powerbi', 'Power BI Client Secret (encrypted)', @Now)
              ON DUPLICATE KEY UPDATE config_value = @Secret, updated_at = @Now",
            new { Secret = config.ClientSecretEncrypted, Now = DateTime.UtcNow });
    }

    public async Task UpdateLastSyncAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE RS_POWERBI_CONFIG SET last_sync_at = @Now, updated_at = @Now",
            new { Now = DateTime.UtcNow });
    }

    // ── Sync Logs ─────────────────────────────────────────────────

    public async Task InsertSyncLogAsync(PowerBiSyncLog log)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_POWERBI_SYNC_LOG (sync_type, status, items_created, items_updated, items_deleted, items_errored, error_message, started_at, completed_at)
              VALUES (@SyncType, @Status, @ItemsCreated, @ItemsUpdated, @ItemsDeleted, @ItemsErrored, @ErrorMessage, @StartedAt, @CompletedAt)",
            new
            {
                log.SyncType,
                log.Status,
                log.ItemsCreated,
                log.ItemsUpdated,
                log.ItemsDeleted,
                log.ItemsErrored,
                log.ErrorMessage,
                log.StartedAt,
                log.CompletedAt
            });
    }

    public async Task<List<PowerBiSyncLog>> GetSyncHistoryAsync(int limit = 50)
    {
        using var conn = await _database.GetConnectionAsync();
        var results = await conn.QueryAsync<PowerBiSyncLog>(
            @"SELECT id AS Id, sync_type AS SyncType, status AS Status,
                     items_created AS ItemsCreated, items_updated AS ItemsUpdated,
                     items_deleted AS ItemsDeleted, items_errored AS ItemsErrored,
                     error_message AS ErrorMessage, started_at AS StartedAt,
                     completed_at AS CompletedAt
              FROM RS_POWERBI_SYNC_LOG
              ORDER BY started_at DESC
              LIMIT @Limit",
            new { Limit = limit });
        return results.ToList();
    }

    public async Task<PowerBiSyncLog?> GetLatestSyncLogAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PowerBiSyncLog>(
            @"SELECT id AS Id, sync_type AS SyncType, status AS Status,
                     items_created AS ItemsCreated, items_updated AS ItemsUpdated,
                     items_deleted AS ItemsDeleted, items_errored AS ItemsErrored,
                     error_message AS ErrorMessage, started_at AS StartedAt,
                     completed_at AS CompletedAt
              FROM RS_POWERBI_SYNC_LOG
              ORDER BY started_at DESC
              LIMIT 1");
    }
}
