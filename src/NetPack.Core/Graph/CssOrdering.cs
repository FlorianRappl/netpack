namespace NetPack.Graph;

using NetPack.Graph.Bundles;

/// <summary>
/// Implements deterministic CSS ordering based on JS module evaluation order
/// and detects when CSS import order conflicts across chunk groups.
/// </summary>
public static class CssOrdering
{
    /// <summary>
    /// Detects ordering conflicts among CSS modules imported by multiple chunk
    /// groups in different relative orders. Uses per-bundle CSS import lists
    /// built during graph traversal (already in declaration order).
    /// </summary>
    public static IEnumerable<CssOrderConflict> DetectConflicts(BundlerContext context)
    {
        var conflicts = new List<CssOrderConflict>();

        // Check all pairs of CSS nodes that appear in at least two bundles
        var allCssNodes = context.CssImportedByBundles.Keys.ToList();

        for (var i = 0; i < allCssNodes.Count; i++)
        {
            for (var j = i + 1; j < allCssNodes.Count; j++)
            {
                var nodeA = allCssNodes[i];
                var nodeB = allCssNodes[j];

                var bundlesA = context.CssImportedByBundles.GetValueOrDefault(nodeA);
                var bundlesB = context.CssImportedByBundles.GetValueOrDefault(nodeB);

                if (bundlesA is null || bundlesB is null) continue;

                var common = bundlesA.Intersect(bundlesB).ToList();
                if (common.Count < 2) continue;

                // Check relative order in each common bundle using CssPerBundleOrder
                // (already in declaration order — no re-sorting needed)
                int? consistentOrder = null;

                foreach (var bundle in common)
                {
                    if (!context.CssPerBundleOrder.TryGetValue(bundle, out var list))
                    {
                        continue;
                    }

                    var posA = list.IndexOf(nodeA);
                    var posB = list.IndexOf(nodeB);
                    if (posA < 0 || posB < 0) continue;

                    var orderAB = posA < posB ? 1 : -1;

                    if (consistentOrder is null)
                    {
                        consistentOrder = orderAB;
                    }
                    else if (consistentOrder != orderAB)
                    {
                        conflicts.Add(new CssOrderConflict(
                            nodeA, nodeB,
                            [.. bundlesA.Select(b => b.Name)],
                            [.. bundlesB.Select(b => b.Name)]));
                        break;
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Returns the ordered list of CSS file names — useful for diagnostic/debug output.
    /// </summary>
    public static IReadOnlyList<string> GetOrderedCssFiles(BundlerContext context)
    {
        return context.CssImportedByBundles.Keys
            .OrderBy(n => context.CssImporterOrder.TryGetValue(n, out var order) ? order : int.MaxValue)
            .Select(n => Path.GetFileName(n.FileName))
            .ToList();
    }
}

public readonly record struct CssOrderConflict(
    Node ModuleA,
    Node ModuleB,
    IReadOnlyList<string> BundleA,
    IReadOnlyList<string> BundleB
);
