using System.Data;
using Dapper;
using DaisyReport.Api.DataProviders.Abstractions;
using Npgsql;

namespace DaisyReport.Api.DataProviders.Providers;

public sealed class PostgreSqlProvider : IDataProvider
{
    public DataProviderType ProviderType => DataProviderType.PostgreSql;
    public string ParameterPrefix => "@";

    public async Task<IDbConnection> CreateConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
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
        return await connection.ExecuteScalarAsync<string>("SELECT version()");
    }

    public async Task<List<TableMetadata>> GetTablesAsync(IDbConnection connection, string? schema = null)
    {
        var sql = @"
            SELECT table_name AS ""Name"",
                   table_schema AS ""Schema"",
                   table_type AS ""TableType""
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')";

        if (!string.IsNullOrEmpty(schema))
            sql += @" AND table_schema = @Schema";

        sql += @" ORDER BY table_schema, table_name";

        var result = await connection.QueryAsync<TableMetadata>(sql, new { Schema = schema });
        return result.ToList();
    }

    public async Task<List<ColumnMetadata>> GetColumnsAsync(IDbConnection connection, string table, string? schema = null)
    {
        var sql = @"
            SELECT
                c.column_name AS ""Name"",
                c.data_type AS ""DataType"",
                c.ordinal_position AS ""OrdinalPosition"",
                CASE WHEN c.is_nullable = 'YES' THEN true ELSE false END AS ""IsNullable"",
                CASE WHEN pk.column_name IS NOT NULL THEN true ELSE false END AS ""IsPrimaryKey"",
                CASE WHEN c.column_default LIKE 'nextval%' THEN true ELSE false END AS ""IsAutoIncrement"",
                c.character_maximum_length AS ""MaxLength"",
                c.numeric_precision AS ""NumericPrecision"",
                c.numeric_scale AS ""NumericScale"",
                c.column_default AS ""DefaultValue"",
                c.table_name AS ""TableName"",
                c.table_schema AS ""Schema""
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT ku.table_schema, ku.table_name, ku.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage ku
                    ON tc.constraint_name = ku.constraint_name
                    AND tc.table_schema = ku.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY'
            ) pk ON pk.table_schema = c.table_schema
                 AND pk.table_name = c.table_name
                 AND pk.column_name = c.column_name
            WHERE c.table_name = @Table
              AND c.table_schema NOT IN ('pg_catalog', 'information_schema')";

        if (!string.IsNullOrEmpty(schema))
            sql += @" AND c.table_schema = @Schema";

        sql += @" ORDER BY c.ordinal_position";

        var result = await connection.QueryAsync<ColumnMetadata>(sql, new { Table = table, Schema = schema });
        return result.ToList();
    }

    public async Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(IDbConnection connection, string table, string? schema = null)
    {
        var sql = @"
            SELECT
                tc.constraint_name AS ""ConstraintName"",
                kcu.column_name AS ""ColumnName"",
                ccu.table_name AS ""ReferencedTable"",
                ccu.column_name AS ""ReferencedColumn"",
                ccu.table_schema AS ""ReferencedSchema"",
                kcu.table_name AS ""TableName"",
                kcu.table_schema AS ""Schema""
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON tc.constraint_name = ccu.constraint_name
                AND tc.table_schema = ccu.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND kcu.table_name = @Table";

        if (!string.IsNullOrEmpty(schema))
            sql += @" AND kcu.table_schema = @Schema";

        sql += @" ORDER BY tc.constraint_name, kcu.ordinal_position";

        var result = await connection.QueryAsync<ForeignKeyMetadata>(sql, new { Table = table, Schema = schema });
        return result.ToList();
    }

    public string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
