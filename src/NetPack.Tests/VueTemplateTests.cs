namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

public class VueTemplateTests
{
    private static IReadOnlySet<string> Locals(params string[] names) => new HashSet<string>(names);

    // -- expression prefixing ------------------------------------------------

    [Fact]
    public void Prefixes_free_identifiers_with_ctx()
    {
        var result = VueExpression.Prefix("count + 1", Locals());
        Assert.Contains("_ctx.count", result);
        Assert.DoesNotContain("_ctx.1", result);
    }

    [Fact]
    public void Leaves_locals_and_globals_untouched()
    {
        Assert.DoesNotContain("_ctx", VueExpression.Prefix("item.id", Locals("item")));

        var globals = VueExpression.Prefix("Math.max(a, b)", Locals());
        Assert.StartsWith("Math.max(", globals);
        Assert.Contains("_ctx.a", globals);
        Assert.Contains("_ctx.b", globals);
    }

    [Fact]
    public void Arrow_parameters_shadow_the_context()
    {
        var result = VueExpression.Prefix("list.filter(x => x.active)", Locals());
        Assert.Contains("_ctx.list", result);
        Assert.DoesNotContain("_ctx.x", result);
    }

    [Fact]
    public void Expands_object_shorthand_when_prefixing()
    {
        var result = VueExpression.Prefix("{ id: id, name: name }", Locals());
        Assert.Contains("_ctx.id", result);
        Assert.Contains("_ctx.name", result);
    }

    // -- end-to-end render precompilation ------------------------------------

    private static async Task<string> BundleVue(
        string vue, string script = "<script setup>\nconst msg = 'hi'\n</script>",
        params (string Name, string Content)[] extra)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-tpl-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "App.vue"), vue + "\n" + script + "\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "import App from './App.vue';\nconsole.log(App);\n");

            foreach (var (name, content) in extra)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            // `vue` is external so the test does not need it installed.
            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), new[] { "vue" }, Array.Empty<string>());
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
            return output;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Precompiles_interpolation_to_a_render_function()
    {
        var output = await BundleVue("<template><p>{{ msg }}</p></template>");

        Assert.Contains(".render = function (_ctx, _cache)", output);
        Assert.Contains("_vue_toDisplayString(_ctx.msg)", output);
        Assert.Contains("from \"vue\"", output);
        Assert.DoesNotContain(".template =", output);
    }

    [Fact]
    public async Task Precompiles_v_for_to_render_list()
    {
        var output = await BundleVue(
            "<template><ul><li v-for=\"item in items\" :key=\"item.id\">{{ item.name }}</li></ul></template>",
            "<script setup>\nconst items = []\n</script>");

        Assert.Contains("_vue_renderList(_ctx.items", output);
        // The v-for alias `item` is a local, so it is not prefixed with _ctx
        // (note `_ctx.items` legitimately contains the substring "_ctx.item").
        Assert.Contains("_vue_toDisplayString(item.name)", output);
        Assert.DoesNotContain("_ctx.item.", output);
    }

    [Fact]
    public async Task Precompiles_v_if_to_a_conditional()
    {
        var output = await BundleVue(
            "<template><span v-if=\"ok\">yes</span></template>",
            "<script setup>\nconst ok = true\n</script>");

        Assert.Contains("_ctx.ok ?", output);
        Assert.Contains("_vue_createCommentVNode(\"v-if\", true)", output);
    }

    [Fact]
    public async Task Precompiles_event_handlers()
    {
        var output = await BundleVue(
            "<template><button @click=\"inc\">+</button></template>",
            "<script setup>\nfunction inc() {}\n</script>");

        Assert.Contains("onClick: _ctx.inc", output);
    }

    [Fact]
    public async Task Resolves_components_and_registers_setup_imports()
    {
        var output = await BundleVue(
            "<template><my-widget /></template>",
            "<script setup>\nimport MyWidget from './MyWidget.vue'\n</script>",
            ("MyWidget.vue", "<template><span>w</span></template>"));

        Assert.Contains("_vue_resolveComponent(\"my-widget\")", output);
        Assert.Contains("_component_my_widget", output);
        // The imported component is auto-registered so resolveComponent finds it.
        Assert.Contains("components: { MyWidget }", output);
    }

    [Fact]
    public async Task Falls_back_to_runtime_template_for_unsupported_constructs()
    {
        // A custom directive is outside the supported subset -> runtime compilation.
        var output = await BundleVue(
            "<template><div v-custom=\"x\">hi</div></template>",
            "<script setup>\nconst x = 1\n</script>");

        Assert.Contains(".template =", output);
        Assert.DoesNotContain(".render = function", output);
    }
}
