using System.Data;
using MySqlConnector;

namespace DaisyReport.Api.Infrastructure;

public interface IDatabase
{
    Task<IDbConnection> GetConnectionAsync();
}

public class Database : IDatabase
{
    private readonly string _connectionString;

    public Database(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySQL")
            ?? throw new InvalidOperationException("MySQL connection string not configured.");
    }

    public async Task<IDbConnection> GetConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
