using System.Data;

namespace DaisyReport.Api.DataProviders.Abstractions;

public interface IDataProvider
{
    DataProviderType ProviderType { get; }
    string ParameterPrefix { get; }

    Task<IDbConnection> CreateConnectionAsync(string connectionString, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default);
    Task<string?> GetServerVersionAsync(IDbConnection connection);
    Task<List<TableMetadata>> GetTablesAsync(IDbConnection connection, string? schema = null);
    Task<List<ColumnMetadata>> GetColumnsAsync(IDbConnection connection, string table, string? schema = null);
    Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(IDbConnection connection, string table, string? schema = null);
    string QuoteIdentifier(string identifier);
}
