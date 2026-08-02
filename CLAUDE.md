# CLAUDE.md

Guidance for working in the **netpack** repository. Read this first — it captures
the architecture and the non-obvious conventions/gotchas that are easy to trip on.

## What netpack is

netpack is a web bundler (esbuild/Vite-class) built as an **Ahead-of-Time (AoT)
compiled .NET CLI**, with a **hand-written JavaScript/TypeScript/JSX parser,
printer, minifier and tree-shaker** — no Babel/Acorn/esbuild dependency for the
core. It takes a single entry point (HTML, JS/TS/JSX, CSS, or `federation.json`),
follows what it references, and emits bundles. It's distributed as a native binary
per platform through npm (wrapper package `netpack` + `@netpack/<os>-<arch>`).

## Repository layout

```
src/
  NetPack.Core/     Managed library — ALL bundler logic. Dependency-light (AngleSharp only).
                    Namespace is `NetPack` (RootNamespace), assembly/package id is `NetPack.Core`.
  NetPack.Cli/      AoT executable. Output binary is `netpack` (TargetName). SkiaSharp + ASP.NET + CommandLineParser.
  NetPack.Build/    MSBuild task package (NetPack.Build) built on NetPack.Core. Cross-platform image processor (ImageSharp).
  NetPack.Tests/    xUnit tests.
  npm/              npm wrapper: run()/typed `netpack` API (dev/main.ts → dist), install.ts, @netpack/<platform> packages.
  resources/        Embedded runtime assets (e.g. module-federation/remote.js).
  Directory.Build.props   Shared version (VersionPrefix), LangVersion 13.
  NetPack.sln
docs/               User documentation (Markdown). See "Docs system" below.
www/                Astro documentation website (netpack.anglevisions.com).
data/               Sample projects used by trial.sh / manual perf runs.
art/                NuGet icon.
build.sh platform.sh trial.sh version.sh   Helper scripts.
```

Inside `NetPack.Core`:

- `Graph/` — the heart. `Traverse.cs` builds the module graph (resolution,
  loaders, framework dispatch). `Bundles/` renders bundles; `Bundles/Formats/`
  holds the ESM/CJS/UMD/SystemJS envelopes. `Platforms/` has the web/node/deno
  targets. `Writers/` emits to memory or disk. `Visitors/` walks module ASTs.
- `Syntax/` — the native JS toolchain: `Tokenizer`, `Parser.*` (partial class),
  `Printer/JsPrinter`, `Minifier/Mangler`, `Optimizer/TreeShaker`, `Ast/`
  (node model + `AstRewriter`).
- `NodeJs.cs` — the optional Node "bridge" (see below).
- `Bundler.cs` — the public library entry point (`Bundler.BundleAsync` /
  `WriteToDirectoryAsync` + `BundleOptions`).

## Build, run, test

Requires the .NET 8 SDK and Node.js.

```bash
dotnet build src/NetPack.sln                     # build everything
dotnet test src/NetPack.Tests/NetPack.Tests.csproj   # run the test suite (xUnit)
./build.sh        # AoT-publish the CLI for THIS platform and stage it into the npm package
./trial.sh        # publish + smoke-test graph/bundle against data/projects/large
./platform.sh     # prints the runtime id (e.g. osx-arm64) used by the scripts
```

Run the built binary directly, e.g. `.../publish/netpack bundle src/index.html --minify`.
Commands: `bundle`, `serve`, `analyze`, `graph`, `inspect`.

> Some sandboxes have **no .NET toolchain**. If `dotnet` isn't available, you
> can't compile or run tests here — reason about changes carefully, verify
> regex/parse/string logic with small Node/Python scripts, and tell the user to
> run `dotnet test` to confirm. Never claim tests pass if you couldn't run them.

## Architecture notes

- **Pipeline.** `Traverse.From(...)` builds the graph: parse each module, discover
  dependencies (`JsVisitor`/`HtmlVisitor`), resolve them, dispatch by extension.
  Then bundles are formed per connected component (`Connected`), and a `Writer`
  calls each `Bundle.Stringify(OutputOptions)` to render output. JS lowering
  (JSX→factory calls, imports→`require`, dynamic import, dead-branch folding)
  happens in `JsBundle` at render time.
- **Output formats** live behind `JsModuleFormat` (`Bundles/Formats/`). The
  envelope decides how externals/shared bundles are linked and how the entry is
  exported. ESM is default.
- **Platforms** (`Platforms/PlatformTarget.cs`) decide which specifiers are
  runtime built-ins (kept external) and which `package.json` `exports` conditions
  apply. On `node`, bare core modules resolve locally first, else are emitted as
  `node:`-prefixed externals.
- **The Node bridge (`NodeJs.cs`)** shells out to Node for things whose canonical
  compiler is JS: Sass, LESS, PostCSS, Svelte, Solid (`babel-preset-solid`), and
  `.codegen`. These are **opt-in**: the relevant npm package must be installed;
  when absent the bridge logs a failure and returns null rather than crashing.
  Vue and Astro, by contrast, are compiled **natively** in C#.
