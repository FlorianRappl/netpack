namespace NetPack.Tests;

using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Multi-step rebuild tests using the IncrementalTestHelper — mirrors rspack's
/// hot-case pattern: build → edit → rebuild → assert → edit → rebuild → assert.
/// </summary>
public class IncrementalRebuildTests
{
    [Fact]
    public async Task Single_edit_changes_output()
    {
        using var test = new IncrementalTestHelper();
        await test.Setup("main.js",
            ("main.js", "import { a } from './a.js'; export default a;"),
            ("a.js", "export const a = 1;"));

        // Step 0: Initial build
        var output0 = await test.Build();
        test.AssertValidJs();
        test.AssertOutputContains(0, "a");

        // Edit a.js
        await test.Edit("a.js", "export const a = 99;");

        // Step 1: Rebuild — output should reflect change
        var output1 = await test.Rebuild();
        test.AssertValidJs();
        test.AssertCacheHits(minExpected: 1); // main.js unchanged → cache hit

        // Outputs differ between steps (different content)
        Assert.NotEqual(output0, output1);
    }

    [Fact]
    public async Task Adding_a_module_and_rebuilding_produces_valid_output()
    {
        using var test = new IncrementalTestHelper();
        await test.Setup("main.js",
            ("main.js", "import { a } from './a.js'; export default a;"),
            ("a.js", "export const a = 1;"));

        await test.Build();

        // Add a new module
        await test.AddFile("b.js", "export const b = 42;");
        await test.Edit("a.js",
            "import { b } from './b.js'; export const a = b + 1;");

        var output = await test.Rebuild();
        test.AssertValidJs();
    }

    [Fact]
    public async Task Removing_a_module_and_rebuilding_produces_valid_output()
    {
        using var test = new IncrementalTestHelper();
        await test.Setup("main.js",
            ("main.js", "import { a } from './a.js'; export default a;"),
            ("a.js", "import { b } from './b.js'; export const a = b;"),
            ("b.js", "export const b = 10;"));

        await test.Build();

        // Remove b.js and update a.js
        test.DeleteFile("b.js");
        await test.Edit("a.js", "export const a = 100;");

        var output = await test.Rebuild();
        test.AssertValidJs();
    }

    [Fact]
    public async Task Three_consecutive_edits_all_produce_valid_output()
    {
        using var test = new IncrementalTestHelper();
        await test.Setup("main.js",
            ("main.js", "export default 1;"));

        // Step 0
        await test.Build();
        test.AssertValidJs();

        // Step 1
        await test.Edit("main.js", "export default 2;");
        await test.Rebuild();
        test.AssertValidJs();

        // Step 2
        await test.Edit("main.js", "export default 3;");
        await test.Rebuild();
        test.AssertValidJs();

        // Step 3
        await test.Edit("main.js", "export default 4;");
        await test.Rebuild();
        test.AssertValidJs();
    }

    [Fact]
    public async Task Import_order_change_preserves_correctness()
    {
        using var test = new IncrementalTestHelper();
        await test.Setup("main.js",
            ("main.js",
                "import { a } from './a.js';" +
                "import { x } from './x.js';" +
                "export default a + x;"),
            ("a.js", "export const a = 10;"),
            ("x.js", "export const x = 20;"));

        await test.Build();
        test.AssertValidJs();

        // Swap import order
        await test.Edit("main.js",
            "import { x } from './x.js';" +
            "import { a } from './a.js';" +
            "export default a + x;");

        var output = await test.Rebuild();
        test.AssertValidJs();
    }

    [Fact]
    public async Task Adding_a_file_that_is_not_imported_does_not_break_rebuild()
    {
        using var test = new IncrementalTestHelper();
        await test.Setup("main.js",
            ("main.js", "export default 1;"));

        await test.Build();

        // Add an unrelated file — should not affect the build.
        await test.AddFile("unrelated.js", "console.log('hi');");

        var output = await test.Rebuild();
        test.AssertValidJs();
    }
}
