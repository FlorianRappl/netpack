namespace NetPack.Graph;

public class Cycle
{
    private readonly HashSet<Node> visited = [];
    private readonly HashSet<Node> recursionStack = [];

    private Cycle() {}

    public static bool Detect(Node node)
    {
        var cycle = new Cycle();
        return cycle.HasCycle(node);
    }

    private bool HasCycle(Node node)
    {
        if (recursionStack.Contains(node))
        {
            return true;
        }

        if (visited.Contains(node))
        {
            return false;
        }

        visited.Add(node);
        recursionStack.Add(node);

        foreach (var reference in node.Children)
        {
            if (HasCycle(reference))
            {
                return true;
            }
        }

        recursionStack.Remove(node);
        return false;
    }
}
