namespace NetPack.Commands;

using CommandLine;
using NetPack.Graph;

[Verb("graph", HelpText = "Displays the bundling graph starting at the given entry point.")]
public class GraphCommand : ICommand
{
    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string? FilePath { get; set; }

    [Option("outfile", HelpText = "The optional file where the graph should be stored as a JSON.")]
    public string? OutFile { get; set; }

    public async Task Run()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var result = await Traverse.From(file);
        var context = result.Context;

        Console.WriteLine("Assets:");

        foreach (var asset in context.Assets)
        {
            Console.WriteLine("  {0} ({1})", asset.Root.FileName, asset.Type);
        }

        Console.WriteLine("");
        Console.WriteLine("Dependencies:");

        foreach (var dep in context.Dependencies)
        {
            Console.WriteLine("  {0} ({1})", dep.Name, dep.Version);
        }

        Console.WriteLine("");
        Console.WriteLine("Bundles:");

        foreach (var bundle in context.Bundles)
        {
            Console.WriteLine("  {0} ({1})", bundle.Root.FileName, bundle.Root.Children.Count);
        }
        
        Console.WriteLine("");
        Console.WriteLine("Total: Processed {0} modules.", context.Modules.Count);

        if (!string.IsNullOrEmpty(OutFile))
        {
            var target = Path.Combine(Environment.CurrentDirectory, OutFile);
            var nodes = context.Bundles.Select(m => m.Root);
            File.WriteAllText(target, GraphParser.Serialize(nodes, context.Modules.Values));
        }
    }
}
