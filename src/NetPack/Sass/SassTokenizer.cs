namespace NetPack.Sass;

using System;

public ref struct SassTokenizer
{
    private ReadOnlySpan<char> _content;
    private int _position;

    public SassTokenizer(ReadOnlySpan<char> content)
    {
        _content = content;
        _position = 0;
    }

    public readonly bool IsActive => _position < _content.Length;

    public Token NextToken()
    {
        while (IsActive)
        {
            char current = Peek();

            if (char.IsWhiteSpace(current))
            {
                _position++;  // Skip whitespace
                continue;
            }

            if (current == '/')
            {
                if (Peek(1) == '/')
                {
                    return ReadComment();
                }
                else if (Peek(1) == '*')
                {
                    return ReadMultiLineComment();
                }
                else
                {
                    return new Token(TokenType.Unknown, _content.Slice(_position++, 1));
                }
            }

            if (current == '$')
            {
                return ReadVariable();
            }

            if (current == '{')
            {
                return new Token(TokenType.CurlyBracketOpen, _content.Slice(_position++, 1));
            }

            if (current == '}')
            {
                return new Token(TokenType.CurlyBracketClose, _content.Slice(_position++, 1));
            }

            if (current == ':')
            {
                return new Token(TokenType.Colon, _content.Slice(_position++, 1));
            }

            if (current == ';')
            {
                return new Token(TokenType.Semicolon, _content.Slice(_position++, 1));
            }

            if (current == '@')
            {
                return ReadAtRule();
            }

            if (current == '\n' || current == '\r')
            {
                _position++;  // Skip newlines
                continue;
            }

            return ReadWord();
        }

        return new Token(TokenType.Unknown, []);  // Return an empty token when end of input
    }

    private Token ReadComment()
    {
        var start = _position;
        _position += 2; // Skip initial `//`

        while (IsActive && Peek() != '\n')
        {
            _position++;
        }

        return new Token(TokenType.Comment, _content.Slice(start + 2, _position - start - 2).Trim());
    }

    private Token ReadMultiLineComment()
    {
        var start = _position;
        _position += 2; // Skip initial `/*`

        while (IsActive && !(Peek() == '*' && Peek(1) == '/'))
        {
            _position++;
        }

        _position += 2; // Skip closing `*/`
        return new Token(TokenType.Comment, _content.Slice(start + 2, _position - start - 4).Trim());
    }

    private Token ReadVariable()
    {
        var start = _position;
        _position++;

        while (IsActive && (char.IsLetterOrDigit(Peek()) || Peek() == '-' || Peek() == '_'))
        {
            _position++;
        }

        return new Token(TokenType.Variable, _content[start.._position]);
    }

    private Token ReadAtRule()
    {
        var start = _position;
        _position++;

        while (IsActive && char.IsLetterOrDigit(Peek()))
        {
            _position++;
        }

        return new Token(TokenType.AtRule, _content[start.._position]);
    }

    private Token ReadWord()
    {
        var start = _position;

        while (IsActive && !char.IsWhiteSpace(Peek()) && Peek() != ':' && Peek() != ';' && Peek() != '{' && Peek() != '}')
        {
            _position++;
        }

        var slice = _content[start.._position].Trim();
        
        if (slice.Length > 0 && slice[^1] == ':')
        {
            return new Token(TokenType.Property, slice[..^1]);  // Remove trailing colon
        }

        return new Token(TokenType.Value, slice);
    }

    private readonly char Peek(int offset = 0)
    {
        if (_position + offset >= _content.Length)
        {
            return '\0';
        }

        return _content[_position + offset];
    }
}
