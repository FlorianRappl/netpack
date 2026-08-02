namespace NetPack.Plugins;

using System.Text.Json;
using System.Threading.Tasks;
using NetPack.Json;

/// <summary>
/// An <see cref="IHookRunner"/> that executes hook modules over the Node bridge
/// (<see cref="NodeJs"/>) — the same IPC used for Sass/Svelte/Solid. Payloads are
/// exchanged as source-generated JSON, so no reflection is used.
/// </summary>
internal sealed class NodeHookRunner(NodeJs bridge) : IHookRunner
{
    public async Task<HookInvocation?> RunAsync(string modulePath, HookInvocation payload)
    {
        var json = JsonSerializer.Serialize(payload, SourceGenerationContext.Default.HookInvocation);
        var response = await bridge.RunCommand("hook", modulePath, json);
        return response.Deserialize(SourceGenerationContext.Default.HookInvocation);
    }
}
