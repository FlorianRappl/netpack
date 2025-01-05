namespace NetPack.Fragments;

using AngleSharp.Css.Dom;
using NetPack.Graph;

public class CssFragment(Node root, ICssStyleSheet stylesheet, IDictionary<ICssProperty, Node> replacements)
{
    public Node Root => root;

    public ICssStyleSheet Stylesheet => stylesheet;

    public IDictionary<ICssProperty, Node> Replacements => replacements;
}
