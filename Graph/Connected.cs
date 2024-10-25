namespace NewBundler.Graph;

public class Connected
{
    public static List<HashSet<Node>> FindIndependentGraphs(List<Node> nodes)
    {
        var allVisitedNodes = new HashSet<Node>();
        var graphs = new List<HashSet<Node>>();

        foreach (var node in nodes)
        {
            if (!allVisitedNodes.Contains(node))
            {
                var currentGraph = new HashSet<Node>();
                Traverse(node, currentGraph, allVisitedNodes);
                graphs.Add(currentGraph);
            }
        }

        return graphs;
    }

    private static void Traverse(Node node, HashSet<Node> currentGraph, HashSet<Node> allVisitedNodes)
    {
        if (allVisitedNodes.Contains(node))
        {
            return;
        }

        currentGraph.Add(node);
        allVisitedNodes.Add(node);

        foreach (var reference in node.References)
        {
            Traverse(reference, currentGraph, allVisitedNodes);
        }
    }
}
