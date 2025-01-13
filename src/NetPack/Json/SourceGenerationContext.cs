namespace NetPack.Json;

using System.Text.Json.Serialization;
using NetPack.Graph;

[JsonSerializable(typeof(List<UnresolvedNode>))]
[JsonSerializable(typeof(UnresolvedNode))]
[JsonSerializable(typeof(NodesDefinition))]
[JsonSerializable(typeof(InputDefinition))]
[JsonSerializable(typeof(Dictionary<string, InputDefinition>))]
[JsonSerializable(typeof(OutputExportDefinition))]
[JsonSerializable(typeof(OutputImportDefinition))]
[JsonSerializable(typeof(List<OutputExportDefinition>))]
[JsonSerializable(typeof(List<OutputImportDefinition>))]
[JsonSerializable(typeof(OutputNode))]
[JsonSerializable(typeof(InputNode))]
[JsonSerializable(typeof(Dictionary<string, OutputNode>))]
[JsonSerializable(typeof(Dictionary<string, InputNode>))]
[JsonSerializable(typeof(List<InputImportDefinition>))]
[JsonSerializable(typeof(InputImportDefinition))]
[JsonSerializable(typeof(MetadataContainer))]
[JsonSerializable(typeof(Importmap))]
[JsonSerializable(typeof(ModuleFederation))]
[JsonSerializable(typeof(Dictionary<string, SharedEntry>))]
[JsonSerializable(typeof(Dictionary<string, RemoteEntry>))]
[JsonSerializable(typeof(SharedEntry))]
[JsonSerializable(typeof(RemoteEntry))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
