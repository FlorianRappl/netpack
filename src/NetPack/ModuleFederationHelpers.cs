namespace NetPack;

using System.Reflection;
using System.Text;
using System.Text.Json;
using NetPack.Graph;
using NetPack.Json;

static class ModuleFederationHelpers
{
    public static async Task<ModuleFederation> ReadFrom(string entry)
    {
        using var stream = File.OpenRead(entry);
        var result = await JsonSerializer.DeserializeAsync(stream, SourceGenerationContext.Default.ModuleFederation);
        return result!;
    }

    /// <summary>
    /// Validates the <c>kind</c> field of a <c>federation.json</c>. An omitted or
    /// empty value defaults to <c>module</c>. Any value other than <c>module</c> or
    /// <c>native</c> throws, listing the available options.
    /// </summary>
    public static string NormalizeKind(string? kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return "module";
        }

        return kind switch
        {
            "module" or "native" => kind,
            _ => throw new NotSupportedException(
                $"Unknown federation kind '{kind}'. Available options: module (default), native."),
        };
    }

    /// <summary>
    /// Builds the code for a <b>native federation</b> remote: a plain ES module that
    /// imports every shared dependency directly (so e.g. <c>import * as React from
    /// "react"</c> ends up at the top, resolved by the host's import map), exposes
    /// its modules as lazily imported ESM chunks, and re-exports both maps.
    /// </summary>
    public static string CreateNativeContainerCode(ModuleFederation details)
    {
        var shared = details.Shared?.Keys.ToList() ?? [];
        var locals = new List<string>();
        var builder = new StringBuilder();

        for (var i = 0; i < shared.Count; i++)
        {
            var local = $"__shared_{i}";
            locals.Add(local);
            builder.Append("import * as ").Append(local).Append(" from ").Append(GetString(shared[i])).Append(";\n");
        }

        // Reference the shared imports through an exported map so they survive tree
        // shaking and stay pinned at the top of the module.
        builder.Append("export const shared = ")
            .Append(GetObject(shared.Select((name, i) => $"[{GetString(name)}]: {locals[i]}")))
            .Append(";\n");

        // Each expose becomes its own ESM chunk, loaded on demand.
        builder.Append("export const exposes = ")
            .Append(GetObject(details.Exposes?.Select(m => $"[{GetString(m.Key)}]: () => import({GetString(m.Value)})") ?? []))
            .Append(";\n");

        builder.Append("export default { name: ").Append(GetString(details.Name)).Append(", exposes, shared };\n");
        return builder.ToString();
    }

    public static async Task<string> CreateContainerCode(BundlerContext context, ModuleFederation details)
    {
        var assembly = typeof(ModuleFederationHelpers).GetTypeInfo().Assembly;
        var names = assembly.GetManifestResourceNames();
        using var stream = assembly.GetManifestResourceStream($"NetPack.remote.js")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var code = await reader.ReadToEndAsync();

        return code
            .Replace("__name__", GetString(details.Name))
            .Replace("__share_scope__", GetString(details.ShareScope))
            .Replace("__share_strategy__", GetString(details.ShareStrategy))
            .Replace("__exposes__", GetObject(details.Exposes?.Select(m => $"[{GetString(m.Key)}]: () => import({GetString(m.Value)})") ?? []))
            .Replace("__remotes__", GetArray(details.Remotes?.Select(m => $"{{ alias: {GetString(m.Key)}, name: {GetString(m.Value.Name)}, entry: {GetString(m.Value.Entry)}, type: {GetString(m.Value.Entry)} }}") ?? []))
            .Replace("__shared__", GetObject(details.Shared?.Select(m => $"[{GetString(m.Key)}]: {{ version: {GetString(GetDependencyVersion(m.Key, context))}, scope: {GetString(m.Value.ShareScope)}, lib: () => import({GetString("shared:" + m.Key)}), shareConfig: {{ singleton: {GetString(m.Value.IsSingleton)}, requiredVersion: {GetString(m.Value.RequiredVersion)} }} }}") ?? []));
    }

    private static string GetDependencyVersion(string name, BundlerContext context)
    {
        var dep = context.Dependencies.FirstOrDefault(m => m.Name == name);
        return dep?.Version ?? "*";
    }

    private static string GetString(bool value)
    {
        return value ? "true" : "false";
    }

    private static string GetString(string value)
    {
        return $"'{value}'";
    }

    private static string GetObject(IEnumerable<string> properties)
    {
        return string.Concat("{", string.Join(", ", properties), "}");
    }

    private static string GetArray(IEnumerable<string> values)
    {
        return string.Concat("[", string.Join(", ", values), "]");
    }
}
