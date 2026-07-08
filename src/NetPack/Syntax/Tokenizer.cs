namespace NetPack.Syntax;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// A hand-written, allocation-light tokenizer for JavaScript, TypeScript and
/// JSX. It scans a source string into <see cref="Token"/> values on demand.
///
/// Design notes:
/// <list type="bullet">
/// <item>Trivia (whitespace, line breaks, comments) is skipped between
/// significant tokens; the fact that a newline preceded a token is recorded in
/// <see cref="TokenFlags.PrecededByNewLine"/> so the parser can perform
/// automatic semicolon insertion.</item>
/// <item>The <c>/</c> character is ambiguous between division and the start of a
/// regular expression. The tokenizer resolves this with the same
/// previous-token heuristic used by Acorn/esbuild; the parser can also force a
/// rescan via <see cref="ReScanAsRegex"/>.</item>
/// <item>Template literals and JSX require the parser to drive re-scans at the
/// right grammar positions; see <see cref="ReScanTemplateContinuation"/>,
/// <see cref="ScanJsxText"/> and <see cref="ScanJsxIdentifier"/>.</item>
/// </list>
/// </summary>
public sealed class Tokenizer
{
    private readonly string _source;
    private readonly int _length;
    private readonly TokenizerOptions _options;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly StringBuilder _builder = new();

    private int _pos;
    private int _line = 1;
    private int _lineStart;
    private bool _precededByNewLine;
    private TokenKind _previousKind = TokenKind.Unknown;

    // Token-in-progress bookkeeping.
    private int _tokenStart;
    private int _tokenLine;
    private int _tokenColumn;
    private TokenFlags _flags;

