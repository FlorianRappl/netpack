namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Bundles a spread of real npm packages (CJS and ESM, barrels, JSX, modern TS
/// output) and asserts every emitted bundle re-parses as valid JavaScript with
/// zero diagnostics. This is the "does it survive what actually ships on npm"
/// backstop that hand-written fixture tests can't provide — the class of bug the
/// lucide-react tree-shaking report exposed.
///
/// The corpus is assembled out-of-band by <c>./corpus.sh</c> into
/// <c>&lt;repo&gt;/test-corpus</c> (or <c>$NETPACK_CORPUS</c>) and is intentionally
/// not committed. When it is absent these tests skip, so the normal suite stays
/// green without it; CI runs <c>./corpus.sh</c> first to exercise them.
///
/// Scope: this validates static output correctness (the parser, printer, minifier,
/// tree-shaker and JS lowering all produce valid JS for real inputs). It does not
/// execute the bundles, so it does not catch missing browser polyfills or runtime
/// interop issues — those need a separate execution harness.
/// </summary>
public class CorpusTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task Corpus_projects_bundle_to_valid_javascript()
    {
        var entriesDir = FindCorpusEntries();
        if (entriesDir is null)
        {
            _output.WriteLine("No corpus found (run ./corpus.sh); skipping.");
            return;
        }

        var entries = Directory
            .EnumerateFiles(entriesDir)
            .Where(f => f.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(entries.Count > 0, $"Corpus at {entriesDir} has no entry files.");

        var failures = new List<string>();

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            try
            {
                using var graph = await Traverse.From(entry);

                // Minified (the hardest path: mangler + tree-shaker + lowering all run).
                var options = new OutputOptions { IsOptimizing = true, IsReloading = false };
                var bundles = graph.Context.Bundles.Values.OfType<JsBundle>().ToList();

                if (bundles.Count == 0)
                {
                    failures.Add($"{name}: produced no JS bundle.");
                    continue;
                }

                foreach (var bundle in bundles)
                {
                    var code = bundle.Stringify(options);
                    var reparsed = Parser.ParseModule(code, "out.js",
                        new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });

                    if (reparsed.Diagnostics.Count > 0)
                    {
                        var first = reparsed.Diagnostics[0];
                        failures.Add($"{name} → {bundle.GetFileName()}: invalid output " +
                            $"({reparsed.Diagnostics.Count} diagnostic(s), first: {first.Message}).");
                    }
                }

                _output.WriteLine($"{name}: {bundles.Count} bundle(s) OK.");
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Corpus bundling failures:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// Locates the corpus <c>entries</c> directory: <c>$NETPACK_CORPUS/entries</c>
    /// when the variable is set, otherwise a <c>test-corpus/entries</c> found by
    /// walking up from the test assembly location. Returns null when absent.
    /// </summary>
    private static string? FindCorpusEntries()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NETPACK_CORPUS");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            var dir = Path.Combine(fromEnv, "entries");
            return Directory.Exists(dir) ? dir : null;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "test-corpus", "entries");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return null;
    }
}
