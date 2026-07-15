namespace NetPack.Tests;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

public class VueSfcTests
{
    private static void AssertValidJs(string code)
    {
        var reparsed = Parser.ParseModule(code, "out.js", new ParserOptions { Tolerant = true, TypeScript = true });
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public void Generates_component_with_template_and_scoped_style()
    {
        var sfc = new VueDescriptor
        {
            Template = "<div class=\"a\">Hi</div>",
            Script = "export default { name: 'Comp' }",
            Styles = [new VueStyleBlock { Css = ".a[data-v-abc123]{color:red}", Scoped = true }],
            RelativePath = "Comp.vue",
            ScopeId = "data-v-abc123",
        };

        var code = VueSfc.Generate(sfc);

        Assert.Contains("const __sfc_main = { name: 'Comp' }", code);
        Assert.Contains(".template = \"<div class=\\\"a\\\">Hi</div>\"", code);
        Assert.Contains(".__scopeId = \"data-v-abc123\"", code);
        Assert.Contains("document.createElement(\"style\")", code);
        Assert.Contains("export default __sfc_main", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Preserves_imports_and_defineComponent_default()
    {
        var sfc = new VueDescriptor
        {
            Script = "import { defineComponent } from 'vue';\nexport default defineComponent({ data: () => ({ n: 1 }) })",
            Template = "<p>{{ n }}</p>",
            RelativePath = "C.vue",
            ScopeId = "data-v-1",
        };

        var code = VueSfc.Generate(sfc);

        Assert.Contains("import { defineComponent } from 'vue'", code);
        Assert.Contains("const __sfc_main = defineComponent({ data: () => ({ n: 1 }) })", code);
        // No scoped styles -> no scope id assignment.
        Assert.DoesNotContain("__scopeId", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Missing_script_still_yields_a_component()
    {
        var sfc = new VueDescriptor { Template = "<span/>", RelativePath = "E.vue" };
        var code = VueSfc.Generate(sfc);

        Assert.Contains("const __sfc_main = {}", code);
        Assert.Contains("export default __sfc_main", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Strips_typescript_from_script_lang_ts()
    {
        // A lang="ts" script reaches Generate verbatim; the type annotation must be
        // gone once the generated module is parsed as a Vue module (TS stripping).
        var sfc = new VueDescriptor
        {
            Script = "interface Props { msg: string }\nexport default { name: 'T' as string }",
            Template = "<i/>",
            RelativePath = "T.vue",
        };

        var code = VueSfc.Generate(sfc);
        var reparsed = Parser.ParseModule(code, "T.vue", ParserOptions.ForFile("T.vue"));
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public async Task Bundles_a_vue_single_file_component_end_to_end()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-vue-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "Comp.vue"),
                "<template>\n  <div class=\"box\">Hello</div>\n</template>\n" +
                "<script>\nexport default { name: 'Comp' }\n</script>\n" +
                "<style scoped>\n.box { color: red; }\n</style>\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import Comp from './Comp.vue';\nconsole.log(Comp);\n");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // The .vue compiled into the bundle: the template is precompiled to a
            // render function, the scope id and scoped CSS selector are present, and
            // the whole bundle is valid JS.
            Assert.Contains(".render = function", output);
            Assert.Contains("data-v-", output);
            Assert.Contains("Hello", output);
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Script_setup_hoists_imports_and_returns_bindings()
    {
        var sfc = new VueDescriptor
        {
            ScriptSetup =
                "import { ref } from 'vue'\n" +
                "const props = defineProps({ msg: String })\n" +
                "const emit = defineEmits(['change'])\n" +
                "const count = ref(0)\n" +
                "function inc() { count.value++; emit('change') }",
            Template = "<button @click=\"inc\">{{ count }}</button>",
            RelativePath = "Counter.vue",
        };

        var code = VueSfc.Generate(sfc);

        // Imports hoisted above the component object.
        Assert.True(code.IndexOf("import { ref } from 'vue'") < code.IndexOf("const __sfc_main"));
        Assert.Contains("props: { msg: String }", code);
        Assert.Contains("emits: ['change']", code);
        Assert.Contains("setup(__props, __ctx)", code);
        Assert.Contains("const props = __props", code);
        Assert.Contains("const emit = __ctx.emit", code);
        Assert.Contains("return { ref, props, emit, count, inc };", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Script_setup_withDefaults_uses_merge_helper()
    {
        var sfc = new VueDescriptor
        {
            ScriptSetup = "const props = withDefaults(defineProps({ msg: String }), { msg: 'hi' })",
            RelativePath = "D.vue",
        };

        var code = VueSfc.Generate(sfc);

        Assert.Contains("function __mergeDefaults", code);
        Assert.Contains("props: __mergeDefaults({ msg: String }, { msg: 'hi' })", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Script_setup_expands_defineExpose_and_defineOptions()
    {
        var sfc = new VueDescriptor
        {
            ScriptSetup = "defineOptions({ inheritAttrs: false })\nconst x = 1\ndefineExpose({ x })",
            RelativePath = "O.vue",
        };

        var code = VueSfc.Generate(sfc);

        Assert.Contains("...({ inheritAttrs: false })", code);
        Assert.Contains("__ctx.expose({ x })", code);
        Assert.DoesNotContain("defineOptions", code);
        Assert.Contains("return { x };", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Script_setup_merges_with_classic_script_base()
    {
        var sfc = new VueDescriptor
        {
            Script = "export default { name: 'Merged', inheritAttrs: false }",
            ScriptSetup = "const a = 1",
            RelativePath = "M.vue",
        };

        var code = VueSfc.Generate(sfc);

        Assert.Contains("const __sfc_base = { name: 'Merged', inheritAttrs: false }", code);
        Assert.Contains("...__sfc_base", code);
        Assert.Contains("return { a };", code);
        AssertValidJs(code);
    }

    [Fact]
    public void Script_setup_strips_typescript()
    {
        var sfc = new VueDescriptor
        {
            ScriptSetup = "const n: number = 1\nconst props = defineProps({ x: String })",
            RelativePath = "T.vue",
        };

        var code = VueSfc.Generate(sfc);
        var reparsed = Parser.ParseModule(code, "T.vue", ParserOptions.ForFile("T.vue"));
        Assert.Empty(reparsed.Diagnostics);
        Assert.Contains("props: { x: String }", code);
    }

    [Fact]
    public async Task Bundles_a_script_setup_component_end_to_end()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-vue-setup-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "Counter.vue"),
                "<template>\n  <button>{{ count }}</button>\n</template>\n" +
                "<script setup>\n" +
                "const props = defineProps({ msg: String })\n" +
                "const emit = defineEmits(['go'])\n" +
                "let count = 0\n" +
                "function inc() { count++; emit('go') }\n" +
                "</script>\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import Counter from './Counter.vue';\nconsole.log(Counter);\n");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("setup(__props, __ctx)", output);
            Assert.Contains("return {", output);
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
