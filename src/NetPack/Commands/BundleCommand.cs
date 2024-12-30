namespace NetPack.Commands;

using CommandLine;
using NetPack.Graph;

[Verb("bundle", HelpText = "Bundles the code starting at the given entry point.")]
public class BundleCommand : ICommand
{
    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string? FilePath { get; set; }

    [Option("outdir", HelpText = "The directory where the generated files should be placed.")]
    public string? OutDir { get; set; }

    [Option("minify", Default = false, HelpText = "Indicates if the generated files should be optimized for file size.")]
    public bool Minify { get; set; }

    public async Task Run()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var result = await Traverse.From(file);

        Console.WriteLine("Assets:");

        foreach (var asset in result.Context.Assets)
        {
            Console.WriteLine("  {0} ({1})", asset.Root.FileName, asset.Type);
        }

        Console.WriteLine("");
        Console.WriteLine("Dependencies:");

        foreach (var dep in result.Context.Dependencies)
        {
            Console.WriteLine("  {0} ({1})", dep.Name, dep.Version);
        }

        Console.WriteLine("");
        Console.WriteLine("Bundles:");

        foreach (var bundle in result.Context.Bundles)
        {
            Console.WriteLine("  {0} ({1})", bundle.Root.FileName, bundle.Root.Children.Count);
        }
        
        Console.WriteLine("");
        Console.WriteLine("Total: Processed {0} modules.", result.Context.Modules.Count);
    }
}
