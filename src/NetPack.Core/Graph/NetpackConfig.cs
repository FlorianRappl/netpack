namespace NetPack.Graph;

using System.Text.Json;

/// <summary>
/// Represents the netpack configuration loaded from a config file.
/// Values here serve as defaults; CLI flags override them.
/// </summary>
public class NetpackConfig
{
    /// <summary>Build mode (e.g. development, production).</summary>
    public string? Mode { get; set; }

    /// <summary>Output directory.</summary>
    public string? OutDir { get; set; }

    /// <summary>Output module format.</summary>
    public string? Format { get; set; }

    /// <summary>Target platform (web, node, deno).</summary>
    public string? Platform { get; set; }

    /// <summary>Whether to minify the output.</summary>
    public bool? Minify { get; set; }

    /// <summary>Whether to emit source maps.</summary>
    public bool? SourceMap { get; set; }

    /// <summary>Compile-time constant replacements.</summary>
    public Dictionary<string, string>? Define { get; set; }

    /// <summary>Import path aliases.</summary>
    public Dictionary<string, string>? ResolveAlias { get; set; }

    /// <summary>File extension loader overrides.</summary>
    public Dictionary<string, string>? Loader { get; set; }

    /// <summary>External dependencies.</summary>
    public List<string>? External { get; set; }

    /// <summary>Shared dependencies.</summary>
    public List<string>? Shared { get; set; }

    /// <summary>Extra package.json 'exports' conditions.</summary>
    public List<string>? Conditions { get; set; }

    /// <summary>How to handle node_modules imports (bundle, external).</summary>
    public string? Packages { get; set; }

    /// <summary>Base path/URL prepended to references to emitted files.</summary>
    public string? PublicPath { get; set; }

    /// <summary>Naming template for emitted bundles.</summary>
    public string? EntryNames { get; set; }

    /// <summary>Preset name (development, production, or custom).</summary>
    public string? Preset { get; set; }

    /// <summary>
    /// Parses a JSON string into a <see cref="NetpackConfig"/>.
    /// </summary>
    public static NetpackConfig ParseJson(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var config = new NetpackConfig();

        if (root.TryGetProperty("mode", out var mode))
            config.Mode = mode.GetString();

        if (root.TryGetProperty("outDir", out var outDir))
            config.OutDir = outDir.GetString();

        if (root.TryGetProperty("format", out var format))
            config.Format = format.GetString();

        if (root.TryGetProperty("platform", out var platform))
            config.Platform = platform.GetString();

        if (root.TryGetProperty("minify", out var minify))
            config.Minify = minify.GetBoolean();

        if (root.TryGetProperty("sourceMap", out var sourceMap))
            config.SourceMap = sourceMap.GetBoolean();

        if (root.TryGetProperty("define", out var define))
            config.Define = ParseStringDict(define);

        if (root.TryGetProperty("resolve", out var resolve) && resolve.TryGetProperty("alias", out var alias))
            config.ResolveAlias = ParseStringDict(alias);

        if (root.TryGetProperty("loader", out var loader))
            config.Loader = ParseStringDict(loader);

        if (root.TryGetProperty("external", out var external))
            config.External = ParseStringList(external);

        if (root.TryGetProperty("shared", out var shared))
            config.Shared = ParseStringList(shared);

        if (root.TryGetProperty("conditions", out var conditions))
            config.Conditions = ParseStringList(conditions);

        if (root.TryGetProperty("packages", out var packages))
            config.Packages = packages.GetString();

        if (root.TryGetProperty("publicPath", out var publicPath))
            config.PublicPath = publicPath.GetString();

        if (root.TryGetProperty("entryNames", out var entryNames))
            config.EntryNames = entryNames.GetString();

        if (root.TryGetProperty("preset", out var preset))
            config.Preset = preset.GetString();

        return config;
    }

    private static Dictionary<string, string> ParseStringDict(JsonElement element)
    {
        var dict = new Dictionary<string, string>();

        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.GetString() ?? "";
        }

        return dict;
    }

    private static List<string> ParseStringList(JsonElement element)
    {
        var list = new List<string>();

        foreach (var item in element.EnumerateArray())
        {
            list.Add(item.GetString() ?? "");
        }

        return list;
    }
}
