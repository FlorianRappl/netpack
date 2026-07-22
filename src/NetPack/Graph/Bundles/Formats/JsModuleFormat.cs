namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// Strategy for a JavaScript output module format — the "envelope" a
/// <see cref="JsBundle"/> is emitted in. It abstracts every format-specific
/// decision the bundle otherwise has no opinion about:
///
/// <list type="bullet">
///   <item>how the bundle imports a sibling bundle netpack emitted (a shared chunk);</item>
///   <item>how a hoisted external <c>import</c> is expressed;</item>
///   <item>how the shared registry / entry module is exported;</item>
///   <item>how a module-relative reference (dynamic-import chunk or asset URL) is
///     resolved at runtime, and how a dynamic <c>import()</c> is written;</item>
///   <item>how the whole assembled bundle is wrapped.</item>
/// </list>
///
/// The registry (<c>__m</c>) and the lazy <c>require</c> runtime (<c>__r</c>) are
/// plain in-bundle JavaScript and therefore format-agnostic; only the linkage at
/// the module boundary differs between formats. Today only
/// <see cref="EsmModuleFormat"/> exists; CommonJS, UMD and SystemJS subclass this
/// next.
/// </summary>
abstract class JsModuleFormat
{
    protected static Ast.StringLiteral MakeString(string text) => new(text, text);

    /// <summary>The <c>--public-path</c> prefix applied to every emitted-file
    /// reference this format writes (see <see cref="Ref"/>). Empty by default.</summary>
    public string PublicPath { get; set; } = "";

    /// <summary>The runtime specifier for an emitted sibling file — document
    /// relative by default, or under <see cref="PublicPath"/> when set.</summary>
    protected string Ref(string fileName) => Helpers.PublicUrl(PublicPath, fileName);

    /// <summary>Imports a sibling bundle netpack emitted (a shared chunk), binding
    /// its registry to <paramref name="local"/>.</summary>
    public abstract Ast.Statement ImportSharedBundle(Ast.Identifier local, string fileName);

    /// <summary>Rewrites a hoisted external <c>import … from "pkg"</c> (a dependency
    /// left out of the bundle) into the target format.</summary>
    public abstract Ast.Statement RewriteExternalImport(Ast.ImportDeclaration declaration);

    /// <summary>Exports the shared registry object <paramref name="registry"/>
    /// (emitted by shared bundles, consumed via <see cref="ImportSharedBundle"/>).</summary>
    public abstract IReadOnlyList<Ast.Statement> ExportRegistry(Ast.Identifier registry);

    /// <summary>Exports the entry bundle, given the root module's
    /// <c>require(id)</c> result and its export names.</summary>
    public abstract IReadOnlyList<Ast.Statement> ExportRoot(Ast.Expression rootRequire, IReadOnlyList<string> exportNames);

    /// <summary>The expression a module-relative reference (a dynamic-import chunk
    /// or an emitted asset) resolves to at runtime.</summary>
    public abstract Ast.Expression AutoReference(string fileName);

    /// <summary>Wraps a resolved reference in a dynamic import.</summary>
    public virtual Ast.Expression DynamicImport(Ast.Expression target) => new Ast.ImportExpression(target);

    /// <summary>Wraps the fully-assembled bundle in the format's envelope
    /// (identity by default — ESM and CommonJS need no wrapper).</summary>
    public virtual Ast.SourceFile Wrap(Ast.SourceFile module) => module;
}
