namespace NetPack;

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using NetPack.Graph;

[JsonSerializable(typeof(GraphParser.NodesDefinition))]
[JsonSerializable(typeof(List<GraphParser.UnresolvedNode>))]
[JsonSerializable(typeof(GraphParser.UnresolvedNode))]
[JsonSerializable(typeof(Task<IResult>))]
[JsonSerializable(typeof(Importmap))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
