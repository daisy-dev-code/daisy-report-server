using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace DaisyReport.Api.Infrastructure;

public class MigrationRunner
{
    private readonly IDatabase _database;
    private readonly ILogger<MigrationRunner> _logger;
    private readonly string _migrationsPath;

    public MigrationRunner(IDatabase database, ILogger<MigrationRunner> logger, IConfiguration configuration)
    {
        _database = database;
        _logger = logger;

        // Resolve shared/migrations/ relative to the project root
        var basePath = AppContext.BaseDirectory;
        _migrationsPath = configuration["Migrations:Path"]
            ?? Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "..", "shared", "migrations"));
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Running database migrations from {Path}", _migrationsPath);

        using var connection = await _database.GetConnectionAsync();

        // Create migration tracking table if it doesn't exist
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS RS_SCHEMA_VERSION (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                filename VARCHAR(255) NOT NULL UNIQUE,
                checksum VARCHAR(64) NOT NULL,
                applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )");

        if (!Directory.Exists(_migrationsPath))
        {
            _logger.LogWarning("Migrations directory not found: {Path}. Skipping migrations.", _migrationsPath);
            return;
        }

        var sqlFiles = Directory.GetFiles(_migrationsPath, "*.sql")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        if (sqlFiles.Count == 0)
        {
            _logger.LogInformation("No migration files found.");
            return;
        }

        foreach (var file in sqlFiles)
        {
            var filename = Path.GetFileName(file);
            var sql = await File.ReadAllTextAsync(file);
            var checksum = ComputeChecksum(sql);

            var existing = await connection.QuerySingleOrDefaultAsync<(string Checksum, DateTime AppliedAt)?>(
                "SELECT checksum AS Checksum, applied_at AS AppliedAt FROM RS_SCHEMA_VERSION WHERE filename = @Filename",
                new { Filename = filename });

            if (existing != null)
            {
                if (existing.Value.Checksum != checksum)
                {
                    _logger.LogError(
                        "Migration {Filename} checksum mismatch! Expected {Expected}, got {Actual}. Migration file may have been tampered with.",
                        filename, existing.Value.Checksum, checksum);
                    throw new InvalidOperationException(
                        $"Migration checksum mismatch for {filename}. Database integrity may be compromised.");
                }

                _logger.LogDebug("Migration {Filename} already applied, skipping.", filename);
                continue;
            }

            _logger.LogInformation("Applying migration: {Filename}", filename);

            try
            {
                // Split on delimiter for multi-statement SQL files
                var statements = SplitSqlStatements(sql);
                foreach (var statement in statements)
                {
                    if (!string.IsNullOrWhiteSpace(statement))
                    {
                        await connection.ExecuteAsync(statement);
                    }
                }

                await connection.ExecuteAsync(
                    "INSERT INTO RS_SCHEMA_VERSION (filename, checksum) VALUES (@Filename, @Checksum)",
                    new { Filename = filename, Checksum = checksum });

                _logger.LogInformation("Migration {Filename} applied successfully.", filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply migration {Filename}", filename);
                throw;
            }
        }

        _logger.LogInformation("All migrations applied successfully.");
    }

    private static string ComputeChecksum(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }

    private static List<string> SplitSqlStatements(string sql)
    {
        // Split on semicolons but respect delimiters used in stored procedures
        var statements = new List<string>();
        var current = new StringBuilder();

        foreach (var line in sql.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            // Skip DELIMITER directives (used for stored procs)
            if (trimmed.TrimStart().StartsWith("DELIMITER", StringComparison.OrdinalIgnoreCase))
                continue;

            current.AppendLine(trimmed);

            if (trimmed.TrimEnd().EndsWith(';'))
            {
                var statement = current.ToString().Trim().TrimEnd(';');
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    statements.Add(statement);
                }
                current.Clear();
            }
        }

        // Add any remaining content
        var remaining = current.ToString().Trim().TrimEnd(';');
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            statements.Add(remaining);
        }

        return statements;
    }
}
