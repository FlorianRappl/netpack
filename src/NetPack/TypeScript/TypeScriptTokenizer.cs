namespace NetPack.TypeScript;

public ref struct TypeScriptTokenizer
{
    private static readonly HashSet<string> Keywords =
    [
        "let", "const", "var", "function", "if", "else", "for", "while", "return", "class", "constructor", "new", "try", "catch", "finally",
        "import", "export", "extends", "implements", "interface", "type", "as", "from", "null", "undefined", "true", "false"
    ];

    private static readonly HashSet<char> Operators = ['+', '-', '*', '/', '=', '>', '<', '!', '&', '|', '^', '%', '~', '?'];
    private static readonly HashSet<char> Symbols = ['(', ')', '{', '}', '[', ']', ';', ':', '.', ',', '"'];

    private readonly ReadOnlySpan<char> _input;
    private int _position;

    public TypeScriptTokenizer(ReadOnlySpan<char> input)
    {
        _input = input;
        _position = 0;
    }

    private readonly char Current => _position < _input.Length ? _input[_position] : '\0';

    private readonly char NextChar() => _position + 1 < _input.Length ? _input[_position + 1] : '\0';

    public readonly bool IsActive => _position < _input.Length;

    public Token NextToken()
    {
        while (IsActive)
        {
            if (char.IsWhiteSpace(Current))
            {
                return ReadWhitespace();
            }
            else if (char.IsLetter(Current) || Current == '_' || Current == '$')
            {
                return ReadIdentifierOrKeyword();
            }
            else if (char.IsDigit(Current))
            {
                return ReadNumber();
            }
            else if (Operators.Contains(Current))
            {
                return ReadOperator();
            }
            else if (Symbols.Contains(Current))
            {
                return ReadSymbol();
            }
            else if (Current == '/' && (NextChar() == '/' || NextChar() == '*'))
            {
                return ReadComment();
            }
            else if (Current == '"' || Current == '\'' || Current == '`')
            {
                return ReadStringOrTemplate();
            }
            else
            {
                var token = new Token(TokenType.Unknown, _input.Slice(_position, 1));
                _position++;
                return token;
            }
        }
        
        return new Token(TokenType.Unknown, []);
    }

    private Token ReadWhitespace()
    {
        var start = _position;

        while (IsActive && char.IsWhiteSpace(Current))
        {
            _position++;
        }

        return new Token(TokenType.Whitespace, _input[start.._position]);
    }

    private Token ReadIdentifierOrKeyword()
    {
        var start = _position;

        while (IsActive && (char.IsLetterOrDigit(Current) || Current == '_' || Current == '$'))
        {
            _position++;
        }

        var value = _input[start.._position];
        var type = Keywords.Contains(value.ToString()) ? TokenType.Keyword : TokenType.Identifier;
        return new Token(type, value);
    }

    private Token ReadNumber()
    {
        var start = _position;
        
        while (IsActive && char.IsDigit(Current))
        {
            _position++;
        }

        return new Token(TokenType.Number, _input[start.._position]);
    }

    private Token ReadOperator()
    {
        var start = _position;
        _position++; // Single-character operators for simplicity
        return new Token(TokenType.Operator, _input.Slice(start, 1));
    }

    private Token ReadSymbol()
    {
        var start = _position;
        _position++;
        return new Token(TokenType.Symbol, _input.Slice(start, 1));
    }

    private Token ReadComment()
    {
        var start = _position;

        if (NextChar() == '/')
        {
            while (IsActive && Current != '\n')
            {
                _position++;
            }
        }
        else
        {
            _position += 2; // Skip '/*'

            while (IsActive && !(Current == '*' && NextChar() == '/'))
            {
                _position++;
            }
            _position += 2; // Skip '*/'
        }

        return new Token(TokenType.Comment, _input[start.._position]);
    }

    private Token ReadStringOrTemplate()
    {
        var quote = Current;
        var isTemplateString = quote == '`';
        var start = _position;
        _position++; // Skip starting quote
        var escape = false;

        while (IsActive && (Current != quote || escape || isTemplateString))
        {
            if (escape)
            {
                escape = false;
            }
            else if (Current == '\\')
            {
                escape = true;
            }
            else if (isTemplateString && Current == '$' && NextChar() == '{')
            {
                _position += 2; // Skip '${'
                while (IsActive && !(Current == '}' && !escape))
                {
                    _position++;
                    if (Current == '\\')
                        escape = !escape;
                    else
                        escape = false;
                }
                _position++; // Skip ending '}'
                continue;
            }
            _position++;
        }

        _position++; // Skip ending quote
        return new Token(isTemplateString ? TokenType.TemplateString : TokenType.String, _input[start.._position]);
    }
}
