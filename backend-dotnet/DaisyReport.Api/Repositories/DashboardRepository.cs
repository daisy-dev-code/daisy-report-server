using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly IDatabase _database;

    public DashboardRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<Dashboard?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();

        var dashboard = await conn.QuerySingleOrDefaultAsync<Dashboard>(
            @"SELECT id AS Id, folder_id AS FolderId, name AS Name, description AS Description,
                     layout AS Layout, columns AS Columns, reload_interval AS ReloadInterval,
                     is_primary AS IsPrimary, is_config_protected AS IsConfigProtected,
                     created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt,
                     version AS Version
              FROM RS_DASHBOARD WHERE id = @Id",
            new { Id = id });

        if (dashboard == null) return null;

        var dadgets = (await conn.QueryAsync<Dadget>(
            @"SELECT id AS Id, dashboard_id AS DashboardId, dtype AS Dtype,
                     col_position AS ColPosition, row_position AS RowPosition,
                     width_span AS WidthSpan, height AS Height, config AS Config,
                     created_at AS CreatedAt
              FROM RS_DADGET WHERE dashboard_id = @DashboardId
              ORDER BY col_position, row_position",
            new { DashboardId = id })).ToList();

        dashboard.Dadgets = dadgets;
        return dashboard;
    }

    public async Task<(List<Dashboard> Dashboards, int Total)> ListAsync(int page, int pageSize, long? folderId)
    {
        using var conn = await _database.GetConnectionAsync();

        var whereClause = folderId.HasValue ? "WHERE folder_id = @FolderId" : "";
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_DASHBOARD {whereClause}",
            new { FolderId = folderId });

        var dashboards = (await conn.QueryAsync<Dashboard>(
            $@"SELECT id AS Id, folder_id AS FolderId, name AS Name, description AS Description,
                      layout AS Layout, columns AS Columns, reload_interval AS ReloadInterval,
                      is_primary AS IsPrimary, is_config_protected AS IsConfigProtected,
                      created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt,
                      version AS Version
               FROM RS_DASHBOARD {whereClause}
               ORDER BY name ASC
               LIMIT @PageSize OFFSET @Offset",
            new { FolderId = folderId, PageSize = pageSize, Offset = offset })).ToList();

        return (dashboards, total);
    }

    public async Task<long> CreateAsync(Dashboard dashboard)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_DASHBOARD (folder_id, name, description, layout, columns,
                     reload_interval, is_primary, is_config_protected, created_by, created_at, updated_at, version)
              VALUES (@FolderId, @Name, @Description, @Layout, @Columns,
                     @ReloadInterval, @IsPrimary, @IsConfigProtected, @CreatedBy, @CreatedAt, @UpdatedAt, @Version);
              SELECT LAST_INSERT_ID();",
            new
            {
                dashboard.FolderId,
                dashboard.Name,
                dashboard.Description,
                dashboard.Layout,
                dashboard.Columns,
                dashboard.ReloadInterval,
                dashboard.IsPrimary,
                dashboard.IsConfigProtected,
                dashboard.CreatedBy,
                dashboard.CreatedAt,
                dashboard.UpdatedAt,
                dashboard.Version
            });
        return id;
    }

    public async Task<bool> UpdateAsync(Dashboard dashboard)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_DASHBOARD SET
                folder_id = @FolderId, name = @Name, description = @Description,
                layout = @Layout, columns = @Columns, reload_interval = @ReloadInterval,
                is_primary = @IsPrimary, is_config_protected = @IsConfigProtected,
                updated_at = @UpdatedAt, version = version + 1
              WHERE id = @Id",
            new
            {
                dashboard.Id,
                dashboard.FolderId,
                dashboard.Name,
                dashboard.Description,
                dashboard.Layout,
                dashboard.Columns,
                dashboard.ReloadInterval,
                dashboard.IsPrimary,
                dashboard.IsConfigProtected,
                dashboard.UpdatedAt
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        // Delete dadgets first, then the dashboard
        await conn.ExecuteAsync("DELETE FROM RS_DADGET WHERE dashboard_id = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM RS_FAVORITE WHERE entity_type = 'Dashboard' AND entity_id = @Id", new { Id = id });
        var rows = await conn.ExecuteAsync("DELETE FROM RS_DASHBOARD WHERE id = @Id", new { Id = id });
        return rows > 0;
    }

    public async Task<long> AddDadgetAsync(Dadget dadget)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_DADGET (dashboard_id, dtype, col_position, row_position,
                     width_span, height, config, created_at)
              VALUES (@DashboardId, @Dtype, @ColPosition, @RowPosition,
                     @WidthSpan, @Height, @Config, @CreatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                dadget.DashboardId,
                dadget.Dtype,
                dadget.ColPosition,
                dadget.RowPosition,
                dadget.WidthSpan,
                dadget.Height,
                dadget.Config,
                dadget.CreatedAt
            });
        return id;
    }

    public async Task<bool> UpdateDadgetAsync(Dadget dadget)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_DADGET SET
                dtype = @Dtype, col_position = @ColPosition, row_position = @RowPosition,
                width_span = @WidthSpan, height = @Height, config = @Config
              WHERE id = @Id AND dashboard_id = @DashboardId",
            new
            {
                dadget.Id,
                dadget.DashboardId,
                dadget.Dtype,
                dadget.ColPosition,
                dadget.RowPosition,
                dadget.WidthSpan,
                dadget.Height,
                dadget.Config
            });
        return rows > 0;
    }

    public async Task<bool> RemoveDadgetAsync(long dadgetId)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_DADGET WHERE id = @Id",
            new { Id = dadgetId });
        return rows > 0;
    }

    public async Task UpdateLayoutAsync(long dashboardId, List<DadgetPosition> positions)
    {
        using var conn = await _database.GetConnectionAsync();
        using var transaction = conn.BeginTransaction();

        try
        {
            foreach (var pos in positions)
            {
                await conn.ExecuteAsync(
                    @"UPDATE RS_DADGET SET
                        col_position = @ColPosition, row_position = @RowPosition,
                        width_span = @WidthSpan, height = @Height
                      WHERE id = @DadgetId AND dashboard_id = @DashboardId",
                    new
                    {
                        pos.DadgetId,
                        DashboardId = dashboardId,
                        pos.ColPosition,
                        pos.RowPosition,
                        pos.WidthSpan,
                        pos.Height
                    },
                    transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<Dashboard>> GetBookmarksAsync(long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        var dashboards = (await conn.QueryAsync<Dashboard>(
            @"SELECT d.id AS Id, d.folder_id AS FolderId, d.name AS Name, d.description AS Description,
                     d.layout AS Layout, d.columns AS Columns, d.reload_interval AS ReloadInterval,
                     d.is_primary AS IsPrimary, d.is_config_protected AS IsConfigProtected,
                     d.created_by AS CreatedBy, d.created_at AS CreatedAt, d.updated_at AS UpdatedAt,
                     d.version AS Version
              FROM RS_FAVORITE b
              INNER JOIN RS_DASHBOARD d ON d.id = b.entity_id
              WHERE b.user_id = @UserId AND b.entity_type = 'Dashboard'
              ORDER BY d.name",
            new { UserId = userId })).ToList();

        return dashboards;
    }

    public async Task<bool> AddBookmarkAsync(long userId, long dashboardId)
    {
        using var conn = await _database.GetConnectionAsync();
        try
        {
            var rows = await conn.ExecuteAsync(
                @"INSERT INTO RS_FAVORITE (user_id, entity_type, entity_id, created_at)
                  VALUES (@UserId, 'Dashboard', @DashboardId, @Now)",
                new { UserId = userId, DashboardId = dashboardId, Now = DateTime.UtcNow });
            return rows > 0;
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            // Duplicate entry — already bookmarked
            return true;
        }
    }

    public async Task<bool> RemoveBookmarkAsync(long userId, long dashboardId)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_FAVORITE WHERE user_id = @UserId AND entity_type = 'Dashboard' AND entity_id = @DashboardId",
            new { UserId = userId, DashboardId = dashboardId });
        return rows > 0;
    }
}
