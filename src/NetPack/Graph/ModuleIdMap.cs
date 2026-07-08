namespace NetPack.Graph;

using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Assigns compact, stable integer ids to modules keyed by their file name.
///
/// A production build uses a fresh map per compile. The dev server keeps a
/// single map alive across recompiles so a module keeps the same id every time
/// it is rebuilt — which is what lets hot-module replacement address an already
/// loaded module by id.
/// </summary>
public sealed class ModuleIdMap
{
    private int _next = -1;
    private readonly ConcurrentDictionary<string, int> _ids = new();

    public int Get(string fileName)
        => _ids.GetOrAdd(fileName, static (_, self) => Interlocked.Increment(ref self._next), this);

    /// <summary>True when the file already has an assigned id.</summary>
    public bool Has(string fileName) => _ids.ContainsKey(fileName);
}
