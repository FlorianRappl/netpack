# NetPack.Core

[`NetPack.Core`](https://www.nuget.org/packages/NetPack.Core) is netpack's bundler
engine as an embeddable, managed .NET library — the same engine the CLI is built
on. Use it to bundle **programmatically**, or to use netpack's TypeScript/JSX
parser and printer on their own. See [Using netpack from .NET](./dotnet-libraries.md)
for how it compares to the CLI and to [`NetPack.Build`](./netpack-build.md).

It targets **.NET 8**, is trim/AOT-friendly, and has a single managed dependency
(AngleSharp).

## When to use it

- bundling as a step inside a larger .NET program or a custom build tool;
- generating assets on the fly (e.g. a server that compiles on demand);
- writing tests that assert on bundle output;
- using netpack's **TypeScript/JSX parser, printer, minifier or tree-shaker** on
  their own, without bundling anything.

## Install

```sh
dotnet add package NetPack.Core
```

The package id is `NetPack.Core`, but the assembly and namespace are `NetPack` —
so you write `using NetPack;`.

## Bundle a project

The entry point is the static `Bundler` facade. It takes an entry file (an
`.html`, `.js`/`.ts`, `.jsx`/`.tsx` or `.css`) and either returns the emitted
files in memory or writes them to a directory:

```csharp
using NetPack;

// …in memory
var result = await Bundler.BundleAsync("src/index.html", new BundleOptions
{
    Minify = true,
    Platform = Platform.Web,
    Format = ModuleFormat.Esm,
});

byte[] indexHtml = result.Outputs["index.html"];
foreach (var file in result.Files)
    Console.WriteLine($"{file.Name}  {file.Size} bytes  ({file.Modules} modules)");

// …or straight to a directory (created if missing)
await Bundler.WriteToDirectoryAsync("src/index.html", "dist",
    new BundleOptions { Minify = true, SourceMaps = true });
```

`BundleResult` exposes `Files` (the emitted-file report — name, size, module
count) and `Outputs` (a `name → byte[]` map of the contents).

## Options

`BundleOptions` mirrors the [CLI flags](./other-features.md); every property is
optional, so `new BundleOptions()` is a valid production-ESM build.

| Property | Default | Purpose |
| --- | --- | --- |
| `Minify` | `false` | minify + tree-shake |
| `SourceMaps` | `false` | emit a source map per JS bundle |
| `Format` | `ModuleFormat.Esm` | `Esm`, `CommonJs`, `Umd`, `SystemJs` — see [output formats](./output-formats.md) |
| `Platform` | `Platform.Web` | `Web`, `Node`, `Deno` — see [platforms](./platforms.md) |
| `EntryNames` | `[name]` | naming template, e.g. `[name]-[hash]` for cache-busting |
| `PublicPath` | `""` | base URL/path prepended to emitted-file references |
| `Externals` | empty | import specifiers to keep external |
| `Shared` | empty | dependencies emitted as shared bundles + import-map entries |
| `Define` | — | compile-time constant substitutions |
| `Alias` | — | import-specifier rewrites |
| `Loader` | — | per-extension loader overrides |
| `Conditions` | — | extra `package.json` `exports` conditions |
| `ExternalPackages` | `false` | keep every `node_modules` import external |

## Use the parser and printer directly

The TypeScript/JSX front-end is public, so you can parse, inspect and print
source without bundling:

```csharp
using NetPack.Syntax;
using NetPack.Syntax.Printer;

var module = Parser.ParseModule("const x: number = 1;", "in.ts");
string js = JsPrinter.Print(module);            // -> "const x = 1;"

// The AST is in NetPack.Syntax.Ast; diagnostics are on the parsed module.
bool ok = module.Diagnostics.Count == 0;
```

`NetPack.Syntax` exposes the `Tokenizer`, `Parser`, the AST
(`NetPack.Syntax.Ast`), `JsPrinter` (with `PrinterOptions.Compact` for minified
output), and the source-map builder.

## Custom asset processing

The core has **no native dependencies**, so binary asset handling (image
resizing/re-encoding) is opt-in. Implement `IAssetProcessor` and register it per
extension; unregistered types fall back to a pass-through copy:

```csharp
using NetPack.Assets;
using NetPack.Graph;

public sealed class MyImageProcessor : IAssetProcessor
{
    public Task<Stream> ProcessAsync(Asset asset, OutputOptions options)
    {
        // resize/re-encode asset.Root.FileName using the library of your choice…
        return Task.FromResult<Stream>(File.OpenRead(asset.Root.FileName));
    }
}

AssetProcessorFactory.Register(".png", new MyImageProcessor());
```

This is exactly how netpack's own distributions add image support: the native CLI
registers a SkiaSharp processor, and [`NetPack.Build`](./netpack-build.md)
registers a cross-platform ImageSharp one — keeping the core itself
dependency-free.

## Node.js

Plain JS/TS/JSX/CSS/HTML bundling is entirely native — no Node.js involved. Node
is only required for the optional preprocessor features (Sass, Less, PostCSS, and
Svelte compilation), which shell out to a local Node install.
