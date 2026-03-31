using System.Data;
using Dapper;
using DaisyReport.Api.DataProviders.Abstractions;

namespace DaisyReport.Api.DataProviders.Endpoints;

public static class MetadataEndpoints
{
    public static void MapMetadataEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/metadata").RequireAuthorization();

        group.MapGet("/{datasourceId:long}/tables", ListTables);
        group.MapGet("/{datasourceId:long}/columns/{table}", ListColumns);
        group.MapGet("/{datasourceId:long}/foreign-keys/{table}", ListForeignKeys);
        group.MapGet("/{datasourceId:long}/preview/{table}", PreviewTable);
        group.MapPost("/{datasourceId:long}/execute", ExecuteSql);
        group.MapGet("/{datasourceId:long}/server-version", GetServerVersion);
        group.MapPost("/{datasourceId:long}/test", TestConnection);
    }

    private static async Task<IResult> ListTables(
        long datasourceId,
        IConnectionFactory connectionFactory,
        string? schema = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);
            var tables = await info.Provider.GetTablesAsync(conn, schema);

            return Results.Ok(new { data = tables });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve tables: {ex.Message}");
        }
    }

    private static async Task<IResult> ListColumns(
        long datasourceId,
        string table,
        IConnectionFactory connectionFactory,
        string? schema = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);
            var columns = await info.Provider.GetColumnsAsync(conn, table, schema);

            return Results.Ok(new { data = columns });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve columns: {ex.Message}");
        }
    }

    private static async Task<IResult> ListForeignKeys(
        long datasourceId,
        string table,
        IConnectionFactory connectionFactory,
        string? schema = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);
            var fks = await info.Provider.GetForeignKeysAsync(conn, table, schema);

            return Results.Ok(new { data = fks });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve foreign keys: {ex.Message}");
        }
    }

    private static async Task<IResult> PreviewTable(
        long datasourceId,
        string table,
        IConnectionFactory connectionFactory,
        int limit = 100,
        string? schema = null,
        CancellationToken ct = default)
    {
        try
        {
            // Clamp limit to a safe range
            limit = Math.Clamp(limit, 1, 1000);

            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);

            var qualifiedTable = !string.IsNullOrEmpty(schema)
                ? $"{info.Provider.QuoteIdentifier(schema)}.{info.Provider.QuoteIdentifier(table)}"
                : info.Provider.QuoteIdentifier(table);

            // Build a safe SELECT with limit syntax appropriate for the provider
            var sql = info.ProviderType switch
            {
                DataProviderType.SqlServer => $"SELECT TOP {limit} * FROM {qualifiedTable}",
                _ => $"SELECT * FROM {qualifiedTable} LIMIT {limit}"
            };

            var rows = await conn.QueryAsync(sql);
            var data = rows.Select(r => (IDictionary<string, object>)r).ToList();

            return Results.Ok(new
            {
                table,
                schema,
                limit,
                rowCount = data.Count,
                data
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to preview table: {ex.Message}");
        }
    }

    private static async Task<IResult> ExecuteSql(
        long datasourceId,
        ExecuteSqlRequest request,
        HttpContext httpContext,
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        // Admin-only check
        var userRole = httpContext.Items["UserRole"]?.ToString();
        if (!string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return Results.BadRequest(new { error = "SQL statement is required." });
        }

        try
        {
            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);

            var sql = request.Sql.Trim();
            var isQuery = sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                          sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                          sql.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase) ||
                          sql.StartsWith("DESCRIBE", StringComparison.OrdinalIgnoreCase) ||
                          sql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase) ||
                          sql.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);

            if (isQuery)
            {
                var rows = await conn.QueryAsync(sql, commandTimeout: request.TimeoutSeconds ?? 30);
                var data = rows.Select(r => (IDictionary<string, object>)r).ToList();

                return Results.Ok(new
                {
                    type = "query",
                    rowCount = data.Count,
                    data
                });
            }
            else
            {
                var affected = await conn.ExecuteAsync(sql, commandTimeout: request.TimeoutSeconds ?? 30);
                return Results.Ok(new
                {
                    type = "execute",
                    rowsAffected = affected
                });
            }
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"SQL execution failed: {ex.Message}");
        }
    }

    private static async Task<IResult> GetServerVersion(
        long datasourceId,
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        try
        {
            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString, ct);
            var version = await info.Provider.GetServerVersionAsync(conn);

            return Results.Ok(new
            {
                datasourceId,
                providerType = info.ProviderType.ToString(),
                serverVersion = version
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get server version: {ex.Message}");
        }
    }

    private static async Task<IResult> TestConnection(
        long datasourceId,
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        try
        {
            var info = await connectionFactory.GetConnectionInfoAsync(datasourceId, ct);
            var success = await info.Provider.TestConnectionAsync(info.ConnectionString, ct);

            return success
                ? Results.Ok(new { success = true, message = "Connection successful.", providerType = info.ProviderType.ToString() })
                : Results.BadRequest(new { success = false, message = "Connection failed." });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, message = $"Connection failed: {ex.Message}" });
        }
    }
}

public record ExecuteSqlRequest(string Sql, int? TimeoutSeconds = 30);
