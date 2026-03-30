using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;
using MySqlConnector;

namespace DaisyReport.Api.Repositories;

public class DatasourceRepository : IDatasourceRepository
{
    private readonly IDatabase _database;

    public DatasourceRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<Datasource?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Datasource>(
            @"SELECT d.id AS Id, d.name AS Name, d.description AS Description,
                     d.dtype AS Dtype, d.folder_id AS FolderId,
                     d.created_at AS CreatedAt, d.updated_at AS UpdatedAt,
                     db.driver_class AS DriverClass, db.jdbc_url AS JdbcUrl,
                     db.username AS Username, db.password_encrypted AS PasswordEncrypted,
                     db.min_pool AS MinPool, db.max_pool AS MaxPool,
                     db.query_timeout AS QueryTimeout
              FROM RS_DATASOURCE d
              LEFT JOIN RS_DATABASE_DATASOURCE db ON db.datasource_id = d.id
              WHERE d.id = @Id",
            new { Id = id });
    }

    public async Task<List<Datasource>> ListAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        var datasources = await conn.QueryAsync<Datasource>(
            @"SELECT d.id AS Id, d.name AS Name, d.description AS Description,
                     d.dtype AS Dtype, d.folder_id AS FolderId,
                     d.created_at AS CreatedAt, d.updated_at AS UpdatedAt,
                     db.driver_class AS DriverClass, db.jdbc_url AS JdbcUrl,
                     db.username AS Username, db.password_encrypted AS PasswordEncrypted,
                     db.min_pool AS MinPool, db.max_pool AS MaxPool,
                     db.query_timeout AS QueryTimeout
              FROM RS_DATASOURCE d
              LEFT JOIN RS_DATABASE_DATASOURCE db ON db.datasource_id = d.id
              ORDER BY d.id ASC");
        return datasources.ToList();
    }

    public async Task<long> CreateAsync(Datasource datasource)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_DATASOURCE (name, description, dtype, folder_id, created_at, updated_at)
              VALUES (@Name, @Description, @Dtype, @FolderId, @CreatedAt, @UpdatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                datasource.Name,
                datasource.Description,
                datasource.Dtype,
                datasource.FolderId,
                datasource.CreatedAt,
                datasource.UpdatedAt
            },
            tx);

        if (datasource.Dtype == "database")
        {
            await conn.ExecuteAsync(
                @"INSERT INTO RS_DATABASE_DATASOURCE (datasource_id, driver_class, jdbc_url, username, password_encrypted, min_pool, max_pool, query_timeout)
                  VALUES (@DatasourceId, @DriverClass, @JdbcUrl, @Username, @PasswordEncrypted, @MinPool, @MaxPool, @QueryTimeout)",
                new
                {
                    DatasourceId = id,
                    datasource.DriverClass,
                    datasource.JdbcUrl,
                    datasource.Username,
                    datasource.PasswordEncrypted,
                    datasource.MinPool,
                    datasource.MaxPool,
                    datasource.QueryTimeout
                },
                tx);
        }

        tx.Commit();
        return id;
    }

    public async Task<bool> UpdateAsync(Datasource datasource)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_DATASOURCE SET
                name = @Name, description = @Description, dtype = @Dtype,
                folder_id = @FolderId, updated_at = @UpdatedAt
              WHERE id = @Id",
            new
            {
                datasource.Id,
                datasource.Name,
                datasource.Description,
                datasource.Dtype,
                datasource.FolderId,
                datasource.UpdatedAt
            },
            tx);

        if (datasource.Dtype == "database")
        {
            await conn.ExecuteAsync(
                @"INSERT INTO RS_DATABASE_DATASOURCE (datasource_id, driver_class, jdbc_url, username, password_encrypted, min_pool, max_pool, query_timeout)
                  VALUES (@Id, @DriverClass, @JdbcUrl, @Username, @PasswordEncrypted, @MinPool, @MaxPool, @QueryTimeout)
                  ON DUPLICATE KEY UPDATE
                    driver_class = @DriverClass, jdbc_url = @JdbcUrl, username = @Username,
                    password_encrypted = @PasswordEncrypted, min_pool = @MinPool,
                    max_pool = @MaxPool, query_timeout = @QueryTimeout",
                new
                {
                    datasource.Id,
                    datasource.DriverClass,
                    datasource.JdbcUrl,
                    datasource.Username,
                    datasource.PasswordEncrypted,
                    datasource.MinPool,
                    datasource.MaxPool,
                    datasource.QueryTimeout
                },
                tx);
        }

        tx.Commit();
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            "DELETE FROM RS_DATABASE_DATASOURCE WHERE datasource_id = @Id",
            new { Id = id },
            tx);

        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_DATASOURCE WHERE id = @Id",
            new { Id = id },
            tx);

        tx.Commit();
        return rows > 0;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(long id)
    {
        var datasource = await GetByIdAsync(id);
        if (datasource == null)
            return (false, "Datasource not found.");

        if (datasource.Dtype != "database")
            return (false, "Only database datasources can be tested.");

        try
        {
            // Parse JDBC URL to MySQL connection string
            var jdbcUrl = datasource.JdbcUrl ?? string.Empty;
            var connString = ConvertJdbcToConnectionString(jdbcUrl, datasource.Username, datasource.PasswordEncrypted);

            using var testConn = new MySqlConnection(connString);
            await testConn.OpenAsync();
            await testConn.ExecuteAsync("SELECT 1");

            return (true, "Connection successful.");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private static string ConvertJdbcToConnectionString(string jdbcUrl, string? username, string? password)
    {
        // jdbc:mysql://host:port/database -> Server=host;Port=port;Database=database
        var url = jdbcUrl.Replace("jdbc:mysql://", "");
        var parts = url.Split('/');
        var hostPort = parts[0].Split(':');
        var host = hostPort[0];
        var port = hostPort.Length > 1 ? hostPort[1] : "3306";
        var database = parts.Length > 1 ? parts[1].Split('?')[0] : "";

        return $"Server={host};Port={port};Database={database};User={username};Password={password};";
    }
}
