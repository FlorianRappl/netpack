namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// Universal Module Definition output: the whole bundle is wrapped in the classic
/// UMD IIFE that adapts to CommonJS, AMD, or a browser global. Externals and
/// sibling bundles become factory parameters (resolved via <c>require</c>,
/// <c>define([…])</c> or <c>root[…]</c>), and the entry is returned from the
/// factory. The browser-global name is derived from the entry's file name.
/// </summary>
sealed class UmdModuleFormat : JsModuleFormat
{
    private const string BaseUrl = "__nurl";
    private const string Interop = "__umdInterop";

    private readonly List<(string Specifier, string Local)> _deps = [];
    private int _externals;
    private bool _needsBaseUrl;
    private bool _needsInterop;

    public override Ast.Statement ImportSharedBundle(Ast.Identifier local, string fileName)
    {
        // The binding is provided directly by the matching factory parameter.
        _deps.Add(($"./{fileName}", local.Name));
        return new Ast.EmptyStatement();
    }

    public override Ast.Statement RewriteExternalImport(Ast.ImportDeclaration declaration)
    {
        var param = $"__dep_{_externals++}";
        _deps.Add((declaration.Source.Value, param));

        if (declaration.Specifiers.Count == 0)
        {
            return new Ast.EmptyStatement();
        }

        // The factory receives the raw module (require / define / global), so apply
        // the same default interop the CommonJS format does.
        _needsInterop = true;
        var interop = new Ast.CallExpression(new Ast.Identifier(Interop),
            new List<Ast.Expression> { new Ast.Identifier(param) }, false);
        return FormatSupport.BindImport(declaration, interop);
    }

    public override IReadOnlyList<Ast.Statement> ExportRegistry(Ast.Identifier registry)
        => new List<Ast.Statement> { new Ast.ReturnStatement(registry) };

    public override IReadOnlyList<Ast.Statement> ExportRoot(Ast.Expression rootRequire, IReadOnlyList<string> exportNames)
        => new List<Ast.Statement> { new Ast.ReturnStatement(rootRequire) };

    public override Ast.Expression AutoReference(string fileName)
    {
        _needsBaseUrl = true;
        var url = new Ast.NewExpression(new Ast.Identifier("URL"),
            new List<Ast.Expression> { MakeString($"./{fileName}"), new Ast.Identifier(BaseUrl) });
        return new Ast.MemberExpression(url, new Ast.Identifier("href"), false, false);
    }

    public override Ast.SourceFile Wrap(Ast.SourceFile module)
    {
        var requireDeps = string.Join(", ", _deps.Select(d => $"require({Quote(d.Specifier)})"));
        var depNames = string.Join(", ", _deps.Select(d => Quote(d.Specifier)));
        var rootDeps = string.Join(", ", _deps.Select(d => $"root[{Quote(d.Specifier)}]"));
        var parameters = string.Join(", ", _deps.Select(d => d.Local));
        var global = Quote(GlobalName(module.FileName));

        var template =
            "(function (root, factory) {\n" +
            "  if (typeof exports === \"object\" && typeof module !== \"undefined\") { module.exports = factory(" + requireDeps + "); }\n" +
            "  else if (typeof define === \"function\" && define.amd) { define([" + depNames + "], factory); }\n" +
            "  else { root[" + global + "] = factory(" + rootDeps + "); }\n" +
            "})(typeof self !== \"undefined\" ? self : this, function (" + parameters + ") { \"__NETPACK_BODY__\"; });\n";

        var body = new List<Ast.Statement>();

        if (_needsInterop)
        {
            body.AddRange(FormatSupport.Parse(
                $"function {Interop}(m) {{ return m && m.__esModule ? m : Object.assign({{ default: m }}, m); }}"));
        }

        if (_needsBaseUrl)
        {
            // Best effort: the running script's URL in the browser, else __filename.
            body.AddRange(FormatSupport.Parse(
                $"const {BaseUrl} = typeof document !== \"undefined\" && document.currentScript ? document.currentScript.src : (typeof __filename !== \"undefined\" ? __filename : \"\");"));
        }

        body.AddRange(module.Body);
        return FormatSupport.Inject(template, body);
    }

    private static string Quote(string value) => CssModules.JsString(value);

    private static string GlobalName(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var sb = new StringBuilder();

        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '$' ? c : '_');
        }

        var result = sb.ToString();
        return result.Length == 0 || char.IsDigit(result[0]) ? "_" + result : result;
    }
}
