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
/// A minify benchmark that intentionally creates many shadowed references in
/// nested scopes so the reference collector's shadow lookup path is exercised.
/// </summary>
public class MinifyShadowingStressTests
{
    private const int ModuleCount = 60;
    private const int ShadowDepth = 16;
    private const int ShadowUses = 12;

    private readonly ITestOutputHelper _output;

    public MinifyShadowingStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Shadowed_nested_references_minify_validly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-shadow-" + Path.GetRandomFileName());
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
            Assert.Contains("SHADOW_MARKER", minified);
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

        for (var moduleIndex = 0; moduleIndex < ModuleCount; moduleIndex++)
        {
            var fileName = $"mod-{moduleIndex:D3}.ts";
            var filePath = Path.Combine(srcDir, fileName);
            var lines = new System.Collections.Generic.List<string>();

            if (moduleIndex == 0)
            {
                lines.Add("export const live = 0;");
            }
            else
            {
                lines.Add($"import {{ live as prevLive }} from './mod-{moduleIndex - 1:D3}';");
                lines.Add("export function combine(seed: number) {");
                lines.Add("  let total = seed;");

                for (var depth = 0; depth < ShadowDepth; depth++)
                {
                    var indent = new string(' ', (depth + 1) * 2);
                    lines.Add($"{indent}{{");
                    lines.Add($"{indent}  const live = total + {moduleIndex} + {depth};");

                    for (var use = 0; use < ShadowUses; use++)
                    {
                        lines.Add($"{indent}  total += live + {use};");
                    }
                }

                for (var depth = ShadowDepth - 1; depth >= 0; depth--)
                {
                    var indent = new string(' ', (depth + 1) * 2);
                    lines.Add($"{indent}  total += prevLive;");
                    lines.Add($"{indent}}}");
                }

                lines.Add("  return total + prevLive;");
                lines.Add("}");
                lines.Add($"export const live = combine(prevLive);");
            }

            lines.Add($"export const marker = 'SHADOW_MARKER_{moduleIndex:D3}';");
            await File.WriteAllTextAsync(filePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        var imports = string.Join(Environment.NewLine,
            Enumerable.Range(0, 1).Select(_ => $"import {{ live }} from './mod-{ModuleCount - 1:D3}';"));

        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.ts"),
            $"{imports}\n\nexport const total = live;\nexport const marker = 'SHADOW_MARKER';\n");
    }
}