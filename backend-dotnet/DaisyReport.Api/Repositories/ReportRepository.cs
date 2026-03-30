using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class ReportRepository : IReportRepository
{
    private readonly IDatabase _database;

    public ReportRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<Report?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Report>(
            @"SELECT id AS Id, folder_id AS FolderId, name AS Name, description AS Description,
                     key_field AS KeyField, engine_type AS EngineType, datasource_id AS DatasourceId,
                     query_text AS QueryText, config AS Config, created_by AS CreatedBy,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_REPORT WHERE id = @Id",
            new { Id = id });
    }

    public async Task<(List<Report> Reports, int Total)> ListAsync(int page, int pageSize, long? folderId)
    {
        using var conn = await _database.GetConnectionAsync();

        var whereClause = folderId.HasValue ? "WHERE folder_id = @FolderId" : "";
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_REPORT {whereClause}",
            new { FolderId = folderId });

        var reports = (await conn.QueryAsync<Report>(
            $@"SELECT id AS Id, folder_id AS FolderId, name AS Name, description AS Description,
                      key_field AS KeyField, engine_type AS EngineType, datasource_id AS DatasourceId,
                      query_text AS QueryText, config AS Config, created_by AS CreatedBy,
                      created_at AS CreatedAt, updated_at AS UpdatedAt
               FROM RS_REPORT {whereClause}
               ORDER BY id ASC
               LIMIT @PageSize OFFSET @Offset",
            new { FolderId = folderId, PageSize = pageSize, Offset = offset })).ToList();

        return (reports, total);
    }

    public async Task<long> CreateAsync(Report report)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_REPORT (folder_id, name, description, key_field, engine_type, datasource_id, query_text, config, created_by, created_at, updated_at)
              VALUES (@FolderId, @Name, @Description, @KeyField, @EngineType, @DatasourceId, @QueryText, @Config, @CreatedBy, @CreatedAt, @UpdatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
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
        return id;
    }

    public async Task<bool> UpdateAsync(Report report)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_REPORT SET
                folder_id = @FolderId, name = @Name, description = @Description,
                key_field = @KeyField, engine_type = @EngineType, datasource_id = @DatasourceId,
                query_text = @QueryText, config = @Config, updated_at = @UpdatedAt
              WHERE id = @Id",
            new
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
                report.UpdatedAt
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_REPORT WHERE id = @Id",
            new { Id = id });
        return rows > 0;
    }

    public async Task<List<ReportParameter>> GetParametersAsync(long reportId)
    {
        using var conn = await _database.GetConnectionAsync();
        var parameters = await conn.QueryAsync<ReportParameter>(
            @"SELECT id AS Id, report_id AS ReportId, name AS Name, key_field AS KeyField,
                     type AS Type, default_value AS DefaultValue,
                     mandatory AS Mandatory, position AS Position
              FROM RS_PARAMETER_DEF
              WHERE report_id = @ReportId
              ORDER BY position ASC",
            new { ReportId = reportId });
        return parameters.ToList();
    }
}
