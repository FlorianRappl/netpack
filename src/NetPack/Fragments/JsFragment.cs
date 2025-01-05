namespace NetPack.Fragments;

using NetPack.Graph;

public class JsFragment(Node root, Acornima.Ast.Module ast, IDictionary<Acornima.Ast.Node, Node> replacements, string[] exportNames)
{
    public Node Root => root;
    
    public Acornima.Ast.Module Ast => ast;

    public IDictionary<Acornima.Ast.Node, Node> Replacements => replacements;

    public string[] ExportNames => exportNames;
}
