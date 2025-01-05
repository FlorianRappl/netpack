namespace NetPack.Fragments;

using NetPack.Graph;

public class HtmlFragment(Node root, AngleSharp.Dom.IDocument document, Dictionary<AngleSharp.Dom.IElement, Node> replacements)
{
    public Node Root => root;

    public AngleSharp.Dom.IDocument Document => document;

    public IDictionary<AngleSharp.Dom.IElement, Node> Replacements => replacements;
}
