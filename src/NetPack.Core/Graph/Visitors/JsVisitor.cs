namespace NetPack.Graph.Visitors;

using NetPack.Fragments;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;
using AstNode = NetPack.Syntax.Ast.Node;
using static NetPack.Helpers;

/// <summary>
/// Walks a parsed module to discover its dependencies (static imports,
/// re-exports, dynamic <c>import()</c> and CommonJS <c>require()</c>) and to
/// collect the module's export names. Dead branches guarded by constant string
/// comparisons (e.g. <c>process.env.NODE_ENV</c> after substitution) are not
/// traversed, so their dependencies are not pulled into the graph.
/// </summary>
class JsVisitor(
    Bundle bundle,
    GraphNode current,
    Func<Bundle?, GraphNode, string, (int? Width, int? Height, string? Format), Task<GraphNode?>> report,
    Func<GraphNode, string, string, IReadOnlyList<(string Key, string Specifier)>>? glob = null) : AstRewriter
{
    private readonly Func<Bundle?, GraphNode, string, (int? Width, int? Height, string? Format), Task<GraphNode?>> _report = report;
    private readonly Func<GraphNode, string, string, IReadOnlyList<(string Key, string Specifier)>>? _glob = glob;
    private readonly Bundle _bundle = bundle;
    private readonly GraphNode _current = current;
    private readonly List<string> _exportNames = [];
    private readonly List<AstNode> _elements = [];
    private readonly List<Task<GraphNode?>> _tasks = [];
    private bool _hasDynamicRequire;

    public async Task<JsFragment> FindChildren(SourceFile ast)
    {
        Visit(ast);
        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _elements);
        return new JsFragment(_current, ast, replacements, [.. _exportNames])
        {
            HasDynamicRequire = _hasDynamicRequire,
        };
    }

    protected override AstNode VisitIfStatement(IfStatement node)
    {
        if (node.Test is BinaryExpression be && be.Left is StringLiteral left && be.Right is StringLiteral right)
        {
            if ((be.Operator == TokenKind.EqualsEqualsEquals && left.Value == right.Value) ||
                (be.Operator == TokenKind.ExclamationEqualsEquals && left.Value != right.Value))
            {
                Visit(node.Consequent);
                return node;
            }
            if ((be.Operator == TokenKind.EqualsEqualsEquals && left.Value != right.Value) ||
                (be.Operator == TokenKind.ExclamationEqualsEquals && left.Value == right.Value))
            {
                if (node.Alternate is not null)
                {
                    Visit(node.Alternate);
                }
                return node;
            }
        }

        return base.VisitIfStatement(node);
    }

    protected override AstNode VisitExportAllDeclaration(ExportAllDeclaration node)
    {
        _elements.Add(node);
        _tasks.Add(_report(_bundle, _current, node.Source.Value, default));
        return base.VisitExportAllDeclaration(node);
    }

    protected override AstNode VisitImportDeclaration(ImportDeclaration node)
    {
        // `import type ...` (or an import whose every specifier is an individual
        // `type` import) has no runtime module to load — it is erased at print
        // time, so it must not be resolved or bundled (mirrors the Astro/Vue SFC
        // handling of type-only imports).
        if (node.TypeOnly || IsAllTypeOnly(node))
        {
            return base.VisitImportDeclaration(node);
        }

        _elements.Add(node);
        // A bare import can request an image variant via a query string, e.g.
        // `import img from './logo.png?width=200&height=100'` — parsed centrally
        // in Traverse.InnerProcess (which also strips it before resolving the
        // file), so nothing extra is passed here.
        _tasks.Add(_report(_bundle, _current, node.Source.Value, default));
        return base.VisitImportDeclaration(node);
    }

    /// <summary>True when an import has specifiers and every one is a
    /// member-level <c>type</c> import (so nothing runtime is actually imported).</summary>
    private static bool IsAllTypeOnly(ImportDeclaration node)
    {
        if (node.Specifiers.Count == 0)
        {
            return false;
        }

        foreach (var specifier in node.Specifiers)
        {
            if (specifier is not ImportSpecifier { TypeOnly: true })
            {
                return false;
            }
        }

        return true;
    }

    protected override AstNode VisitExportDefaultDeclaration(ExportDefaultDeclaration node)
    {
        _exportNames.Add("default");
        return base.VisitExportDefaultDeclaration(node);
    }

    protected override AstNode VisitExportNamedDeclaration(ExportNamedDeclaration node)
    {
        // `export type { ... }` (optionally `from '...'`) is type-only — no runtime
        // re-export, no module to resolve.
        if (node.TypeOnly)
        {
            return base.VisitExportNamedDeclaration(node);
        }

        if (node.Source is not null)
        {
            _elements.Add(node);
            _tasks.Add(_report(_bundle, _current, node.Source.Value, default));
        }

        foreach (var specifier in node.Specifiers)
        {
            if (specifier.Exported is Identifier ident)
            {
                _exportNames.Add(ident.Name);
            }
            else if (specifier.Exported is StringLiteral lit)
            {
                _exportNames.Add(lit.Value);
            }
        }

        if (node.Declaration is not null)
        {
            foreach (var name in GetDeclarationExportNames(node.Declaration))
            {
                _exportNames.Add(name);
            }
        }

        return base.VisitExportNamedDeclaration(node);
    }

    private static IEnumerable<string> GetDeclarationExportNames(Statement declaration)
    {
        switch (declaration)
        {
            case VariableStatement variable:
                foreach (var d in variable.Declarations)
                {
                    if (d.Id is Identifier id)
                    {
                        yield return id.Name;
                    }
                }
                break;
            case FunctionDeclaration func when func.Id is not null:
                yield return func.Id.Name;
                break;
            case ClassDeclaration cls when cls.Id is not null:
                yield return cls.Id.Name;
                break;
        }
    }

    protected override AstNode VisitImportExpression(ImportExpression node)
    {
        if (StaticString(node.Source) is { } specifier)
        {
            _elements.Add(node);
            _tasks.Add(_report(null, _current, specifier, default));
            return base.VisitImportExpression(node);
        }

        // A "context" import — `import(`./dir/${x}.ext`)` — a relative template with
        // a static prefix and a non-empty static suffix around one dynamic part.
        // Expand it into a selector over the matching files, each a static dynamic
        // import (and therefore its own lazy chunk), so the dynamic choice resolves
        // at build time to a known set.
        if (TryExpandContext(node.Source, dynamic: true) is { } expanded)
        {
            return expanded;
        }

        return base.VisitImportExpression(node);
    }

    /// <summary>
    /// If <paramref name="source"/> is a context template (a relative static prefix
    /// and a non-empty static suffix around one dynamic part) that matches files on
    /// disk, returns the expanded selector; otherwise null. <paramref name="dynamic"/>
    /// chooses lazy <c>import()</c> chunks vs. inlined synchronous <c>require()</c>.
    /// </summary>
    private AstNode? TryExpandContext(Expression source, bool dynamic)
    {
        if (_glob is null || source is not TemplateLiteral { Expressions.Count: 1, Quasis.Count: 2 } template)
        {
            return null;
        }

        var prefix = template.Quasis[0].Cooked;
        var suffix = template.Quasis[1].Cooked;
        if (!prefix.StartsWith('.') || suffix.Length == 0)
        {
            return null;
        }

        var matches = _glob(_current, prefix, suffix);
        return matches.Count > 0 ? BuildContextSelector(template.Expressions[0], matches, dynamic) : null;
    }

    /// <summary>
    /// Builds <c>({ "key": () =&gt; import("./dir/key.ext"), … })[keyExpr]()</c> (or
    /// <c>require(…)</c> when <paramref name="dynamic"/> is false) for a context
    /// import/require, registering each inner static import/require so it is resolved
    /// and — for <c>import()</c> — code-split like any other dynamic import. The
    /// dynamic key selects the entry at runtime.
    /// </summary>
    private AstNode BuildContextSelector(Expression keyExpr, IReadOnlyList<(string Key, string Specifier)> matches, bool dynamic)
    {
        var properties = new List<AstNode>();

        foreach (var (key, specifier) in matches)
        {
            Expression inner;

            if (dynamic)
            {
                // Lazy chunk: reported with no host bundle so it is code-split.
                inner = new ImportExpression(new StringLiteral(specifier, specifier));
                _elements.Add(inner);
                _tasks.Add(_report(null, _current, specifier, default));
            }
            else
            {
                // Synchronous require: inlined into this bundle like any other require.
                inner = new CallExpression(
                    new Identifier("require"),
                    new List<Expression> { new StringLiteral(specifier, specifier) },
                    optional: false);
                _elements.Add(inner);
                _tasks.Add(_report(_bundle, _current, specifier, default));
            }

            var loader = new ArrowFunctionExpression(new List<Parameter>(), inner, false);
            properties.Add(new Property(new StringLiteral(key, key), loader, PropertyKind.Init, false, false, false));
        }

        var map = new ParenthesizedExpression(new ObjectExpression(properties));
        var selected = new MemberExpression(map, keyExpr, computed: true, optional: false);
        return new CallExpression(selected, new List<Expression>(), optional: false);
    }

    protected override AstNode VisitCallExpression(CallExpression node)
    {
        if (node.Callee is Identifier ident && ident.Name == "require" && node.Arguments.Count == 1)
        {
            if (StaticString(node.Arguments[0]) is { } specifier)
            {
                _elements.Add(node);
                _tasks.Add(_report(_bundle, _current, specifier, default));
            }
            else if (TryExpandContext(node.Arguments[0], dynamic: false) is { } expanded)
            {
                // A context require — `require(`./dir/${x}.ext`)` — expanded into a
                // selector over synchronous requires of the matching files.
                return expanded;
            }
            else
            {
                // A require() whose argument isn't a constant specifier — left as a
                // real runtime require. The entry bundle's runtime adds a native
                // fallback so it still works on Node/Deno.
                _hasDynamicRequire = true;
            }
        }

        return base.VisitCallExpression(node);
    }

    /// <summary>
    /// Evaluates a <c>require()</c> / <c>import()</c> argument to a constant module
    /// specifier when it is statically knowable, so accidental-dynamic forms are
    /// still bundled: a plain string, a parenthesized one, a template literal with
    /// no substitutions (<c>`./a`</c>), and concatenation of any of these
    /// (<c>"./" + "a"</c>). A genuinely dynamic argument (a variable, a call, a
    /// template with substitutions) returns null and is left untouched — a real
    /// runtime <c>require</c>/<c>import</c>.
    /// </summary>
    private static string? StaticString(Expression expression) => expression switch
    {
        StringLiteral s => s.Value,
        ParenthesizedExpression p => StaticString(p.Expression),
        TemplateLiteral { Expressions.Count: 0, Quasis.Count: 1 } t => t.Quasis[0].Cooked,
        BinaryExpression { Operator: TokenKind.Plus } b
            => StaticString(b.Left) is { } left && StaticString(b.Right) is { } right
                ? left + right
                : null,
        _ => null,
    };
}
