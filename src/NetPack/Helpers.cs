namespace NetPack;

using System.Text;
using NetPack.Graph;

public static class Helpers
{
    private static readonly HashSet<char> invalid = [.. Path.GetInvalidFileNameChars()];

    public static readonly HashSet<string> BundleTypes = [".css", ".js", ".html"];

    public static readonly Dictionary<string, string> ExtensionMap = new()
    {
        { ".json", ".json" },
        { ".webmanifest", ".json" },
        { ".codegen", ".codegen" },
        { ".ts", ".js" },
        { ".cts", ".js" },
        { ".mts", ".js" },
        { ".tsx", ".js" },
        { ".mjs", ".js" },
        { ".jsx", ".js" },
        { ".js", ".js" },
        { ".cjs", ".js" },
        { ".html", ".html" },
        { ".htm", ".html" },
        { ".css", ".css" },
        { ".sass", ".css" },
        { ".scss", ".css" },
        { ".less", ".css" }
    };
    
    public static string CombinePath(string parent, string sub)
    {
        return Path.GetFullPath(Path.Combine(parent, sub));
    }

    public static string CombinePath(string parent, string mid, string sub)
    {
        return Path.GetFullPath(Path.Combine(parent, mid, sub));
    }

    public static string GetType(string extension)
    {
        return ExtensionMap.GetValueOrDefault(extension) ?? extension;
    }

    public static bool IsAssetType(string extension)
    {
        return !BundleTypes.Contains(extension);
    }

    public static IDictionary<T, Node> GetReplacements<T>(Node?[] nodes, IEnumerable<T> elements)
        where T : class
    {
        return elements.Select((r, i) => (nodes[i]!, r)).Where(m => m.Item1 is not null).ToDictionary(m => m.r, m => m.Item1);
    }

    public static string ToFileName(string name)
    {
        var sb = new StringBuilder();

        foreach (var c in name)
        {
            if (!invalid.Contains(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
