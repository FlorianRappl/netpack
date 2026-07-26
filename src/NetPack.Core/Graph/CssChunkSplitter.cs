namespace NetPack.Graph;

using NetPack.Graph.Bundles;

/// <summary>
/// Analyzes CSS imports to determine which CSS files are shared across multiple
/// entry points and should be extracted into separate CSS chunks.
/// </summary>
public class CssChunkSplitter
{
    private readonly BundlerContext _context;

    public CssChunkSplitter(BundlerContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Computes shared CSS files that are imported by multiple entry bundles.
    /// Returns a mapping of CSS nodes to the shared chunk names.
    /// </summary>
    public IDictionary<Node, string> ComputeSharedCss()
    {
        var sharedCss = new Dictionary<Node, string>();

        // Find CSS files imported by multiple bundles
        var chunkIndex = 1;
        foreach (var (cssNode, bundles) in _context.CssImportedByBundles)
        {
            if (bundles.Count > 1)
            {
                var chunkName = $"common.{chunkIndex:0000}.css";
                sharedCss[cssNode] = chunkName;
                chunkIndex++;
            }
        }

        return sharedCss;
    }

    /// <summary>
    /// Creates CSS bundles for shared CSS chunks.
    /// </summary>
    public void CreateSharedCssBundles(IDictionary<Node, string> sharedCss)
    {
        foreach (var (cssNode, chunkName) in sharedCss)
        {
            if (!_context.CssFragments.ContainsKey(cssNode))
            {
                continue;
            }

            // Create a new node for the shared chunk
            var chunkNode = new Node(
                Path.Combine(Path.GetDirectoryName(cssNode.FileName)!, chunkName),
                cssNode.Bytes);

            // Create a CssBundle for the shared chunk
            var bundle = new CssBundle(_context, chunkNode, BundleFlags.Shared);

            // Copy the CSS fragment to the chunk
            if (_context.CssFragments.TryGetValue(cssNode, out var fragment))
            {
                _context.CssFragments.TryAdd(chunkNode, fragment);
            }

            // Add the bundle to the context
            _context.Bundles.TryAdd(chunkNode, bundle);

            // Update the CSS imports to point to the shared chunk
            _context.CssImports[cssNode] = bundle;
        }
    }
}
