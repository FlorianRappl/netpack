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

    /// <summary>
    /// The JSX factory used to lower this module's JSX elements (the replacement
    /// for <c>React.createElement</c>). Resolved per file from a leading
    /// <c>@jsx</c> pragma or the project's <c>tsconfig.json</c>; defaults to
    /// <c>React.createElement</c>.
    /// </summary>
    public string JsxFactory { get; set; } = "React.createElement";

    /// <summary>
    /// The JSX fragment factory used for <c>&lt;&gt;...&lt;/&gt;</c> shorthand
    /// (the replacement for <c>React.Fragment</c>). Resolved from a leading
    /// <c>@jsxFrag</c> pragma or <c>tsconfig.json</c>; defaults to
    /// <c>React.Fragment</c>.
    /// </summary>
    public string JsxFragmentFactory { get; set; } = "React.Fragment";
}
