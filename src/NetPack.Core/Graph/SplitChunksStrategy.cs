namespace NetPack.Graph;

using System.Text.RegularExpressions;
using NetPack.Config;

/// <summary>
/// Chunk grouping strategy that implements webpack/rspack's
/// <c>optimization.splitChunks</c> with <c>cacheGroups</c>. Builds on top of
/// <see cref="Connected"/>'s shared-module identification, then applies
/// cacheGroup rules to re-group matching modules into named chunks.
/// </summary>
public class SplitChunksStrategy(SplitChunksConfig config) : IChunkGroupingStrategy
{
    private static readonly Dictionary<string, Regex> _regexCache = [];
    private static readonly object _cacheLock = new();

    public IDictionary<Node, HashSet<Node>> GroupChunks(IEnumerable<Node> entryNodes, BundlerContext context)
    {
        var connected = new Connected((i, nodes) => $"common.{i:0000}{nodes.First().Type}");
        var graphs = new Dictionary<Node, HashSet<Node>>(connected.Apply(entryNodes));

        if (config.CacheGroups is null || config.CacheGroups.Count == 0)
        {
            return graphs;
        }

        // Determine the first matching module's extension for naming
        var firstModule = graphs.Values
            .SelectMany(v => v)
            .FirstOrDefault(m => !m.IsEmpty && !m.IsAsset);
        var type = firstModule?.Type ?? ".js";

        // Build module → parents index from the current graphs
        var moduleParents = new Dictionary<Node, HashSet<Node>>();
        foreach (var (root, modules) in graphs)
        {
            foreach (var module in modules)
            {
                if (module.IsEmpty || module.IsAsset)
                {
                    continue;
                }

                if (!moduleParents.TryGetValue(module, out var parents))
                {
                    moduleParents[module] = parents = [];
                }

                parents.Add(root);
            }
        }

        var groups = config.CacheGroups
            .Select(kv => (Key: kv.Key, Cfg: kv.Value))
            .OrderByDescending(g => g.Cfg.Priority ?? 0)
            .ToList();

        var hasDefault = config.CacheGroups.ContainsKey("default");
        var assignedModules = new HashSet<Node>();

        foreach (var (key, group) in groups)
        {
            var matching = moduleParents
                .Where(kv => !assignedModules.Contains(kv.Key))
                .Where(kv => kv.Value.Count > 0)
                .Where(kv => MatchesTest(kv.Key, group.Test))
                .Select(kv => kv.Key)
                .ToHashSet();

            if (matching.Count == 0)
            {
                continue;
            }

            var enforce = group.Enforce == true;
            var minChunks = group.MinChunks ?? config.MinChunks ?? 1;

            if (!enforce)
            {
                var effectiveMinChunks = minChunks;
                var filtered = matching
                    .Where(m => (moduleParents.TryGetValue(m, out var p) ? p.Count : 0) >= effectiveMinChunks)
                    .ToHashSet();

                if (filtered.Count == 0)
                {
                    continue;
                }

                matching = filtered;
            }

            // Skip auto-generated "default" group with no explicit config
            if (key == "default" && group.Test is null && group.Name is null && group.Enforce is null && group.Priority is null)
            {
                continue;
            }

            if (!enforce)
            {
                var totalSize = matching.Sum(m => m.Bytes);
                var minSize = group.MinSize ?? config.MinSize ?? 0;
                if (totalSize < minSize)
                {
                    continue;
                }
            }

            var chunkName = (group.Name ?? key) + type;
            var chunkRoot = new Node(chunkName, matching.Sum(m => m.Bytes));

            foreach (var module in matching)
            {
                assignedModules.Add(module);

                if (moduleParents.TryGetValue(module, out var parents))
                {
                    foreach (var parent in parents)
                    {
                        if (graphs.TryGetValue(parent, out var modules))
                        {
                            modules.Remove(module);
                            modules.Add(chunkRoot);
                        }
                    }
                }
            }

            graphs[chunkRoot] = matching;
        }

        // Apply top-level minSize/minChunks to Connected's auto-extracted shared
        // chunks that remain (the default group wasn't overridden).
        if (config.MinSize > 0 || config.MinChunks > 1)
        {
            var sharedRoots = graphs.Keys
                .Where(k => k.FileName.StartsWith("common."))
                .ToList();

            foreach (var sharedRoot in sharedRoots)
            {
                if (!graphs.TryGetValue(sharedRoot, out var modules))
                {
                    continue;
                }

                if (config.MinChunks > 1)
                {
                    var allSatisfyMinChunks = modules.All(m =>
                        moduleParents.TryGetValue(m, out var parents) && parents.Count >= config.MinChunks);

                    if (!allSatisfyMinChunks)
                    {
                        // Push modules back to their entry chunks
                        foreach (var module in modules.ToList())
                        {
                            if (moduleParents.TryGetValue(module, out var parents))
                            {
                                foreach (var parent in parents)
                                {
                                    if (graphs.TryGetValue(parent, out var parentModules))
                                    {
                                        parentModules.Remove(sharedRoot);
                                        parentModules.Add(module);
                                    }
                                }
                            }
                        }

                        graphs.Remove(sharedRoot);
                    }
                }

                if (config.MinSize > 0 && graphs.ContainsKey(sharedRoot))
                {
                    var totalSize = graphs[sharedRoot].Sum(m => m.Bytes);
                    if (totalSize < config.MinSize)
                    {
                        foreach (var module in graphs[sharedRoot].ToList())
                        {
                            if (moduleParents.TryGetValue(module, out var parents))
                            {
                                foreach (var parent in parents)
                                {
                                    if (graphs.TryGetValue(parent, out var parentModules))
                                    {
                                        parentModules.Remove(sharedRoot);
                                        parentModules.Add(module);
                                    }
                                }
                            }
                        }

                        graphs.Remove(sharedRoot);
                    }
                }
            }
        }

        // Remove empty graphs
        var emptyRoots = graphs
            .Where(kv => kv.Value.Count == 0)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var root in emptyRoots)
        {
            graphs.Remove(root);
        }

        return graphs;
    }

    private static bool MatchesTest(Node module, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        if (!_regexCache.TryGetValue(pattern, out var regex))
        {
            lock (_cacheLock)
            {
                if (!_regexCache.TryGetValue(pattern, out regex))
                {
                    regex = new Regex(
                        "^" + Regex.Escape(pattern)
                            .Replace("\\*\\*/", ".*/")
                            .Replace("\\*\\*", ".*")
                            .Replace("\\*", "[^/]*") + "$",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    _regexCache[pattern] = regex;
                }
            }
        }

        var path = module.FileName.Replace('\\', '/');
        return regex.IsMatch(path);
    }
}