- **Framework dispatch** happens in `Traverse.AddNewNodeToBundle` (by extension
  for `.vue`/`.astro`/`.svelte`) and in `ProcessJavaScript` (Solid routes
  `.jsx`/`.tsx` through the bridge when `solid-js` is detected and `react` isn't).

## Conventions & gotchas (important)

- **AoT-safe only.** No runtime reflection or reflection-based serialization.
  JSON uses source-generated contexts — add new DTOs to
  `Json/SourceGenerationContext.cs`. Avoid patterns that produce IL2026/IL3050
  trim/AoT warnings; the CLI treats them seriously.
- **Assembly identity is deliberate and fragile.** The library's namespace is
  `NetPack` but its assembly/package id is `NetPack.Core`; the CLI's binary is
  `netpack` (via `TargetName`, not `AssemblyName`). This avoids NuGet restore
  ambiguity and case-insensitive file collisions (`NetPack.dll` vs `netpack.dll`)
  that break the AoT/ILC step. `InternalsVisibleTo` grants `netpack`,
  `NetPack.Cli`, `NetPack.Tests`. Don't "tidy" these names.
- **The printer normalizes string literals to double quotes.** Assertions on
  emitted code must expect `"x"`, not `'x'`.
- **JS lowering mutates the AST in place.** A given `Bundle` should be
  `Stringify`-d **once**. In tests that need multiple renders of the same input,
  build a **fresh `Traverse.From` graph per render** (see `CliOptionsTier2Tests`,
  `ModuleFormatTests`).
- **AST visitors must not recurse down long operator spines.** `AstRewriter`
  iterates the left spine of binary/logical chains to avoid stack overflow on
  machine-generated `a + b + c + …`. Keep new passes iterative there; the printer
  still recurses (only shallow chains are exercised today).
- **`NETPACK_VERIFY=1`** re-parses generated bundles and reports the first invalid
  location — invaluable when changing the printer or a format.

## Adding a CLI option (the established pattern)

When you add an option that affects output, touch all of these so the surfaces
stay consistent (grep an existing option like `--banner` or `--public-path` to
copy the shape):

1. `Graph/OutputOptions.cs` (or the relevant options record) — the property.
2. `Bundles/…` — consume it where output is produced.
3. `NetPack.Cli/Commands/{Bundle,Serve,Analyze}Command.cs` — the `[Option]` and
   the mapping into `OutputOptions`.
4. `NetPack.Core/Bundler.cs` `BundleOptions` — the library facade.
5. `src/npm/dev/main.ts` and `src/npm/netpack/main.d.ts` — the typed programmatic
   API (they mirror the CLI flags; `main.d.ts` is hand-written and must stay in sync).
6. If the option should be settable from a preset: add the nullable property to
   `Config/PresetConfig.cs` and a `Candidates` token + per-verb allow-list entry in
   `NetPack.Cli/PresetArgs.cs` (see "Config & presets" below).
7. Docs (`docs/getting-started.md` tables + a section in `docs/other-features.md`
   or the relevant page) and tests.

## Config & presets

- User config is a JSONC **preset** (`netpack.json`, auto-discovered, and/or
  `--preset <ref>`). Presets carry CLI options plus `presets` (composition) and
  `hooks` (JS modules run over the Node bridge).
- **Hooks** map to the `CompilerHooks`/`CompilationHooks` tap system
  (`Plugins/`). `PresetHooks.Bind` registers a bridge-backed tap per resolved
  module (`NodeHookRunner`); `Traverse` fires the compiler/compilation/module
  lifecycle and `ResultWriter` fires the optimize/seal/emit/asset points. Every
  call site gates on the hook's tap `Count`, so a hook-less build pays nothing.
  Run-level hooks (`invalid`/`watchClose`/`shutdown`/`failed`) are bindable but
  reserved (no session-level hosting yet).
- `NetPack.Core/Config/` owns it: `PresetConfig` (nullable option DTO +
  JSONC-tolerant source-gen context) and `Presets.Resolve` (standalone
  module resolution, recursive load with dedup/cycle-safety, first-write-wins
  option merge, base-first deduped hook resolution). This layer is pure and
  graph-independent so it can run before `Traverse` and be unit-tested directly.
- `NetPack.Cli/PresetArgs.cs` bridges it to the CLI by **argument augmentation**:
  it strips `--preset`, resolves presets, and appends tokens for options the user
  didn't pass (per-verb allow-list), so the command classes stay untouched and CLI
  flags always win. Options merge first-write-wins; hooks accumulate base-first.

## Docs system

- User docs are top-level `docs/*.md` **only** — the Astro loader globs
  non-recursively, so `docs/impl/` (internal design notes) is intentionally
  excluded from the site.
- The sidebar order lives in `www/docs/src/lib/docs.ts` (`NAV_GROUPS`, by id).
  A doc not listed there still appears under a trailing "More" group. Page titles
  come from each file's H1.
- `docs/README.md` is the docs index and is **maintained by hand to mirror**
  `NAV_GROUPS`. When you add a doc, update both, plus the repo `README.md`
  checklist/feature line if it's a headline feature.
- Cross-links between docs use relative `./name.md` (so they also work on GitHub);
  the site rewrites them.

## Testing conventions

- xUnit. Tests create work in a fresh temp dir (`Path.GetTempPath()` + random
  name), `package.json` `{}`, write inputs, run `Traverse.From`, and assert on the
  `Stringify` output (usually the primary bundle:
  `Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary)`), cleaning up in a
  `finally`.
- Prefer parsing the emitted bundle with `Parser.ParseModule(..., Tolerant=true)`
  and asserting **zero diagnostics** to prove generated code is valid JS.
- Framework integrations needing a Node round-trip (Svelte/Solid/Sass) are tested
  only at the routing/detection level in-repo (extension mapping, dependency
  detection); full compilation is verified manually with the packages installed.

## Style

Match the surrounding code. Documentation and XML doc comments are written in
prose (the codebase favors clear `///` summaries explaining *why*). Keep new
public API minimal and mirror existing patterns rather than inventing new ones.
