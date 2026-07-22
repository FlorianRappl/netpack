namespace NetPack.Tests;

using System.IO;
using System.Text.Json;
using NetPack.Graph;
using Xunit;

/// <summary>
/// Unit coverage for <see cref="Dependency"/>: the <c>sideEffects</c> contract
/// that drives tree-shaking safety, and the entry-point field precedence
/// (<c>browser</c> / <c>module</c> / <c>main</c>).
/// </summary>
public class DependencySideEffectsTests
{
    private static readonly string Location = Path.Combine(Path.GetTempPath(), "netpack-dep", "package.json");
    private static string Dir => Path.GetDirectoryName(Location)!;

    // The JsonDocument is intentionally kept alive (not disposed) so the backing
    // JsonElement remains valid for the assertion.
    private static Dependency Dep(string json, bool useBrowserField = true)
    {
        var doc = JsonDocument.Parse(json);
        return new Dependency(Location, doc.RootElement, useBrowserField);
    }

    private static string Norm(string path) => path.Replace('\\', '/');

    [Fact]
    public void Side_effects_true_marks_every_file()
    {
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\",\"sideEffects\":true}");
        Assert.True(dep.HasSideEffects(Path.Combine(Dir, "anything.js")));
    }

    [Fact]
    public void Side_effects_false_marks_nothing()
    {
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\",\"sideEffects\":false}");
        Assert.False(dep.HasSideEffects(Path.Combine(Dir, "anything.js")));
    }

    [Fact]
    public void Absent_side_effects_defaults_to_true()
    {
        // The conservative default: assume side effects unless told otherwise.
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\"}");
        Assert.True(dep.HasSideEffects(Path.Combine(Dir, "anything.js")));
    }

    [Fact]
    public void Side_effects_array_matches_only_listed_files()
    {
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\",\"sideEffects\":[\"effect.js\"]}");
        Assert.True(dep.HasSideEffects(Path.Combine(Dir, "effect.js")));
        Assert.False(dep.HasSideEffects(Path.Combine(Dir, "pure.js")));
    }

    [Fact]
    public void Reads_name_and_version()
    {
        var dep = Dep("{\"name\":\"my-lib\",\"version\":\"2.3.4\"}");
        Assert.Equal("my-lib", dep.Name);
        Assert.Equal("2.3.4", dep.Version);
    }

    [Fact]
    public void Entry_falls_back_to_main()
    {
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\",\"main\":\"./lib/index.js\"}");
        Assert.EndsWith("/lib/index.js", Norm(dep.Entry));
    }

    [Fact]
    public void Entry_prefers_module_over_main()
    {
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\",\"module\":\"./esm.js\",\"main\":\"./cjs.js\"}");
        Assert.EndsWith("/esm.js", Norm(dep.Entry));
    }

    [Fact]
    public void Entry_prefers_browser_when_the_browser_field_applies()
    {
        var json = "{\"name\":\"p\",\"version\":\"1.0.0\",\"browser\":\"./b.js\",\"module\":\"./m.js\",\"main\":\"./c.js\"}";
        Assert.EndsWith("/b.js", Norm(Dep(json, useBrowserField: true).Entry));
        Assert.EndsWith("/m.js", Norm(Dep(json, useBrowserField: false).Entry));
    }

    [Fact]
    public void Entry_defaults_to_index_js_when_unspecified()
    {
        var dep = Dep("{\"name\":\"p\",\"version\":\"1.0.0\"}");
        Assert.EndsWith("/index.js", Norm(dep.Entry));
    }
}
