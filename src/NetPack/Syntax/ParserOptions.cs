namespace NetPack.Syntax;

/// <summary>Configuration for the <see cref="Parser"/>.</summary>
public readonly record struct ParserOptions
{
    /// <summary>Recover from errors instead of throwing (NetPack default).</summary>
    public bool Tolerant { get; init; }

    /// <summary>Enable JSX element parsing (.jsx / .tsx).</summary>
    public bool Jsx { get; init; }

    /// <summary>
    /// Enable TypeScript syntax (type annotations, <c>interface</c>, <c>type</c>,
    /// <c>enum</c>, <c>as</c>/<c>satisfies</c>, non-null assertions, ...). These
    /// constructs are erased so the resulting AST is plain JavaScript.
    /// </summary>
    public bool TypeScript { get; init; }

    public static ParserOptions Default => new()
    {
        Tolerant = true,
        Jsx = true,
        TypeScript = true,
    };

    /// <summary>Chooses options from a file extension.</summary>
    public static ParserOptions ForFile(string fileName)
    {
        var jsx = fileName.EndsWith(".jsx") || fileName.EndsWith(".tsx");
        var ts = fileName.EndsWith(".ts") || fileName.EndsWith(".tsx")
            || fileName.EndsWith(".mts") || fileName.EndsWith(".cts");
        // A Vue SFC compiles to a virtual JS module whose <script> may be TypeScript
        // (lang="ts"); parse it with TS stripping enabled.
        var vue = fileName.EndsWith(".vue");
        // JSX is also commonly used in plain .js files; enable it permissively.
        return new ParserOptions
        {
            Tolerant = true,
            Jsx = jsx || fileName.EndsWith(".js") || fileName.EndsWith(".mjs"),
            TypeScript = ts || vue,
        };
    }
}
