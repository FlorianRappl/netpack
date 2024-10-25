using NetPack.TypeScript;
using NetPack.Sass;
using NewBundler.Graph;

var file = Path.Combine(Environment.CurrentDirectory, args[0]);
var code = File.ReadAllText(file);
var ext = Path.GetExtension(file);

string[] tsExtensions = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts"];
string[] sassExtensions = [".scss", ".sass", ".css"];
string[] graphExtensions = [".json"];

if (tsExtensions.Contains(ext))
{
    var tokenizer = new TypeScriptTokenizer(code);

    while (tokenizer.IsActive)
    {
        var token = tokenizer.NextToken();
        Console.WriteLine(token.ToString());
    }
}
else if (sassExtensions.Contains(ext))
{
    var tokenizer = new SassTokenizer(code);

    while (tokenizer.IsActive)
    {
        var token = tokenizer.NextToken();
        Console.WriteLine(token.ToString());
    }
}
else if (graphExtensions.Contains(ext))
{
    var p = new Parser(code);
    var nodes = p.GetNodes();
    var root = nodes[0];
    var descendent = nodes[1];
    Console.WriteLine("Has cycles: {0}", Cycle.Detect(root));

    var connected = Connected.FindIndependentGraphs([root, descendent]);

    foreach (var graph in connected)
    {
        Console.WriteLine("Found connected graph: {0}", string.Join(", ", graph.Select(m => m.Name)));
    }
}
else
{
    // we don't know ...
}
