using System.Text.RegularExpressions;

namespace DaisyReport.Api.DynamicList;

/// <summary>
/// Validates and compiles computed column expressions into safe SQL.
/// Prevents SQL injection by whitelisting allowed functions and blocking DML keywords.
/// </summary>
public partial class ComputedColumnEngine
{
    // ── Forbidden patterns (DML / DDL) ─────────────────────────────────────────

    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "EXEC", "EXECUTE", "GRANT", "REVOKE", "CALL", "LOAD", "INTO",
        "INFORMATION_SCHEMA", "SLEEP", "BENCHMARK", "OUTFILE", "DUMPFILE"
    };

    // ── Allowed SQL functions ──────────────────────────────────────────────────

    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Control flow
        "CASE", "WHEN", "THEN", "ELSE", "END", "IF", "IFNULL",
        // Null handling
        "COALESCE", "NULLIF", "ISNULL",
        // Type conversion
        "CAST", "CONVERT",
        // String functions
        "CONCAT", "CONCAT_WS", "SUBSTRING", "SUBSTR", "LEFT", "RIGHT",
        "TRIM", "LTRIM", "RTRIM", "UPPER", "LOWER", "REPLACE", "REVERSE",
        "LENGTH", "CHAR_LENGTH", "LPAD", "RPAD", "LOCATE", "INSTR",
        "FORMAT",
        // Math functions
        "ABS", "CEIL", "CEILING", "FLOOR", "ROUND", "TRUNCATE",
        "MOD", "POWER", "POW", "SQRT", "LOG", "LOG2", "LOG10",
        "SIGN", "PI", "RAND",
        // Date functions
        "NOW", "CURDATE", "CURTIME", "DATE", "TIME", "YEAR", "MONTH", "DAY",
        "HOUR", "MINUTE", "SECOND", "DAYOFWEEK", "DAYOFYEAR", "WEEK",
        "DATE_FORMAT", "DATE_ADD", "DATE_SUB", "DATEDIFF", "TIMESTAMPDIFF",
        "STR_TO_DATE",
        // Aggregate (for use in computed columns within aggregation context)
        "SUM", "AVG", "COUNT", "MIN", "MAX"
    };

    private static readonly Regex IdentifierRegex = ValidIdentifier();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex ValidIdentifier();

    private static readonly Regex FunctionCallRegex = FunctionCall();

    [GeneratedRegex(@"\b([a-zA-Z_][a-zA-Z0-9_]*)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionCall();

    /// <summary>
    /// Validates and compiles a computed column expression.
    /// Resolves column references via alias mapping and validates against injection.
    /// </summary>
    /// <param name="expression">The user-provided SQL expression.</param>
    /// <param name="columnAliases">Map of alias -> actual column name for reference resolution.</param>
    /// <returns>A safe SQL expression string.</returns>
    public string CompileColumn(string expression, Dictionary<string, string> columnAliases)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Computed column expression cannot be empty.");

        // Check for forbidden keywords
        ValidateNoForbiddenKeywords(expression);

        // Validate all function calls are on the whitelist
        ValidateFunctionCalls(expression);

        // Check for dangerous patterns
        ValidateNoDangerousPatterns(expression);

        // Resolve column references: replace {column_alias} with _rs_base.`column_name`
        var resolved = ResolveColumnReferences(expression, columnAliases);

        return resolved;
    }

    // ── Validation ─────────────────────────────────────────────────────────────

    private static void ValidateNoForbiddenKeywords(string expression)
    {
        // Tokenize by splitting on non-word chars and check each token
        var tokens = Regex.Split(expression, @"\W+")
            .Where(t => !string.IsNullOrEmpty(t));

        foreach (var token in tokens)
        {
            if (ForbiddenKeywords.Contains(token))
            {
                throw new InvalidOperationException(
                    $"Forbidden keyword '{token}' in computed column expression.");
            }
        }
    }

    private static void ValidateFunctionCalls(string expression)
    {
        var matches = FunctionCallRegex.Matches(expression);
        foreach (Match match in matches)
        {
            var funcName = match.Groups[1].Value;

            // Skip if it's a known keyword used in CASE expressions etc.
            if (string.Equals(funcName, "AS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "AND", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "OR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "NOT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "IN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "IS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "BETWEEN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(funcName, "LIKE", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!AllowedFunctions.Contains(funcName))
            {
                throw new InvalidOperationException(
                    $"Function '{funcName}' is not allowed in computed column expressions.");
            }
        }
    }

    private static void ValidateNoDangerousPatterns(string expression)
    {
        // Block comment injection
        if (expression.Contains("--") || expression.Contains("/*") || expression.Contains("*/"))
            throw new InvalidOperationException("SQL comments are not allowed in computed column expressions.");

        // Block semicolons (statement termination)
        if (expression.Contains(';'))
            throw new InvalidOperationException("Semicolons are not allowed in computed column expressions.");

        // Block subqueries
        if (Regex.IsMatch(expression, @"\bSELECT\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("SELECT subqueries are not allowed in computed column expressions.");
    }

    // ── Column Reference Resolution ────────────────────────────────────────────

    private static string ResolveColumnReferences(string expression, Dictionary<string, string> columnAliases)
    {
        // Replace {alias} with _rs_base.`column_name`
        var resolved = Regex.Replace(expression, @"\{(\w+)\}", match =>
        {
            var alias = match.Groups[1].Value;

            if (columnAliases.TryGetValue(alias, out var colName))
                return $"_rs_base.`{colName}`";

            // If not found in aliases, treat as a direct column name
            return $"_rs_base.`{alias}`";
        });

        return resolved;
    }
}
