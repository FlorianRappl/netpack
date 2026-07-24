namespace NetPack.Syntax;

/// <summary>Configuration for the <see cref="Tokenizer"/>.</summary>
public readonly record struct TokenizerOptions
{
    /// <summary>
    /// When true the tokenizer keeps going after lexical errors, recording a
    /// <see cref="Diagnostic"/> and emitting a best-effort token. This mirrors
    /// the behaviour NetPack relied on from Acornima's tolerant mode.
    /// </summary>
    public bool Tolerant { get; init; }

    /// <summary>
    /// When true, <c>#!</c> at the very start of the file is treated as a
    /// hashbang comment (Node scripts) rather than a private identifier.
    /// </summary>
    public bool AllowHashbang { get; init; }

    /// <summary>The dialect being scanned. Affects a couple of contextual
    /// decisions (e.g. whether JSX rescans are expected to be requested).</summary>
    public LanguageVariant Variant { get; init; }

    public static TokenizerOptions Default => new()
    {
        Tolerant = true,
        AllowHashbang = true,
        Variant = LanguageVariant.Standard,
    };
}

/// <summary>The syntactic dialect of the source being processed.</summary>
public enum LanguageVariant
{
    /// <summary>Plain JavaScript / TypeScript (no JSX).</summary>
    Standard,

    /// <summary>JSX enabled (.jsx / .tsx).</summary>
    Jsx,
}
