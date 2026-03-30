using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySqlConnector;

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
            CREATE TABLE IF NOT EXISTS _migration_log (
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
                "SELECT checksum AS Checksum, applied_at AS AppliedAt FROM _migration_log WHERE filename = @Filename",
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
                // Use raw MySqlCommand (not Dapper) to avoid @-parameter parsing issues
                var statements = SplitSqlStatements(sql);
                foreach (var statement in statements)
                {
                    if (!string.IsNullOrWhiteSpace(statement))
                    {
                        try
                        {
                            using var cmd = new MySqlCommand(statement, (MySqlConnection)connection);
                            await cmd.ExecuteNonQueryAsync();
                        }
                        catch (MySqlException ex) when (ex.Number is 1050 or 1062 or 1061 or 1304 or 1359 or 1060)
                        {
                            // 1050 = Table already exists, 1062 = Duplicate entry, 1061 = Duplicate key name
                            _logger.LogDebug("Skipping already-applied statement in {Filename}: {Message}", filename, ex.Message);
                        }
                    }
                }

                await connection.ExecuteAsync(
                    "INSERT INTO _migration_log (filename, checksum) VALUES (@Filename, @Checksum)",
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
        // Strip block comments /* ... */ (handles commented-out demo data)
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"/\*[\s\S]*?\*/", "", System.Text.RegularExpressions.RegexOptions.Compiled);

        var statements = new List<string>();
        var current = new StringBuilder();
        var delimiter = ";";

        foreach (var line in sql.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var trimmedUpper = trimmed.TrimStart();

            // Handle DELIMITER directives
            if (trimmedUpper.StartsWith("DELIMITER", StringComparison.OrdinalIgnoreCase))
            {
                var newDelim = trimmedUpper["DELIMITER".Length..].Trim();
                if (!string.IsNullOrEmpty(newDelim))
                    delimiter = newDelim;
                continue;
            }

            current.AppendLine(trimmed);

            if (trimmed.TrimEnd().EndsWith(delimiter, StringComparison.Ordinal))
            {
                var stmt = current.ToString().Trim();
                if (stmt.EndsWith(delimiter, StringComparison.Ordinal))
                    stmt = stmt[..^delimiter.Length].Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                    statements.Add(stmt);
                current.Clear();
            }
        }

        var remaining = current.ToString().Trim();
        if (remaining.EndsWith(";", StringComparison.Ordinal))
            remaining = remaining[..^1].Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
            statements.Add(remaining);

        return statements;
    }
}
