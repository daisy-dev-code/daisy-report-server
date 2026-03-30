namespace DaisyReport.Api.ExpressionEngine;

public class TokenizerException : Exception
{
    public int Position { get; }
    public int Line { get; }

    public TokenizerException(string message, int position, int line)
        : base($"Tokenizer error at line {line}, position {position}: {message}")
    {
        Position = position;
        Line = line;
    }
}

public class Tokenizer
{
    private readonly string _source;
    private int _pos;
    private int _line;
    private readonly List<Token> _tokens = new();
    private readonly List<string> _errors = new();

    public IReadOnlyList<string> Errors => _errors;

    public Tokenizer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pos = 0;
        _line = 1;
    }

    public List<Token> Tokenize()
    {
        _tokens.Clear();
        _errors.Clear();
        _pos = 0;
        _line = 1;

        while (_pos < _source.Length)
        {
            if (_errors.Count >= 10)
                break;

            char c = Current;

            if (c == '\n')
            {
                _line++;
                _pos++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                _pos++;
                continue;
            }

            if (c == '$' && Peek(1) == '{')
            {
                _tokens.Add(MakeToken(TokenType.TemplateStart, "${", 2));
                continue;
            }

            if (c == '}')
            {
                _tokens.Add(MakeToken(TokenType.TemplateEnd, "}", 1));
                continue;
            }

            if (c == '\'' || c == '"')
            {
                ReadString(c);
                continue;
            }

            if (char.IsDigit(c))
            {
                ReadNumber();
                continue;
            }

            if (IsIdentStart(c))
            {
                ReadIdentifier();
                continue;
            }

            if (TryReadOperatorOrDelimiter())
                continue;

            AddError($"Unexpected character '{c}'");
            _pos++;
        }

        _tokens.Add(new Token { Type = TokenType.EOF, Value = "", Position = _pos, Line = _line });
        return _tokens;
    }

    private char Current => _pos < _source.Length ? _source[_pos] : '\0';

    private char Peek(int offset)
    {
        int idx = _pos + offset;
        return idx < _source.Length ? _source[idx] : '\0';
    }

    private Token MakeToken(TokenType type, string value, int length)
    {
        var token = new Token { Type = type, Value = value, Position = _pos, Line = _line };
        _pos += length;
        return token;
    }

    private void ReadString(char quote)
    {
        int start = _pos;
        int startLine = _line;
        _pos++; // skip opening quote

        var sb = new System.Text.StringBuilder();
        while (_pos < _source.Length)
        {
            char c = Current;
            if (c == '\\' && _pos + 1 < _source.Length)
            {
                char next = _source[_pos + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); _pos += 2; continue;
                    case 't': sb.Append('\t'); _pos += 2; continue;
                    case 'r': sb.Append('\r'); _pos += 2; continue;
                    case '\\': sb.Append('\\'); _pos += 2; continue;
                    case '\'': sb.Append('\''); _pos += 2; continue;
                    case '"': sb.Append('"'); _pos += 2; continue;
                    case '$': sb.Append('$'); _pos += 2; continue;
                    default: sb.Append('\\'); sb.Append(next); _pos += 2; continue;
                }
            }

            if (c == '\n') _line++;

            if (c == quote)
            {
                _pos++; // skip closing quote
                _tokens.Add(new Token
                {
                    Type = TokenType.StringLiteral,
                    Value = sb.ToString(),
                    Position = start,
                    Line = startLine
                });
                return;
            }

            sb.Append(c);
            _pos++;
        }

        AddError("Unterminated string literal");
        _tokens.Add(new Token
        {
            Type = TokenType.StringLiteral,
            Value = sb.ToString(),
            Position = start,
            Line = startLine
        });
    }

    private void ReadNumber()
    {
        int start = _pos;
        bool hasDot = false;

        while (_pos < _source.Length && (char.IsDigit(Current) || Current == '.'))
        {
            if (Current == '.')
            {
                if (hasDot) break;
                // check next char is digit to distinguish from property access
                if (_pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1]))
                    hasDot = true;
                else
                    break;
            }
            _pos++;
        }

        string value = _source[start.._pos];
        _tokens.Add(new Token
        {
            Type = TokenType.NumberLiteral,
            Value = value,
            Position = start,
            Line = _line
        });
    }

    private void ReadIdentifier()
    {
        int start = _pos;
        while (_pos < _source.Length && IsIdentPart(Current))
            _pos++;

        string value = _source[start.._pos];
        TokenType type = value switch
        {
            "true" or "false" => TokenType.BooleanLiteral,
            "null" => TokenType.NullLiteral,
            _ => TokenType.Identifier
        };

        _tokens.Add(new Token
        {
            Type = type,
            Value = value,
            Position = start,
            Line = _line
        });
    }

    private bool TryReadOperatorOrDelimiter()
    {
        char c = Current;
        char next = Peek(1);

        switch (c)
        {
            case '+': _tokens.Add(MakeToken(TokenType.Plus, "+", 1)); return true;
            case '-': _tokens.Add(MakeToken(TokenType.Minus, "-", 1)); return true;
            case '*': _tokens.Add(MakeToken(TokenType.Star, "*", 1)); return true;
            case '/': _tokens.Add(MakeToken(TokenType.Slash, "/", 1)); return true;
            case '%': _tokens.Add(MakeToken(TokenType.Percent, "%", 1)); return true;
            case '(': _tokens.Add(MakeToken(TokenType.LeftParen, "(", 1)); return true;
            case ')': _tokens.Add(MakeToken(TokenType.RightParen, ")", 1)); return true;
            case ',': _tokens.Add(MakeToken(TokenType.Comma, ",", 1)); return true;
            case '?': _tokens.Add(MakeToken(TokenType.QuestionMark, "?", 1)); return true;
            case ':': _tokens.Add(MakeToken(TokenType.Colon, ":", 1)); return true;
            case '.': _tokens.Add(MakeToken(TokenType.Dot, ".", 1)); return true;

            case '=':
                if (next == '=') { _tokens.Add(MakeToken(TokenType.Equal, "==", 2)); return true; }
                AddError("Expected '==' but found '='");
                _pos++;
                return true;

            case '!':
                if (next == '=') { _tokens.Add(MakeToken(TokenType.NotEqual, "!=", 2)); return true; }
                _tokens.Add(MakeToken(TokenType.Not, "!", 1));
                return true;

            case '<':
                if (next == '=') { _tokens.Add(MakeToken(TokenType.LessEqual, "<=", 2)); return true; }
                _tokens.Add(MakeToken(TokenType.LessThan, "<", 1));
                return true;

            case '>':
                if (next == '=') { _tokens.Add(MakeToken(TokenType.GreaterEqual, ">=", 2)); return true; }
                _tokens.Add(MakeToken(TokenType.GreaterThan, ">", 1));
                return true;

            case '&':
                if (next == '&') { _tokens.Add(MakeToken(TokenType.And, "&&", 2)); return true; }
                AddError("Expected '&&' but found '&'");
                _pos++;
                return true;

            case '|':
                if (next == '|') { _tokens.Add(MakeToken(TokenType.Or, "||", 2)); return true; }
                AddError("Expected '||' but found '|'");
                _pos++;
                return true;
        }

        return false;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void AddError(string message)
    {
        if (_errors.Count < 10)
            _errors.Add($"Line {_line}, position {_pos}: {message}");
    }
}
