namespace NetPack.Commands;

using CommandLine;
using NetPack.Graph;

[Verb("bundle", HelpText = "Bundles the code starting at the given entry point.")]
public class BundleCommand : ICommand
{
    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string? FilePath { get; set; }

    [Option("outdir", Default = "dist", HelpText = "The directory where the generated files should be placed.")]
    public string OutDir { get; set; } = "dist";

    [Option("minify", Default = false, HelpText = "Indicates if the generated files should be optimized for file size.")]
    public bool Minify { get; set; }

    [Option("clean", Default = false, HelpText = "Indicates if the output directory should be cleaned first.")]
    public bool Clean { get; set; }

    public async Task Run()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var outdir = Path.Combine(Environment.CurrentDirectory, OutDir);
        var result = await Traverse.From(file);

        if (Clean && Directory.Exists(outdir))
        {
            Directory.Delete(outdir, true);
        }

        await result.Context.WriteOut(outdir, Minify);
    }
}