    public Tokenizer(string source, TokenizerOptions options = default)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _length = _source.Length;
        _options = options.Equals(default(TokenizerOptions)) ? TokenizerOptions.Default : options;
    }

    /// <summary>Diagnostics accumulated during tolerant scanning.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Current absolute scan position.</summary>
    public int Position => _pos;

    /// <summary>Returns the raw source text spanned by a token.</summary>
    public string GetText(in Token token) => _source.Substring(token.Start, token.End - token.Start);

    /// <summary>Convenience: fully tokenizes the source into a list (mostly for
    /// tests and tooling; the parser pulls tokens one at a time).</summary>
    public List<Token> Tokenize()
    {
        var list = new List<Token>();
        Token token;
        do
        {
            token = Next();
            list.Add(token);
        }
        while (token.Kind != TokenKind.EndOfFile);
        return list;
    }

    // -- character access --------------------------------------------------

    private char Current => _pos < _length ? _source[_pos] : '\0';

    private char Peek(int offset = 1)
    {
        var index = _pos + offset;
        return index < _length ? _source[index] : '\0';
    }

    private bool AtEnd => _pos >= _length;

    private int Column(int position) => position - _lineStart + 1;

    private void NewLine()
    {
        _line++;
        _lineStart = _pos;
    }

    private void AddError(string message, int position)
    {
        _diagnostics.Add(new Diagnostic(message, position, _line, Column(position)));
        if (!_options.Tolerant)
        {
            throw new SyntaxException(message, position, _line, Column(position));
        }
    }

    // -- token production --------------------------------------------------

    private Token Make(TokenKind kind, string? value = null)
    {
        var flags = _flags;
        if (_precededByNewLine)
        {
            flags |= TokenFlags.PrecededByNewLine;
        }

        var token = new Token(kind, _tokenStart, _pos, _tokenLine, _tokenColumn, flags, value);
        _previousKind = kind;
        return token;
    }

    /// <summary>Scans and returns the next significant token.</summary>
    public Token Next()
    {
        _precededByNewLine = false;
        SkipTrivia();
        _flags = TokenFlags.None;
        _tokenStart = _pos;
        _tokenLine = _line;
        _tokenColumn = Column(_pos);

        if (AtEnd)
        {
            return Make(TokenKind.EndOfFile);
        }

        var c = Current;

        if (CharUtil.IsIdentifierStart(c) || c == '\\')
        {
            return ScanIdentifierOrKeyword();
        }

        if (CharUtil.IsDecimalDigit(c) || (c == '.' && CharUtil.IsDecimalDigit(Peek())))
        {
            return ScanNumber();
        }

        switch (c)
        {
            case '"':
            case '\'':
                return ScanString(c);
            case '`':
                return ScanTemplate(fromSubstitution: false);
            case '#':
                return ScanPrivateIdentifier();
            default:
                return ScanPunctuator();
        }
    }

    // -- trivia ------------------------------------------------------------

    private void SkipTrivia()
    {
        // Hashbang only valid at the very beginning of the file.
        if (_pos == 0 && _options.AllowHashbang && Current == '#' && Peek() == '!')
        {
            while (!AtEnd && !CharUtil.IsLineTerminator(Current))
            {
                _pos++;
            }
        }

        while (!AtEnd)
        {
            var c = Current;

            if (c == '\n')
            {
                _pos++;
                NewLine();
                _precededByNewLine = true;
            }
            else if (c == '\r')
            {
                _pos++;
                if (Current == '\n')
                {
                    _pos++;
                }
                NewLine();
                _precededByNewLine = true;
            }
            else if (c == CharUtil.LineSeparator || c == CharUtil.ParagraphSeparator)
            {
                _pos++;
                NewLine();
                _precededByNewLine = true;
            }
            else if (CharUtil.IsWhiteSpace(c))
            {
                _pos++;
            }
            else if (c == '/' && Peek() == '/')
            {
                SkipLineComment();
            }
            else if (c == '/' && Peek() == '*')
            {
                SkipBlockComment();
            }
            else
            {
                break;
            }
        }
    }

    private void SkipLineComment()
    {
        _pos += 2;
        while (!AtEnd && !CharUtil.IsLineTerminator(Current))
        {
            _pos++;
        }
    }

    private void SkipBlockComment()
    {
        var start = _pos;
        _pos += 2;
        while (!AtEnd)
        {
            var c = Current;
            if (c == '*' && Peek() == '/')
            {
                _pos += 2;
                return;
            }

            if (c == '\n' || c == '\r' || c == CharUtil.LineSeparator || c == CharUtil.ParagraphSeparator)
            {
                if (c == '\r' && Peek() == '\n')
                {
                    _pos++;
                }
                _pos++;
                NewLine();
                _precededByNewLine = true;
            }
            else
            {
                _pos++;
            }
        }

        AddError("Unterminated block comment.", start);
    }

    // -- identifiers -------------------------------------------------------

    private Token ScanIdentifierOrKeyword()
    {
        var name = ScanIdentifierName(out var hadEscape);

        if (hadEscape)
        {
            _flags |= TokenFlags.UnicodeEscape;
            // Escaped identifiers are never treated as keywords.
            return Make(TokenKind.Identifier, name);
        }

        var kind = Keywords.Classify(name);
        return Make(kind, name);
    }

    private string ScanIdentifierName(out bool hadEscape)
    {
        hadEscape = false;
        var start = _pos;
        _builder.Clear();

        while (!AtEnd)
        {
            var c = Current;

            if (c == '\\')
            {
                hadEscape = true;
                // Flush pending literal run.
                if (_pos > start)
                {
                    _builder.Append(_source, start, _pos - start);
                }

                var cp = ScanUnicodeEscape();
                AppendCodePoint(_builder, cp);
                start = _pos;
            }
            else if (CharUtil.IsIdentifierPart(c))
            {
                _pos++;
            }
            else
            {
                break;
            }
        }

        if (!hadEscape)
        {
            return _source.Substring(start, _pos - start);
        }

        if (_pos > start)
        {
            _builder.Append(_source, start, _pos - start);
        }

        return _builder.ToString();
    }

    private int ScanUnicodeEscape()
    {
        // Assumes Current == '\\'.
        var escapeStart = _pos;
        _pos++; // consume backslash

        if (Current != 'u')
        {
            AddError("Invalid escape in identifier.", escapeStart);
            return '�';
        }

        _pos++; // consume 'u'

        if (Current == '{')
        {
            _pos++;
            var value = 0;
            var any = false;
            while (!AtEnd && Current != '}')
            {
                var digit = CharUtil.HexValue(Current);
                if (digit < 0)
                {
                    break;
                }
                value = (value * 16) + digit;
                any = true;
                _pos++;
            }

            if (Current == '}')
            {
                _pos++;
            }
            else
            {
                AddError("Unterminated unicode escape.", escapeStart);
            }

            if (!any || value > 0x10FFFF)
            {
                AddError("Invalid unicode code point.", escapeStart);
                return '�';
            }

            return value;
        }

        return ScanFixedHex(4, escapeStart);
    }

    private int ScanFixedHex(int count, int errorPos)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            var digit = CharUtil.HexValue(Current);
            if (digit < 0)
            {
                AddError("Invalid hexadecimal escape.", errorPos);
                return '�';
            }
            value = (value * 16) + digit;
            _pos++;
        }
        return value;
    }

    private Token ScanPrivateIdentifier()
    {
        _pos++; // consume '#'
        if (!CharUtil.IsIdentifierStart(Current) && Current != '\\')
        {
            AddError("Expected an identifier after '#'.", _tokenStart);
            return Make(TokenKind.PrivateIdentifier, "#");
        }

        var name = ScanIdentifierName(out _);
        return Make(TokenKind.PrivateIdentifier, "#" + name);
    }

    // -- numbers -----------------------------------------------------------

    private Token ScanNumber()
    {
        var start = _pos;

        if (Current == '0' && (Peek() == 'x' || Peek() == 'X'))
        {
            _pos += 2;
            ScanRadixDigits(CharUtil.IsHexDigit);
            return FinishNumber(start);
        }

        if (Current == '0' && (Peek() == 'o' || Peek() == 'O'))
        {
            _pos += 2;
            ScanRadixDigits(CharUtil.IsOctalDigit);
            return FinishNumber(start);
        }

        if (Current == '0' && (Peek() == 'b' || Peek() == 'B'))
        {
            _pos += 2;
            ScanRadixDigits(CharUtil.IsBinaryDigit);
            return FinishNumber(start);
        }

        // Legacy octal (0777) — a leading zero followed by more digits.
        if (Current == '0' && CharUtil.IsDecimalDigit(Peek()))
        {
            _flags |= TokenFlags.LegacyOctal;
            while (CharUtil.IsDecimalDigit(Current))
            {
                _pos++;
            }
            return FinishNumber(start);
        }

        ScanDecimalDigits();

        if (Current == '.')
        {
            _pos++;
            ScanDecimalDigits();
        }

        if (Current == 'e' || Current == 'E')
        {
            _pos++;
            if (Current == '+' || Current == '-')
            {
                _pos++;
            }
            if (!CharUtil.IsDecimalDigit(Current))
            {
                AddError("Missing exponent digits.", start);
            }
            ScanDecimalDigits();
        }

        return FinishNumber(start);
    }

    private void ScanRadixDigits(Func<char, bool> isDigit)
    {
        var any = false;
        while (!AtEnd)
        {
            if (Current == '_')
            {
                _flags |= TokenFlags.ContainsSeparator;
                _pos++;
                continue;
            }
            if (!isDigit(Current))
            {
                break;
            }
            any = true;
            _pos++;
        }

        if (!any)
        {
            AddError("Missing digits in numeric literal.", _tokenStart);
        }
    }

    private void ScanDecimalDigits()
    {
        while (!AtEnd)
        {
            if (Current == '_')
            {
                _flags |= TokenFlags.ContainsSeparator;
                _pos++;
                continue;
            }
            if (!CharUtil.IsDecimalDigit(Current))
            {
                break;
            }
            _pos++;
        }
    }

    private Token FinishNumber(int start)
    {
        var kind = TokenKind.NumericLiteral;

        if (Current == 'n')
        {
            _pos++;
            kind = TokenKind.BigIntLiteral;
        }

        // An identifier directly following a number is a lexical error
        // (e.g. `3in`), but we recover by stopping the numeric token here.
        if (CharUtil.IsIdentifierStart(Current))
        {
            AddError("Identifier cannot immediately follow a numeric literal.", _pos);
        }

        var text = _source.Substring(start, _pos - start);
        return Make(kind, text);
    }

    // -- strings -----------------------------------------------------------

    private Token ScanString(char quote)
    {
        _pos++; // opening quote
        _builder.Clear();
        var runStart = _pos;

        while (true)
        {
            if (AtEnd)
            {
                _flags |= TokenFlags.Unterminated;
                AddError("Unterminated string literal.", _tokenStart);
                break;
            }

            var c = Current;

            if (c == quote)
            {
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                _pos++; // closing quote
                break;
            }

            if (c == '\\')
            {
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                ScanEscapeSequence();
                runStart = _pos;
                continue;
            }

            if (CharUtil.IsLineTerminator(c) && c != CharUtil.LineSeparator && c != CharUtil.ParagraphSeparator)
            {
                _flags |= TokenFlags.Unterminated;
                AddError("Unterminated string literal.", _tokenStart);
                break;
            }

            _pos++;
        }

        return Make(TokenKind.StringLiteral, _builder.ToString());
    }

    private void ScanEscapeSequence()
    {
        var escapeStart = _pos;
        _pos++; // backslash

        if (AtEnd)
        {
            _flags |= TokenFlags.Unterminated;
            return;
        }

        var c = Current;
        switch (c)
        {
            case 'n': _builder.Append('\n'); _pos++; break;
            case 't': _builder.Append('\t'); _pos++; break;
            case 'r': _builder.Append('\r'); _pos++; break;
            case 'b': _builder.Append('\b'); _pos++; break;
            case 'f': _builder.Append('\f'); _pos++; break;
            case 'v': _builder.Append('\v'); _pos++; break;
            case '0':
                if (!CharUtil.IsDecimalDigit(Peek()))
                {
                    _builder.Append('\0');
                    _pos++;
                }
                else
                {
                    _flags |= TokenFlags.LegacyOctal;
                    _builder.Append(ScanLegacyOctalEscape());
                }
                break;
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
                _flags |= TokenFlags.LegacyOctal;
                _builder.Append(ScanLegacyOctalEscape());
                break;
            case 'x':
                _pos++;
                _builder.Append((char)ScanFixedHex(2, escapeStart));
                break;
            case 'u':
                AppendCodePoint(_builder, ScanUnicodeEscapeFromBackslash(escapeStart));
                break;
            case '\r':
                _pos++;
                if (Current == '\n')
                {
                    _pos++;
                }
                break; // line continuation
            case '\n':
            case CharUtil.LineSeparator:
            case CharUtil.ParagraphSeparator:
                _pos++;
                break; // line continuation
            default:
                _builder.Append(c);
                _pos++;
                break;
        }
    }

    // ScanUnicodeEscape expects Current to be at the backslash. In escape
    // sequences we have already consumed the backslash, so reset to it.
    private int ScanUnicodeEscapeFromBackslash(int backslashPos)
    {
        _pos = backslashPos;
        return ScanUnicodeEscape();
    }

    private static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp <= 0xFFFF)
        {
            // Includes lone surrogate escapes such as \uD800, which are valid
            // in string literals and must be preserved as a single char.
            sb.Append((char)cp);
        }
        else
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }
    }

    private char ScanLegacyOctalEscape()
    {
        var value = 0;
        var count = 0;
        while (count < 3 && CharUtil.IsOctalDigit(Current))
        {
            var next = (value * 8) + (Current - '0');
            if (next > 0xFF)
            {
                break;
            }
            value = next;
            _pos++;
            count++;
        }
        return (char)value;
    }

    // -- templates ---------------------------------------------------------

    private Token ScanTemplate(bool fromSubstitution)
    {
        // fromSubstitution == false: Current is the opening backtick.
        // fromSubstitution == true:  Current is the closing brace of ${ ... }.
        _pos++; // consume ` or }
        _builder.Clear();
        var runStart = _pos;
        var isTail = false;

        while (true)
        {
            if (AtEnd)
            {
                _flags |= TokenFlags.Unterminated;
                AddError("Unterminated template literal.", _tokenStart);
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                isTail = true;
                break;
            }

            var c = Current;

            if (c == '`')
            {
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                _pos++;
                isTail = true;
                break;
            }

            if (c == '$' && Peek() == '{')
            {
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                _pos += 2;
                isTail = false;
                break;
            }

            if (c == '\\')
            {
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                ScanEscapeSequence();
                runStart = _pos;
                continue;
            }

            if (c == '\r')
            {
                // Normalize CRLF / CR to LF inside template cooked value.
                if (_pos > runStart)
                {
                    _builder.Append(_source, runStart, _pos - runStart);
                }
                _builder.Append('\n');
                _pos++;
                if (Current == '\n')
                {
                    _pos++;
                }
                NewLine();
                runStart = _pos;
                continue;
            }

            if (c == '\n' || c == CharUtil.LineSeparator || c == CharUtil.ParagraphSeparator)
            {
                _pos++;
                NewLine();
                continue;
            }

            _pos++;
        }

        var kind = (fromSubstitution, isTail) switch
        {
            (false, true) => TokenKind.NoSubstitutionTemplate,
            (false, false) => TokenKind.TemplateHead,
            (true, true) => TokenKind.TemplateTail,
            (true, false) => TokenKind.TemplateMiddle,
        };

        return Make(kind, _builder.ToString());
    }

    /// <summary>
    /// Continues scanning a template literal after the parser has consumed the
    /// substitution expression and is positioned on the closing <c>}</c>.
    /// Produces a <see cref="TokenKind.TemplateMiddle"/> or
    /// <see cref="TokenKind.TemplateTail"/> token.
    /// </summary>
    public Token ReScanTemplateContinuation()
    {
        _precededByNewLine = false;
        _flags = TokenFlags.None;
        _tokenStart = _pos;
        _tokenLine = _line;
        _tokenColumn = Column(_pos);

        if (Current != '}')
        {
            AddError("Expected '}' to continue template literal.", _pos);
        }

        return ScanTemplate(fromSubstitution: true);
    }

    // -- regex -------------------------------------------------------------

    private bool RegexAllowed()
    {
        var prev = _previousKind;

        if (prev == TokenKind.Unknown)
        {
            return true;
        }

        switch (prev)
        {
            case TokenKind.Identifier:
            case TokenKind.PrivateIdentifier:
            case TokenKind.NumericLiteral:
            case TokenKind.BigIntLiteral:
            case TokenKind.StringLiteral:
            case TokenKind.RegExpLiteral:
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateTail:
            case TokenKind.CloseParen:
            case TokenKind.CloseBracket:
            case TokenKind.CloseBrace:
            case TokenKind.ThisKeyword:
            case TokenKind.SuperKeyword:
            case TokenKind.TrueKeyword:
            case TokenKind.FalseKeyword:
            case TokenKind.NullKeyword:
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                return false;
            default:
                // A contextual (non-reserved) keyword used as an identifier
                // acts like a value, so division follows it.
                if (Keywords.IsKeyword(prev) && !Keywords.IsReservedWord(prev))
                {
                    return false;
                }
                return true;
        }
    }

    /// <summary>
    /// Forces the token that begins at the current position to be scanned as a
    /// regular expression literal. The parser calls this when the grammar
    /// unambiguously requires a regex but the heuristic produced a <c>/</c>.
    /// </summary>
    public Token ReScanAsRegex(in Token slashToken)
    {
        _pos = slashToken.Start;
        _tokenStart = _pos;
        _tokenLine = slashToken.Line;
        _tokenColumn = slashToken.Column;
        _flags = TokenFlags.None;
        return ScanRegex();
    }

    private Token ScanRegex()
    {
        var start = _tokenStart;
        _pos++; // opening slash
        var inClass = false;

        while (true)
        {
            if (AtEnd || CharUtil.IsLineTerminator(Current))
            {
                _flags |= TokenFlags.Unterminated;
                AddError("Unterminated regular expression literal.", start);
                break;
            }

            var c = Current;

            if (c == '\\')
            {
                _pos += 2; // escape — skip next char
                continue;
            }

            if (c == '[')
            {
                inClass = true;
            }
            else if (c == ']')
            {
                inClass = false;
            }
            else if (c == '/' && !inClass)
            {
                _pos++;
                break;
            }

            _pos++;
        }

        // Flags.
        while (!AtEnd && CharUtil.IsIdentifierPart(Current))
        {
            _pos++;
        }

        var text = _source.Substring(start, _pos - start);
        return Make(TokenKind.RegExpLiteral, text);
    }

    // -- punctuators -------------------------------------------------------

    private Token ScanPunctuator()
    {
        var c = Current;
        switch (c)
        {
            case '{': _pos++; return Make(TokenKind.OpenBrace);
            case '}': _pos++; return Make(TokenKind.CloseBrace);
            case '(': _pos++; return Make(TokenKind.OpenParen);
            case ')': _pos++; return Make(TokenKind.CloseParen);
            case '[': _pos++; return Make(TokenKind.OpenBracket);
            case ']': _pos++; return Make(TokenKind.CloseBracket);
            case ';': _pos++; return Make(TokenKind.Semicolon);
            case ',': _pos++; return Make(TokenKind.Comma);
            case '~': _pos++; return Make(TokenKind.Tilde);
            case '@': _pos++; return Make(TokenKind.At);
            case '`': _pos++; return Make(TokenKind.Backtick);

            case '.':
                if (Peek() == '.' && Peek(2) == '.')
                {
                    _pos += 3;
                    return Make(TokenKind.DotDotDot);
                }
                _pos++;
                return Make(TokenKind.Dot);

            case '?':
                if (Peek() == '.' && !CharUtil.IsDecimalDigit(Peek(2)))
                {
                    _pos += 2;
                    return Make(TokenKind.QuestionDot);
                }
                if (Peek() == '?')
                {
                    if (Peek(2) == '=')
                    {
                        _pos += 3;
                        return Make(TokenKind.QuestionQuestionEquals);
                    }
                    _pos += 2;
                    return Make(TokenKind.QuestionQuestion);
                }
                _pos++;
                return Make(TokenKind.Question);

            case ':': _pos++; return Make(TokenKind.Colon);

            case '<':
                if (Peek() == '<')
                {
                    if (Peek(2) == '=')
                    {
                        _pos += 3;
                        return Make(TokenKind.LessThanLessThanEquals);
                    }
                    _pos += 2;
                    return Make(TokenKind.LessThanLessThan);
                }
                if (Peek() == '=')
                {
                    _pos += 2;
                    return Make(TokenKind.LessThanEquals);
                }
                _pos++;
                return Make(TokenKind.LessThan);

            case '>':
                // '>>', '>>>' and their '=' forms are emitted as single tokens
                // so the expression parser stays simple. Generic type argument
                // skipping (`Array<Map<K, V>>`) counts the '>' multiplicity of
                // these tokens instead of relying on separate '>' tokens.
                if (Peek() == '>')
                {
                    if (Peek(2) == '>')
                    {
                        if (Peek(3) == '=')
                        {
                            _pos += 4;
                            return Make(TokenKind.GreaterThanGreaterThanGreaterThanEquals);
                        }
                        _pos += 3;
                        return Make(TokenKind.GreaterThanGreaterThanGreaterThan);
                    }
                    if (Peek(2) == '=')
                    {
                        _pos += 3;
                        return Make(TokenKind.GreaterThanGreaterThanEquals);
                    }
                    _pos += 2;
                    return Make(TokenKind.GreaterThanGreaterThan);
                }
                if (Peek() == '=')
                {
                    _pos += 2;
                    return Make(TokenKind.GreaterThanEquals);
                }
                _pos++;
                return Make(TokenKind.GreaterThan);

            case '=':
                if (Peek() == '=')
                {
                    if (Peek(2) == '=')
                    {
                        _pos += 3;
                        return Make(TokenKind.EqualsEqualsEquals);
                    }
                    _pos += 2;
                    return Make(TokenKind.EqualsEquals);
                }
                if (Peek() == '>')
                {
                    _pos += 2;
                    return Make(TokenKind.Arrow);
                }
                _pos++;
                return Make(TokenKind.Equals);

            case '!':
                if (Peek() == '=')
                {
                    if (Peek(2) == '=')
                    {
                        _pos += 3;
                        return Make(TokenKind.ExclamationEqualsEquals);
                    }
                    _pos += 2;
                    return Make(TokenKind.ExclamationEquals);
                }
                _pos++;
                return Make(TokenKind.Exclamation);

            case '+':
                if (Peek() == '+') { _pos += 2; return Make(TokenKind.PlusPlus); }
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.PlusEquals); }
                _pos++;
                return Make(TokenKind.Plus);

            case '-':
                if (Peek() == '-') { _pos += 2; return Make(TokenKind.MinusMinus); }
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.MinusEquals); }
                _pos++;
                return Make(TokenKind.Minus);

            case '*':
                if (Peek() == '*')
                {
                    if (Peek(2) == '=') { _pos += 3; return Make(TokenKind.AsteriskAsteriskEquals); }
                    _pos += 2;
                    return Make(TokenKind.AsteriskAsterisk);
                }
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.AsteriskEquals); }
                _pos++;
                return Make(TokenKind.Asterisk);

            case '%':
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.PercentEquals); }
                _pos++;
                return Make(TokenKind.Percent);

            case '&':
                if (Peek() == '&')
                {
                    if (Peek(2) == '=') { _pos += 3; return Make(TokenKind.AmpersandAmpersandEquals); }
                    _pos += 2;
                    return Make(TokenKind.AmpersandAmpersand);
                }
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.AmpersandEquals); }
                _pos++;
                return Make(TokenKind.Ampersand);

            case '|':
                if (Peek() == '|')
                {
                    if (Peek(2) == '=') { _pos += 3; return Make(TokenKind.BarBarEquals); }
                    _pos += 2;
                    return Make(TokenKind.BarBar);
                }
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.BarEquals); }
                _pos++;
                return Make(TokenKind.Bar);

            case '^':
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.CaretEquals); }
                _pos++;
                return Make(TokenKind.Caret);

            case '/':
                if (RegexAllowed())
                {
                    return ScanRegex();
                }
                if (Peek() == '=') { _pos += 2; return Make(TokenKind.SlashEquals); }
                _pos++;
                return Make(TokenKind.Slash);

            default:
                AddError($"Unexpected character '{c}'.", _pos);
                _pos++;
                return Make(TokenKind.Unknown, c.ToString());
        }
    }

    // -- JSX helpers -------------------------------------------------------

    /// <summary>
    /// Scans a run of JSX text up to the next <c>&lt;</c> or <c>{</c>. The
    /// parser calls this while consuming the children of a JSX element.
    /// </summary>
    public Token ScanJsxText()
    {
        _precededByNewLine = false;
        _flags = TokenFlags.None;
        _tokenStart = _pos;
        _tokenLine = _line;
        _tokenColumn = Column(_pos);

        var start = _pos;
        while (!AtEnd)
        {
            var c = Current;
            if (c == '<' || c == '{')
            {
                break;
            }
            if (c == '\n' || c == '\r' || c == CharUtil.LineSeparator || c == CharUtil.ParagraphSeparator)
            {
                if (c == '\r' && Peek() == '\n')
                {
                    _pos++;
                }
                _pos++;
                NewLine();
            }
            else
            {
                _pos++;
            }
        }

        var text = _source.Substring(start, _pos - start);
        return Make(TokenKind.StringLiteral, text);
    }

    /// <summary>
    /// Scans a JSX identifier, which unlike a JS identifier may contain hyphens
    /// (e.g. <c>data-role</c>) and namespaces are handled by the parser via the
    /// surrounding <c>:</c> punctuator.
    /// </summary>
    public Token ScanJsxIdentifier()
    {
        _precededByNewLine = false;
        SkipTrivia();
        _flags = TokenFlags.None;
        _tokenStart = _pos;
        _tokenLine = _line;
        _tokenColumn = Column(_pos);

        if (!CharUtil.IsIdentifierStart(Current))
        {
            return Next();
        }

        var start = _pos;
        _pos++;
        while (!AtEnd && (CharUtil.IsIdentifierPart(Current) || Current == '-'))
        {
            _pos++;
        }

        var text = _source.Substring(start, _pos - start);
        return Make(TokenKind.Identifier, text);
    }

    /// <summary>
    /// Resets the tokenizer position (used by the parser for speculative
    /// look-ahead / backtracking).
    /// </summary>
    public void Reset(int position, int line, int lineStart, TokenKind previousKind)
    {
        _pos = position;
        _line = line;
        _lineStart = lineStart;
        _previousKind = previousKind;
    }

    /// <summary>Captures enough state to later <see cref="Reset"/> to here.</summary>
    public (int Position, int Line, int LineStart, TokenKind PreviousKind) Snapshot()
        => (_pos, _line, _lineStart, _previousKind);

    /// <summary>
    /// Repositions the scanner to the start of <paramref name="token"/>. Used by
    /// the parser when it must re-scan a span in a different lexical mode
    /// (template continuation, JSX text / identifiers).
    /// </summary>
    public void RepositionTo(in Token token)
    {
        _pos = token.Start;
        _line = token.Line;
        _lineStart = token.Start - (token.Column - 1);
        _previousKind = TokenKind.Unknown;
    }

    /// <summary>
    /// Re-scans a template continuation starting at the given token position
    /// (the closing <c>}</c> of a substitution).
    /// </summary>
    public Token ReScanTemplateContinuationAt(in Token brace)
    {
        _pos = brace.Start;
        _line = brace.Line;
        _lineStart = brace.Start - (brace.Column - 1);
        return ReScanTemplateContinuation();
    }
}
