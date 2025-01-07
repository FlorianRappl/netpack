namespace NetPack.Graph.Visitors;

using AngleSharp.Css.Dom;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using static NetPack.Helpers;

class CssVisitor(Bundle bundle, Node current, Func<Bundle?, Node, string, Task<Node?>> report)
{
    private readonly Func<Bundle?, Node, string, Task<Node?>> _report = report;
    private readonly Bundle _bundle = bundle;
    private readonly Node _current = current;
    private readonly List<ICssProperty> _properties = [];
    private readonly List<Task<Node?>> _tasks = [];

    public async Task<CssFragment> FindChildren(ICssStyleSheet sheet)
    {
        foreach (var rule in sheet.Rules)
        {
            if (rule is ICssStyleRule style)
            {
                foreach (var decl in style.Style)
                {
                    var path = decl.RawValue.AsUrl();

                    if (path is not null)
                    {
                        _properties.Add(decl);
                        _tasks.Add(_report(_bundle, _current, path));
                    }
                }
            }
        }

        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _properties);
        return new CssFragment(_current, sheet, replacements);
    }
}