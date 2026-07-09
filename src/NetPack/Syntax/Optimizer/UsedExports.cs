namespace NetPack.Syntax.Optimizer;

using System.Collections.Generic;

/// <summary>
/// Describes which of a module's exports are actually consumed by the rest of
/// the program. <see cref="All"/> means "keep everything" — the module is the
/// entry, is imported as a namespace, is pulled in through CommonJS
/// <c>require</c> / dynamic <c>import()</c>, or is re-exported with
/// <c>export * from</c>, so individual exports cannot be pruned.
/// </summary>
public sealed class UsedExports
{
    public bool All { get; set; }

    public HashSet<string> Names { get; } = new(System.StringComparer.Ordinal);

    public bool Contains(string name) => All || Names.Contains(name);

    public void Add(string name) => Names.Add(name);

    public void MarkAll() => All = true;

    public static UsedExports Everything() => new() { All = true };
}
