namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using NetPack.Syntax;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// CommonJS output: sibling bundles and externals are loaded with
/// <c>require(…)</c>, the bundle is exposed through <c>module.exports</c>, dynamic
/// imports use native <c>import()</c> (supported in Node CJS), and module-relative
/// references resolve against a <c>__filename</c>-derived base URL. Targets Node.
/// </summary>
sealed class CommonJsModuleFormat : JsModuleFormat
{
    private const string BaseUrl = "__nurl";
    private const string Interop = "__cjsInterop";

    private bool _needsInterop;
    private bool _needsBaseUrl;

    public override Ast.Statement ImportSharedBundle(Ast.Identifier local, string fileName)
        => new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
        {
            new Ast.VariableDeclarator(local, Require(Ref(fileName))),
        });

    public override Ast.Statement RewriteExternalImport(Ast.ImportDeclaration declaration)
    {
        var required = Require(declaration.Source.Value);

        if (declaration.Specifiers.Count == 0)
        {
            return new Ast.ExpressionStatement(required);
        }

        _needsInterop = true;
        var interop = new Ast.CallExpression(new Ast.Identifier(Interop), new List<Ast.Expression> { required }, false);
        return FormatSupport.BindImport(declaration, interop);
    }

    public override IReadOnlyList<Ast.Statement> ExportRegistry(Ast.Identifier registry)
        => new List<Ast.Statement> { ModuleExports(registry) };

    public override IReadOnlyList<Ast.Statement> ExportRoot(Ast.Expression rootRequire, IReadOnlyList<string> exportNames)
        => new List<Ast.Statement> { ModuleExports(rootRequire) };

    public override Ast.Expression AutoReference(string fileName)
    {
        _needsBaseUrl = true;
        var url = new Ast.NewExpression(new Ast.Identifier("URL"),
            new List<Ast.Expression> { MakeString(Ref(fileName)), new Ast.Identifier(BaseUrl) });
        return new Ast.MemberExpression(url, new Ast.Identifier("href"), false, false);
    }

    public override Ast.SourceFile Wrap(Ast.SourceFile module)
    {
        if (!_needsInterop && !_needsBaseUrl)
        {
            return module;
        }

        var body = new List<Ast.Statement>();

        if (_needsInterop)
        {
            body.AddRange(FormatSupport.Parse(
                $"function {Interop}(m) {{ return m && m.__esModule ? m : Object.assign({{ default: m }}, m); }}"));
        }

        if (_needsBaseUrl)
        {
            body.AddRange(FormatSupport.Parse(
                $"const {BaseUrl} = require(\"url\").pathToFileURL(__filename).href;"));
        }

        body.AddRange(module.Body);
        return new Ast.SourceFile(module.FileName, body, module.Diagnostics);
    }

    private static Ast.CallExpression Require(string specifier)
        => new(new Ast.Identifier("require"), new List<Ast.Expression> { MakeString(specifier) }, false);

    private static Ast.Statement ModuleExports(Ast.Expression value)
    {
        var target = new Ast.MemberExpression(new Ast.Identifier("module"), new Ast.Identifier("exports"), false, false);
        return new Ast.ExpressionStatement(new Ast.AssignmentExpression(TokenKind.Equals, target, value));
    }
}
