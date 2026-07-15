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

            // The .vue compiled into the bundle: template string, scope id and the
            // scoped CSS selector are all present, and the whole bundle is valid JS.
            Assert.Contains(".template =", output);
            Assert.Contains("data-v-", output);
            Assert.Contains("Hello", output);
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
