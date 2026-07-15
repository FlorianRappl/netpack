namespace NetPack.Tests;

using System.Collections.Generic;
using NetPack;
using NetPack.Json;
using NetPack.Syntax;
using Xunit;

public class FederationTests
{
    [Theory]
    [InlineData(null, "module")]
    [InlineData("", "module")]
    [InlineData("module", "module")]
    [InlineData("native", "native")]
    public void NormalizeKind_accepts_known_kinds(string? kind, string expected)
    {
        Assert.Equal(expected, ModuleFederationHelpers.NormalizeKind(kind));
    }

    [Fact]
    public void NormalizeKind_rejects_unknown_kinds_and_lists_options()
    {
        var ex = Assert.Throws<System.NotSupportedException>(() => ModuleFederationHelpers.NormalizeKind("remote"));
        Assert.Contains("module (default), native", ex.Message);
    }

    [Fact]
    public void Native_remote_imports_shared_deps_directly_and_lazy_loads_exposes()
    {
        var definition = new ModuleFederation
        {
            Name = "myRemote",
            Kind = "native",
            Shared = new Dictionary<string, SharedEntry> { ["react"] = new() },
            Exposes = new Dictionary<string, string> { ["./Button"] = "./src/Button.js" },
        };

        var code = ModuleFederationHelpers.CreateNativeContainerCode(definition);

        // Shared dependency imported directly, pinned at the top.
        Assert.Contains("import * as __shared_0 from 'react';", code);
        Assert.StartsWith("import * as __shared_0 from 'react';", code);
        // Expose becomes a lazily imported ESM chunk.
        Assert.Contains("() => import('./src/Button.js')", code);
        // Shared + exposes maps and a default export.
        Assert.Contains("export const shared =", code);
        Assert.Contains("export const exposes =", code);
        Assert.Contains("export default { name: 'myRemote', exposes, shared };", code);

        // The generated remote is valid ES module syntax.
        Assert.Empty(Parser.ParseModule(code, "remoteEntry.js", new ParserOptions { Tolerant = true }).Diagnostics);
    }

    [Fact]
    public void Native_remote_without_shared_or_exposes_is_still_valid()
    {
        var definition = new ModuleFederation { Name = "empty", Kind = "native" };
        var code = ModuleFederationHelpers.CreateNativeContainerCode(definition);

        Assert.Contains("export const shared = {};", code);
        Assert.Contains("export const exposes = {};", code);
        Assert.Empty(Parser.ParseModule(code, "remoteEntry.js", new ParserOptions { Tolerant = true }).Diagnostics);
    }
}
