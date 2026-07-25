namespace NetPack.Graph.Writers;

/// <summary>
/// Precomputed source paths for watch-mode lookups. It turns repeated file and
/// directory membership checks into constant-time set lookups.
/// </summary>
internal sealed class SourcePathIndex
{
    private readonly HashSet<string> _files;
    private readonly HashSet<string> _directories;

    public SourcePathIndex(BundlerContext context)
    {
        _files = new HashSet<string>(StringComparer.Ordinal);
        _directories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in context.Modules.Values)
        {
            _files.Add(module.FileName);

            var directory = Path.GetDirectoryName(module.FileName);
            if (!string.IsNullOrEmpty(directory))
            {
                _directories.Add(directory);
            }
        }
    }

    public bool ContainsFile(string fullPath) => _files.Contains(fullPath);

    public bool ContainsDirectory(string fullPath) => _directories.Contains(fullPath);
}