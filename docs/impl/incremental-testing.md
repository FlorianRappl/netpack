# NetPack Incremental Build Testing

> **Internal development note** (kept off the public docs site). Documents the
> test infrastructure and patterns for multi-step rebuild testing.

## Purpose

This document describes the workflow for writing and running multi-step
rebuild tests for netpack. The test pattern follows the standard bundler test
flow: build → edit → rebuild → assert → edit → rebuild → assert.

## When to use

- Writing regression tests for module graph changes during rebuilds
- Testing watch-mode rebuild correctness
- Adding tests for new features that affect rebuild behavior
- Verifying that edits to source files produce correct output

## Test infrastructure

### IncrementalTestHelper

The `IncrementalTestHelper` class in `src/NetPack.Tests/IncrementalTestHelper.cs`
wraps the full lifecycle:

```csharp
using var test = new IncrementalTestHelper();

// 1. Setup — create a temp project with source files
await test.Setup("main.js",
    ("main.js", "import { a } from './a.js'; export default a;"),
    ("a.js", "export const a = 1;"));

// 2. Initial build
var output0 = await test.Build();
test.AssertValidJs();

// 3. Edit a file
await test.Edit("a.js", "export const a = 99;");

// 4. Rebuild — output should reflect the edit
var output1 = await test.Rebuild();
test.AssertValidJs();

// 5. Assert on output
test.AssertOutputContains(0, "export const a = 1"); // step 0 output
test.AssertOutputContains(1, "export const a = 99"); // step 1 output
```

### Available methods

| Method | Purpose |
|--------|---------|
| `Setup(entry, files...)` | Create temp dir, write package.json + source files |
| `Build(options?)` | Full cold build, populates cache, returns JS output |
| `Rebuild(options?)` | Warm rebuild, reuses cache, returns JS output |
| `Edit(fileName, newContent)` | Overwrite a source file |
| `AddFile(fileName, content)` | Create a new source file |
| `DeleteFile(fileName)` | Remove a source file |
| `AssertValidJs()` | Re-parse last output, assert zero diagnostics |
| `AssertOutputContains(step, substring)` | Assert step N output contains text |
| `AssertOutputDoesNotContain(step, substring)` | Assert step N output does NOT contain text |
| `Outputs` | All outputs indexed by step |
| `Step` | Current step number |

## Test patterns

### Pattern 1: Single edit, verify output changed

```csharp
[Fact]
public async Task Single_edit_changes_output()
{
    using var test = new IncrementalTestHelper();
    await test.Setup("main.js",
        ("main.js", "import { a } from './a.js'; export default a;"),
        ("a.js", "export const a = 1;"));

    var output0 = await test.Build();
    test.AssertValidJs();

    await test.Edit("a.js", "export const a = 99;");

    var output1 = await test.Rebuild();
    test.AssertValidJs();
    Assert.NotEqual(output0, output1);
}
```

### Pattern 2: Multi-step (3+ edits) with snapshot-style assertions

```csharp
[Fact]
public async Task Three_consecutive_edits()
{
    using var test = new IncrementalTestHelper();
    await test.Setup("main.js", ("main.js", "export default 1;"));

    await test.Build();        // step 0
    test.AssertOutputContains(0, "1");

    await test.Edit("main.js", "export default 2;");
    await test.Rebuild();      // step 1
    test.AssertOutputContains(1, "2");

    await test.Edit("main.js", "export default 3;");
    await test.Rebuild();      // step 2
    test.AssertOutputContains(2, "3");
}
```

### Pattern 3: Add module → rebuild

```csharp
[Fact]
public async Task Adding_a_module()
{
    using var test = new IncrementalTestHelper();
    await test.Setup("main.js",
        ("main.js", "import { a } from './a.js'; export default a;"),
        ("a.js", "export const a = 1;"));

    await test.Build();

    await test.AddFile("b.js", "export const b = 42;");
    await test.Edit("a.js", "import { b } from './b.js'; export const a = b + 1;");

    var output = await test.Rebuild();
    test.AssertValidJs();
}
```

### Pattern 4: Remove module → rebuild

```csharp
[Fact]
public async Task Removing_a_module()
{
    using var test = new IncrementalTestHelper();
    await test.Setup("main.js",
        ("main.js", "import { a } from './a.js'; export default a;"),
        ("a.js", "import { b } from './b.js'; export const a = b;"),
        ("b.js", "export const b = 10;"));

    await test.Build();

    test.DeleteFile("b.js");
    await test.Edit("a.js", "export const a = 100;");

    var output = await test.Rebuild();
    test.AssertValidJs();
}
```

### Pattern 5: Build with custom options

```csharp
[Fact]
public async Task Minified_build_produces_valid_output()
{
    using var test = new IncrementalTestHelper();
    await test.Setup("main.js", ("main.js", "export default 1;"));

    var opts = new OutputOptions { IsOptimizing = true, IsReloading = false };
    var output = await test.Build(opts);
    test.AssertValidJs();
}
```

## Running tests

```bash
# All incremental tests
dotnet test src/NetPack.Tests/NetPack.Tests.csproj --filter Incremental

# Full test suite
dotnet test src/NetPack.Tests/NetPack.Tests.csproj
```

## Writing new test cases

1. Create a test method in `src/NetPack.Tests/IncrementalRebuildTests.cs`.

2. Use `IncrementalTestHelper` for multi-step tests to get automatic temp dir
   cleanup and assertion helpers.

3. Follow the pattern: `Setup → Build → Edit → Rebuild → Assert`. Repeat the
   Edit → Rebuild → Assert cycle for additional steps.

4. Always call `test.AssertValidJs()` after each build/rebuild to catch
   broken output.

5. Use `test.AssertOutputContains(step, substring)` to verify specific
   content at each step.
