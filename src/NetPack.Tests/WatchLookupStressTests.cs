namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Server;
using Xunit;

/// <summary>
/// Synthetic watch-mode stress for source-path lookups. It isolates the hot
/// path used by file-change filtering: checking whether a changed path belongs
/// to the current build.
/// </summary>
public class WatchLookupStressTests
{
    private const int ModuleCount = 1500;
    private const int LookupRounds = 400;

    [Fact]
    public async Task Lookup_index_matches_the_current_build_shape()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-watch-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await CreateProject(dir);

            using var graph = await Traverse.From(Path.Combine(dir, "src", "app.ts"));
            var memory = new MemoryResultWriter(graph.Context);
            var locator = (IFileLocator)memory;

            var modules = graph.Context.Modules.Values.Select(m => m.FileName).ToArray();
            var checksum = 0;
            for (var round = 0; round < LookupRounds; round++)
            {
                foreach (var file in modules)
                {
                    checksum += locator.HasFile(file) ? 1 : 0;
                }

                checksum += locator.HasFile(Path.Combine(dir, "missing.js")) ? 1 : 0;
            }

            var expected = modules.Length * LookupRounds;
            Assert.Equal(expected, checksum);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task CreateProject(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);

        for (var index = 0; index < ModuleCount; index++)
        {
            var fileName = $"mod-{index:D4}.{(index % 2 == 0 ? "ts" : "js")}";
            var filePath = Path.Combine(srcDir, fileName);

            if (index == 0)
            {
                await File.WriteAllTextAsync(filePath, "export const value0000 = 0;\n");
                continue;
            }

            var previous = $"./mod-{index - 1:D4}";
            var content = index % 2 == 0
                ? $"import {{ value{index - 1:D4} }} from '{previous}';\nexport const value{index:D4}: number = value{index - 1:D4} + {index};\n"
                : $"import {{ value{index - 1:D4} }} from '{previous}';\nexport const value{index:D4} = value{index - 1:D4} + {index};\n";

            await File.WriteAllTextAsync(filePath, content);
        }

        var imports = string.Join(Environment.NewLine,
            Enumerable.Range(0, ModuleCount).Select(index => $"import {{ value{index:D4} }} from './mod-{index:D4}';"));
        var total = string.Join(" + ", Enumerable.Range(0, ModuleCount).Select(index => $"value{index:D4}"));

        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.ts"),
            $"{imports}\n\nexport const total = {total};\n");
    }
}