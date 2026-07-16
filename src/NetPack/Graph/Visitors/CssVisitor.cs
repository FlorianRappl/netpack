namespace NetPack.Graph.Visitors;

using AngleSharp.Css.Dom;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using static NetPack.Helpers;

class CssVisitor(Bundle bundle, Node current, Func<Bundle?, Node, string, (int? Width, int? Height, string? Format), Task<Node?>> report)
{
    private readonly Func<Bundle?, Node, string, (int? Width, int? Height, string? Format), Task<Node?>> _report = report;
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
                var variant = GetBackgroundSizeVariant(style);

                foreach (var decl in style.Style)
                {
                    var path = decl.RawValue.AsUrl();

                    if (path is not null)
                    {
                        // Only `background-image`/`background` (the properties a
                        // sibling `background-size` actually sizes) request a
                        // variant; other url()-bearing declarations in the same
                        // rule (e.g. a `cursor` or `list-style-image` that happens
                        // to share the rule) are left at their original size.
                        var isBackgroundImage = decl.Name is "background-image" or "background";
                        _properties.Add(decl);
                        _tasks.Add(_report(_bundle, _current, path, isBackgroundImage ? variant : default));
                    }
                }
            }
        }

        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _properties);
        return new CssFragment(_current, sheet, replacements);
    }

    /// <summary>
    /// Reads a `background-size` declaration in the same rule as a pixel
    /// width/height pair, so the rule's background image gets a resized variant
    /// matching the box it's painted into. Only literal `px` lengths are
    /// understood — keywords (`cover`, `contain`, `auto` alone) and relative
    /// units (`%`, `vw`, …) can't be resolved to an absolute pixel size at build
    /// time, so those leave the image at its original size.
    /// </summary>
    private static (int? Width, int? Height, string? Format) GetBackgroundSizeVariant(ICssStyleRule style)
    {
        // No CSS declaration maps to an output format — that only comes from a
        // `?format=` query string on the url() itself, parsed centrally in
        // Traverse.InnerProcess.
        foreach (var decl in style.Style)
        {
            if (decl.Name == "background-size")
            {
                var (width, height) = ParseBackgroundSize(decl.Value);
                return (width, height, null);
            }
        }

        return default;
    }

    private static (int? Width, int? Height) ParseBackgroundSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        var tokens = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var width = tokens.Length > 0 ? ParsePixelLength(tokens[0]) : null;
        var height = tokens.Length > 1 ? ParsePixelLength(tokens[1]) : null;
        return (width, height);
    }

    private static int? ParsePixelLength(string token)
    {
        return token.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(
                token[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var pixels)
            && pixels > 0
                ? (int)Math.Round(pixels)
                : null;
    }
}