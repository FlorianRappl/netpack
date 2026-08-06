namespace NetPack.Graph.Savings;

using NetPack.Graph.Bundles;
using NetPack.Json;

/// <summary>
/// Inspects the finished chunk graph for bundle-shape inefficiencies and turns
/// them into actionable recommendations. This is a pure, graph-only pass (no
/// I/O, no network) so it is cheap enough to run on every metafile.
///
/// It looks for three things:
/// <list type="bullet">
///   <item><b>Duplicated modules</b> — a source module whose code lands in more
///   than one output bundle. Extracting it into a shared chunk removes the
///   duplicated bytes outright.</item>
///   <item><b>Orphan shared chunks</b> — a <c>common.*</c> chunk pulled in by a
///   single entry. It buys no de-duplication and only costs a request, so it
///   should be merged back into its one consumer.</item>
///   <item><b>Small low-fan-out shared chunks</b> — a small chunk shared by only
///   a couple of entries. Inlining it into each trades a little duplicated code
///   for one fewer request and more predictable loading.</item>
/// </list>
/// </summary>
internal static class SavingsAnalyzer
{
    /// <summary>Chunks at or below this size are considered cheap enough to
    /// inline into their few consumers rather than keep as a separate request.</summary>
    private const int SmallChunkBytes = 20 * 1024;

    /// <summary>Fan-out at or below this is "few enough" that a small shared
    /// chunk is usually not worth its own request.</summary>
    private const int LowFanOut = 2;

    /// <summary>A single output larger than this is worth splitting for parallel
    /// download and caching. Matches webpack's default performance budget.</summary>
    private const int OversizedBundleBytes = 244 * 1024;

    /// <summary>Only bundles at least this large are considered for the
    /// "one module dominates the bundle" check (keeps tiny bundles quiet).</summary>
    private const int DominantBundleFloor = 50 * 1024;

    /// <summary>A module must be at least this big, and take at least
    /// <see cref="DominantShare"/> of its bundle, to be called out on its own.</summary>
    private const int DominantModuleBytes = 50 * 1024;

    /// <summary>Fraction of a bundle a single module must occupy to be "dominant".</summary>
    private const double DominantShare = 0.5;

    /// <summary>A split candidate must free at least this much (absolute) or
    /// <see cref="SplitCandidateShare"/> of the bundle to be worth suggesting.</summary>
    private const int SplitCandidateBytes = 30 * 1024;

    /// <summary>…or this fraction of the oversized bundle.</summary>
    private const double SplitCandidateShare = 0.2;

    /// <summary>Assets at or below this are cheap enough that inlining them
    /// (saving a request) usually beats a separate file.</summary>
    private const int SmallAssetBytes = 4 * 1024;

    /// <summary>An inlined asset larger than this bloats the bundle and can't be
    /// cached on its own — usually better emitted as a file.</summary>
    private const int LargeInlinedAssetBytes = 8 * 1024;

