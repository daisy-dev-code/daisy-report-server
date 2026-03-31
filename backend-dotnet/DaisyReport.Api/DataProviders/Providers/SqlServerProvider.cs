using System.Data;
using Dapper;
using DaisyReport.Api.DataProviders.Abstractions;
using Microsoft.Data.SqlClient;

namespace DaisyReport.Api.DataProviders.Providers;

public sealed class SqlServerProvider : IDataProvider
{
    public DataProviderType ProviderType => DataProviderType.SqlServer;
    public string ParameterPrefix => "@";

    public async Task<IDbConnection> CreateConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
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
        return await connection.ExecuteScalarAsync<string>(
            "SELECT @@VERSION");
    }

    public async Task<List<TableMetadata>> GetTablesAsync(IDbConnection connection, string? schema = null)
    {
        var sql = @"
            SELECT TABLE_NAME AS Name,
                   TABLE_SCHEMA AS [Schema],
                   TABLE_TYPE AS TableType
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')";

        if (!string.IsNullOrEmpty(schema))
            sql += " AND TABLE_SCHEMA = @Schema";

        sql += " ORDER BY TABLE_SCHEMA, TABLE_NAME";

        var result = await connection.QueryAsync<TableMetadata>(sql, new { Schema = schema });
        return result.ToList();
    }

    public async Task<List<ColumnMetadata>> GetColumnsAsync(IDbConnection connection, string table, string? schema = null)
    {
        var sql = @"
            SELECT
                c.COLUMN_NAME AS Name,
                c.DATA_TYPE AS DataType,
                c.ORDINAL_POSITION AS OrdinalPosition,
                CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                CASE WHEN sc.is_identity = 1 THEN 1 ELSE 0 END AS IsAutoIncrement,
                c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
                c.NUMERIC_PRECISION AS NumericPrecision,
                c.NUMERIC_SCALE AS NumericScale,
                c.COLUMN_DEFAULT AS DefaultValue,
                c.TABLE_NAME AS TableName,
                c.TABLE_SCHEMA AS [Schema]
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA
                 AND pk.TABLE_NAME = c.TABLE_NAME
                 AND pk.COLUMN_NAME = c.COLUMN_NAME
            LEFT JOIN sys.columns sc
                ON sc.object_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME))
                AND sc.name = c.COLUMN_NAME
            WHERE c.TABLE_NAME = @Table";

        if (!string.IsNullOrEmpty(schema))
            sql += " AND c.TABLE_SCHEMA = @Schema";

        sql += " ORDER BY c.ORDINAL_POSITION";

        var result = await connection.QueryAsync<ColumnMetadata>(sql, new { Table = table, Schema = schema });
        return result.ToList();
    }

    public async Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(IDbConnection connection, string table, string? schema = null)
    {
        var sql = @"
            SELECT
                fk.CONSTRAINT_NAME AS ConstraintName,
                cu.COLUMN_NAME AS ColumnName,
                cu2.TABLE_NAME AS ReferencedTable,
                cu2.COLUMN_NAME AS ReferencedColumn,
                cu2.TABLE_SCHEMA AS ReferencedSchema,
                cu.TABLE_NAME AS TableName,
                cu.TABLE_SCHEMA AS [Schema]
            FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS fk
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE cu
                ON fk.CONSTRAINT_NAME = cu.CONSTRAINT_NAME
                AND fk.CONSTRAINT_SCHEMA = cu.CONSTRAINT_SCHEMA
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE cu2
                ON fk.UNIQUE_CONSTRAINT_NAME = cu2.CONSTRAINT_NAME
                AND fk.UNIQUE_CONSTRAINT_SCHEMA = cu2.CONSTRAINT_SCHEMA
                AND cu.ORDINAL_POSITION = cu2.ORDINAL_POSITION
            WHERE cu.TABLE_NAME = @Table";

        if (!string.IsNullOrEmpty(schema))
            sql += " AND cu.TABLE_SCHEMA = @Schema";

        sql += " ORDER BY fk.CONSTRAINT_NAME, cu.ORDINAL_POSITION";

        var result = await connection.QueryAsync<ForeignKeyMetadata>(sql, new { Table = table, Schema = schema });
        return result.ToList();
    }

    public string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }
}
