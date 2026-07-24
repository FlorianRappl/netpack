namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using NetPack.Syntax;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// Shared helpers for the non-ESM <see cref="JsModuleFormat"/> implementations:
/// parsing runtime snippets to AST, lowering an ESM <c>import</c> to a
/// destructuring binding against an already-resolved module expression, and
/// splicing the assembled bundle body into a parsed envelope template.
/// </summary>
static class FormatSupport
{
    private static readonly ParserOptions Options = new() { Tolerant = true, TypeScript = false, Jsx = false };

    /// <summary>Parses a snippet of ordinary JavaScript into its statements.</summary>
    public static List<Ast.Statement> Parse(string source)
        => new(Parser.ParseModule(source, "netpack:format", Options).Body);

    /// <summary>
    /// Lowers an ESM <c>import</c>'s specifiers to a <c>const … = source</c> binding
    /// against <paramref name="source"/> — the resolved module expression (a
    /// <c>require(…)</c> call, a factory parameter, a SystemJS setter variable, …).
    /// </summary>
    public static Ast.Statement BindImport(Ast.ImportDeclaration import, Ast.Expression source)
    {
        var declarators = new List<Ast.VariableDeclarator>();
        var properties = new List<Ast.Node>();
        var init = source;

        foreach (var specifier in import.Specifiers)
        {
            if (specifier is Ast.ImportNamespaceSpecifier)
            {
                var id = new Ast.Identifier(specifier.Local.Name);
                declarators.Add(new Ast.VariableDeclarator(id, init));
                init = id;
            }
            else
            {
                properties.Add(new Ast.Property(ImportedName(specifier), specifier.Local, Ast.PropertyKind.Init, false, false, false));
            }
        }

        if (properties.Count > 0)
        {
            declarators.Add(new Ast.VariableDeclarator(new Ast.ObjectExpression(properties), init));
        }

        if (declarators.Count == 0)
        {
            // A side-effect-only import (`import "x"`): evaluate for effect.
            return new Ast.ExpressionStatement(init);
        }

        return new Ast.VariableStatement(Ast.VariableKind.Const, declarators);
    }

    // A fresh key node (see the note on JsBundle.GetImportName): the parser shares
    // one Identifier for a specifier's imported and local names, so reusing it as
    // the destructuring key would let the mangler rename the property key too.
    private static Ast.Node ImportedName(Ast.ImportSpecifierBase specifier) => specifier switch
    {
        Ast.ImportSpecifier { Imported: Ast.Identifier id } => new Ast.Identifier(id.Name),
        Ast.ImportSpecifier spec => spec.Imported,
        Ast.ImportDefaultSpecifier => new Ast.Identifier("default"),
        _ => specifier.Local,
    };

    /// <summary>
    /// Parses an envelope <paramref name="template"/> and replaces the body of the
    /// block flagged with a leading <c>"__NETPACK_BODY__";</c> marker with
    /// <paramref name="body"/> — used to wrap the bundle in a UMD IIFE or a
    /// SystemJS <c>System.register</c> factory.
    /// </summary>
    public static Ast.SourceFile Inject(string template, IList<Ast.Statement> body)
    {
        var parsed = Parser.ParseModule(template, "netpack:envelope", Options);
        new BodyInjector(body).Visit(parsed);
        return parsed;
    }

    private sealed class BodyInjector(IList<Ast.Statement> body) : Ast.AstRewriter
    {
        protected override Ast.Node VisitBlockStatement(Ast.BlockStatement node)
        {
            if (node.Body.Count > 0
                && node.Body[0] is Ast.ExpressionStatement { Expression: Ast.StringLiteral { Value: "__NETPACK_BODY__" } })
            {
                node.Body.Clear();

                foreach (var statement in body)
                {
                    node.Body.Add(statement);
                }

                return node;
            }

            return base.VisitBlockStatement(node);
        }
    }
}
