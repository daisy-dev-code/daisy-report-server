using System.Globalization;
using System.Text;

namespace DaisyReport.Api.ExpressionEngine;

/// <summary>
/// Walks the AST and produces a MySQL-compatible SQL string with bind parameters
/// instead of evaluating values in memory.
/// </summary>
public class SqlCompiler
{
    private readonly List<object> _parameters = new();
    private string _columnName = "";
    private string _tableName = "";

    public (string Sql, List<object> Parameters) Compile(AstNode node, string columnName, string tableName)
    {
        _parameters.Clear();
        _columnName = columnName;
        _tableName = tableName;

        string sql = Visit(node);
        return (sql, new List<object>(_parameters));
    }

    private string Visit(AstNode node)
    {
        return node switch
        {
            NumberNode n => VisitNumber(n),
            StringNode s => VisitString(s),
            BooleanNode b => VisitBoolean(b),
            NullNode => "NULL",
            IdentifierNode id => VisitIdentifier(id),
            BinaryNode bin => VisitBinary(bin),
            UnaryNode un => VisitUnary(un),
            TernaryNode tern => VisitTernary(tern),
            MethodCallNode mc => VisitMethodCall(mc),
            PropertyAccessNode pa => VisitPropertyAccess(pa),
            _ => throw new InvalidOperationException($"Cannot compile {node.GetType().Name} to SQL")
        };
    }

    private string VisitNumber(NumberNode node)
    {
        _parameters.Add(node.Value);
        return "?";
    }

    private string VisitString(StringNode node)
    {
        _parameters.Add(node.Value);
        return "?";
    }

    private string VisitBoolean(BooleanNode node)
    {
        return node.Value ? "TRUE" : "FALSE";
    }

    private string VisitIdentifier(IdentifierNode node)
    {
        // Identifiers become column references with backtick quoting
        return $"`{EscapeBacktick(node.Name)}`";
    }

    private string VisitBinary(BinaryNode node)
    {
        string left = Visit(node.Left);
        string right = Visit(node.Right);
        string op = MapOperator(node.Operator);
        return $"({left} {op} {right})";
    }

    private string VisitUnary(UnaryNode node)
    {
        string operand = Visit(node.Operand);
        return node.Operator switch
        {
            "!" => $"(NOT {operand})",
            "-" => $"(-{operand})",
            _ => throw new InvalidOperationException($"Unknown unary operator '{node.Operator}'")
        };
    }

    private string VisitTernary(TernaryNode node)
    {
        string condition = Visit(node.Condition);
        string trueExpr = Visit(node.TrueExpr);
        string falseExpr = Visit(node.FalseExpr);
        return $"(CASE WHEN {condition} THEN {trueExpr} ELSE {falseExpr} END)";
    }

    private string VisitMethodCall(MethodCallNode node)
    {
        // Check for today.* methods
        if (node.Target is IdentifierNode targetId && targetId.Name.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return CompileTodayMethod(node.MethodName, node.Arguments);
        }

        // Check for chained today methods (e.g. today.addDays(1).format("..."))
        if (node.Target is MethodCallNode innerCall &&
            innerCall.Target is IdentifierNode innerId &&
            innerId.Name.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            string innerSql = VisitMethodCall(innerCall);
            return CompileDateChainedMethod(innerSql, node.MethodName, node.Arguments);
        }

        // Aggregate functions: identifier.avg(), identifier.count(), etc.
        if (node.Target is IdentifierNode aggTarget)
        {
            string col = $"`{EscapeBacktick(aggTarget.Name)}`";
            return node.MethodName.ToLowerInvariant() switch
            {
                "avg" => $"(SELECT AVG({col}) FROM `{EscapeBacktick(_tableName)}`)",
                "count" => $"(SELECT COUNT({col}) FROM `{EscapeBacktick(_tableName)}`)",
                "sum" => $"(SELECT SUM({col}) FROM `{EscapeBacktick(_tableName)}`)",
                "min" => $"(SELECT MIN({col}) FROM `{EscapeBacktick(_tableName)}`)",
                "max" => $"(SELECT MAX({col}) FROM `{EscapeBacktick(_tableName)}`)",
                _ => throw new InvalidOperationException($"Unknown SQL method '{node.MethodName}' on column '{aggTarget.Name}'")
            };
        }

        throw new InvalidOperationException($"Cannot compile method call '{node.MethodName}' to SQL");
    }

