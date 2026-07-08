namespace NetPack.Syntax;

using System;

/// <summary>
/// Extra lexical information attached to a <see cref="Token"/> that the parser
/// needs but that is not captured by the <see cref="TokenKind"/> alone.
/// </summary>
[Flags]
public enum TokenFlags
{
    None = 0,

    /// <summary>At least one line terminator appeared before this token. Used
    /// for automatic semicolon insertion and for restricting productions.</summary>
    PrecededByNewLine = 1 << 0,

    /// <summary>The literal was not properly terminated (unterminated string,
    /// template, comment or regex). Recorded tolerantly rather than thrown.</summary>
    Unterminated = 1 << 1,

    /// <summary>A numeric literal used underscore separators (<c>1_000</c>).</summary>
    ContainsSeparator = 1 << 2,

    /// <summary>A legacy octal literal (<c>0777</c>) which is invalid in strict mode.</summary>
    LegacyOctal = 1 << 3,

    /// <summary>The token contained an escape sequence that is invalid in the
    /// current context (e.g. <c>\8</c> in a template).</summary>
    ContainsInvalidEscape = 1 << 4,

    /// <summary>The identifier contained a unicode escape (<c>A</c>). Such
    /// identifiers cannot be treated as keywords.</summary>
    UnicodeEscape = 1 << 5,
}

/// <summary>
/// A single lexical token. Tokens are lightweight value types that reference a
/// span of the original source by offset; the raw text is obtained on demand
/// via <see cref="Tokenizer.GetText(in Token)"/>, avoiding per-token string
/// allocations on the hot path.
/// </summary>
public readonly struct Token
{
    public Token(TokenKind kind, int start, int end, int line, int column, TokenFlags flags, string? value)
    {
        Kind = kind;
        Start = start;
        End = end;
        Line = line;
        Column = column;
        Flags = flags;
        Value = value;
    }

    /// <summary>The classified kind of the token.</summary>
    public TokenKind Kind { get; }

    /// <summary>Inclusive start offset into the source text.</summary>
    public int Start { get; }

    /// <summary>Exclusive end offset into the source text.</summary>
    public int End { get; }

    /// <summary>1-based line number of the token start.</summary>
    public int Line { get; }

    /// <summary>1-based column number of the token start.</summary>
    public int Column { get; }

    /// <summary>Extra lexical flags (see <see cref="TokenFlags"/>).</summary>
    public TokenFlags Flags { get; }

    /// <summary>
    /// The cooked value for literals and identifiers where escapes have been
    /// resolved (string/template contents, identifier name, numeric text).
    /// Null for pure punctuator tokens whose text is fully implied by the kind.
    /// </summary>
    public string? Value { get; }

    /// <summary>Number of source characters spanned by the token.</summary>
    public int Length => End - Start;

    public bool HasFlag(TokenFlags flag) => (Flags & flag) == flag;

    public bool PrecededByNewLine => (Flags & TokenFlags.PrecededByNewLine) != 0;

    public bool IsUnterminated => (Flags & TokenFlags.Unterminated) != 0;

    /// <summary>True for any of the template fragment kinds.</summary>
    public bool IsTemplate =>
        Kind is TokenKind.NoSubstitutionTemplate
            or TokenKind.TemplateHead
            or TokenKind.TemplateMiddle
            or TokenKind.TemplateTail;

    public override string ToString() => Value is null
        ? $"{Kind} @{Line}:{Column}"
        : $"{Kind}({Value}) @{Line}:{Column}";
}
