namespace NetPack;

using System.Text;
using NetPack.Graph;

public static class Helpers
{
    /// <summary>
    /// Returns the MIME type for a file extension (leading dot optional). Falls
    /// back to <c>application/octet-stream</c> for unknown extensions.
    /// </summary>
    public static string GetMimeType(string extension)
    {
        // Normalize: strip leading dot if present, lowercase.
        var ext = extension.ToLowerInvariant();
        if (ext.StartsWith('.'))
        {
            ext = ext[1..]; // ".png" → "png"
        }

        return ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            "avif" => "image/avif",
            "bmp" => "image/bmp",
            "ico" => "image/x-icon",
            "woff" => "font/woff",
            "woff2" => "font/woff2",
            "ttf" => "font/ttf",
            "otf" => "font/otf",
            "json" => "application/json",
            "txt" => "text/plain",
            "css" => "text/css",
            "wasm" => "application/wasm",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Converts asset bytes to a data URI using the file extension to pick the
    /// MIME type (e.g. <c>data:image/png;base64,iVBORw…</c>).
    /// </summary>
    public static string ToDataUri(string extension, byte[] content)
    {
        var mime = GetMimeType(extension);
        var base64 = Convert.ToBase64String(content);
        return $"data:{mime};base64,{base64}";
    }
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
        { ".vue", ".js" },
        { ".svelte", ".js" },
        { ".astro", ".js" },
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

    /// <summary>
    /// Builds the runtime URL an emitted file is referenced by. With no public
    /// path the reference stays document-relative (<c>./file.js</c>); a
    /// <c>--public-path</c> replaces that prefix (<c>https://cdn/app/file.js</c>,
    /// <c>/static/file.js</c>) so assets and chunks can be served from elsewhere.
    /// </summary>
    public static string PublicUrl(string publicPath, string fileName)
        => string.IsNullOrEmpty(publicPath) ? $"./{fileName}" : $"{publicPath.TrimEnd('/')}/{fileName}";

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
