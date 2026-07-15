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

    // -- modifiers -----------------------------------------------------------

    [Fact]
    public async Task Compiles_event_system_modifiers()
    {
        var output = await BundleVue(
            "<template><button @click.stop.prevent=\"go\">x</button></template>",
            "<script setup>\nfunction go(){}\n</script>");

        Assert.Contains("_vue_withModifiers(_ctx.go, [\"stop\", \"prevent\"])", output);
    }

    [Fact]
    public async Task Compiles_key_and_event_option_modifiers()
    {
        var output = await BundleVue(
            "<template><input @keyup.enter=\"submit\" @focus.once=\"onFocus\"></template>",
            "<script setup>\nfunction submit(){}\nfunction onFocus(){}\n</script>");

        Assert.Contains("_vue_withKeys(_ctx.submit, [\"enter\"])", output);
        Assert.Contains("onFocusOnce:", output);
    }

    [Fact]
    public async Task Compiles_v_bind_modifiers()
    {
        var output = await BundleVue(
            "<template><div :view-box.camel=\"vb\" :bar.prop=\"x\"></div></template>",
            "<script setup>\nconst vb='0 0 1 1'\nconst x=1\n</script>");

        Assert.Contains("viewBox: _ctx.vb", output);
        Assert.Contains("\".bar\": _ctx.x", output);
    }

    [Fact]
    public async Task Compiles_v_model_modifiers_on_native_input()
    {
        var output = await BundleVue(
            "<template><input v-model.trim.number=\"name\"></template>",
            "<script setup>\nconst name=''\n</script>");

        Assert.Contains("_vue_vModelText", output);
        Assert.Contains("void 0, { trim: true, number: true }", output);
    }

    [Fact]
    public async Task Compiles_dynamic_component()
    {
        var output = await BundleVue(
            "<template><component :is=\"page\" /></template>",
            "<script setup>\nconst page='div'\n</script>");

        Assert.Contains("_vue_resolveDynamicComponent(_ctx.page)", output);
    }

    [Fact]
    public async Task Scoped_styles_stamp_scope_id_in_render()
    {
        var output = await BundleVue(
            "<template><p class=\"box\">hi</p></template>\n<style scoped>.box{color:red}</style>");

        // The render is wrapped in pushScopeId/popScopeId (substring-matched so the
        // assertions survive the bundler rewriting the external `vue` import).
        Assert.Contains("pushScopeId(\"data-v-", output);
        Assert.Contains("popScopeId()", output);
        Assert.Contains(".__scopeId = \"data-v-", output);
    }
}
