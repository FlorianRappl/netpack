namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using System.Linq;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// The native ECMAScript module format: real <c>import</c> / <c>export</c>
/// statements, dynamic <c>import()</c>, and <c>import.meta.url</c>-relative
/// references for chunks and assets. This is netpack's default and needs no outer
/// envelope.
/// </summary>
sealed class EsmModuleFormat : JsModuleFormat
{
    public override Ast.Statement ImportSharedBundle(Ast.Identifier local, string fileName)
        => new Ast.ImportDeclaration(
            new List<Ast.ImportSpecifierBase> { new Ast.ImportDefaultSpecifier(local) },
            MakeString($"./{fileName}"), false);

    public override Ast.Statement RewriteExternalImport(Ast.ImportDeclaration declaration) => declaration;

    public override IReadOnlyList<Ast.Statement> ExportRegistry(Ast.Identifier registry)
        => new List<Ast.Statement> { new Ast.ExportDefaultDeclaration(registry) };

    public override IReadOnlyList<Ast.Statement> ExportRoot(Ast.Expression rootRequire, IReadOnlyList<string> exportNames)
    {
        var trailer = new List<Ast.Statement>();

        if (exportNames.Count == 0)
        {
            trailer.Add(new Ast.ExportDefaultDeclaration(rootRequire));
            return trailer;
        }

        var offset = 0;
        // A fresh node per bundle (bundles are stringified/mangled in parallel,
        // so a shared mutable Identifier would be raced across them).
        var defaultLocal = new Ast.Identifier("_default");
        var properties = new List<Ast.Node>();

        foreach (var name in exportNames)
        {
            properties.Add(name == "default"
                ? new Ast.Property(new Ast.Identifier(name), defaultLocal, Ast.PropertyKind.Init, false, false, false)
                : new Ast.Property(new Ast.Identifier(name), new Ast.Identifier(name), Ast.PropertyKind.Init, false, true, false));
        }

        trailer.Add(new Ast.VariableStatement(Ast.VariableKind.Const, new List<Ast.VariableDeclarator>
        {
            new Ast.VariableDeclarator(new Ast.ObjectExpression(properties), rootRequire),
        }));

        if (exportNames.Contains("default"))
        {
            offset = 1;
            trailer.Add(new Ast.ExportDefaultDeclaration(defaultLocal));
        }

        if (exportNames.Count > offset)
        {
            var names = exportNames
                .Where(name => name != "default")
                .Select(name => new Ast.ExportSpecifier(new Ast.Identifier(name), new Ast.Identifier(name), false))
                .ToList();
            trailer.Add(new Ast.ExportNamedDeclaration(null, names, null, false));
        }

        return trailer;
    }

    public override Ast.Expression AutoReference(string fileName)
    {
        var importMeta = new Ast.MemberExpression(new Ast.Identifier("import"), new Ast.Identifier("meta"), false, false);
        var importMetaUrl = new Ast.MemberExpression(importMeta, new Ast.Identifier("url"), false, false);
        var urlParse = new Ast.MemberExpression(new Ast.Identifier("URL"), new Ast.Identifier("parse"), false, false);
        var relative = new Ast.CallExpression(urlParse, new List<Ast.Expression> { MakeString($"./{fileName}"), importMetaUrl }, false);
        return new Ast.MemberExpression(relative, new Ast.Identifier("href"), false, false);
    }
}
