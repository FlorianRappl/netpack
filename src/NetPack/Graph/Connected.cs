namespace NetPack.Graph;

public class Connected
{
    private readonly Dictionary<string, HashSet<Node>> allGraphs = [];
    private readonly Dictionary<Node, HashSet<Node>> allNodes = [];
    private readonly HashSet<Node> processedNodes = [];

    private Connected()
    {
    }

    public IDictionary<string, HashSet<Node>> AllGraphs => allGraphs;

    public static IDictionary<string, HashSet<Node>> FindIndependentGraphs(IEnumerable<Node> nodes)
    {
        var connected = new Connected();
        connected.Identify(nodes);
        connected.Optimize();
        return connected.AllGraphs;
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
                allGraphs[node.FileName] = currentGraph;
            }
        }
    }

    private void Assign(Node parent, HashSet<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (allNodes.TryGetValue(node, out var parents))
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
                    sharedNode = new Node($"_c{shared.Count + 1}");
                    shared.Add(key, sharedNode);

                    foreach (var parent in parents)
                    {
                        allGraphs[parent.FileName].Add(sharedNode);
                    }
                }

                sharedNode.Children.Add(node);

                foreach (var parent in parents)
                {
                    allGraphs[parent.FileName].Remove(node);
                }
            }
        }

        foreach (var common in shared.Values)
        {
            allGraphs[common.FileName] = [.. common.Children];
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
