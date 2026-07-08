namespace NetPack.Fragments;

using NetPack.Syntax.Ast;
using GraphNode = NetPack.Graph.Node;

/// <summary>
/// The parsed representation of a JavaScript/TypeScript module: its NetPack AST,
/// the collected export names, and the map from reference-bearing AST nodes
/// (imports, requires, dynamic imports) to the graph nodes they resolve to.
/// </summary>
public class JsFragment(GraphNode root, SourceFile ast, IDictionary<Node, GraphNode> replacements, string[] exportNames)
{
    public GraphNode Root => root;

    public SourceFile Ast => ast;

    /// <summary>Maps an AST node (import/require/dynamic-import) to the resolved graph node.</summary>
    public IDictionary<Node, GraphNode> Replacements => replacements;

    public string[] ExportNames => exportNames;
}
