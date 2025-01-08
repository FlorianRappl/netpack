namespace NetPack;

using System.Text.Json.Serialization;
using NetPack.Graph;

[JsonSerializable(typeof(List<GraphParser.UnresolvedNode>))]
[JsonSerializable(typeof(GraphParser.UnresolvedNode))]
[JsonSerializable(typeof(GraphParser.NodesDefinition))]
[JsonSerializable(typeof(Metadata.InputDefinition))]
[JsonSerializable(typeof(Dictionary<string, Metadata.InputDefinition>))]
[JsonSerializable(typeof(Metadata.OutputExportDefinition))]
[JsonSerializable(typeof(Metadata.OutputImportDefinition))]
[JsonSerializable(typeof(List<Metadata.OutputExportDefinition>))]
[JsonSerializable(typeof(List<Metadata.OutputImportDefinition>))]
[JsonSerializable(typeof(Metadata.OutputNode))]
[JsonSerializable(typeof(Metadata.InputNode))]
[JsonSerializable(typeof(Dictionary<string, Metadata.OutputNode>))]
[JsonSerializable(typeof(Dictionary<string, Metadata.InputNode>))]
[JsonSerializable(typeof(List<Metadata.InputImportDefinition>))]
[JsonSerializable(typeof(Metadata.InputImportDefinition))]
[JsonSerializable(typeof(Metadata.MetadataContainer))]
[JsonSerializable(typeof(Importmap))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
