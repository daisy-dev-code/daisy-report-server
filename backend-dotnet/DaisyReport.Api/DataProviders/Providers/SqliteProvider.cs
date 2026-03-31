using System.Data;
using Dapper;
using DaisyReport.Api.DataProviders.Abstractions;
using Microsoft.Data.Sqlite;

namespace DaisyReport.Api.DataProviders.Providers;

public sealed class SqliteProvider : IDataProvider
{
    public DataProviderType ProviderType => DataProviderType.Sqlite;
    public string ParameterPrefix => "@";

    public async Task<IDbConnection> CreateConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetServerVersionAsync(IDbConnection connection)
    {
        return await connection.ExecuteScalarAsync<string>("SELECT sqlite_version()");
    }

    public async Task<List<TableMetadata>> GetTablesAsync(IDbConnection connection, string? schema = null)
    {
        var sql = @"
            SELECT name AS Name,
                   CASE WHEN type = 'view' THEN 'VIEW' ELSE 'TABLE' END AS TableType
            FROM sqlite_master
            WHERE type IN ('table', 'view')
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name";

        var result = await connection.QueryAsync<TableMetadata>(sql);
        return result.ToList();
    }

    public async Task<List<ColumnMetadata>> GetColumnsAsync(IDbConnection connection, string table, string? schema = null)
    {
        // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
        var pragmaResult = await connection.QueryAsync(
            $"PRAGMA table_info({QuoteIdentifier(table)})");

        var columns = new List<ColumnMetadata>();
        foreach (var row in pragmaResult)
        {
            columns.Add(new ColumnMetadata
            {
                Name = (string)row.name,
                DataType = (string)(row.type ?? "TEXT"),
                OrdinalPosition = (int)(long)row.cid + 1,
                IsNullable = (long)row.notnull == 0,
                IsPrimaryKey = (long)row.pk > 0,
                IsAutoIncrement = false, // Detected below
                DefaultValue = row.dflt_value?.ToString(),
                TableName = table
            });
        }

        // Check for autoincrement: sqlite_master CREATE TABLE statement
        var createSql = await connection.ExecuteScalarAsync<string>(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name = @Table",
            new { Table = table });

        if (createSql != null)
        {
            var upperSql = createSql.ToUpperInvariant();
            if (upperSql.Contains("AUTOINCREMENT"))
            {
                // Mark the primary key column as auto-increment
                foreach (var col in columns.Where(c => c.IsPrimaryKey))
                {
                    col.IsAutoIncrement = true;
                }
            }
        }

        return columns;
    }

    public async Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(IDbConnection connection, string table, string? schema = null)
    {
        var pragmaResult = await connection.QueryAsync(
            $"PRAGMA foreign_key_list({QuoteIdentifier(table)})");

        var foreignKeys = new List<ForeignKeyMetadata>();
        foreach (var row in pragmaResult)
        {
            foreignKeys.Add(new ForeignKeyMetadata
            {
                ConstraintName = $"fk_{table}_{(int)(long)row.id}",
                ColumnName = (string)row.from,
                ReferencedTable = (string)row.table,
                ReferencedColumn = (string)row.to,
                TableName = table
            });
        }

        return foreignKeys;
    }

    public string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
