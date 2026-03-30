namespace DaisyReport.Api.ExpressionEngine.BuiltIns;

/// <summary>
/// Provides the "sutils" built-in object with string manipulation functions.
/// Methods can be called on string values via method chaining or directly.
/// </summary>
public class StringUtils
{
    public object? Invoke(string method, string target, List<object?> args)
    {
        return method.ToLowerInvariant() switch
        {
            "left" => Left(target, RequireInt(args, 0, method)),
            "right" => Right(target, RequireInt(args, 0, method)),
            "length" => (double)target.Length,
            "trim" => target.Trim(),
            "touppercase" => target.ToUpperInvariant(),
            "tolowercase" => target.ToLowerInvariant(),
            "replace" => target.Replace(RequireString(args, 0, method), RequireString(args, 1, method)),
            "contains" => target.Contains(RequireString(args, 0, method), StringComparison.Ordinal),
            "indexof" => (double)target.IndexOf(RequireString(args, 0, method), StringComparison.Ordinal),
            "substring" => Substring(target, RequireInt(args, 0, method), args.Count >= 2 ? RequireIntNullable(args, 1) : null),
            "startswith" => target.StartsWith(RequireString(args, 0, method), StringComparison.Ordinal),
            "endswith" => target.EndsWith(RequireString(args, 0, method), StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"Unknown string method '{method}'")
        };
    }

    private static string Left(string s, int n)
    {
        if (n < 0) n = 0;
        return n >= s.Length ? s : s[..n];
    }

    private static string Right(string s, int n)
    {
        if (n < 0) n = 0;
        return n >= s.Length ? s : s[^n..];
    }

    private static string Substring(string s, int start, int? end)
    {
        if (start < 0) start = 0;
        if (start >= s.Length) return "";

        if (end.HasValue)
        {
            int endIdx = Math.Min(end.Value, s.Length);
            if (endIdx <= start) return "";
            return s[start..endIdx];
        }

        return s[start..];
    }

    private static int RequireInt(List<object?> args, int index, string method)
    {
        if (args.Count <= index || args[index] is null)
            throw new InvalidOperationException($"String method '{method}' requires argument at position {index}");

        return Convert.ToInt32(ToDouble(args[index]!));
    }

    private static int? RequireIntNullable(List<object?> args, int index)
    {
        if (args.Count <= index || args[index] is null)
            return null;
        return Convert.ToInt32(ToDouble(args[index]!));
    }

    private static string RequireString(List<object?> args, int index, string method)
    {
        if (args.Count <= index || args[index] is null)
            throw new InvalidOperationException($"String method '{method}' requires a string argument at position {index}");

        return args[index]!.ToString()!;
    }

    private static double ToDouble(object value) => value switch
    {
        double d => d,
        int i => i,
        float f => f,
        long l => l,
        _ => Convert.ToDouble(value)
    };
}