    /// <param name="context">The finished build graph to inspect.</param>
    /// <param name="inlineLimit">The active <c>--inline-limit</c> (bytes; 0 = off),
    /// needed to know which assets are currently inlined vs emitted as files.</param>
    public static SavingsReport Analyze(BundlerContext context, int inlineLimit = 0)
    {
        var root = Environment.CurrentDirectory;

        var jsBundles = context.Bundles.Values.Where(b => b.Type == ".js").ToList();
        var shared = jsBundles.Where(b => b.IsShared).ToList();
        var entries = jsBundles.Where(b => !b.IsShared).ToList();

        // A shared chunk's synthetic root node is present in the Items of every
        // entry that pulls it in, which is exactly the fan-out we want.
        var sharedByRoot = new Dictionary<Node, Bundle>();
        foreach (var s in shared)
        {
            sharedByRoot[s.Root] = s;
        }

        var importers = new Dictionary<Bundle, List<Bundle>>();
        foreach (var s in shared)
        {
            importers[s] = [];
        }

        foreach (var entry in entries)
        {
            foreach (var item in entry.Items)
            {
                if (sharedByRoot.TryGetValue(item, out var s))
                {
                    importers[s].Add(entry);
                }
            }
        }

        var recommendations = new List<SavingsRecommendation>();
        var potentialBytes = 0;

        string Name(Bundle b) => b.GetFileName();
        static int SizeOf(Bundle b) => b.Items.Where(m => m.Type == ".js").Sum(m => m.Bytes);

        // 1) Modules that physically appear in more than one bundle. Under the
        //    default strategy netpack already extracts shared modules, so this
        //    mainly catches split-chunks / externals edge cases — but when it
        //    fires it is a guaranteed win.
        var moduleToBundles = new Dictionary<Node, List<Bundle>>();
        foreach (var bundle in jsBundles)
        {
            foreach (var item in bundle.Items)
            {
                if (item.Type != ".js" || item.Bytes <= 0 || sharedByRoot.ContainsKey(item))
                {
                    continue;
                }

                if (!moduleToBundles.TryGetValue(item, out var owners))
                {
                    moduleToBundles[item] = owners = [];
                }

                owners.Add(bundle);
            }
        }

        foreach (var (module, owners) in moduleToBundles)
        {
            if (owners.Count <= 1)
            {
                continue;
            }

            var wasted = module.Bytes * (owners.Count - 1);
            potentialBytes += wasted;

            var name = Path.GetRelativePath(root, module.FileName);
            var outputs = owners.Select(Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            recommendations.Add(new SavingsRecommendation
            {
                Kind = "duplicate-module",
                Severity = "high",
                Bytes = wasted,
                Requests = 0,
                Modules = [name],
                Bundles = outputs,
                Message =
                    $"'{name}' is bundled into {owners.Count} outputs ({string.Join(", ", outputs)}). "
                    + $"Move it into a shared dependency (or import it from a single module) to drop ~{Human(wasted)} of duplicated code.",
            });
        }

        // 2) & 3) Shared-chunk fan-out.
        foreach (var s in shared)
        {
            var users = importers[s];
            var size = SizeOf(s);

            if (users.Count <= 1)
            {
                var consumer = users.Count == 1 ? Name(users[0]) : "its only consumer";
                recommendations.Add(new SavingsRecommendation
                {
                    Kind = "merge-orphan-chunk",
                    Severity = "high",
                    Bytes = 0,
                    Requests = 1,
                    Bundles = [Name(s), .. users.Select(Name)],
                    Message =
                        $"Shared chunk '{Name(s)}' ({Human(size)}) is loaded by {(users.Count == 0 ? "no" : "a single")} entry"
                        + $"{(users.Count == 1 ? $" ({consumer})" : "")}. "
                        + $"It de-duplicates nothing — merge it into {consumer} to save one request at no size cost.",
                });
            }
            else if (users.Count <= LowFanOut && size > 0 && size <= SmallChunkBytes)
            {
                var added = size * (users.Count - 1);
                var consumers = users.Select(Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                recommendations.Add(new SavingsRecommendation
                {
                    Kind = "inline-small-chunk",
                    Severity = "medium",
                    Bytes = -added,
                    Requests = 1,
                    Bundles = [Name(s), .. consumers],
                    Message =
                        $"Shared chunk '{Name(s)}' ({Human(size)}) is used by only {users.Count} bundles ({string.Join(", ", consumers)}). "
                        + $"Inlining it into each removes one request for ~{Human(added)} of extra code — fewer requests and more predictable load order.",
                });
            }
        }

        // 3b) Asset inlining. An asset either ships as its own file (a request)
        //     or is baked into the bundle as a data URI (bytes, duplicated per
        //     referencing bundle). Flag the cases where flipping that choice
        //     clearly helps.
        var assetBundles = new Dictionary<Node, HashSet<Bundle>>();
        foreach (var bundle in context.Bundles.Values)
        {
            foreach (var item in bundle.Items)
            {
                foreach (var child in item.Children.Concat(item.References))
                {
                    if (context.Assets.ContainsKey(child))
                    {
                        if (!assetBundles.TryGetValue(child, out var set))
                        {
                            assetBundles[child] = set = [];
                        }

                        set.Add(bundle);
                    }
                }
            }
        }

        foreach (var (assetNode, asset) in context.Assets)
        {
            var size = asset.Content.Length;
            if (size <= 0 || !assetBundles.TryGetValue(assetNode, out var users) || users.Count == 0)
            {
                continue;
            }

            var file = asset.GetFileName();
            var assetPath = Path.GetRelativePath(root, assetNode.FileName);

            if (!IsInlined(assetNode, size, inlineLimit))
            {
                // Emitted as a separate file. Small + single-use → inlining
                // trades a few bytes for one fewer request.
                if (size <= SmallAssetBytes && users.Count == 1)
                {
                    recommendations.Add(new SavingsRecommendation
                    {
                        Kind = "inline-asset",
                        Severity = "low",
                        Bytes = -size,
                        Requests = 1,
                        Modules = [assetPath],
                        Bundles = [.. users.Select(Name)],
                        Message =
                            $"'{file}' ({Human(size)}) is a separate request but used in only one bundle. "
                            + $"Inlining it (raise --inline-limit to ≥ {size}, or add ?inline to the import) would save one request for just {Human(size)} added.",
                    });
                }
            }
            else if (users.Count > 1)
            {
                // Inlined into several bundles → duplicated. Emitting one file
                // removes the duplication and makes it cacheable.
                var wasted = size * (users.Count - 1);
                potentialBytes += wasted;
                recommendations.Add(new SavingsRecommendation
                {
                    Kind = "stop-inlining-asset",
                    Severity = "medium",
                    Bytes = wasted,
                    Requests = 0,
                    Modules = [assetPath],
                    Bundles = [.. users.Select(Name)],
                    Message =
                        $"'{file}' ({Human(size)}) is inlined into {users.Count} bundles, duplicating it each time. "
                        + $"Stop inlining it (drop --inline-limit below {size}, or add ?inline=never) so it becomes one cacheable request and drop ~{Human(wasted)}.",
                });
            }
            else if (size >= LargeInlinedAssetBytes)
            {
                // Inlined once, but big enough that a cacheable file beats
                // bloating the bundle.
                recommendations.Add(new SavingsRecommendation
                {
                    Kind = "stop-inlining-asset",
                    Severity = "low",
                    Bytes = size,
                    Requests = -1,
                    Modules = [assetPath],
                    Bundles = [.. users.Select(Name)],
                    Message =
                        $"'{file}' ({Human(size)}) is inlined into the bundle. "
                        + $"Emitting it as a file drops {Human(size)} from the bundle and lets the browser cache it separately (one extra request).",
                });
            }
        }

        // 4) The same package resolved at more than one version. Every extra
        //    version ships a full copy of that library, so this is usually the
        //    single biggest, easiest win when it happens.
        var packageVersions = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var dependency in context.Dependencies)
        {
            string name, version;
            try
            {
                name = dependency.Name;
                version = dependency.Version;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
            {
                continue;
            }

            if (!packageVersions.TryGetValue(name, out var set))
            {
                packageVersions[name] = set = new SortedSet<string>(StringComparer.Ordinal);
            }

            set.Add(version);
        }

        foreach (var (package, versions) in packageVersions)
        {
            if (versions.Count <= 1)
            {
                continue;
            }

            recommendations.Add(new SavingsRecommendation
            {
                Kind = "duplicate-package",
                Severity = "high",
                Bytes = 0,
                Requests = 0,
                Modules = [package],
                Message =
                    $"'{package}' is present at {versions.Count} versions ({string.Join(", ", versions)}). "
                    + "Each version ships a full copy — align them on one version (dedupe / update your lockfile) to drop the extra copies.",
            });
        }

        // 5) Oversized outputs and single modules that dominate a bundle.
        foreach (var bundle in jsBundles)
        {
            var size = SizeOf(bundle);

            if (size > OversizedBundleBytes)
            {
                // Don't just say "split it" — find where. A good lazy-load point
                // is a top-level import whose dependency subtree is only reachable
                // through it (so moving it to import() actually shrinks the entry).
                var candidates = FindSplitCandidates(bundle, sharedByRoot)
                    .Where(c => c.Bytes >= SplitCandidateBytes || c.Bytes >= size * SplitCandidateShare)
                    .Take(3)
                    .ToList();

                string message;
                List<string>? modules = null;
                string severity;

                if (candidates.Count > 0)
                {
                    var best = candidates
                        .Select(c => $"'{Path.GetRelativePath(root, c.Gateway.FileName)}' (~{Human(c.Bytes)})")
                        .ToList();
                    modules = candidates
                        .Select(c => Path.GetRelativePath(root, c.Gateway.FileName))
                        .ToList();
                    severity = size > OversizedBundleBytes * 2 ? "high" : "medium";
                    message =
                        $"'{Name(bundle)}' is {Human(size)}, above the {Human(OversizedBundleBytes)} budget. "
                        + $"The clearest thing to lazy-load is {best[0]} of dependencies reached only through that import — "
                        + $"turn it into a dynamic import() to move it off the critical path"
                        + (best.Count > 1 ? $". Other candidates: {string.Join(", ", best.Skip(1))}." : ".");
                }
                else
                {
                    // Nothing detaches cleanly — the modules are entangled, so an
                    // oversized bundle may simply be the honest shape here.
                    severity = "low";
                    message =
                        $"'{Name(bundle)}' is {Human(size)}, above the {Human(OversizedBundleBytes)} budget, but no single import "
                        + "cleanly splits off a large subtree — its modules are used throughout. Splitting may not help; "
                        + "consider route-level code splitting in your app, or accept the size.";
                }

                recommendations.Add(new SavingsRecommendation
                {
                    Kind = "oversized-bundle",
                    Severity = severity,
                    Bytes = 0,
                    Requests = 0,
                    Bundles = [Name(bundle)],
                    Modules = modules,
                    Message = message,
                });
            }

            if (size < DominantBundleFloor)
            {
                continue;
            }

            var heaviest = bundle.Items
                .Where(m => m.Type == ".js" && m.Bytes > 0 && !sharedByRoot.ContainsKey(m))
                .OrderByDescending(m => m.Bytes)
                .FirstOrDefault();

            if (heaviest is not null && heaviest.Bytes >= DominantModuleBytes && heaviest.Bytes >= size * DominantShare)
            {
                var name = Path.GetRelativePath(root, heaviest.FileName);
                var pct = (int)Math.Round(100.0 * heaviest.Bytes / size);
                var isVendor = name.Replace('\\', '/').Contains("/node_modules/") || name.StartsWith("node_modules/");
                recommendations.Add(new SavingsRecommendation
                {
                    Kind = "heavy-module",
                    Severity = "medium",
                    Bytes = 0,
                    Requests = 0,
                    Modules = [name],
                    Bundles = [Name(bundle)],
                    Message =
                        $"'{name}' ({Human(heaviest.Bytes)}) is {pct}% of '{Name(bundle)}'. "
                        + (isVendor
                            ? "Load it lazily where it's used, or swap it for a lighter alternative."
                            : "Consider splitting it out behind a dynamic import() so it isn't in the critical path."),
                });
            }
        }

        // Most impactful first: high before medium/low, then by bytes saved.
        recommendations.Sort((a, b) =>
        {
            var byRank = Rank(b.Severity).CompareTo(Rank(a.Severity));
            return byRank != 0 ? byRank : b.Bytes.CompareTo(a.Bytes);
        });

        return new SavingsReport
        {
            PotentialBytes = potentialBytes,
            Recommendations = recommendations.Count > 0 ? recommendations : null,
        };
    }

    /// <summary>
    /// Finds the natural lazy-load points inside a bundle: each top-level import
    /// of the entry, scored by the size of the dependency subtree that is only
    /// reachable through that import. Converting such an import to a dynamic
    /// <c>import()</c> moves that whole exclusive subtree out of the entry, so
    /// these are the modules a developer should actually consider splitting.
    /// A gateway that is also reached by another static import scores ~0 and is
    /// correctly ignored.
    /// </summary>
    private static List<(Node Gateway, int Bytes)> FindSplitCandidates(
        Bundles.Bundle bundle, Dictionary<Node, Bundles.Bundle> sharedByRoot)
    {
        var items = new HashSet<Node>(
            bundle.Items.Where(m => m.Type == ".js" && m.Bytes > 0 && !sharedByRoot.ContainsKey(m)));

        var root = bundle.Root;

        IEnumerable<Node> Adjacent(Node n)
        {
            foreach (var child in n.Children)
            {
                if (items.Contains(child))
                {
                    yield return child;
                }
            }
        }

        var results = new List<(Node, int)>();

        foreach (var gateway in Adjacent(root).Distinct())
        {
            // What the entry still statically needs once the single root→gateway
            // edge becomes dynamic (other importers of the gateway still count).
            var keep = new HashSet<Node> { root };
            var stack = new Stack<Node>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                foreach (var child in Adjacent(node))
                {
                    if (node == root && child == gateway)
                    {
                        continue; // sever only this one static edge
                    }

                    if (keep.Add(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            // Everything the gateway pulls in.
            var subtree = new HashSet<Node> { gateway };
            stack.Push(gateway);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                foreach (var child in Adjacent(node))
                {
                    if (subtree.Add(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            var exclusiveBytes = 0;
            foreach (var module in subtree)
            {
                if (!keep.Contains(module))
                {
                    exclusiveBytes += module.Bytes;
                }
            }

            if (exclusiveBytes > 0)
            {
                results.Add((gateway, exclusiveBytes));
            }
        }

        results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return results;
    }

    /// <summary>Mirrors <see cref="Bundles.Bundle.IsInlined"/> for the metafile
    /// pass, where no <see cref="OutputOptions"/> instance is on hand.</summary>
    private static bool IsInlined(Node node, int size, int inlineLimit)
    {
        if (node.InlineLimitOverride == -1) return false;
        if (node.InlineLimitOverride > 0) return size <= node.InlineLimitOverride.Value;
        return inlineLimit > 0 && size <= inlineLimit;
    }

    private static int Rank(string severity) => severity switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0,
    };

    private static string Human(int bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024.0;
        return kb < 1024 ? $"{kb:0.#} KB" : $"{kb / 1024.0:0.#} MB";
    }
}
