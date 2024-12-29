using CommandLine;
using NetPack.Graph;

namespace NetPack.Commands;

[Verb("bundle", HelpText = "Bundles the code starting at the given entry point.")]
public class BundleCommand : ICommand
{
    [Value(0)]
    public string? FilePath { get; set; }

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
