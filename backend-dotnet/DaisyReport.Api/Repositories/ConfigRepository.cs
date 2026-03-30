using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class ConfigRepository : IConfigRepository
{
    private readonly IDatabase _database;

    public ConfigRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<List<ConfigEntry>> ListAsync(string? category = null)
    {
        using var conn = await _database.GetConnectionAsync();

        var whereClause = string.IsNullOrWhiteSpace(category) ? "" : "WHERE category = @Category";

        var entries = await conn.QueryAsync<ConfigEntry>(
            $@"SELECT id AS Id, config_key AS ConfigKey, config_value AS ConfigValue,
                      category AS Category, description AS Description, updated_at AS UpdatedAt
               FROM RS_SYSTEM_CONFIG {whereClause}
               ORDER BY category, config_key",
            new { Category = category });

        return entries.ToList();
    }

    public async Task<ConfigEntry?> GetByKeyAsync(string key)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ConfigEntry>(
            @"SELECT id AS Id, config_key AS ConfigKey, config_value AS ConfigValue,
                     category AS Category, description AS Description, updated_at AS UpdatedAt
              FROM RS_SYSTEM_CONFIG WHERE config_key = @Key",
            new { Key = key });
    }

    public async Task<bool> SetAsync(string key, string value, string? category = null, string? description = null)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"INSERT INTO RS_SYSTEM_CONFIG (config_key, config_value, category, description, updated_at)
              VALUES (@Key, @Value, @Category, @Description, @Now)
              ON DUPLICATE KEY UPDATE
                config_value = @Value,
                category = COALESCE(@Category, category),
                description = COALESCE(@Description, description),
                updated_at = @Now",
            new
            {
                Key = key,
                Value = value,
                Category = category,
                Description = description,
                Now = DateTime.UtcNow
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_SYSTEM_CONFIG WHERE config_key = @Key",
            new { Key = key });
        return rows > 0;
    }
}
