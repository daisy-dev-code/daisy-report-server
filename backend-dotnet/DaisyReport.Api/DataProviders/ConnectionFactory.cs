using System.Data;
using Dapper;
using DaisyReport.Api.DataProviders.Abstractions;
using DaisyReport.Api.Infrastructure;

namespace DaisyReport.Api.DataProviders;

public interface IConnectionFactory
{
    Task<IDbConnection> CreateConnectionAsync(long datasourceId, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(long datasourceId, CancellationToken ct = default);
    Task<DatasourceConnectionInfo> GetConnectionInfoAsync(long datasourceId, CancellationToken ct = default);
}

/// <summary>
/// Holds resolved connection details for a datasource.
/// </summary>
public sealed class DatasourceConnectionInfo
{
    public long DatasourceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DataProviderType ProviderType { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
    public IDataProvider Provider { get; init; } = null!;
}

public sealed class ConnectionFactory : IConnectionFactory
{
    private readonly IDatabase _database;
    private readonly IDataProviderRegistry _registry;
    private readonly ILogger<ConnectionFactory> _logger;

    public ConnectionFactory(IDatabase database, IDataProviderRegistry registry, ILogger<ConnectionFactory> logger)
    {
        _database = database;
        _registry = registry;
        _logger = logger;
    }

    public async Task<IDbConnection> CreateConnectionAsync(long datasourceId, CancellationToken ct = default)
    {
        var info = await GetConnectionInfoAsync(datasourceId, ct);
        return await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);
    }

    public async Task<bool> TestConnectionAsync(long datasourceId, CancellationToken ct = default)
    {
        try
        {
            var info = await GetConnectionInfoAsync(datasourceId, ct);
            return await info.Provider.TestConnectionAsync(info.ConnectionString, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed for datasource {DatasourceId}", datasourceId);
            return false;
        }
    }

    public async Task<DatasourceConnectionInfo> GetConnectionInfoAsync(long datasourceId, CancellationToken ct = default)
    {
        using var conn = await _database.GetConnectionAsync();

        var ds = await conn.QuerySingleOrDefaultAsync<DatasourceRow>(
            @"SELECT d.id AS Id, d.name AS Name, d.dtype AS Dtype,
                     db.driver_class AS DriverClass, db.jdbc_url AS JdbcUrl,
                     db.username AS Username, db.password_encrypted AS PasswordEncrypted,
                     db.provider_type AS ProviderTypeValue
              FROM RS_DATASOURCE d
              LEFT JOIN RS_DATABASE_DATASOURCE db ON db.datasource_id = d.id
              WHERE d.id = @Id",
            new { Id = datasourceId });

        if (ds == null)
            throw new KeyNotFoundException($"Datasource {datasourceId} not found.");

        if (!string.Equals(ds.Dtype, "database", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Datasource {datasourceId} is not a database datasource (type: {ds.Dtype}).");

        if (string.IsNullOrEmpty(ds.JdbcUrl))
            throw new InvalidOperationException($"Datasource {datasourceId} has no connection URL configured.");

        // Determine provider type: prefer explicit column, fall back to detection
        DataProviderType providerType;
        if (ds.ProviderTypeValue.HasValue)
        {
            providerType = (DataProviderType)ds.ProviderTypeValue.Value;
        }
        else
        {
            providerType = JdbcUrlConverter.DetectProviderType(ds.JdbcUrl, ds.DriverClass);
            _logger.LogDebug(
                "Auto-detected provider type {ProviderType} for datasource {DatasourceId} from URL/driver",
                providerType, datasourceId);
        }

        var provider = _registry.GetProvider(providerType);
        var connectionString = JdbcUrlConverter.Convert(ds.JdbcUrl, ds.Username, ds.PasswordEncrypted);

        return new DatasourceConnectionInfo
        {
            DatasourceId = datasourceId,
            Name = ds.Name,
            ProviderType = providerType,
            ConnectionString = connectionString,
            Provider = provider
        };
    }

    /// <summary>
    /// Internal DTO for the datasource query. Uses MatchNamesWithUnderscores.
    /// </summary>
    private sealed class DatasourceRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Dtype { get; set; } = string.Empty;
        public string? DriverClass { get; set; }
        public string? JdbcUrl { get; set; }
        public string? Username { get; set; }
        public string? PasswordEncrypted { get; set; }
        public int? ProviderTypeValue { get; set; }
    }
}
