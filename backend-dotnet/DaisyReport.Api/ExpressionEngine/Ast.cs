namespace DaisyReport.Api.ExpressionEngine;

public abstract class AstNode
{
    public int Position { get; set; }
}

public class NumberNode : AstNode
{
    public double Value { get; set; }
}

public class StringNode : AstNode
{
    public string Value { get; set; } = "";
}

public class BooleanNode : AstNode
{
    public bool Value { get; set; }
}

public class NullNode : AstNode { }

public class IdentifierNode : AstNode
{
    public string Name { get; set; } = "";
}

public class BinaryNode : AstNode
{
    public AstNode Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public AstNode Right { get; set; } = null!;
}

public class UnaryNode : AstNode
{
    public string Operator { get; set; } = "";
    public AstNode Operand { get; set; } = null!;
}

public class TernaryNode : AstNode
{
    public AstNode Condition { get; set; } = null!;
    public AstNode TrueExpr { get; set; } = null!;
    public AstNode FalseExpr { get; set; } = null!;
}

public class MethodCallNode : AstNode
{
    public AstNode Target { get; set; } = null!;
    public string MethodName { get; set; } = "";
    public List<AstNode> Arguments { get; set; } = new();
}

public class PropertyAccessNode : AstNode
{
    public AstNode Target { get; set; } = null!;
    public string PropertyName { get; set; } = "";
}

public class TemplateNode : AstNode
{
    public List<AstNode> Parts { get; set; } = new();
}
