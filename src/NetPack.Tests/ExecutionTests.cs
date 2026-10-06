namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NetPack;
using NetPack.Graph;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Bundles a fixture with the library API (<see cref="Bundler"/>) targeting Node
/// and actually <em>runs</em> the output under <c>node</c>, asserting on stdout.
/// Re-parsing proves output is valid JS; this proves it executes — module linking,
/// CommonJS interop, circular dependencies and ESM output all behave at runtime.
/// Skips when <c>node</c> is not on PATH (so the suite still runs without it).
/// </summary>
public class ExecutionTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task Esm_import_links_and_runs()
    {
        var stdout = await BundleAndRun(ModuleFormat.CommonJs,
            ("main.js", "import { greet } from './util.js';\nconsole.log(greet('X'));"),
            ("util.js", "export function greet(n) { return 'HELLO_' + n; }"));

        if (stdout is null) return; // node unavailable
        Assert.Contains("HELLO_X", stdout);
    }

    [Fact]
    public async Task CommonJs_dependency_interops_at_runtime()
    {
        var stdout = await BundleAndRun(ModuleFormat.CommonJs,
            ("main.js", "const dep = require('./dep.js');\nconsole.log('DEP:' + dep.foo);"),
            ("dep.js", "module.exports = { foo: 'BAR' };"));

        if (stdout is null) return;
        Assert.Contains("DEP:BAR", stdout);
    }

    [Fact]
    public async Task Default_interop_of_a_commonjs_module_runs()
    {
        // `import d from` a CJS module must yield module.exports via the runtime's
        // default interop.
        var stdout = await BundleAndRun(ModuleFormat.CommonJs,
            ("main.js", "import dep from './dep.js';\nconsole.log('D:' + dep.value);"),
            ("dep.js", "module.exports = { value: 'CJS_DEFAULT' };"));

        if (stdout is null) return;
        Assert.Contains("D:CJS_DEFAULT", stdout);
    }

    [Fact]
    public async Task Circular_dependencies_resolve_at_runtime()
    {
        var stdout = await BundleAndRun(ModuleFormat.CommonJs,
            ("main.js", "const b = require('./b.js');\nmodule.exports = { name: 'A', b };\nconsole.log('A sees ' + b.name);"),
            ("b.js", "const a = require('./main.js');\nmodule.exports = { name: 'B', aKind: typeof a };"));

        if (stdout is null) return;
        Assert.Contains("A sees B", stdout);
    }

    [Fact]
    public async Task Esm_format_output_runs_under_node()
    {
        var stdout = await BundleAndRun(ModuleFormat.Esm,
            ("main.js", "import { v } from './util.js';\nconsole.log('ESM:' + v);"),
            ("util.js", "export const v = 'RAN';"));

        if (stdout is null) return;
        Assert.Contains("ESM:RAN", stdout);
    }

    [Fact]
    public async Task Bundled_path_shim_runs_under_node()
    {
        // The shipped browser `path` shim must parse, bundle and run correctly
        // through netpack (aliased in place of the Node builtin).
        var shim = FindRepoFile(Path.Combine("src", "npm", "netpack", "browser", "path.mjs"));
        if (shim is null)
        {
            _output.WriteLine("path shim not found; skipping.");
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "netpack-shim-" + Path.GetRandomFileName());
        var outDir = Path.Combine(dir, "dist");
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import path from 'path';\nconsole.log('J:' + path.join('a', 'b', '..', 'c'));");

            await Bundler.WriteToDirectoryAsync(
                Path.Combine(dir, "main.js"), outDir,
                new BundleOptions
                {
                    Platform = Platform.Node,
                    Format = ModuleFormat.CommonJs,
                    Alias = new Dictionary<string, string> { ["path"] = shim },
                });

            var stdout = await RunNode(Path.Combine(outDir, "main.js"));
            if (stdout is null) return;
            Assert.Contains("J:a/c", stdout);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string? FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Bundles the fixture to a temp dir targeting Node and runs the entry output
    /// under <c>node</c>, returning stdout — or null when node isn't installed.
    /// </summary>
    private async Task<string?> BundleAndRun(ModuleFormat format, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-exec-" + Path.GetRandomFileName());
        var outDir = Path.Combine(dir, "dist");
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            await Bundler.WriteToDirectoryAsync(
                Path.Combine(dir, "main.js"), outDir,
                new BundleOptions { Platform = Platform.Node, Format = format });

            // ESM output must be run as a module; mark the output dir so node treats
            // the emitted .js as ESM.
            if (format == ModuleFormat.Esm)
            {
                await File.WriteAllTextAsync(Path.Combine(outDir, "package.json"), "{ \"type\": \"module\" }");
            }

            return await RunNode(Path.Combine(outDir, "main.js"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private async Task<string?> RunNode(string scriptPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(scriptPath);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            _output.WriteLine("node not found on PATH; skipping execution test.");
            return null;
        }

        if (proc is null)
        {
            return null;
        }

        using (proc)
        {
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Assert.True(proc.ExitCode == 0,
                $"node exited with {proc.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            return stdout;
        }
    }
}
