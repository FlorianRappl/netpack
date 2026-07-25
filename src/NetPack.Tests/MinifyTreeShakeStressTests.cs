namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// A bundle --minify stress case that specifically exercises tree-shaking over
/// many live bindings. Each module exports a small vector of values that stays
/// live through the whole chain, so the tree shaker has to resolve many names.
/// </summary>
public class MinifyTreeShakeStressTests
{
    private const int ModuleCount = 80;
    private const int LiveExportCount = 12;
    private const int DeadDeclCount = 24;

    private readonly ITestOutputHelper _output;

    public MinifyTreeShakeStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Tree_shaking_many_live_exports_stays_valid()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-shake-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await CreateProject(dir);

            using var graph = await Traverse.From(Path.Combine(dir, "src", "app.ts"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

            var pretty = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            var minified = bundle.Stringify(new OutputOptions { IsOptimizing = true, IsReloading = false });

            _output.WriteLine($"pretty={pretty.Length} minified={minified.Length}");

            Assert.True(minified.Length < pretty.Length);
            Assert.Empty(Parser.ParseModule(minified, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
            Assert.Contains("TOTAL_MARKER", minified);
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

        var liveNames = Enumerable.Range(0, LiveExportCount).Select(index => $"v{index:D2}").ToArray();

        for (var moduleIndex = 0; moduleIndex < ModuleCount; moduleIndex++)
        {
            var fileName = $"mod-{moduleIndex:D3}.ts";
            var filePath = Path.Combine(srcDir, fileName);

            var lines = new System.Collections.Generic.List<string>();

            if (moduleIndex == 0)
            {
                for (var liveIndex = 0; liveIndex < LiveExportCount; liveIndex++)
                {
                    lines.Add($"export const {liveNames[liveIndex]} = {liveIndex};");
                }
            }
            else
            {
                var previous = $"./mod-{moduleIndex - 1:D3}";
                var importSpecifiers = string.Join(", ", liveNames.Select(name => $"{name} as prev_{name}"));
                lines.Add($"import {{ {importSpecifiers} }} from '{previous}';");

                for (var liveIndex = 0; liveIndex < LiveExportCount; liveIndex++)
                {
                    var current = liveNames[liveIndex];
                    lines.Add($"export const {current} = prev_{current} + {moduleIndex} + {liveIndex};");
                }
            }

            for (var deadIndex = 0; deadIndex < DeadDeclCount; deadIndex++)
            {
                lines.Add($"const dead_{moduleIndex:D3}_{deadIndex:D2} = {moduleIndex} + {deadIndex};");
            }

            await File.WriteAllTextAsync(filePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        var imports = string.Join(Environment.NewLine,
            liveNames.Select(name => $"import {{ {name} }} from './mod-{ModuleCount - 1:D3}';"));
        var total = string.Join(" + ", liveNames);

        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.ts"),
            $"{imports}\n\nexport const total = {total};\nexport const marker = 'TOTAL_MARKER';\n");
    }
}