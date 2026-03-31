using System.Data;
using Dapper;
using DaisyReport.Api.DataProviders.Abstractions;
using MySqlConnector;

namespace DaisyReport.Api.DataProviders.Providers;

public sealed class MySqlProvider : IDataProvider
{
    public DataProviderType ProviderType => DataProviderType.MySql;
    public string ParameterPrefix => "@";

    public async Task<IDbConnection> CreateConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new MySqlConnection(connectionString);
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
        return await connection.ExecuteScalarAsync<string>("SELECT VERSION()");
    }

    public async Task<List<TableMetadata>> GetTablesAsync(IDbConnection connection, string? schema = null)
    {
        var dbName = schema ?? connection.Database;

        var sql = @"
            SELECT TABLE_NAME AS Name,
                   TABLE_SCHEMA AS `Schema`,
                   TABLE_TYPE AS TableType
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @DbName
            ORDER BY TABLE_NAME";

        var result = await connection.QueryAsync<TableMetadata>(sql, new { DbName = dbName });
        return result.ToList();
    }

    public async Task<List<ColumnMetadata>> GetColumnsAsync(IDbConnection connection, string table, string? schema = null)
    {
        var dbName = schema ?? connection.Database;

        var sql = @"
            SELECT
                c.COLUMN_NAME AS Name,
                c.DATA_TYPE AS DataType,
                c.ORDINAL_POSITION AS OrdinalPosition,
                CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                CASE WHEN c.COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS IsPrimaryKey,
                CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END AS IsAutoIncrement,
                c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
                c.NUMERIC_PRECISION AS NumericPrecision,
                c.NUMERIC_SCALE AS NumericScale,
                c.COLUMN_DEFAULT AS DefaultValue,
                c.TABLE_NAME AS TableName,
                c.TABLE_SCHEMA AS `Schema`
            FROM information_schema.COLUMNS c
            WHERE c.TABLE_NAME = @Table
              AND c.TABLE_SCHEMA = @DbName
            ORDER BY c.ORDINAL_POSITION";

        var result = await connection.QueryAsync<ColumnMetadata>(sql, new { Table = table, DbName = dbName });
        return result.ToList();
    }

    public async Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(IDbConnection connection, string table, string? schema = null)
    {
        var dbName = schema ?? connection.Database;

        var sql = @"
            SELECT
                kcu.CONSTRAINT_NAME AS ConstraintName,
                kcu.COLUMN_NAME AS ColumnName,
                kcu.REFERENCED_TABLE_NAME AS ReferencedTable,
                kcu.REFERENCED_COLUMN_NAME AS ReferencedColumn,
                kcu.REFERENCED_TABLE_SCHEMA AS ReferencedSchema,
                kcu.TABLE_NAME AS TableName,
                kcu.TABLE_SCHEMA AS `Schema`
            FROM information_schema.KEY_COLUMN_USAGE kcu
            WHERE kcu.TABLE_NAME = @Table
              AND kcu.TABLE_SCHEMA = @DbName
              AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION";

        var result = await connection.QueryAsync<ForeignKeyMetadata>(sql, new { Table = table, DbName = dbName });
        return result.ToList();
    }

    public string QuoteIdentifier(string identifier)
    {
        return $"`{identifier.Replace("`", "``")}`";
    }
}
