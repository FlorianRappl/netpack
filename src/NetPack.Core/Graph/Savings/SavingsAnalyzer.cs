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

    public static SavingsReport Analyze(BundlerContext context)
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
