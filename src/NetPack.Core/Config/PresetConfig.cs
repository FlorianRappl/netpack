namespace NetPack.Config;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A netpack <b>preset</b>: a transportable JSON(C) bundle of CLI options, plus
/// optional composition (<see cref="Presets"/>) and lifecycle <see cref="Hooks"/>.
///
/// Every option field is <b>nullable</b> so "unset" is distinguishable from "set
/// to its default" — presets resolve <i>first-write-wins</i> across the chain
/// (see <see cref="NetPack.Config.Presets"/>). A preset is just convenience for
/// the CLI flags, except <see cref="Hooks"/>, which are only expressible here.
/// </summary>
public sealed class PresetConfig
{
    /// <summary>Other presets to compose in (by path or package reference), each
    /// applied after this one — i.e. this preset's own values take precedence.</summary>
    [JsonPropertyName("presets")]
    public List<string>? Presets { get; set; }

    /// <summary>Lifecycle hook name → JS module references (resolved and run via
    /// the Node bridge). Multiple callbacks per hook; arrays merge across presets.</summary>
    [JsonPropertyName("hooks")]
    public Dictionary<string, List<string>>? Hooks { get; set; }

    [JsonPropertyName("outdir")] public string? OutDir { get; set; }

    [JsonPropertyName("minify")] public bool? Minify { get; set; }

    [JsonPropertyName("sourcemap")] public bool? SourceMap { get; set; }

    [JsonPropertyName("clean")] public bool? Clean { get; set; }

    [JsonPropertyName("external")] public List<string>? External { get; set; }

    [JsonPropertyName("shared")] public List<string>? Shared { get; set; }

    [JsonPropertyName("format")] public string? Format { get; set; }

    [JsonPropertyName("platform")] public string? Platform { get; set; }

    [JsonPropertyName("define")] public Dictionary<string, string>? Define { get; set; }

    [JsonPropertyName("alias")] public Dictionary<string, string>? Alias { get; set; }

    [JsonPropertyName("loader")] public Dictionary<string, string>? Loader { get; set; }

    [JsonPropertyName("entryNames")] public string? EntryNames { get; set; }

    [JsonPropertyName("publicPath")] public string? PublicPath { get; set; }

    [JsonPropertyName("conditions")] public List<string>? Conditions { get; set; }

    [JsonPropertyName("packages")] public string? Packages { get; set; }

    [JsonPropertyName("banner")] public string? Banner { get; set; }

    [JsonPropertyName("port")] public int? Port { get; set; }
}

/// <summary>The outcome of resolving a preset chain.</summary>
public sealed class ResolvedPresets
{
    public ResolvedPresets(
        PresetConfig options,
        IReadOnlyDictionary<string, IReadOnlyList<string>> hooks,
        IReadOnlyList<string> sources)
    {
        Options = options;
        Hooks = hooks;
        Sources = sources;
    }

    /// <summary>The merged options (first-write-wins across the resolved chain).</summary>
    public PresetConfig Options { get; }

    /// <summary>Hook name → resolved absolute module paths, in execution order
    /// (base presets first), deduplicated by path.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Hooks { get; }

    /// <summary>Every resolved preset file, in option-precedence order (highest
    /// first). Useful for diagnostics and change-watching.</summary>
    public IReadOnlyList<string> Sources { get; }
}

/// <summary>
/// Source-generated JSON context for <see cref="PresetConfig"/>. Configured for
/// JSONC — comments and trailing commas are tolerated, since preset files are
/// hand-authored — and kept separate from the main context so those lenient
/// options don't leak into the bundler's other JSON handling.
/// </summary>
[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PresetConfig))]
internal partial class ConfigSourceGenerationContext : JsonSerializerContext
{
}
