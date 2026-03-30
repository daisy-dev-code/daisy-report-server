using System.Globalization;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.ReportEngine;

public class ParameterResolver
{
    /// <summary>
    /// Resolve parameters using the chain: supplied -> default -> expression -> type coercion -> required check.
    /// </summary>
    public Task<Dictionary<string, object?>> ResolveAsync(
        List<ReportParameter> definitions,
        Dictionary<string, string> supplied)
    {
        var resolved = new Dictionary<string, object?>();

        foreach (var param in definitions)
        {
            object? value = null;

            // 1. Supplied value from request
            if (supplied.TryGetValue(param.Name, out var suppliedValue) && !string.IsNullOrEmpty(suppliedValue))
            {
                value = suppliedValue;
            }
            // 2. Default value from definition
            else if (!string.IsNullOrEmpty(param.DefaultValue))
            {
                var defaultVal = param.DefaultValue;

                // 3. Expression evaluation (${...} syntax)
                if (defaultVal.StartsWith("${") && defaultVal.EndsWith("}"))
                {
                    value = EvaluateExpression(defaultVal);
                }
                else
                {
                    value = defaultVal;
                }
            }

            // 4. Type coercion
            if (value != null)
            {
                value = CoerceType(value.ToString()!, param.Type);
            }

            // 5. Required check
            if (param.Mandatory && value == null)
            {
                throw new ArgumentException($"Required parameter '{param.Name}' was not supplied and has no default value.");
            }

            resolved[param.Name] = value;
        }

        return Task.FromResult(resolved);
    }

    private static object? EvaluateExpression(string expression)
    {
        var inner = expression[2..^1]; // strip ${ and }

        return inner.ToUpperInvariant() switch
        {
            "NOW" => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            "TODAY" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
            "CURRENT_YEAR" => DateTime.UtcNow.Year.ToString(),
            "CURRENT_MONTH" => DateTime.UtcNow.Month.ToString(),
            "YESTERDAY" => DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
            "FIRST_OF_MONTH" => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).ToString("yyyy-MM-dd"),
            "LAST_OF_MONTH" => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month,
                DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month)).ToString("yyyy-MM-dd"),
            _ => null // Unknown expression returns null
        };
    }

    private static object? CoerceType(string value, string paramType)
    {
        return paramType.ToLowerInvariant() switch
        {
            "int" or "integer" or "number" =>
                long.TryParse(value, out var intVal) ? intVal : throw new ArgumentException($"Cannot coerce '{value}' to integer."),
            "decimal" or "float" or "double" =>
                double.TryParse(value, CultureInfo.InvariantCulture, out var dblVal) ? dblVal : throw new ArgumentException($"Cannot coerce '{value}' to decimal."),
            "date" =>
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateVal) ? dateVal : throw new ArgumentException($"Cannot coerce '{value}' to date."),
            "bool" or "boolean" =>
                bool.TryParse(value, out var boolVal) ? boolVal : throw new ArgumentException($"Cannot coerce '{value}' to boolean."),
            _ => value // text and unknown types stay as string
        };
    }
}
