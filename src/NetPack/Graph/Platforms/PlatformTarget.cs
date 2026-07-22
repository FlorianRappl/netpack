namespace NetPack.Graph;

using System;
using System.Collections.Generic;

/// <summary>The runtime a build targets. Mirrors esbuild's <c>platform</c>.</summary>
public enum Platform
{
    /// <summary>Browsers / web workers (default).</summary>
    Web,

    /// <summary>Node.js.</summary>
    Node,

    /// <summary>Deno.</summary>
    Deno,
}

/// <summary>
/// A platform decides which bare specifiers are provided by the runtime — its
/// built-ins — and are therefore kept external (never bundled), and how a
/// dependency's <c>package.json</c> entry is chosen (whether the <c>browser</c>
/// field applies). This is the netpack equivalent of esbuild's <c>platform</c>.
/// </summary>
abstract class PlatformTarget
{
    /// <summary>True when <paramref name="specifier"/> is a runtime built-in that
    /// must not be bundled (e.g. <c>node:fs</c> / <c>fs</c> on Node).</summary>
    public abstract bool IsBuiltin(string specifier);

    /// <summary>Whether the <c>browser</c> field of a dependency's package.json is
    /// honoured when picking its entry point.</summary>
    public virtual bool UseBrowserField => false;

    /// <summary>
    /// The condition names honoured when resolving a package's <c>exports</c> map,
    /// in addition to the always-matched <c>default</c>. netpack is ESM-first, so
    /// <c>import</c>/<c>module</c> lead and <c>require</c> is intentionally omitted:
    /// a CJS-only package still resolves through the legacy <c>main</c> fallback,
    /// but a dual package yields its ESM entry for better tree-shaking.
    /// </summary>
    public virtual IReadOnlyList<string> Conditions { get; } = ["import", "module", "default"];
}

/// <summary>Browser / web-worker target — no bare-module built-ins; the
/// <c>browser</c> package.json field wins.</summary>
sealed class WebPlatform : PlatformTarget
{
    public override bool IsBuiltin(string specifier) => false;

    public override bool UseBrowserField => true;

    public override IReadOnlyList<string> Conditions { get; } = ["import", "module", "browser", "default"];
}

/// <summary>Node.js target — the <c>node:</c> scheme and every Node core module
/// are provided by the runtime.</summary>
sealed class NodePlatform : PlatformTarget
{
    public override bool IsBuiltin(string specifier)
        => specifier.StartsWith("node:", StringComparison.Ordinal) || PlatformTargets.IsNodeCore(specifier);

    public override IReadOnlyList<string> Conditions { get; } = ["import", "module", "node", "default"];
}

/// <summary>Deno target — the <c>node:</c>, <c>npm:</c> and <c>jsr:</c> schemes are
/// resolved by the runtime.</summary>
sealed class DenoPlatform : PlatformTarget
{
    public override bool IsBuiltin(string specifier)
        => specifier.StartsWith("node:", StringComparison.Ordinal)
            || specifier.StartsWith("npm:", StringComparison.Ordinal)
            || specifier.StartsWith("jsr:", StringComparison.Ordinal);

    public override IReadOnlyList<string> Conditions { get; } = ["import", "module", "deno", "node", "default"];
}

static class PlatformTargets
{
    public static PlatformTarget For(Platform platform) => platform switch
    {
        Platform.Node => new NodePlatform(),
        Platform.Deno => new DenoPlatform(),
        _ => new WebPlatform(),
    };

    /// <summary>True for a Node core module specifier, including subpaths such as
    /// <c>fs/promises</c> (matched on the leading segment).</summary>
    public static bool IsNodeCore(string specifier)
    {
        var slash = specifier.IndexOf('/');
        var name = slash < 0 ? specifier : specifier[..slash];
        return NodeCore.Contains(name);
    }

    private static readonly HashSet<string> NodeCore =
    [
        "assert", "async_hooks", "buffer", "child_process", "cluster", "console",
        "constants", "crypto", "dgram", "diagnostics_channel", "dns", "domain",
        "events", "fs", "http", "http2", "https", "inspector", "module", "net",
        "os", "path", "perf_hooks", "process", "punycode", "querystring", "readline",
        "repl", "stream", "string_decoder", "sys", "timers", "tls", "trace_events",
        "tty", "url", "util", "v8", "vm", "wasi", "worker_threads", "zlib",
    ];
}
