namespace DaisyReport.Api.ExpressionEngine;

public class ParseException : Exception
{
    public List<string> Errors { get; }

    public ParseException(List<string> errors)
        : base($"Parse failed with {errors.Count} error(s): {string.Join("; ", errors)}")
    {
        Errors = errors;
    }
}

public class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private readonly List<string> _errors = new();

    public IReadOnlyList<string> Errors => _errors;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _pos = 0;
    }

    public AstNode Parse()
    {
        var node = ParseTernary();

        if (Current.Type != TokenType.EOF && Current.Type != TokenType.TemplateEnd)
            AddError($"Unexpected token '{Current.Value}' at position {Current.Position}");

        if (_errors.Count > 0)
            throw new ParseException(new List<string>(_errors));

        return node;
    }

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

    private Token Advance()
    {
        var token = Current;
        if (_pos < _tokens.Count - 1)
            _pos++;
        return token;
    }

    private bool Match(TokenType type)
    {
        if (Current.Type == type)
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Expect(TokenType type, string? errorMessage = null)
    {
        if (Current.Type == type)
            return Advance();

        AddError(errorMessage ?? $"Expected {type} but found {Current.Type} ('{Current.Value}') at position {Current.Position}");
        return Current;
    }

    private void AddError(string message)
    {
        if (_errors.Count < 10)
            _errors.Add(message);
    }

    // Precedence 1: Ternary (right associative)
    private AstNode ParseTernary()
    {
        var node = ParseOr();

        if (Current.Type == TokenType.QuestionMark)
        {
            int pos = Current.Position;
            Advance();
            var trueExpr = ParseTernary(); // right associative
            Expect(TokenType.Colon, $"Expected ':' in ternary expression at position {Current.Position}");
            var falseExpr = ParseTernary(); // right associative
            return new TernaryNode
            {
                Condition = node,
                TrueExpr = trueExpr,
                FalseExpr = falseExpr,
                Position = pos
            };
        }

        return node;
    }

    // Precedence 2: Or (||)
    private AstNode ParseOr()
    {
        var left = ParseAnd();

        while (Current.Type == TokenType.Or)
        {
            int pos = Current.Position;
            Advance();
            var right = ParseAnd();
            left = new BinaryNode { Left = left, Operator = "||", Right = right, Position = pos };
        }

        return left;
    }

    // Precedence 3: And (&&)
    private AstNode ParseAnd()
    {
        var left = ParseEquality();

        while (Current.Type == TokenType.And)
        {
            int pos = Current.Position;
            Advance();
            var right = ParseEquality();
            left = new BinaryNode { Left = left, Operator = "&&", Right = right, Position = pos };
        }

        return left;
    }

    // Precedence 4: Equality (==, !=)
    private AstNode ParseEquality()
    {
        var left = ParseComparison();

        while (Current.Type is TokenType.Equal or TokenType.NotEqual)
        {
            int pos = Current.Position;
            string op = Advance().Value;
            var right = ParseComparison();
            left = new BinaryNode { Left = left, Operator = op, Right = right, Position = pos };
        }

        return left;
    }

    // Precedence 5: Comparison (<, <=, >, >=)
    private AstNode ParseComparison()
    {
        var left = ParseAddition();

        while (Current.Type is TokenType.LessThan or TokenType.LessEqual or TokenType.GreaterThan or TokenType.GreaterEqual)
        {
            int pos = Current.Position;
            string op = Advance().Value;
            var right = ParseAddition();
            left = new BinaryNode { Left = left, Operator = op, Right = right, Position = pos };
        }

        return left;
    }

    // Precedence 6: Addition (+, -)
    private AstNode ParseAddition()
    {
        var left = ParseMultiplication();

        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            int pos = Current.Position;
            string op = Advance().Value;
            var right = ParseMultiplication();
            left = new BinaryNode { Left = left, Operator = op, Right = right, Position = pos };
        }

        return left;
    }

    // Precedence 7: Multiplication (*, /, %)
    private AstNode ParseMultiplication()
    {
        var left = ParseUnary();

        while (Current.Type is TokenType.Star or TokenType.Slash or TokenType.Percent)
        {
            int pos = Current.Position;
            string op = Advance().Value;
            var right = ParseUnary();
            left = new BinaryNode { Left = left, Operator = op, Right = right, Position = pos };
        }

        return left;
    }

    // Precedence 8: Unary (!, -)
    private AstNode ParseUnary()
    {
        if (Current.Type == TokenType.Not)
        {
            int pos = Current.Position;
            Advance();
            var operand = ParseUnary();
            return new UnaryNode { Operator = "!", Operand = operand, Position = pos };
        }

        if (Current.Type == TokenType.Minus)
        {
            int pos = Current.Position;
            Advance();
            var operand = ParseUnary();
            return new UnaryNode { Operator = "-", Operand = operand, Position = pos };
        }

        return ParsePostfix();
    }

    // Precedence 9: Postfix — method call and property access chains
    private AstNode ParsePostfix()
    {
        var node = ParsePrimary();

        while (true)
        {
            if (Current.Type == TokenType.Dot)
            {
                Advance();
                var nameToken = Expect(TokenType.Identifier, $"Expected property/method name after '.' at position {Current.Position}");
                string name = nameToken.Value;
                int pos = nameToken.Position;

                if (Current.Type == TokenType.LeftParen)
                {
                    Advance();
                    var args = ParseArguments();
                    Expect(TokenType.RightParen, $"Expected ')' after arguments at position {Current.Position}");
                    node = new MethodCallNode
                    {
                        Target = node,
                        MethodName = name,
                        Arguments = args,
                        Position = pos
                    };
                }
                else
                {
                    node = new PropertyAccessNode
                    {
                        Target = node,
                        PropertyName = name,
                        Position = pos
                    };
                }
            }
            else if (Current.Type == TokenType.LeftParen && node is IdentifierNode)
            {
                // Direct function call: funcName(args)
                int pos = node.Position;
                Advance();
                var args = ParseArguments();
                Expect(TokenType.RightParen, $"Expected ')' after arguments at position {Current.Position}");
                node = new MethodCallNode
                {
                    Target = new NullNode { Position = pos },
                    MethodName = ((IdentifierNode)node).Name,
                    Arguments = args,
                    Position = pos
                };
            }
            else
            {
                break;
            }
        }

        return node;
    }

    // Precedence 10: Primary
    private AstNode ParsePrimary()
    {
        var token = Current;

        switch (token.Type)
        {
            case TokenType.NumberLiteral:
                Advance();
                if (double.TryParse(token.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double numVal))
                    return new NumberNode { Value = numVal, Position = token.Position };
                AddError($"Invalid number '{token.Value}' at position {token.Position}");
                return new NumberNode { Value = 0, Position = token.Position };

            case TokenType.StringLiteral:
                Advance();
                return new StringNode { Value = token.Value, Position = token.Position };

            case TokenType.BooleanLiteral:
                Advance();
                return new BooleanNode { Value = token.Value == "true", Position = token.Position };

            case TokenType.NullLiteral:
                Advance();
                return new NullNode { Position = token.Position };

            case TokenType.Identifier:
                Advance();
                return new IdentifierNode { Name = token.Value, Position = token.Position };

            case TokenType.LeftParen:
                Advance();
                var expr = ParseTernary();
                Expect(TokenType.RightParen, $"Expected ')' at position {Current.Position}");
                return expr;

            default:
                AddError($"Unexpected token '{token.Value}' ({token.Type}) at position {token.Position}");
                Advance();
                return new NullNode { Position = token.Position };
        }
    }

    private List<AstNode> ParseArguments()
    {
        var args = new List<AstNode>();

        if (Current.Type == TokenType.RightParen)
            return args;

        args.Add(ParseTernary());

        while (Current.Type == TokenType.Comma)
        {
            Advance();
            args.Add(ParseTernary());
        }

        return args;
    }
}
