namespace NetPack.Tests;

using NetPack;
using Xunit;

public class SvelteTests
{
    [Fact]
    public void Svelte_files_are_treated_as_javascript_modules()
    {
        // `.svelte` resolves and lands in a JS bundle; the compiler output (an ES
        // module) is produced by the Node bridge at process time. End-to-end
        // compilation is covered manually since it requires `svelte` installed.
        Assert.True(Helpers.ExtensionMap.ContainsKey(".svelte"));
        Assert.Equal(".js", Helpers.GetType(".svelte"));
    }
}
