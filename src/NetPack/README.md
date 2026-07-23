# NetPack

**A fast, batteries-included web bundler engine for .NET.** NetPack bundles and
optimizes JavaScript, TypeScript, JSX/TSX, CSS and HTML directly from managed
code — with a hand-written TypeScript/JSX parser, printer, scope-aware minifier
and tree-shaker. The core has no native dependencies and no external toolchain to
install.

> Looking for the command-line tool? Install it from npm (`npm i -D netpack`).
> **This package is the embeddable library** for use from .NET: ASP.NET Core
> build steps, MSBuild tasks, custom tooling, or tests.

## Install

```sh
dotnet add package NetPack.Core
```

The package is `NetPack.Core`, but the assembly and namespace are `NetPack` — so
you still write `using NetPack;`.

## Bundle a project

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

// …or straight to a directory
await Bundler.WriteToDirectoryAsync("src/index.html", "dist",
    new BundleOptions { Minify = true, SourceMaps = true });
```

`BundleOptions` mirrors the CLI flags — all optional, with production-friendly
defaults:

| Option | Purpose |
| --- | --- |
| `Minify`, `SourceMaps` | optimize for size; emit source maps |
| `Format` | `Esm` (default), `CommonJs`, `Umd`, `SystemJs` |
| `Platform` | `Web` (default), `Node`, `Deno` |
| `EntryNames` | naming template, e.g. `[name]-[hash]` for cache-busting |
| `PublicPath` | base URL/path prepended to emitted-file references |
| `Externals`, `Shared` | keep external / emit as shared bundles + import map |
| `Define`, `Alias`, `Loader`, `Conditions` | compile-time constants, import rewrites, per-extension loaders, extra `exports` conditions |
| `ExternalPackages` | keep every `node_modules` import external |

## Use the parser / printer directly

The TypeScript/JSX front-end is public, so you can parse, transform and print
without bundling:

```csharp
using NetPack.Syntax;
using NetPack.Syntax.Printer;

var module = Parser.ParseModule("const x: number = 1;", "in.ts");
string js = JsPrinter.Print(module);            // -> "const x = 1;"
```

`NetPack.Syntax` exposes the `Tokenizer`, `Parser`, the AST (`NetPack.Syntax.Ast`),
`JsPrinter`, and the source-map builder.

## Native asset processing is pluggable

The core references only **AngleSharp** (for HTML/CSS). Image resizing and
re-encoding are opt-in: implement `IAssetProcessor` and register it per
extension. netpack's own CLI registers a SkiaSharp-based processor exactly this
way, which is what keeps the core dependency-free.

```csharp
using NetPack.Assets;

AssetProcessorFactory.Register(".png", new MySkiaImageProcessor());
// unregistered types fall back to a pass-through copy
```

## Notes

- **Dependency-free core** — the only managed dependency is AngleSharp.
- **Node.js** is needed only for the optional Sass/Less/PostCSS/Svelte features
  (which shell out to a local Node install). Plain JS/TS/JSX/CSS/HTML bundling
  needs nothing extra.
- Targets **.NET 8**; trim/AOT-friendly.

## License

MIT. Source, documentation and issues: <https://github.com/FlorianRappl/netpack>.
