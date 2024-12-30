namespace NetPack.Graph;

using System.Text.Json;

using static NetPack.Helpers;

public sealed class Dependency(string location, JsonElement meta)
{
    public string Location => location;
    public string Name => meta.GetProperty("name").GetString()!;
    public string Version => meta.GetProperty("version").GetString()!;
    public string Entry => CombinePath(Path.GetDirectoryName(location)!, GetEntry(meta));

    private static string GetEntry(JsonElement jsonObj)
    {
        if (jsonObj.TryGetProperty("browser", out var browserProperty) && browserProperty.ValueKind == JsonValueKind.String)
        {
            return browserProperty.GetString()!;
        }

        if (jsonObj.TryGetProperty("source", out var sourceProperty) && sourceProperty.ValueKind == JsonValueKind.String)
        {
            return sourceProperty.GetString()!;
        }

        if (jsonObj.TryGetProperty("module", out var moduleProperty) && moduleProperty.ValueKind == JsonValueKind.String)
        {
            return moduleProperty.GetString()!;
        }

        if (jsonObj.TryGetProperty("main", out var mainProperty) && mainProperty.ValueKind == JsonValueKind.String)
        {
            return mainProperty.GetString()!;
        }

        return "index.js";
    }
}
