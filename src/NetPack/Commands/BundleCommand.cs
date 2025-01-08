namespace NetPack.Commands;

using CommandLine;
using NetPack.Graph;
using NetPack.Graph.Writers;

[Verb("bundle", HelpText = "Bundles the code starting at the given entry point.")]
public class BundleCommand : ICommand
{
    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string FilePath { get; set; } = "";

    [Option("outdir", Default = "dist", HelpText = "The directory where the generated files should be placed.")]
    public string OutDir { get; set; } = "dist";

    [Option("minify", Default = false, HelpText = "Indicates if the generated files should be optimized for file size.")]
    public bool Minify { get; set; } = false;

    [Option("clean", Default = false, HelpText = "Indicates if the output directory should be cleaned first.")]
    public bool Clean { get; set; } = false;

    [Option("external", HelpText = "Indicates if an import should be treated as an external.")]
    public IEnumerable<string> Externals { get; set; } = [];

    [Option("shared", HelpText = "Indicates if a dependency should be shared.")]
    public IEnumerable<string> Shared { get; set; } = [];

    public async Task Run()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            throw new InvalidOperationException("You must specify an entry point.");
        }
        
        if (string.IsNullOrEmpty(OutDir))
        {
            throw new InvalidOperationException("You must specify a non-empty target directory.");
        }

        var file = Path.Combine(Environment.CurrentDirectory, FilePath);
        var outdir = Path.Combine(Environment.CurrentDirectory, OutDir);
        var graph = await Traverse.From(file, Externals, Shared);
        var result = new DiskResultWriter(graph.Context, outdir);
        var options = new OutputOptions
        {
            IsOptimizing = Minify,
            IsReloading = false,
        };

        if (Clean && Directory.Exists(outdir))
        {
            Directory.Delete(outdir, true);
        }

        Directory.CreateDirectory(outdir);
        await result.WriteOut(options);
    }
}
