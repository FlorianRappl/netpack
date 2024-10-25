namespace NewBundler.Graph;

using System.Text.Json;
using System.Text.Json.Serialization;

public class Parser(string content)
{
    public string Content => content;

    public List<Node> GetNodes()
    {
        var nodeLookup = new Dictionary<string, Node>();
        var unresolvedNodes = JsonSerializer.Deserialize(Content, SourceGenerationContext.Default.ListUnresolvedNode)!;

        // First, build collection of nodes
        foreach (var unresolved in unresolvedNodes)
        {
            var name = unresolved.Name ?? string.Empty;
            var node = new Node(name);
            nodeLookup[name] = node;
        }

        // Now, resolve references
        foreach (var unresolved in unresolvedNodes)
        {
            var name = unresolved.Name ?? string.Empty;
            var node = nodeLookup[name];

            foreach (var referenceName in unresolved.ReferenceNames)
            {
                if (nodeLookup.TryGetValue(referenceName, out var referencedNode))
                {
                    node.References.Add(referencedNode);
                }
                else
                {
                    throw new Exception($"Unresolved reference: {referenceName} for node {unresolved.Name}");
                }
            }
        }

        return [.. nodeLookup.Values];
    }

    public sealed class UnresolvedNode
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("references")]
        public List<string> ReferenceNames { get; set; } = [];
    }
}

[JsonSerializable(typeof(List<Parser.UnresolvedNode>))]
[JsonSerializable(typeof(Parser.UnresolvedNode))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
