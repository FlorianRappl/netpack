namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using System.Linq;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// SystemJS output: the bundle is wrapped in <c>System.register([deps], (…) =&gt; …)</c>.
/// Dependencies are received through per-dependency <c>setters</c>, the entry is
/// published with <c>_export</c>, dynamic imports go through <c>_context.import</c>,
/// and module-relative references resolve against <c>_context.meta.url</c>.
/// </summary>
sealed class SystemJsModuleFormat : JsModuleFormat
{
    private readonly List<(string Specifier, string Variable)> _deps = [];
    private int _externals;

    public override Ast.Statement ImportSharedBundle(Ast.Identifier local, string fileName)
    {
        var variable = NextDep(Ref(fileName));
        // A shared bundle exposes its registry as the default export.
        var registry = new Ast.MemberExpression(new Ast.Identifier(variable), new Ast.Identifier("default"), false, false);
        return new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
        {
            new Ast.VariableDeclarator(local, registry),
        });
    }

    public override Ast.Statement RewriteExternalImport(Ast.ImportDeclaration declaration)
    {
        var variable = NextDep(declaration.Source.Value);
        return FormatSupport.BindImport(declaration, new Ast.Identifier(variable));
    }

    public override IReadOnlyList<Ast.Statement> ExportRegistry(Ast.Identifier registry)
        => new List<Ast.Statement> { Export(MakeString("default"), registry) };

    public override IReadOnlyList<Ast.Statement> ExportRoot(Ast.Expression rootRequire, IReadOnlyList<string> exportNames)
        => new List<Ast.Statement> { new Ast.ExpressionStatement(ExportCall(rootRequire)) };

    public override Ast.Expression AutoReference(string fileName)
    {
        var meta = new Ast.MemberExpression(new Ast.Identifier("_context"), new Ast.Identifier("meta"), false, false);
        var metaUrl = new Ast.MemberExpression(meta, new Ast.Identifier("url"), false, false);
        var url = new Ast.NewExpression(new Ast.Identifier("URL"),
            new List<Ast.Expression> { MakeString(Ref(fileName)), metaUrl });
        return new Ast.MemberExpression(url, new Ast.Identifier("href"), false, false);
    }

    public override Ast.Expression DynamicImport(Ast.Expression target)
    {
        var contextImport = new Ast.MemberExpression(new Ast.Identifier("_context"), new Ast.Identifier("import"), false, false);
        return new Ast.CallExpression(contextImport, new List<Ast.Expression> { target }, false);
    }

    public override Ast.SourceFile Wrap(Ast.SourceFile module)
    {
        var depNames = string.Join(", ", _deps.Select(d => Quote(d.Specifier)));
        var declarations = _deps.Count > 0 ? "var " + string.Join(", ", _deps.Select(d => d.Variable)) + ";" : "";
        var setters = string.Join(", ", _deps.Select(d => $"function (m) {{ {d.Variable} = m; }}"));

        var template =
            "System.register([" + depNames + "], function (_export, _context) {\n" +
            "  " + declarations + "\n" +
            "  return {\n" +
            "    setters: [" + setters + "],\n" +
            "    execute: function () { \"__NETPACK_BODY__\"; }\n" +
            "  };\n" +
            "});\n";

        return FormatSupport.Inject(template, module.Body);
    }

    private string NextDep(string specifier)
    {
        var variable = $"__dep_{_externals++}";
        _deps.Add((specifier, variable));
        return variable;
    }

    private static Ast.Statement Export(Ast.Expression name, Ast.Expression value)
        => new Ast.ExpressionStatement(new Ast.CallExpression(
            new Ast.Identifier("_export"), new List<Ast.Expression> { name, value }, false));

    private static Ast.CallExpression ExportCall(Ast.Expression value)
        => new(new Ast.Identifier("_export"), new List<Ast.Expression> { value }, false);

    private static string Quote(string value) => CssModules.JsString(value);
}
