namespace NetPack.Graph.Bundles;

using System;

/// <summary>
/// Resolves the <see cref="JsModuleFormat"/> strategy for a requested
/// <see cref="ModuleFormat"/>. Only ESM is implemented today; the other formats
/// are recognised (so the plumbing is in place) but throw until they land.
/// </summary>
static class JsModuleFormats
{
    public static JsModuleFormat For(ModuleFormat format) => format switch
    {
        ModuleFormat.Esm => new EsmModuleFormat(),
        _ => throw new NotSupportedException(
            $"Output format '{format.ToString().ToLowerInvariant()}' is not supported yet (available: esm)."),
    };
}
