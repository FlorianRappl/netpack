namespace NetPack.Tests;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NetPack;
using NetPack.Graph;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Runs a web-target bundle inside a real DOM (jsdom) and asserts it manipulated
/// the document — the browser counterpart to <see cref="ExecutionTests"/>. The UMD
/// output is loaded as a classic &lt;script&gt;, so its entry executes against
/// jsdom's <c>document</c>.
///
/// jsdom is heavy, so it rides along with the opt-in corpus: run <c>./corpus.sh</c>
/// (which installs it) to enable these. Without the corpus — or without
/// <c>node</c> — the test skips, so the suite stays green.
/// </summary>
public class BrowserExecutionTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task Web_bundle_manipulates_the_dom_under_jsdom()
    {
        var corpusModules = FindCorpusNodeModules();
        if (corpusModules is null || !Directory.Exists(Path.Combine(corpusModules, "jsdom")))
        {
            _output.WriteLine("jsdom not available (run ./corpus.sh); skipping.");
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "netpack-dom-" + Path.GetRandomFileName());
        var outDir = Path.Combine(dir, "dist");
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { tag } from './util.js';\n" +
                "const el = document.createElement('div');\n" +
                "el.textContent = tag();\n" +
                "document.body.appendChild(el);\n" +
                "document.body.setAttribute('data-x', 'DOM_OK');");
            await File.WriteAllTextAsync(Path.Combine(dir, "util.js"),
                "export function tag() { return 'FROM_UTIL'; }");

            // UMD loads as a classic script in jsdom; web platform for a browser build.
            await Bundler.WriteToDirectoryAsync(
                Path.Combine(dir, "main.js"), outDir,
                new BundleOptions { Platform = Platform.Web, Format = ModuleFormat.Umd });

            await File.WriteAllTextAsync(Path.Combine(outDir, "run-dom.js"), RunnerScript);

            var stdout = await RunNode(outDir, Path.Combine(outDir, "run-dom.js"), Path.Combine(outDir, "main.js"), corpusModules);
            if (stdout is null || stdout.Contains("__NO_JSDOM__", StringComparison.Ordinal))
            {
                _output.WriteLine("jsdom could not be loaded; skipping.");
                return;
            }

            Assert.Contains("ATTR:DOM_OK", stdout);        // the entry ran and set the attribute
            Assert.Contains("BODY:FROM_UTIL", stdout);     // and the imported helper linked + ran
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private const string RunnerScript =
        "let JSDOM;\n" +
        "try { JSDOM = require('jsdom').JSDOM; } catch (e) { console.log('__NO_JSDOM__'); process.exit(0); }\n" +
        "const fs = require('fs');\n" +
        "const code = fs.readFileSync(process.argv[2], 'utf8');\n" +
        "const dom = new JSDOM('<!DOCTYPE html><html><head></head><body></body></html>', { runScripts: 'dangerously' });\n" +
        "const s = dom.window.document.createElement('script');\n" +
        "s.textContent = code;\n" +
        "dom.window.document.body.appendChild(s);\n" +
        "console.log('ATTR:' + dom.window.document.body.getAttribute('data-x'));\n" +
        "console.log('BODY:' + dom.window.document.body.textContent);\n";

    private async Task<string?> RunNode(string workingDir, string runner, string bundle, string nodePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(runner);
        psi.ArgumentList.Add(bundle);
        psi.Environment["NODE_PATH"] = nodePath;

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            _output.WriteLine("node not found on PATH; skipping.");
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

    private static string? FindCorpusNodeModules()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NETPACK_CORPUS");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            var dir = Path.Combine(fromEnv, "node_modules");
            return Directory.Exists(dir) ? dir : null;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "test-corpus", "node_modules");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return null;
    }
}
