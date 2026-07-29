namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// Asset inlining: when <c>InlineLimit</c> is set, assets below the threshold are
/// embedded as <c>data:…;base64,…</c> URIs in the referencing bundle instead of
/// being emitted as separate files. Covers JS imports, CSS <c>url()</c>, and HTML
/// element references.
/// </summary>
public class AssetInliningTests
{
    // ------------------------------------------------------------------ helpers

    private static OutputOptions Options(int inlineLimit = 0, string publicPath = "", ModuleFormat format = ModuleFormat.Esm, bool optimizing = false)
        => new() { IsOptimizing = optimizing, IsReloading = false, PublicPath = publicPath, Format = format, InlineLimit = inlineLimit };

    private static async Task<string> BundleJsWithAsset(OutputOptions options, string entryJs, byte[] assetBytes, string assetName = "logo.png")
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), entryJs);
            await File.WriteAllBytesAsync(Path.Combine(dir, assetName), assetBytes);

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

            return bundle.Stringify(options);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<string> BundleHtmlWithAsset(OutputOptions options, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            var html = graph.Context.Bundles.Values.OfType<HtmlBundle>().First();
            using var stream = await html.CreateStream(options);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<string> BundleCssWithAsset(OutputOptions options, string cssContent, byte[] assetBytes, string assetName = "logo.png")
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head><link rel=\"stylesheet\" href=\"./app.css\"></head><body></body></html>");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.css"), cssContent);
            await File.WriteAllBytesAsync(Path.Combine(dir, assetName), assetBytes);

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            var css = graph.Context.Bundles.Values.OfType<CssBundle>().First();
            using var stream = await css.CreateStream(options);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -------------------------------------------------------- JS import / require

    [Fact]
    public async Task JS_import_of_small_asset_is_inlined_as_data_uri()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("logo.png", output);
    }

    [Fact]
    public async Task JS_import_of_large_asset_stays_as_file_reference()
    {
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("logo.", output); // emitted with hash
    }

    [Fact]
    public async Task JS_require_of_small_asset_is_inlined_as_data_uri()
    {
        var bytes = new byte[] { 5, 6, 7, 8, 9 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.CommonJs),
            "const url = require('./logo.png'); module.exports = url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("logo.png", output);
    }

    [Fact]
    public async Task JS_asset_not_inlined_when_limit_is_zero()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 0),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("logo.", output);
    }

    [Fact]
    public async Task JS_asset_at_exact_threshold_is_inlined()
    {
        var bytes = new byte[50];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 50),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
    }

    [Fact]
    public async Task JS_asset_one_byte_over_threshold_is_not_inlined()
    {
        var bytes = new byte[51];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 50),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.DoesNotContain("data:image/png;base64,", output);
    }

    [Fact]
    public async Task JS_inlining_works_with_minification()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, optimizing: true),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
    }

    [Fact]
    public async Task JS_inlined_asset_ignores_public_path()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, publicPath: "https://cdn.test/assets"),
            "import url from './logo.png'; export default url;",
            bytes);

        // Data URI is absolute — no public path prefix should appear on it.
        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("https://cdn.test/assets/data:", output);
    }

    [Fact]
    public async Task JS_non_inlined_asset_gets_public_path()
    {
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, publicPath: "https://cdn.test/assets"),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.Contains("https://cdn.test/assets/logo.", output);
    }

    // ------------------------------------------------------------------ CSS url()

    [Fact]
    public async Task CSS_small_asset_is_inlined_in_url()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleCssWithAsset(
            Options(inlineLimit: 100),
            ".a { background: url(./logo.png); }",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("logo.png", output);
    }

    [Fact]
    public async Task CSS_large_asset_stays_as_file_reference()
    {
        var bytes = new byte[2000];
        var output = await BundleCssWithAsset(
            Options(inlineLimit: 100),
            ".a { background: url(./logo.png); }",
            bytes);

        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("logo.", output);
    }

    [Fact]
    public async Task CSS_inlined_asset_with_public_path_uses_data_uri_not_public_path()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var output = await BundleCssWithAsset(
            Options(inlineLimit: 100, publicPath: "https://cdn.test/assets"),
            ".a { background: url(./logo.png); }",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("https://cdn.test/assets/data:", output);
    }

    [Fact]
    public async Task CSS_non_inlined_asset_gets_public_path()
    {
        var bytes = new byte[2000];
        var output = await BundleCssWithAsset(
            Options(inlineLimit: 100, publicPath: "https://cdn.test/assets"),
            ".a { background: url(./logo.png); }",
            bytes);

        Assert.Contains("https://cdn.test/assets/logo.", output);
    }

    // ------------------------------------------------------------------- HTML

    [Fact]
    public async Task HTML_img_small_asset_is_inlined()
    {
        var bytes = new byte[50];
        var output = await BundleHtmlWithAsset(
            Options(inlineLimit: 100),
            ("index.html", "<!doctype html><html><body><img src=\"./icon.png\"></body></html>"),
            ("icon.png", "<svg></svg>")); // string content will be written as text

        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("icon.png", output);
    }

    [Fact]
    public async Task HTML_img_large_asset_stays_as_file()
    {
        // The file content is written as text, which becomes the raw bytes. 2000
        // bytes of utf-8 is well over the 100-byte limit.
        var bigContent = new string('x', 2000);
        var output = await BundleHtmlWithAsset(
            Options(inlineLimit: 100),
            ("index.html", "<!doctype html><html><body><img src=\"./icon.png\"></body></html>"),
            ("icon.png", bigContent));

        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("icon.", output);
    }

    [Fact]
    public async Task HTML_inlined_asset_ignores_public_path()
    {
        var bytes = new byte[50];
        var output = await BundleHtmlWithAsset(
            Options(inlineLimit: 100, publicPath: "https://cdn.test/assets"),
            ("index.html", "<!doctype html><html><body><img src=\"./icon.png\"></body></html>"),
            ("icon.png", new string('a', 50)));

        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("https://cdn.test/assets/data:", output);
    }

    // ------------------------------------------------------------ SVG (image/svg+xml)

    [Fact]
    public async Task JS_small_svg_is_inlined_with_svg_mime_type()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"10\"/></svg>");
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 4096),
            "import icon from './icon.svg'; export default icon;",
            svg,
            "icon.svg");

        Assert.Contains("data:image/svg+xml;base64,", output);
        Assert.DoesNotContain("icon.svg", output);
    }

    // -------------------------------------------------------------- font (woff2)

    [Fact]
    public async Task JS_small_font_is_inlined_with_font_mime_type()
    {
        var font = new byte[100]; // small enough to inline
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 4096),
            "import fontUrl from './font.woff2'; export default fontUrl;",
            font,
            "font.woff2");

        Assert.Contains("data:font/woff2;base64,", output);
    }

    // -------------------------------------------------- bundle result assertions

    [Fact]
    public async Task Inlined_assets_are_not_emitted_as_separate_files()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png'; export default url;",
            bytes);

        // The bundle output should contain the data URI inline
        Assert.Contains("data:image/png;base64,", output);

        // Parse it back to verify it's valid JS (no broken references)
        var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        var reparsed = Parser.ParseModule(output, "out.js", options);
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public async Task Non_inlined_assets_still_emit_valid_js()
    {
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png'; export default url;",
            bytes);

        var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        var reparsed = Parser.ParseModule(output, "out.js", options);
        Assert.Empty(reparsed.Diagnostics);
    }

    // ------------------------------------------------------- different extensions

    [Fact]
    public async Task Unknown_extension_inlines_as_octet_stream()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import data from './file.bin'; export default data;",
            bytes,
            "file.bin");

        Assert.Contains("data:application/octet-stream;base64,", output);
    }

    // -------------------------------------------------- CommonJS format specific

    [Fact]
    public async Task CJS_large_asset_is_not_inlined_and_ref_is_valid()
    {
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.CommonJs),
            "const url = require('./logo.png'); module.exports = url;",
            bytes);

        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("logo.", output);

        // Should be valid JS
        var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        var reparsed = Parser.ParseModule(output, "out.js", options);
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public async Task UMD_format_inlines_small_asset()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.Umd),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);

        var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        var reparsed = Parser.ParseModule(output, "out.js", options);
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public async Task SystemJS_format_inlines_small_asset()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.SystemJs),
            "import url from './logo.png'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);

        var options = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        var reparsed = Parser.ParseModule(output, "out.js", options);
        Assert.Empty(reparsed.Diagnostics);
    }

    // -------------------------------------------------------------- MIME helpers

    [Fact]
    public void GetMimeType_normalizes_leading_dot()
    {
        Assert.Equal("image/png", Helpers.GetMimeType(".png"));
        Assert.Equal("image/png", Helpers.GetMimeType(".PNG"));
        Assert.Equal("image/jpeg", Helpers.GetMimeType(".jpg"));
        Assert.Equal("image/jpeg", Helpers.GetMimeType(".jpeg"));
        Assert.Equal("image/svg+xml", Helpers.GetMimeType(".svg"));
        Assert.Equal("font/woff2", Helpers.GetMimeType(".woff2"));
        Assert.Equal("font/ttf", Helpers.GetMimeType(".ttf"));
        Assert.Equal("application/json", Helpers.GetMimeType(".json"));
        Assert.Equal("text/css", Helpers.GetMimeType(".css"));
        Assert.Equal("application/octet-stream", Helpers.GetMimeType(".unknown"));
    }

    [Fact]
    public void GetMimeType_works_without_leading_dot()
    {
        Assert.Equal("image/png", Helpers.GetMimeType("png"));
        Assert.Equal("image/webp", Helpers.GetMimeType("webp"));
        Assert.Equal("font/woff", Helpers.GetMimeType("woff"));
    }

    [Fact]
    public void ToDataUri_produces_correct_format()
    {
        var uri = Helpers.ToDataUri(".png", new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal("data:image/png;base64,AQID", uri);
    }

    [Fact]
    public void ToDataUri_for_svg()
    {
        var uri = Helpers.ToDataUri(".svg", new byte[] { 65, 66, 67 }); // "ABC"
        Assert.StartsWith("data:image/svg+xml;base64,", uri);
    }

    // ------------------------------------------------------ edge / corner cases

    [Fact]
    public async Task Zero_byte_asset_is_inlined()
    {
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './empty.png'; export default url;",
            Array.Empty<byte>(),
            "empty.png");

        Assert.Contains("data:image/png;base64,", output);
    }

    [Fact]
    public async Task Very_large_inline_limit_inlines_many_assets()
    {
        var bytes = new byte[50000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100000),
            "import url from './big.png'; export default url;",
            bytes,
            "big.png");

        Assert.Contains("data:image/png;base64,", output);
    }

    [Fact]
    public async Task CSS_absolute_url_is_never_inlined()
    {
        var output = await BundleCssWithAsset(
            Options(inlineLimit: 4096),
            ".b { background: url(https://cdn.example.com/x.png); }",
            Array.Empty<byte>());

        // Absolute URLs are not resolved as local assets, so they pass through.
        Assert.Contains("https://cdn.example.com/x.png", output);
    }

    // --------------------------------------------------------------- Bundler API

    [Fact]
    public async Task Bundler_API_inline_limit_is_not_emitted_as_separate_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png'; export default url;");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[] { 1, 2, 3, 4 });

            var result = await Bundler.BundleAsync(
                Path.Combine(dir, "main.js"),
                new BundleOptions { InlineLimit = 100 });

            Assert.Contains(result.Outputs.Keys, k => k.EndsWith(".js"));
            // The logo.png should NOT appear in the outputs since it was inlined.
            Assert.DoesNotContain(result.Files, f => f.Name.Contains("logo.") || f.Name.Contains("png"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bundler_API_large_asset_is_emitted_as_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png'; export default url;");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[2000]);

            var result = await Bundler.BundleAsync(
                Path.Combine(dir, "main.js"),
                new BundleOptions { InlineLimit = 100 });

            Assert.Contains(result.Files, f => f.Name.Contains("logo."));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ----------------------------------------------- mixed inline + non-inline

    [Fact]
    public async Task Mixed_small_and_large_assets_in_same_module()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import small from './small.png'; import large from './large.png';" +
                "export default { small, large };");
            await File.WriteAllBytesAsync(Path.Combine(dir, "small.png"), new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(Path.Combine(dir, "large.png"), new byte[2000]);

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(Options(inlineLimit: 100));

            Assert.Contains("data:image/png;base64,", output);    // small inlined
            Assert.Contains("large.", output);                     // large is file ref
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --------------------------- ESM new URL() pattern not used for data URIs

    [Fact]
    public async Task ESM_inlined_asset_does_not_use_URL_constructor()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.Esm),
            "import url from './logo.png'; export default url;",
            bytes);

        // Inlined: should be a plain string literal, NOT wrapped in new URL(...)
        Assert.Contains("data:image/png;base64,AQIDBA==", output);
        Assert.DoesNotContain("new URL(", output);
    }

    // ---------------------------- CJS __nurl NOT used for data URIs

    [Fact]
    public async Task CJS_inlined_asset_does_not_use_base_url()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.CommonJs),
            "const url = require('./logo.png'); module.exports = url;",
            bytes);

        // Inlined: should be a plain string, NOT use __nurl / URL constructor
        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("__nurl", output);
    }

    // ---------------------------------------- CJS non-inlined still uses __nurl

    [Fact]
    public async Task CJS_large_asset_uses_base_url()
    {
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.CommonJs),
            "const url = require('./logo.png'); module.exports = url;",
            bytes);

        // CJS AutoReference uses new URL(..., __nurl).href for non-inlined assets
        Assert.Contains("__nurl", output);
        Assert.Contains("new URL", output);
    }

    // ---------------------------------------------------------- content hash

    [Fact]
    public async Task Inlined_asset_does_not_appear_in_output_when_using_hash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png'; export default url;");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[] { 1, 2, 3 });

            // Use content-hashed names to verify inlined assets don't mess up hash
            // computation and are not emitted.
            var result = await Bundler.BundleAsync(
                Path.Combine(dir, "main.js"),
                new BundleOptions { InlineLimit = 100, EntryNames = "[name]-[hash]" });

            // The JS bundle should have the hash; the logo.png should NOT be emitted.
            Assert.Contains(result.Outputs.Keys, k => k.EndsWith(".js") && k.Contains("-"));
            Assert.DoesNotContain(result.Files, f => f.Name.Contains("logo."));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------- --loader dataurl + inline limit

    [Fact]
    public async Task Explicit_dataurl_loader_works_with_inline_limit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import icon from './icon.svg'; export default icon;");
            await File.WriteAllTextAsync(Path.Combine(dir, "icon.svg"), "<svg></svg>");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"),
                Array.Empty<string>(), Array.Empty<string>(),
                loaders: new Dictionary<string, string> { [".svg"] = "dataurl" });
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(Options(inlineLimit: 4096));

            // dataurl loader always inlines regardless of size; inline limit doesn't
            // interfere — the loader converts to a JS module with a string export.
            Assert.Contains("data:image/svg+xml;base64,", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -------------------------------------------- ESM format non-inlined check

    [Fact]
    public async Task ESM_large_asset_uses_import_meta_url()
    {
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100, format: ModuleFormat.Esm),
            "import url from './logo.png'; export default url;",
            bytes);

        // ESM non-inlined asset: AutoReference uses URL.parse(..., import.meta.url)
        Assert.Contains("import.meta.url", output);
        Assert.DoesNotContain("data:image/png;base64,", output);
    }

    // ------------------------------- tree-shaking doesn't break inlined assets

    [Fact]
    public async Task Inlined_asset_survives_treeshaking()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-inline-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            // Only the exported function uses the asset — the unused import should
            // be tree-shaken, but the inlined asset should remain.
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png';" +
                "export function getUrl() { return url; }" +
                "console.log('unused');");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[] { 1, 2, 3 });

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(Options(inlineLimit: 100, optimizing: true));

            // The inlined data URI must survive even after tree-shaking and
            // minification cleans up unused code paths.
            Assert.Contains("data:image/png;base64,", output);
            Assert.Contains("getUrl", output);   // the function referencing it survives
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --------------------------------------------------- per-import ?inline=

    [Fact]
    public async Task Inline_always_overrides_global_threshold_for_large_asset()
    {
        var bytes = new byte[50000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png?inline=always'; export default url;",
            bytes);

        // ?inline=always forces inlining even though 50KB > 100B threshold
        Assert.Contains("data:image/png;base64,", output);
        Assert.DoesNotContain("logo.", output);
    }

    [Fact]
    public async Task Inline_never_prevents_inlining_of_small_asset()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 4096),
            "import url from './logo.png?inline=never'; export default url;",
            bytes);

        // ?inline=never prevents inlining even though 3 byte < 4096 threshold
        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("logo.", output);
    }

    [Fact]
    public async Task Inline_numeric_kb_overrides_global_threshold()
    {
        // 2000 bytes = ~2KB — with ?inline=1 (1KB), it's over the limit → not inlined
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 4096),
            "import url from './logo.png?inline=1'; export default url;",
            bytes);

        Assert.DoesNotContain("data:image/png;base64,", output);
        Assert.Contains("logo.", output);
    }

    [Fact]
    public async Task Inline_numeric_kb_allows_larger_asset()
    {
        // 2000 bytes = ~2KB — with ?inline=3 (3KB), it's under the limit → inlined
        var bytes = new byte[2000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png?inline=3'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
    }

    [Fact]
    public async Task Inline_always_ignores_upper_boundary()
    {
        var bytes = new byte[100000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 0),
            "import url from './logo.png?inline=always'; export default url;",
            bytes,
            "logo.png");

        // ?inline=always works even with 100KB and global limit=0 (disabled)
        Assert.Contains("data:image/png;base64,", output);
    }

    [Fact]
    public async Task Inline_never_works_when_globally_disabled()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 0),
            "import url from './logo.png?inline=never'; export default url;",
            bytes);

        // ?inline=never with global limit=0 — no inlining either way
        Assert.DoesNotContain("data:image/png;base64,", output);
    }

    [Fact]
    public async Task Inline_override_preserves_valid_js_output()
    {
        var bytes = new byte[50000];
        var output = await BundleJsWithAsset(
            Options(inlineLimit: 100),
            "import url from './logo.png?inline=always'; export default url;",
            bytes);

        Assert.Contains("data:image/png;base64,", output);
        var opts = new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false };
        Assert.Empty(Parser.ParseModule(output, "out.js", opts).Diagnostics);
    }
}
