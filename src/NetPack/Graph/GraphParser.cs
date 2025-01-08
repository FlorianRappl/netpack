namespace NetPack.Graph;

using System.Text.Json;
using System.Text.Json.Serialization;

public class GraphParser(string content)
{
    public string Content => content;

    public (List<Node>, List<Node>) GetNodes()
    {
        var nodeLookup = new Dictionary<string, Node>();
        var definition = JsonSerializer.Deserialize(Content, SourceGenerationContext.Default.NodesDefinition)!;

        // First, build collection of nodes
        foreach (var unresolved in definition.Nodes)
        {
            var name = unresolved.Name ?? string.Empty;
            var bytes = unresolved.Bytes ?? 1;
            var node = new Node(name, bytes);
            nodeLookup[name] = node;
        }

        // Now, resolve references & children
        foreach (var unresolved in definition.Nodes)
        {
            var name = unresolved.Name ?? string.Empty;
            var node = nodeLookup[name];

            foreach (var childName in unresolved.ChildNames)
            {
                if (nodeLookup.TryGetValue(childName, out var child))
                {
                    node.Children.Add(child);
                }
                else
                {
                    throw new Exception($"Unresolved child: {childName} for node {unresolved.Name}");
                }
            }

            foreach (var referenceName in unresolved.ReferenceNames)
            {
                if (nodeLookup.TryGetValue(referenceName, out var reference))
                {
                    node.References.Add(reference);
                }
                else
                {
                    throw new Exception($"Unresolved reference: {referenceName} for node {unresolved.Name}");
                }
            }
        }

        var entries = definition.Entries.Select(name => nodeLookup[name]).ToList();
        var values = nodeLookup.Values.ToList();
        return (entries, values);
    }

    public static string Serialize(IEnumerable<Node> entries, IEnumerable<Node> nodes)
    {
        var definition = new NodesDefinition
        {
            Entries = entries.Select(m => m.FileName).ToList(),
            Nodes = nodes.Select(m => new UnresolvedNode
            {
                Name = m.FileName,
                ReferenceNames = m.References.Select(r => r.FileName).ToList(),
                ChildNames = m.Children.Select(r => r.FileName).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(definition, SourceGenerationContext.Default.NodesDefinition);
    }

    public sealed class NodesDefinition
    {
        [JsonPropertyName("entries")]
        public List<string> Entries { get; set; } = [];

        [JsonPropertyName("nodes")]
        public List<UnresolvedNode> Nodes { get; set; } = [];
    }

    public sealed class UnresolvedNode
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("bytes")]
        public int? Bytes { get; set; }

        [JsonPropertyName("children")]
        public List<string> ChildNames { get; set; } = [];

        [JsonPropertyName("references")]
        public List<string> ReferenceNames { get; set; } = [];
    }
}
