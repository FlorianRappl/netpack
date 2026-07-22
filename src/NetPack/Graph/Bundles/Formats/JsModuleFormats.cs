namespace NetPack.Graph.Bundles;

using System;

/// <summary>Resolves the <see cref="JsModuleFormat"/> strategy for a requested
/// <see cref="ModuleFormat"/>.</summary>
static class JsModuleFormats
{
    public static JsModuleFormat For(ModuleFormat format, string publicPath = "")
    {
        JsModuleFormat strategy = format switch
        {
            ModuleFormat.Esm => new EsmModuleFormat(),
            ModuleFormat.CommonJs => new CommonJsModuleFormat(),
            ModuleFormat.Umd => new UmdModuleFormat(),
            ModuleFormat.SystemJs => new SystemJsModuleFormat(),
            _ => throw new NotSupportedException($"Output format '{format}' is not supported."),
        };

        strategy.PublicPath = publicPath;
        return strategy;
    }
}
