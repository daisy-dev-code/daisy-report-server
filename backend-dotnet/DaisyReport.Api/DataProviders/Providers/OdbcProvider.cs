using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using DaisyReport.Api.DataProviders.Abstractions;

namespace DaisyReport.Api.DataProviders.Providers;

public sealed class OdbcProvider : IDataProvider
{
    public DataProviderType ProviderType => DataProviderType.Odbc;
    public string ParameterPrefix => "?";

    public async Task<IDbConnection> CreateConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new OdbcConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new OdbcConnection(connectionString);
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

    public Task<string?> GetServerVersionAsync(IDbConnection connection)
    {
        if (connection is DbConnection dbConn)
            return Task.FromResult<string?>(dbConn.ServerVersion);
        return Task.FromResult<string?>(null);
    }

    public Task<List<TableMetadata>> GetTablesAsync(IDbConnection connection, string? schema = null)
    {
        var tables = new List<TableMetadata>();

        if (connection is not DbConnection dbConn)
            return Task.FromResult(tables);

        var schemaTable = dbConn.GetSchema("Tables");
        foreach (DataRow row in schemaTable.Rows)
        {
            var tableType = row["TABLE_TYPE"]?.ToString() ?? "TABLE";
            var tableSchema = row.Table.Columns.Contains("TABLE_SCHEM")
                ? row["TABLE_SCHEM"]?.ToString()
                : row.Table.Columns.Contains("TABLE_SCHEMA")
                    ? row["TABLE_SCHEMA"]?.ToString()
                    : null;

            if (!string.IsNullOrEmpty(schema) &&
                !string.Equals(tableSchema, schema, StringComparison.OrdinalIgnoreCase))
                continue;

            // Filter system tables
            if (tableType.Equals("SYSTEM TABLE", StringComparison.OrdinalIgnoreCase))
                continue;

            tables.Add(new TableMetadata
            {
                Name = row["TABLE_NAME"]?.ToString() ?? string.Empty,
                Schema = tableSchema,
                TableType = tableType
            });
        }

        return Task.FromResult(tables.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList());
    }

    public Task<List<ColumnMetadata>> GetColumnsAsync(IDbConnection connection, string table, string? schema = null)
    {
        var columns = new List<ColumnMetadata>();

        if (connection is not DbConnection dbConn)
            return Task.FromResult(columns);

        var restrictions = new string?[4];
        restrictions[2] = table; // TABLE_NAME is the 3rd restriction
        if (!string.IsNullOrEmpty(schema))
            restrictions[1] = schema; // TABLE_SCHEM is the 2nd restriction

        var schemaTable = dbConn.GetSchema("Columns", restrictions);
        var ordinal = 0;

        foreach (DataRow row in schemaTable.Rows)
        {
            ordinal++;
            columns.Add(new ColumnMetadata
            {
                Name = row["COLUMN_NAME"]?.ToString() ?? string.Empty,
                DataType = row.Table.Columns.Contains("TYPE_NAME")
                    ? row["TYPE_NAME"]?.ToString() ?? "unknown"
                    : row.Table.Columns.Contains("DATA_TYPE")
                        ? row["DATA_TYPE"]?.ToString() ?? "unknown"
                        : "unknown",
                OrdinalPosition = row.Table.Columns.Contains("ORDINAL_POSITION")
                    ? Convert.ToInt32(row["ORDINAL_POSITION"])
                    : ordinal,
                IsNullable = row.Table.Columns.Contains("IS_NULLABLE")
                    && string.Equals(row["IS_NULLABLE"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                MaxLength = row.Table.Columns.Contains("COLUMN_SIZE") && row["COLUMN_SIZE"] != DBNull.Value
                    ? Convert.ToInt32(row["COLUMN_SIZE"])
                    : null,
                TableName = table,
                Schema = schema
            });
        }

        return Task.FromResult(columns);
    }

    public Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(IDbConnection connection, string table, string? schema = null)
    {
        var foreignKeys = new List<ForeignKeyMetadata>();

        if (connection is not DbConnection dbConn)
            return Task.FromResult(foreignKeys);

        try
        {
            var restrictions = new string?[4];
            restrictions[2] = table;
            if (!string.IsNullOrEmpty(schema))
                restrictions[1] = schema;

            var schemaTable = dbConn.GetSchema("ForeignKeys", restrictions);

            foreach (DataRow row in schemaTable.Rows)
            {
                foreignKeys.Add(new ForeignKeyMetadata
                {
                    ConstraintName = row.Table.Columns.Contains("FK_NAME")
                        ? row["FK_NAME"]?.ToString() ?? string.Empty
                        : string.Empty,
                    ColumnName = row.Table.Columns.Contains("FKCOLUMN_NAME")
                        ? row["FKCOLUMN_NAME"]?.ToString() ?? string.Empty
                        : string.Empty,
                    ReferencedTable = row.Table.Columns.Contains("PKTABLE_NAME")
                        ? row["PKTABLE_NAME"]?.ToString() ?? string.Empty
                        : string.Empty,
                    ReferencedColumn = row.Table.Columns.Contains("PKCOLUMN_NAME")
                        ? row["PKCOLUMN_NAME"]?.ToString() ?? string.Empty
                        : string.Empty,
                    TableName = table,
                    Schema = schema
                });
            }
        }
        catch
        {
            // Not all ODBC drivers support ForeignKeys schema collection
        }

        return Task.FromResult(foreignKeys);
    }

    public string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
