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
            .Replace("__shared__", GetObject(details.Shared?.Select(m => $"[{GetString(m.Key)}]: {{ version: {GetString(GetDependencyVersion(m.Key, context))}, scope: {GetString(m.Value.ShareScope)}, lib: () => import({GetString(m.Key)}), shareConfig: {{ singleton: {GetString(m.Value.IsSingleton)}, requiredVersion: {GetString(m.Value.RequiredVersion)} }} }}") ?? []));
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
