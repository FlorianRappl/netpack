namespace NetPack;

public static class Helpers
{
    public static readonly Dictionary<string, string> ExtensionMap = new()
    {
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
}