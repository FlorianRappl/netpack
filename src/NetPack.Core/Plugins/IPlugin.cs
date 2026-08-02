namespace NetPack.Plugins;

/// <summary>
/// The interface that all plugins must implement. Plugins tap into hooks
/// to modify the bundling behavior at various stages.
/// Inspired by rspack's Plugin trait.
/// </summary>
public interface IPlugin
{
    /// <summary>Human-readable name for debugging and error messages.</summary>
    string Name { get; }

    /// <summary>
    /// Called once to register taps on compiler and compilation hooks.
    /// This is where the plugin hooks into the build pipeline.
    /// </summary>
    void Apply(IApplyContext context);
}

/// <summary>
/// Context passed to <see cref="IPlugin.Apply"/> so plugins can tap into hooks.
/// </summary>
public interface IApplyContext
{
    /// <summary>Hooks that fire during the overall compilation lifecycle.</summary>
    CompilerHooks CompilerHooks { get; }

    /// <summary>Hooks that fire during the build/transform phase.</summary>
    CompilationHooks CompilationHooks { get; }
}

/// <summary>
/// The default implementation of <see cref="IApplyContext"/>.
/// </summary>
internal class ApplyContext : IApplyContext
{
    public CompilerHooks CompilerHooks { get; } = new();
    public CompilationHooks CompilationHooks { get; } = new();
}

/// <summary>
/// The plugin driver that manages plugin registration and hook execution.
/// </summary>
public class PluginDriver
{
    private readonly List<IPlugin> _plugins = [];
    private readonly ApplyContext _applyContext = new();

    /// <summary>Compiler-level hooks.</summary>
    public CompilerHooks CompilerHooks => _applyContext.CompilerHooks;

    /// <summary>Compilation-level hooks.</summary>
    public CompilationHooks CompilationHooks => _applyContext.CompilationHooks;

    /// <summary>Registers a plugin and calls its Apply method.</summary>
    public void Add(IPlugin plugin)
    {
        _plugins.Add(plugin);
        plugin.Apply(_applyContext);
    }

    /// <summary>Registers multiple plugins.</summary>
    public void AddRange(IEnumerable<IPlugin> plugins)
    {
        foreach (var plugin in plugins)
        {
            Add(plugin);
        }
    }

    /// <summary>Returns all registered plugins.</summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins;
}
