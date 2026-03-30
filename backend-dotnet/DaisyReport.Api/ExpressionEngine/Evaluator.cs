using System.Globalization;
using DaisyReport.Api.ExpressionEngine.BuiltIns;

namespace DaisyReport.Api.ExpressionEngine;

public class EvaluationContext
{
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object> BuiltIns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a default context pre-populated with today, math, and sutils built-ins.
    /// </summary>
    public static EvaluationContext CreateDefault()
    {
        var ctx = new EvaluationContext();
        ctx.BuiltIns["today"] = new TodayObject();
        ctx.BuiltIns["math"] = new MathObject();
        ctx.BuiltIns["sutils"] = new StringUtils();
        return ctx;
    }
}

public class EvaluationException : Exception
{
    public int Position { get; }

    public EvaluationException(string message, int position = -1)
        : base(message)
    {
        Position = position;
    }
}

public class Evaluator
{
    private readonly EvaluationContext _context;
    private readonly StringUtils _stringUtils;

    public Evaluator(EvaluationContext? context = null)
    {
        _context = context ?? EvaluationContext.CreateDefault();
        _stringUtils = (_context.BuiltIns.TryGetValue("sutils", out var su) && su is StringUtils s)
            ? s
            : new StringUtils();
    }

    public object? Evaluate(AstNode node)
    {
        return node switch
        {
            NumberNode n => n.Value,
            StringNode s => s.Value,
            BooleanNode b => b.Value,
            NullNode => null,
            IdentifierNode id => ResolveIdentifier(id),
            BinaryNode bin => EvaluateBinary(bin),
            UnaryNode un => EvaluateUnary(un),
            TernaryNode tern => EvaluateTernary(tern),
            MethodCallNode mc => EvaluateMethodCall(mc),
            PropertyAccessNode pa => EvaluatePropertyAccess(pa),
            TemplateNode tmpl => EvaluateTemplate(tmpl),
            _ => throw new EvaluationException($"Unknown AST node type: {node.GetType().Name}", node.Position)
        };
    }

    private object? ResolveIdentifier(IdentifierNode id)
    {
        if (_context.BuiltIns.TryGetValue(id.Name, out var builtIn))
            return builtIn;

        if (_context.Variables.TryGetValue(id.Name, out var value))
            return value;

        throw new EvaluationException($"Undefined identifier '{id.Name}'", id.Position);
    }

    private object? EvaluateBinary(BinaryNode node)
    {
        // Short-circuit for logical operators
        if (node.Operator == "&&")
        {
            var left = Evaluate(node.Left);
            if (!IsTruthy(left)) return false;
            return IsTruthy(Evaluate(node.Right));
        }

        if (node.Operator == "||")
        {
            var left = Evaluate(node.Left);
            if (IsTruthy(left)) return true;
            return IsTruthy(Evaluate(node.Right));
        }

        var leftVal = Evaluate(node.Left);
        var rightVal = Evaluate(node.Right);

        return node.Operator switch
        {
            "+" => EvaluateAdd(leftVal, rightVal, node.Position),
            "-" => ToNumber(leftVal, node.Position) - ToNumber(rightVal, node.Position),
            "*" => ToNumber(leftVal, node.Position) * ToNumber(rightVal, node.Position),
            "/" => EvaluateDivide(leftVal, rightVal, node.Position),
            "%" => EvaluateModulo(leftVal, rightVal, node.Position),
            "==" => Equals(leftVal, rightVal),
            "!=" => !Equals(leftVal, rightVal),
            "<" => CompareValues(leftVal, rightVal, node.Position) < 0,
            "<=" => CompareValues(leftVal, rightVal, node.Position) <= 0,
            ">" => CompareValues(leftVal, rightVal, node.Position) > 0,
            ">=" => CompareValues(leftVal, rightVal, node.Position) >= 0,
            _ => throw new EvaluationException($"Unknown operator '{node.Operator}'", node.Position)
        };
    }

    private object? EvaluateAdd(object? left, object? right, int position)
    {
        // String concatenation when either side is a string
        if (left is string || right is string)
            return ToString(left) + ToString(right);

        return ToNumber(left, position) + ToNumber(right, position);
    }

    private object? EvaluateDivide(object? left, object? right, int position)
    {
        double divisor = ToNumber(right, position);
        if (divisor == 0)
            throw new EvaluationException("Division by zero", position);
        return ToNumber(left, position) / divisor;
    }

    private object? EvaluateModulo(object? left, object? right, int position)
    {
        double divisor = ToNumber(right, position);
        if (divisor == 0)
            throw new EvaluationException("Modulo by zero", position);
        return ToNumber(left, position) % divisor;
    }

