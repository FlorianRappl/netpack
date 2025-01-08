namespace NetPack.Graph;

public class Connected(Func<int, IEnumerable<Node>, string> getCommonName)
{
    private readonly Dictionary<Node, HashSet<Node>> allGraphs = [];
    private readonly Dictionary<Node, HashSet<Node>> allNodes = [];
    private readonly HashSet<Node> processedNodes = [];
    private readonly Func<int, IEnumerable<Node>, string> GetCommonName = getCommonName;

    public IDictionary<Node, HashSet<Node>> AllGraphs => allGraphs;

    public IDictionary<Node, HashSet<Node>> Apply(IEnumerable<Node> nodes)
    {
        Identify(nodes);
        Optimize();
        return allGraphs;
    }

    public static IDictionary<Node, HashSet<Node>> FindIndependentGraphs(IEnumerable<Node> nodes)
    {
        var connected = new Connected((i, _) => $"common#{i}");
        return connected.Apply(nodes);
    }

    private void Identify(IEnumerable<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (processedNodes.Add(node))
            {
                var currentGraph = new HashSet<Node>();
                Traverse(node, currentGraph);
                Assign(node, currentGraph);
                allGraphs[node] = currentGraph;
            }
        }
    }

    private void Assign(Node parent, HashSet<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsEmpty)
            {
                // Ignore empty nodes
            }
            else if (allNodes.TryGetValue(node, out var parents))
            {
                parents.Add(parent);
            }
            else
            {
                allNodes.Add(node, [parent]);
            }
        }
    }

    private void Optimize()
    {
        var shared = new Dictionary<string, Node>();

        foreach (var nodeRel in allNodes)
        {
            var parents = nodeRel.Value;
            var node = nodeRel.Key;

            if (parents.Count > 1 && !processedNodes.Contains(node))
            {
                var key = GetKey(parents);

                if (!shared.TryGetValue(key, out var sharedNode))
                {
                    var name = GetCommonName(shared.Count + 1, parents);
                    sharedNode = new Node(name, 0);
                    shared.Add(key, sharedNode);

                    foreach (var parent in parents)
                    {
                        allGraphs[parent].Add(sharedNode);
                    }
                }

                sharedNode.Children.Add(node);

                foreach (var parent in parents)
                {
                    allGraphs[parent].Remove(node);
                }
            }
        }

        foreach (var common in shared.Values)
        {
            allGraphs[common] = [.. common.Children];
        }
    }

    private string GetKey(HashSet<Node> nodes) => string.Join("|", nodes.Select(m => m.FileName).Order());

    private void Traverse(Node current, HashSet<Node> currentGraph)
    {
        if (currentGraph.Add(current))
        {
            foreach (var node in current.Children)
            {
                Traverse(node, currentGraph);
            }

            Identify(current.References);
        }
    }
}
