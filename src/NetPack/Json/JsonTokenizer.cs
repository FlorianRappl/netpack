namespace NetPack.Json;

using System;

public ref struct JsonTokenizer
{
    private ReadOnlySpan<char> _content;
    private int _position;

    public JsonTokenizer(ReadOnlySpan<char> content)
    {
        _content = content;
        _position = 0;
    }

    public readonly bool IsActive => _position < _content.Length;

    public Token NextToken()
    {
        if (IsActive)
        {
            var current = _content[_position];

            switch (current)
            {
                case '{':
                    _position++;
                    return new Token(TokenType.ObjectStart, "{".AsSpan());
                case '}':
                    _position++;
                    return new Token(TokenType.ObjectEnd, "}".AsSpan());
                case '[':
                    _position++;
                    return new Token(TokenType.ArrayStart, "[".AsSpan());
                case ']':
                    _position++;
                    return new Token(TokenType.ArrayEnd, "]".AsSpan());
                case ':':
                case ',':
                    _position++;
                    return new Token(TokenType.Operator, current.ToString().AsSpan());
                case '"':
                    return ParseString();
                case 't':
                    return ParseLiteral("true", TokenType.True);
                case 'f':
                    return ParseLiteral("false", TokenType.False);
                case 'n':
                    return ParseLiteral("null", TokenType.Null);
                case ' ':
                case '\t':
                case '\n':
                case '\r':
                    return ParseWhitespace();
                default:
                    if (char.IsDigit(current) || current == '-')
                        return ParseNumber();
                    break;
            }

            _position++;
            return new Token(TokenType.Unknown, current.ToString().AsSpan());
        }

        return new Token(TokenType.Unknown, []);
    }

    private Token ParseWhitespace()
    {
        var start = _position++;

        while (IsActive && char.IsWhiteSpace(_content[_position]))
        {
            _position++;
        }

        return new Token(TokenType.Whitespace, _content[start .. _position]);
    }

    private Token ParseString()
    {
        var start = _position++;

        while (IsActive)
        {
            var current = _content[_position++];

            if (current == '\\')
            {
                _position++; // Skip escaped character
            }
            else if (current == '"')
            {
                return new Token(TokenType.String, _content[start .. _position]);
            }
        }

        return new Token(TokenType.Unknown, _content[start..]);
    }

    private Token ParseLiteral(string literal, TokenType type)
    {
        if (_content.Slice(_position, literal.Length).ToString() == literal)
        {
            _position += literal.Length;
            return new Token(type, literal.AsSpan());
        }

        return new Token(TokenType.Unknown, _content.Slice(_position, literal.Length));
    }

    private Token ParseNumber()
    {
        var start = _position;
        var hasDecimal = false;

        if (_content[_position] == '-')
        {
            _position++;
        }

        while (IsActive && (char.IsDigit(_content[_position]) || _content[_position] == '.'))
        {
            if (_content[_position] == '.')
            {
                if (hasDecimal)
                    break;

                hasDecimal = true;
            }

            _position++;
        }

        var span = _content[start .. _position];
        return hasDecimal ? new Token(TokenType.Float, span) : new Token(TokenType.Integer, span);
    }
}
