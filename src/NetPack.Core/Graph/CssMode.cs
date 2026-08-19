namespace NetPack.Graph;

/// <summary>
/// How stylesheets imported into a build are referenced in the output. Resolved
/// once per build (see <see cref="BundlerContext.CssMode"/>); <see cref="Auto"/>
/// is never stored — it is resolved to a concrete mode from the entry kind.
/// </summary>
public enum CssMode
{
    /// <summary>Pick the best mode for the entry: <see cref="Link"/> for an HTML
    /// entry, <see cref="None"/> for a JS/TS entry.</summary>
    Auto,

    /// <summary>Reference the emitted CSS file(s): a <c>&lt;link&gt;</c> element for
    /// an HTML entry, or <c>document.head.appendChild(link)</c> at runtime for a
    /// JS/TS entry.</summary>
    Link,

    /// <summary>Inline the CSS: a <c>&lt;style&gt;</c> element for an HTML entry, or
    /// <c>document.head.appendChild(style)</c> at runtime for a JS/TS entry.</summary>
    Style,

    /// <summary>Emit the CSS file(s) only, with no reference from the output.</summary>
    None,

    /// <summary>Export the combined stylesheet text as a <c>styles</c> variable from
    /// the root bundle (for a JS/TS entry, or the entry's script for an HTML entry).</summary>
    Export,
}