    private string VisitPropertyAccess(PropertyAccessNode node)
    {
        if (node.Target is IdentifierNode id)
        {
            // Object.Property → `Object`.`Property` or just `Property` if it's a known context
            return $"`{EscapeBacktick(id.Name)}`.`{EscapeBacktick(node.PropertyName)}`";
        }

        string target = Visit(node.Target);
        return $"{target}.`{EscapeBacktick(node.PropertyName)}`";
    }

    private string CompileTodayMethod(string method, List<AstNode> arguments)
    {
        return method.ToLowerInvariant() switch
        {
            "adddays" => $"DATE_ADD(CURDATE(), INTERVAL {VisitArg(arguments, 0)} DAY)",
            "addmonths" => $"DATE_ADD(CURDATE(), INTERVAL {VisitArg(arguments, 0)} MONTH)",
            "addyears" => $"DATE_ADD(CURDATE(), INTERVAL {VisitArg(arguments, 0)} YEAR)",
            "addhours" => $"DATE_ADD(NOW(), INTERVAL {VisitArg(arguments, 0)} HOUR)",
            "addminutes" => $"DATE_ADD(NOW(), INTERVAL {VisitArg(arguments, 0)} MINUTE)",
            "addseconds" => $"DATE_ADD(NOW(), INTERVAL {VisitArg(arguments, 0)} SECOND)",
            "firstday" => "DATE_FORMAT(CURDATE(), '%Y-%m-01')",
            "lastday" => "LAST_DAY(CURDATE())",
            "cleartime" => "CURDATE()",
            "format" => arguments.Count > 0 && arguments[0] is StringNode fmt
                ? $"DATE_FORMAT(CURDATE(), {VisitArg(arguments, 0)})"
                : "DATE_FORMAT(CURDATE(), '%Y-%m-%d')",
            "getday" => "DAY(CURDATE())",
            "getmonth" => "MONTH(CURDATE())",
            "getyear" => "YEAR(CURDATE())",
            "setday" => $"DATE_FORMAT(CURDATE(), CONCAT('%Y-%m-', LPAD({VisitArg(arguments, 0)}, 2, '0')))",
            "setmonth" => $"DATE_FORMAT(CURDATE(), CONCAT('%Y-', LPAD({VisitArg(arguments, 0)}, 2, '0'), '-%d'))",
            "setyear" => $"DATE_FORMAT(CURDATE(), CONCAT({VisitArg(arguments, 0)}, '-%m-%d'))",
            "sethours" => $"TIMESTAMP(CURDATE(), MAKETIME({VisitArg(arguments, 0)}, 0, 0))",
            "setminutes" => $"TIMESTAMP(CURDATE(), MAKETIME(0, {VisitArg(arguments, 0)}, 0))",
            "setseconds" => $"TIMESTAMP(CURDATE(), MAKETIME(0, 0, {VisitArg(arguments, 0)}))",
            _ => throw new InvalidOperationException($"Cannot compile 'today.{method}' to SQL")
        };
    }

    private string CompileDateChainedMethod(string dateSql, string method, List<AstNode> arguments)
    {
        return method.ToLowerInvariant() switch
        {
            "adddays" => $"DATE_ADD({dateSql}, INTERVAL {VisitArg(arguments, 0)} DAY)",
            "addmonths" => $"DATE_ADD({dateSql}, INTERVAL {VisitArg(arguments, 0)} MONTH)",
            "addyears" => $"DATE_ADD({dateSql}, INTERVAL {VisitArg(arguments, 0)} YEAR)",
            "format" => $"DATE_FORMAT({dateSql}, {VisitArg(arguments, 0)})",
            "firstday" => $"DATE_FORMAT({dateSql}, '%Y-%m-01')",
            "lastday" => $"LAST_DAY({dateSql})",
            "getday" => $"DAY({dateSql})",
            "getmonth" => $"MONTH({dateSql})",
            "getyear" => $"YEAR({dateSql})",
            _ => throw new InvalidOperationException($"Cannot compile chained '{method}' to SQL")
        };
    }

    private string VisitArg(List<AstNode> args, int index)
    {
        if (args.Count <= index)
            throw new InvalidOperationException($"Missing argument at position {index}");
        return Visit(args[index]);
    }

    private static string MapOperator(string op) => op switch
    {
        "==" => "=",
        "!=" => "<>",
        "&&" => "AND",
        "||" => "OR",
        "+" => "+",
        "-" => "-",
        "*" => "*",
        "/" => "/",
        "%" => "%",
        "<" => "<",
        "<=" => "<=",
        ">" => ">",
        ">=" => ">=",
        _ => throw new InvalidOperationException($"Unknown operator '{op}' for SQL compilation")
    };

    private static string EscapeBacktick(string name) => name.Replace("`", "``");
}
