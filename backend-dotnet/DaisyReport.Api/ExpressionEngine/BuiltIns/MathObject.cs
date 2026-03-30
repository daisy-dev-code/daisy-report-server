namespace DaisyReport.Api.ExpressionEngine.BuiltIns;

/// <summary>
/// Provides the "math" built-in object with standard mathematical functions.
/// All numeric values are handled as doubles.
/// </summary>
public class MathObject
{
    private static readonly Random _random = new();
    private static readonly object _randomLock = new();

    public object? Invoke(string method, List<object?> args)
    {
        return method.ToLowerInvariant() switch
        {
            "abs" => Math.Abs(RequireDouble(args, 0, method)),
            "ceil" => Math.Ceiling(RequireDouble(args, 0, method)),
            "floor" => Math.Floor(RequireDouble(args, 0, method)),
            "round" => args.Count >= 2
                ? Math.Round(RequireDouble(args, 0, method), Convert.ToInt32(RequireDouble(args, 1, method)))
                : Math.Round(RequireDouble(args, 0, method)),
            "sqrt" => Math.Sqrt(RequireDouble(args, 0, method)),
            "sin" => Math.Sin(RequireDouble(args, 0, method)),
            "cos" => Math.Cos(RequireDouble(args, 0, method)),
            "tan" => Math.Tan(RequireDouble(args, 0, method)),
            "log" => Math.Log(RequireDouble(args, 0, method)),
            "exp" => Math.Exp(RequireDouble(args, 0, method)),
            "pow" => Math.Pow(RequireDouble(args, 0, method), RequireDouble(args, 1, method)),
            "random" => RandomValue(),
            "min" => Math.Min(RequireDouble(args, 0, method), RequireDouble(args, 1, method)),
            "max" => Math.Max(RequireDouble(args, 0, method), RequireDouble(args, 1, method)),
            "mod" => RequireDouble(args, 0, method) % RequireDouble(args, 1, method),
            _ => throw new InvalidOperationException($"Unknown method 'math.{method}'")
        };
    }

    private static double RandomValue()
    {
        lock (_randomLock)
        {
            return _random.NextDouble();
        }
    }

    private static double RequireDouble(List<object?> args, int index, string method)
    {
        if (args.Count <= index || args[index] is null)
            throw new InvalidOperationException($"Method 'math.{method}' requires argument at position {index}");

        return args[index] switch
        {
            double d => d,
            int i => i,
            float f => f,
            long l => l,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => Convert.ToDouble(args[index])
        };
    }
}
