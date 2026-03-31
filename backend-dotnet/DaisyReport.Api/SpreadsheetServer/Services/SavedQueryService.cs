using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.SpreadsheetServer.Models;
using Serilog;

namespace DaisyReport.Api.SpreadsheetServer.Services;

public interface ISavedQueryService
{
    Task<List<SavedQuerySummary>> ListAsync(long? datasourceId = null);
    Task<SavedQuery?> GetByIdAsync(long id);
    Task<SavedQuery?> GetByNameAsync(string name);
    Task<long> CreateAsync(SavedQuery query);
    Task<bool> UpdateAsync(SavedQuery query);
    Task<bool> DeleteAsync(long id);
}

public class SavedQueryService : ISavedQueryService
{
    private readonly IDatabase _database;
    private readonly ILogger<SavedQueryService> _logger;

    public SavedQueryService(IDatabase database, ILogger<SavedQueryService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<List<SavedQuerySummary>> ListAsync(long? datasourceId = null)
    {
        using var conn = await _database.GetConnectionAsync();

        string sql;
        object param;

        if (datasourceId.HasValue)
        {
            sql = @"SELECT id, name, description, datasource_id, query_type
                     FROM RS_SAVED_QUERY
                     WHERE datasource_id = @DatasourceId
                     ORDER BY name ASC";
            param = new { DatasourceId = datasourceId.Value };
        }
        else
        {
            sql = @"SELECT id, name, description, datasource_id, query_type
                     FROM RS_SAVED_QUERY
                     ORDER BY name ASC";
            param = new { };
        }

        var results = await conn.QueryAsync<SavedQuerySummary>(sql, param);
        return results.ToList();
    }

    public async Task<SavedQuery?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<SavedQuery>(
            @"SELECT id, name, description, datasource_id, query_type, sql_text,
                     visual_model, parameters, created_by, created_at, updated_at
              FROM RS_SAVED_QUERY WHERE id = @Id",
            new { Id = id });
    }

    public async Task<SavedQuery?> GetByNameAsync(string name)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<SavedQuery>(
            @"SELECT id, name, description, datasource_id, query_type, sql_text,
                     visual_model, parameters, created_by, created_at, updated_at
              FROM RS_SAVED_QUERY WHERE name = @Name LIMIT 1",
            new { Name = name });
    }

    public async Task<long> CreateAsync(SavedQuery query)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_SAVED_QUERY
                  (name, description, datasource_id, query_type, sql_text, visual_model, parameters, created_by, created_at, updated_at)
              VALUES
                  (@Name, @Description, @DatasourceId, @QueryType, @SqlText, @VisualModel, @Parameters, @CreatedBy, @CreatedAt, @UpdatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                query.Name,
                query.Description,
                query.DatasourceId,
                query.QueryType,
                query.SqlText,
                query.VisualModel,
                query.Parameters,
                query.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        return id;
    }

    public async Task<bool> UpdateAsync(SavedQuery query)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_SAVED_QUERY SET
                  name = @Name,
                  description = @Description,
                  datasource_id = @DatasourceId,
                  query_type = @QueryType,
                  sql_text = @SqlText,
                  visual_model = @VisualModel,
                  parameters = @Parameters,
                  updated_at = @UpdatedAt
              WHERE id = @Id",
            new
            {
                query.Id,
                query.Name,
                query.Description,
                query.DatasourceId,
                query.QueryType,
                query.SqlText,
                query.VisualModel,
                query.Parameters,
                UpdatedAt = DateTime.UtcNow
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_SAVED_QUERY WHERE id = @Id",
            new { Id = id });
        return rows > 0;
    }
}
