using System.Text.RegularExpressions;
using DaisyReport.Api.DataProviders.Abstractions;

namespace DaisyReport.Api.DataProviders;

public static class JdbcUrlConverter
{
    /// <summary>
    /// Converts a JDBC URL to a native ADO.NET connection string.
    /// If the input is not a JDBC URL, it is returned as-is (assumed to be a native connection string).
    /// </summary>
    public static string Convert(string jdbcUrl, string? username = null, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(jdbcUrl))
            return string.Empty;

        var trimmed = jdbcUrl.Trim();

        // Not a JDBC URL -- pass through as native connection string
        if (!trimmed.StartsWith("jdbc:", StringComparison.OrdinalIgnoreCase))
        {
            return AppendCredentials(trimmed, username, password);
        }

        if (trimmed.StartsWith("jdbc:mysql://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("jdbc:mariadb://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertMySql(trimmed, username, password);
        }

        if (trimmed.StartsWith("jdbc:sqlserver://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertSqlServer(trimmed, username, password);
        }

        if (trimmed.StartsWith("jdbc:jtds:sqlserver://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertJtdsSqlServer(trimmed, username, password);
        }

        if (trimmed.StartsWith("jdbc:postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertPostgreSql(trimmed, username, password);
        }

        if (trimmed.StartsWith("jdbc:oracle:", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertOracle(trimmed, username, password);
        }

        if (trimmed.StartsWith("jdbc:sqlite:", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertSqlite(trimmed);
        }

        // Unknown JDBC prefix -- strip "jdbc:" and return best effort
        return trimmed[5..];
    }

    /// <summary>
    /// Detects the DataProviderType from a JDBC URL or driver class name.
    /// </summary>
    public static DataProviderType DetectProviderType(string? jdbcUrl, string? driverClass)
    {
        // Check driver class first (most reliable)
        if (!string.IsNullOrEmpty(driverClass))
        {
            var dc = driverClass.ToLowerInvariant();
            if (dc.Contains("mysql") || dc.Contains("mariadb")) return DataProviderType.MySql;
            if (dc.Contains("sqlserver") || dc.Contains("jtds")) return DataProviderType.SqlServer;
            if (dc.Contains("postgresql") || dc.Contains("postgres")) return DataProviderType.PostgreSql;
            if (dc.Contains("oracle")) return DataProviderType.Oracle;
            if (dc.Contains("sqlite")) return DataProviderType.Sqlite;
            if (dc.Contains("odbc")) return DataProviderType.Odbc;
            if (dc.Contains("oledb")) return DataProviderType.OleDb;
        }

        // Fall back to JDBC URL pattern
        if (!string.IsNullOrEmpty(jdbcUrl))
        {
            var url = jdbcUrl.ToLowerInvariant();
            if (url.StartsWith("jdbc:mysql:") || url.StartsWith("jdbc:mariadb:")) return DataProviderType.MySql;
            if (url.StartsWith("jdbc:sqlserver:") || url.StartsWith("jdbc:jtds:")) return DataProviderType.SqlServer;
            if (url.StartsWith("jdbc:postgresql:")) return DataProviderType.PostgreSql;
            if (url.StartsWith("jdbc:oracle:")) return DataProviderType.Oracle;
            if (url.StartsWith("jdbc:sqlite:")) return DataProviderType.Sqlite;
            if (url.StartsWith("jdbc:odbc:")) return DataProviderType.Odbc;

            // Native connection string heuristics
            if (url.Contains("server=") && url.Contains("port=") && url.Contains("database="))
                return DataProviderType.MySql; // Most common in this project
            if (url.Contains("data source=") && url.Contains("initial catalog="))
                return DataProviderType.SqlServer;
            if (url.Contains("host=") && url.Contains("database="))
                return DataProviderType.PostgreSql;
        }

        // Default to MySQL (the project's primary database)
        return DataProviderType.MySql;
    }

    private static string ConvertMySql(string jdbcUrl, string? username, string? password)
    {
        // jdbc:mysql://host:port/database?params
        var match = Regex.Match(jdbcUrl,
            @"jdbc:(?:mysql|mariadb)://([^:/]+)(?::(\d+))?(?:/([^?]*))?(?:\?(.+))?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return string.Empty;

        var host = match.Groups[1].Value;
        var port = match.Groups[2].Success ? match.Groups[2].Value : "3306";
        var database = match.Groups[3].Success ? match.Groups[3].Value : "";
        var queryParams = match.Groups[4].Success ? match.Groups[4].Value : "";

        var connStr = $"Server={host};Port={port};Database={database}";

        if (!string.IsNullOrEmpty(username))
            connStr += $";User={username}";
        if (!string.IsNullOrEmpty(password))
            connStr += $";Password={password}";

        // Convert common JDBC parameters
        connStr += ConvertJdbcParams(queryParams, DataProviderType.MySql);

        return connStr;
    }

    private static string ConvertSqlServer(string jdbcUrl, string? username, string? password)
    {
        // jdbc:sqlserver://host:port;databaseName=db;...
        var match = Regex.Match(jdbcUrl,
            @"jdbc:sqlserver://([^:;]+)(?::(\d+))?(?:;(.+))?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return string.Empty;

        var host = match.Groups[1].Value;
        var port = match.Groups[2].Success ? match.Groups[2].Value : "1433";
        var propsString = match.Groups[3].Success ? match.Groups[3].Value : "";

        var props = ParseSemicolonProperties(propsString);
        var database = props.GetValueOrDefault("databaseName") ??
                       props.GetValueOrDefault("database") ?? "";

        var connStr = $"Server={host},{port};Database={database}";

        if (!string.IsNullOrEmpty(username))
            connStr += $";User Id={username}";
        if (!string.IsNullOrEmpty(password))
            connStr += $";Password={password}";

        // Add TrustServerCertificate for dev convenience
        connStr += ";TrustServerCertificate=True";

        return connStr;
    }

    private static string ConvertJtdsSqlServer(string jdbcUrl, string? username, string? password)
    {
        // jdbc:jtds:sqlserver://host:port/database
        var match = Regex.Match(jdbcUrl,
            @"jdbc:jtds:sqlserver://([^:/]+)(?::(\d+))?(?:/([^;?]*))?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return string.Empty;

        var host = match.Groups[1].Value;
        var port = match.Groups[2].Success ? match.Groups[2].Value : "1433";
        var database = match.Groups[3].Success ? match.Groups[3].Value : "";

        var connStr = $"Server={host},{port};Database={database}";

        if (!string.IsNullOrEmpty(username))
            connStr += $";User Id={username}";
        if (!string.IsNullOrEmpty(password))
            connStr += $";Password={password}";

        connStr += ";TrustServerCertificate=True";

        return connStr;
    }

    private static string ConvertPostgreSql(string jdbcUrl, string? username, string? password)
    {
        // jdbc:postgresql://host:port/database?params
        var match = Regex.Match(jdbcUrl,
            @"jdbc:postgresql://([^:/]+)(?::(\d+))?(?:/([^?]*))?(?:\?(.+))?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return string.Empty;

        var host = match.Groups[1].Value;
        var port = match.Groups[2].Success ? match.Groups[2].Value : "5432";
        var database = match.Groups[3].Success ? match.Groups[3].Value : "";

        var connStr = $"Host={host};Port={port};Database={database}";

        if (!string.IsNullOrEmpty(username))
            connStr += $";Username={username}";
        if (!string.IsNullOrEmpty(password))
            connStr += $";Password={password}";

        return connStr;
    }

    private static string ConvertOracle(string jdbcUrl, string? username, string? password)
    {
        // jdbc:oracle:thin:@host:port:SID
        // jdbc:oracle:thin:@//host:port/serviceName
        // jdbc:oracle:thin:@(DESCRIPTION=...)
        var tnsMatch = Regex.Match(jdbcUrl, @"jdbc:oracle:thin:@(\(DESCRIPTION.+\))", RegexOptions.IgnoreCase);
        if (tnsMatch.Success)
        {
            var connStr = $"Data Source={tnsMatch.Groups[1].Value}";
            if (!string.IsNullOrEmpty(username))
                connStr += $";User Id={username}";
            if (!string.IsNullOrEmpty(password))
                connStr += $";Password={password}";
            return connStr;
        }

        var serviceMatch = Regex.Match(jdbcUrl,
            @"jdbc:oracle:thin:@//([^:/]+)(?::(\d+))?/(.+)",
            RegexOptions.IgnoreCase);

        if (serviceMatch.Success)
        {
            var host = serviceMatch.Groups[1].Value;
            var port = serviceMatch.Groups[2].Success ? serviceMatch.Groups[2].Value : "1521";
            var serviceName = serviceMatch.Groups[3].Value;

            var tns = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={serviceName})))";
            var connStr = $"Data Source={tns}";
            if (!string.IsNullOrEmpty(username))
                connStr += $";User Id={username}";
            if (!string.IsNullOrEmpty(password))
                connStr += $";Password={password}";
            return connStr;
        }

        var sidMatch = Regex.Match(jdbcUrl,
            @"jdbc:oracle:thin:@([^:/]+)(?::(\d+))?:(.+)",
            RegexOptions.IgnoreCase);

        if (sidMatch.Success)
        {
            var host = sidMatch.Groups[1].Value;
            var port = sidMatch.Groups[2].Success ? sidMatch.Groups[2].Value : "1521";
            var sid = sidMatch.Groups[3].Value;

            var tns = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SID={sid})))";
            var connStr = $"Data Source={tns}";
            if (!string.IsNullOrEmpty(username))
                connStr += $";User Id={username}";
            if (!string.IsNullOrEmpty(password))
                connStr += $";Password={password}";
            return connStr;
        }

        return string.Empty;
    }

    private static string ConvertSqlite(string jdbcUrl)
    {
        // jdbc:sqlite:path/to/database.db
        // jdbc:sqlite::memory:
        var path = jdbcUrl["jdbc:sqlite:".Length..];
        return $"Data Source={path}";
    }

    private static string AppendCredentials(string connStr, string? username, string? password)
    {
        // Only append if credentials are not already in the string
        var lower = connStr.ToLowerInvariant();

        if (!string.IsNullOrEmpty(username) &&
            !lower.Contains("user=") && !lower.Contains("user id=") &&
            !lower.Contains("uid=") && !lower.Contains("username="))
        {
            connStr += $";User={username}";
        }

        if (!string.IsNullOrEmpty(password) &&
            !lower.Contains("password=") && !lower.Contains("pwd="))
        {
            connStr += $";Password={password}";
        }

        return connStr;
    }

    private static Dictionary<string, string> ParseSemicolonProperties(string props)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(props)) return dict;

        foreach (var pair in props.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = pair[..eqIdx].Trim();
                var value = pair[(eqIdx + 1)..].Trim();
                dict[key] = value;
            }
        }

        return dict;
    }

    private static string ConvertJdbcParams(string queryParams, DataProviderType providerType)
    {
        if (string.IsNullOrEmpty(queryParams)) return string.Empty;

        var result = "";
        var pairs = queryParams.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = pair[..eqIdx].Trim().ToLowerInvariant();
            var value = pair[(eqIdx + 1)..].Trim();

            // Map common JDBC parameters to ADO.NET equivalents
            switch (key)
            {
                case "usessl" when providerType == DataProviderType.MySql:
                    result += $";SslMode={( value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Required" : "None")}";
                    break;
                case "allowpublickeyretrieval" when providerType == DataProviderType.MySql:
                    result += $";AllowPublicKeyRetrieval={value}";
                    break;
                case "servertimezone" when providerType == DataProviderType.MySql:
                    // Not directly applicable in MySqlConnector
                    break;
                case "connecttimeout":
                case "connect_timeout":
                    result += $";Connection Timeout={value}";
                    break;
                case "charset":
                case "characterencoding":
                    result += $";Character Set={value}";
                    break;
                // Skip unknown parameters
            }
        }

        return result;
    }
}
