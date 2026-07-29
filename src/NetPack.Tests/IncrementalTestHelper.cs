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

/// <summary>
/// Multi-step rebuild test helper. Convenience wrapper for the common test
/// pattern: build → edit → rebuild → assert → edit → rebuild → assert ...
///
/// Usage:
/// <code>
/// using var test = new IncrementalTestHelper();
/// await test.Setup("entry.js", ("a.js", "export const a = 1;"));
///
/// var output0 = await test.Build();
/// test.AssertValidJs();
///
/// await test.Edit("a.js", "export const a = 2;");
/// var output1 = await test.Rebuild();
/// test.AssertValidJs();
/// test.AssertOutputContains(1, "a = 2");
/// </code>
/// </summary>
public class IncrementalTestHelper : IDisposable
{
    private string _dir = null!;
    private string _entry = null!;
    private string _lastOutput = "";
    private int _step;
    private readonly List<string> _outputs = [];

    /// <summary>All outputs from each build step (0 = initial, 1, 2, ...).</summary>
    public IReadOnlyList<string> Outputs => _outputs;

    /// <summary>The current step number (0-indexed).</summary>
    public int Step => _step;

    /// <summary>
    /// Creates a temporary project directory and writes the entry file plus
    /// any additional source files. A <c>package.json</c> is written
    /// automatically.
    /// </summary>
    public async Task Setup(string entry, params (string Name, string Content)[] files)
    {
        _dir = Path.Combine(Path.GetTempPath(), "netpack-incr-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _entry = entry;

        await File.WriteAllTextAsync(Path.Combine(_dir, "package.json"), "{}");

        foreach (var (name, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(_dir, name), content);
        }
    }

    /// <summary>
    /// Runs a build. Call this for both initial and subsequent builds.
    /// Stores the output for later assertion via <see cref="Outputs"/>.
    /// </summary>
    public async Task<string> Build(OutputOptions? options = null)
    {
        options ??= new OutputOptions { IsOptimizing = false, IsReloading = false };

        using var graph = await Traverse.From(
            Path.Combine(_dir, _entry), Array.Empty<string>(), Array.Empty<string>());

        var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
        _lastOutput = bundle.Stringify(options);
        _outputs.Add(_lastOutput);
        _step++;
        return _lastOutput;
    }

    /// <summary>Alias for <see cref="Build"/> — rebuild after edits.</summary>
    public Task<string> Rebuild(OutputOptions? options = null) => Build(options);

    /// <summary>Overwrites a source file.</summary>
    public async Task Edit(string fileName, string newContent)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, fileName), newContent);
    }

    /// <summary>Creates a new source file in the project directory.</summary>
    public async Task AddFile(string fileName, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, fileName), content);
    }

    /// <summary>Deletes a source file from the project directory.</summary>
    public void DeleteFile(string fileName)
    {
        File.Delete(Path.Combine(_dir, fileName));
    }

    /// <summary>
    /// Re-parses the last build output and asserts zero diagnostics.
    /// </summary>
    public void AssertValidJs()
    {
        var reparsed = Parser.ParseModule(_lastOutput, "out.js",
            new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
        Assert.Empty(reparsed.Diagnostics);
    }

    /// <summary>
    /// Asserts that the output from a specific step contains the given
    /// substring. Step 0 = first build.
    /// </summary>
    public void AssertOutputContains(int step, string substring)
    {
        Assert.True(_outputs.Count > step, $"No output recorded for step {step}");
        Assert.Contains(substring, _outputs[step]);
    }

    /// <summary>
    /// Asserts that the output from a specific step does NOT contain the
    /// given substring.
    /// </summary>
    public void AssertOutputDoesNotContain(int step, string substring)
    {
        Assert.True(_outputs.Count > step, $"No output recorded for step {step}");
        Assert.DoesNotContain(substring, _outputs[step]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
