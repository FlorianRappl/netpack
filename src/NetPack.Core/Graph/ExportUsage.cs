namespace NetPack.Graph;

using System.Collections.Generic;
using NetPack.Fragments;
using NetPack.Syntax.Optimizer;
using Ast = NetPack.Syntax.Ast;

/// <summary>
/// Computes, for every JavaScript module in the graph, which of its exports are
/// actually consumed by the rest of the program — the input to
/// <see cref="TreeShaker"/>.
///
/// The model is a deliberate over-approximation (it may keep an export that is
/// not strictly needed, but never drops one that is): a module's export is used
/// when some importer imports it by name; a bundle root, a namespace import, a
/// CommonJS <c>require</c>, a dynamic <c>import()</c>, or an <c>export * from</c>
/// all mark the whole module as used.
/// </summary>
public static class ExportUsage
{
    public static Dictionary<Node, UsedExports> Compute(BundlerContext context)
    {
        var result = new Dictionary<Node, UsedExports>();

        UsedExports For(Node node)
        {
            if (!result.TryGetValue(node, out var used))
            {
                used = new UsedExports();
                result[node] = used;
            }
            return used;
        }

        // Every module starts tracked (so side-effect-only modules end up with an
        // empty used-set and can have their exports pruned)…
        foreach (var node in context.JsFragments.Keys)
        {
            For(node);
        }

        // …bundle roots expose their exports to the outside world.
        foreach (var root in context.Bundles.Keys)
        {
            For(root).MarkAll();
        }

        foreach (var fragment in context.JsFragments.Values)
        {
            foreach (var pair in fragment.Replacements)
            {
                var target = pair.Value;
                switch (pair.Key)
                {
                    case Ast.ImportDeclaration import:
                        if (import.Specifiers.Count == 0)
                        {
                            break; // side-effect-only import
                        }
                        foreach (var specifier in import.Specifiers)
                        {
                            switch (specifier)
                            {
                                case Ast.ImportNamespaceSpecifier:
                                    For(target).MarkAll();
                                    break;
                                case Ast.ImportDefaultSpecifier:
                                    For(target).Add("default");
                                    break;
                                case Ast.ImportSpecifier named:
                                    For(target).Add(NameOf(named.Imported));
                                    break;
                            }
                        }
                        break;

                    case Ast.ExportNamedDeclaration reExport when reExport.Source is not null:
                        foreach (var specifier in reExport.Specifiers)
                        {
                            For(target).Add(NameOf(specifier.Local));
                        }
                        break;

                    case Ast.ExportAllDeclaration:
                    case Ast.CallExpression:   // require(...)
                    case Ast.ImportExpression: // import(...)
                        For(target).MarkAll();
                        break;
                }
            }
        }

        return result;
    }

    private static string NameOf(Ast.Node node) => node switch
    {
        Ast.Identifier id => id.Name,
        Ast.StringLiteral literal => literal.Value,
        _ => string.Empty,
    };
}