    private new static bool Equals(object? left, object? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        // Same type comparison
        if (left is double ld && right is double rd)
            return Math.Abs(ld - rd) < 1e-10;

        if (left is string ls && right is string rs)
            return ls == rs;

        if (left is bool lb && right is bool rb)
            return lb == rb;

        if (left is DateTime ldt && right is DateTime rdt)
            return ldt == rdt;

        // Cross-type: try numeric comparison
        if (TryToNumber(left, out double ln) && TryToNumber(right, out double rn))
            return Math.Abs(ln - rn) < 1e-10;

        // Fallback string comparison
        return left.ToString() == right.ToString();
    }

    private static int CompareValues(object? left, object? right, int position)
    {
        if (left is null || right is null)
            throw new EvaluationException("Cannot compare null values", position);

        if (TryToNumber(left, out double ln) && TryToNumber(right, out double rn))
            return ln.CompareTo(rn);

        if (left is string ls && right is string rs)
            return string.Compare(ls, rs, StringComparison.Ordinal);

        if (left is DateTime ldt && right is DateTime rdt)
            return ldt.CompareTo(rdt);

        throw new EvaluationException(
            $"Cannot compare {left.GetType().Name} and {right.GetType().Name}", position);
    }

    private object? EvaluateUnary(UnaryNode node)
    {
        var operand = Evaluate(node.Operand);
        return node.Operator switch
        {
            "!" => !IsTruthy(operand),
            "-" => -ToNumber(operand, node.Position),
            _ => throw new EvaluationException($"Unknown unary operator '{node.Operator}'", node.Position)
        };
    }

    private object? EvaluateTernary(TernaryNode node)
    {
        var condition = Evaluate(node.Condition);
        return IsTruthy(condition) ? Evaluate(node.TrueExpr) : Evaluate(node.FalseExpr);
    }

    private object? EvaluateMethodCall(MethodCallNode node)
    {
        var target = Evaluate(node.Target);
        var args = node.Arguments.Select(Evaluate).ToList();

        // Built-in object method dispatch
        if (target is TodayObject today)
            return today.Invoke(node.MethodName, args);

        if (target is MathObject math)
            return math.Invoke(node.MethodName, args);

        if (target is StringUtils sutils)
        {
            // sutils.method(str, args...) — first arg is the string target
            if (args.Count == 0)
                throw new EvaluationException($"String utility method '{node.MethodName}' requires a string argument", node.Position);
            string strTarget = args[0]?.ToString() ?? "";
            return sutils.Invoke(node.MethodName, strTarget, args.Skip(1).ToList());
        }

        // String value method call (e.g., "hello".toUpperCase())
        if (target is string strVal)
            return _stringUtils.Invoke(node.MethodName, strVal, args);

        // DateTime method call on raw DateTime values
        if (target is DateTime dt)
            return new TodayObject(dt).Invoke(node.MethodName, args);

        // Dictionary property with method call
        if (target is Dictionary<string, object?> dict)
        {
            if (dict.TryGetValue(node.MethodName, out var val))
                return val;
            throw new EvaluationException($"Property '{node.MethodName}' not found in object", node.Position);
        }

        // NullNode target means direct function call — check BuiltIns
        if (node.Target is NullNode)
            throw new EvaluationException($"Undefined function '{node.MethodName}'", node.Position);

        throw new EvaluationException(
            $"Cannot call method '{node.MethodName}' on {target?.GetType().Name ?? "null"}", node.Position);
    }

    private object? EvaluatePropertyAccess(PropertyAccessNode node)
    {
        var target = Evaluate(node.Target);

        if (target is Dictionary<string, object?> dict)
        {
            if (dict.TryGetValue(node.PropertyName, out var val))
                return val;
            throw new EvaluationException($"Property '{node.PropertyName}' not found", node.Position);
        }

        if (target is TodayObject today)
            return today.Invoke(node.PropertyName, new List<object?>());

        throw new EvaluationException(
            $"Cannot access property '{node.PropertyName}' on {target?.GetType().Name ?? "null"}", node.Position);
    }

    private object? EvaluateTemplate(TemplateNode node)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in node.Parts)
        {
            var val = Evaluate(part);
            sb.Append(ToString(val));
        }
        return sb.ToString();
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true
    };

    private static double ToNumber(object? value, int position)
    {
        if (TryToNumber(value, out double result))
            return result;
        throw new EvaluationException($"Cannot convert {value?.GetType().Name ?? "null"} to number", position);
    }

    private static bool TryToNumber(object? value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case int i: result = i; return true;
            case float f: result = f; return true;
            case long l: result = l; return true;
            case bool b: result = b ? 1 : 0; return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                result = parsed;
                return true;
            case null:
                result = 0;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static string ToString(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        TodayObject t => t.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
