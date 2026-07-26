namespace NetPack.Tests;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using Xunit;

/// <summary>
/// Regression coverage for deep, left-associative operator chains
/// (<c>a + b + c + …</c>). These nest thousands deep on the left, so any
/// recursive-descent AST pass that walks <c>Left</c> per level would overflow the
/// stack; <see cref="NetPack.Syntax.Ast.AstRewriter"/> walks the spine iteratively
/// instead.
/// </summary>
public class DeepExpressionTests
{
    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-deep-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData(2000)]
    public async Task Deep_binary_chain_traverses_without_stack_overflow(int terms)
    {
        var dir = Dir();

        try
        {
            var chain = string.Join(" + ", Enumerable.Range(0, terms));
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), $"export const total = {chain};");

            // The dependency visitor (a JsVisitor : AstRewriter) walks this module's
            // AST during traversal — the exact path that used to overflow.
            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));

            Assert.True(graph.Context.Modules.Count > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(2000)]
    public async Task Deep_logical_chain_traverses_without_stack_overflow(int terms)
    {
        var dir = Dir();

        try
        {
            var chain = string.Join(" && ", Enumerable.Range(0, terms).Select(i => $"v{i}"));
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                $"const v0 = 1;\n{string.Join("\n", Enumerable.Range(1, terms - 1).Select(i => $"const v{i} = {i};"))}\nexport const all = {chain};");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));

            Assert.True(graph.Context.Modules.Count > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
