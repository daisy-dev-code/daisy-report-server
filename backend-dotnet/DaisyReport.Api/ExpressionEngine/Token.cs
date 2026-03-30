namespace DaisyReport.Api.ExpressionEngine;

public enum TokenType
{
    // Literals
    NumberLiteral,
    StringLiteral,
    BooleanLiteral,
    NullLiteral,

    // Identifiers & keywords
    Identifier,
    Dot,

    // Operators
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Equal,
    NotEqual,
    LessThan,
    LessEqual,
    GreaterThan,
    GreaterEqual,
    And,
    Or,
    Not,

    // Delimiters
    LeftParen,
    RightParen,
    Comma,
    QuestionMark,
    Colon,

    // Special
    TemplateStart, // ${
    TemplateEnd,   // }
    EOF
}

public class Token
{
    public TokenType Type { get; set; }
    public string Value { get; set; } = "";
    public int Position { get; set; }
    public int Line { get; set; }

    public override string ToString() => $"Token({Type}, \"{Value}\", pos={Position}, line={Line})";
}
