namespace NetPack.Commands;

using CommandLine;
using NetPack.Graph;

[Verb("inspect", HelpText = "Inspects the provided graph structure.")]
public class InspectCommand : ICommand
{
    [Value(0)]
    public string? FilePath { get; set; }

    public Task Run()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var code = File.ReadAllText(file);
        var p = new GraphParser(code);
        var (entries, nodes) = p.GetNodes();
        var connected = Connected.FindIndependentGraphs(entries);

        Console.WriteLine("Has cycles: {0}", entries.Any(Cycle.Detect));

        foreach (var graph in connected)
        {
            var name = graph.Key.FileName;
            var keys = string.Join(", ", graph.Value.Select(m => m.FileName));
            Console.WriteLine("Found connected graph ({0}): {1}", name, keys);
        }

        return Task.CompletedTask;
    }
}
