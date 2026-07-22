namespace NetPack.Graph.Bundles;

using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html;
using NetPack.Json;

public sealed class HtmlBundle(BundlerContext context, Graph.Node root, BundleFlags flags) : Bundle(context, root, flags)
{
    // Dev-server client: listens for hot updates and applies them through the
    // bundle's HMR runtime (globalThis.__netpack), falling back to a full reload
    // when an update cannot be applied or the runtime is unavailable.
    private const string HmrClient =
        "(function(){var s=new EventSource('/netpack');function r(){location.reload();}" +
        "s.addEventListener('reload',r);s.addEventListener('change',r);" +
        "s.addEventListener('update',function(e){try{var n=globalThis.__netpack;if(!n)return r();" +
        "var p=JSON.parse(e.data);n.apply(p.m.map(function(u){return{id:u.i,factory:(0,eval)('('+u.c+')')};}));}" +
        "catch(err){r();}});})();";

    public override Task<Stream> CreateStream(OutputOptions options)
    {
        var src = new MemoryStream();
        Stringify(src, options);
        src.Position = 0;
        return Task.FromResult<Stream>(src);
    }

    private void Stringify(MemoryStream ms, OutputOptions options)
    {
        var fragments = _context.HtmlFragments;

        if (fragments.TryGetValue(Root, out var root))
        {
            var replacements = root.Replacements;
            var document = root.Document;

            foreach (var replacement in replacements)
            {
                var element = replacement.Key;
                var node = replacement.Value;
                var url = Helpers.PublicUrl(options.PublicPath, GetReference(node));

                switch (element.LocalName)
                {
                    case "link":
                    case "a":
                        element.SetAttribute("href", url);
                        break;
                    case "script":
                        element.SetAttribute("type", "module");
                        element.SetAttribute("src", url);
                        break;
                    case "img":
                    case "video":
                    case "audio":
                    case "source":
                    case "iframe":
                        element.SetAttribute("src", url);
                        break;
                    case "object":
                        element.SetAttribute("data", url);
                        break;
                    case "meta":
                        element.SetAttribute("content", url);
                        break;
                }
            }

            if (_context.Shared.Count > 0)
            {
                var importmap = document.QuerySelector("script[type=importmap]");

                if (importmap is null)
                {
                    importmap = document.CreateElement("script");
                    importmap.SetAttribute("type", "importmap");
                    document.Head!.AppendChild(importmap);
                }

                var content = ReadImportmap(importmap);

                foreach (var dependency in _context.Shared)
                {
                    var name = Helpers.ToFileName(dependency);
                    content.Imports!.Add(dependency, Helpers.PublicUrl(options.PublicPath, $"{name}.js"));
                }

                WriteImportmap(importmap, content);
            }

            if (options.IsOptimizing)
            {
                foreach (var node in document.Head!.ChildNodes.OfType<IText>().ToArray())
                {
                    document.Head.RemoveChild(node);
                }

                foreach (var node in document.DocumentElement.ChildNodes.OfType<IText>().ToArray())
                {
                    document.DocumentElement.RemoveChild(node);
                }

                if (document.Body!.ChildNodes.LastOrDefault() is IText text && string.IsNullOrWhiteSpace(text.Data))
                {
                    document.Body.RemoveChild(text);
                }
            }

            if (options.IsReloading)
            {
                // Inject the full HMR client: it handles `update` events (hot-swap
                // via globalThis.__netpack) and falls back to a full page reload
                // for `reload`/`change` events or when no module accepts an update.
                var child = document.CreateElement("script");
                child.TextContent = HmrClient;
                document.Body?.AppendChild(child);
            }

            var formatter = options.IsOptimizing ? MinifyMarkupFormatter.Instance : HtmlMarkupFormatter.Instance;
            using var writer = new StreamWriter(ms, Encoding.UTF8, -1, true);
            document.ToHtml(writer, formatter);
        }
    }

    private static void WriteImportmap(IElement importmap, Importmap content)
    {
        var source = JsonSerializer.Serialize(content, SourceGenerationContext.Default.Importmap);
        importmap.TextContent = source;
    }

    private static Importmap ReadImportmap(IElement importmap)
    {
        var source = importmap.TextContent;
        
        try
        {
            var current = JsonSerializer.Deserialize(source, SourceGenerationContext.Default.Importmap);

            if (current?.Imports is not null)
            {
                return current;
            }
        }
        catch
        {
            // Ignore importmap issues
        }

        return new Importmap
        {
            Imports = [],
        };
    }
}
