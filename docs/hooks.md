# Hooks

Presets are the *only* place you can register **hooks** — extension points that
run a JavaScript module through netpack's Node bridge at a specific point in the
build. They live under the `hooks` key of a [preset](./configuration.md):

```jsonc
// netpack.json
{
  "presets": ["@myorg/base"],
  "hooks": {
    "afterBundling": ["./transform.mjs", "@myorg/tools/stamp.js"]
  }
}
```

Each hook name maps to an array of module references, so several callbacks can
attach, and the arrays merge across the whole preset chain. Hook modules are
resolved with the same mechanism as presets (a path, or a package reference
through `node_modules`).

## Entry shape

An entry can be a bare string (the module to run) or an object that also scopes
and parameterizes it:

```jsonc
{
  "hooks": {
    "afterBundling": [
      "./transform.mjs",                       // shorthand
      {
        "source": "./stamp.mjs",               // the module (required)
        "test": "\\.js$",                        // only .js files/modules
        "exclude": "\\.min\\.js$",               // …but not minified ones
        "mode": "prod",                          // only in optimized builds
        "order": -1,                             // run before default hooks
        "name": "stamp",                         // label for diagnostics
        "options": { "year": 2026 }              // passed to the hook
      }
    ]
  }
}
```

- **`source`** — the hook module (path or package reference). A plain string entry
  is exactly `{ "source": "…" }`.
- **`test`** — a regular expression matched against the name. For **asset** hooks it
  filters which files the hook receives (and may rewrite); for **per-module** hooks
  it skips modules whose path doesn't match. Omitted means "everything". A filtered
  asset hook with no matching files isn't invoked at all — the Node bridge only
  spins up when there's work to do. An invalid regex is reported and ignored.
- **`exclude`** — a regular expression for names to **skip**, applied after `test`.
- **`mode`** — `dev` (dev server only), `prod` (optimized builds only), or `both`
  (default). A hook that doesn't apply to the current build is never invoked.
- **`order`** — an integer that shifts the hook **earlier** (negative) or **later**
  (positive) among the hooks for the same phase. The default (0) keeps the
  base-first order presets already give; use `order` only to override it.
- **`name`** — a label surfaced in diagnostics and passed to the hook as
  `payload.name`.
- **`options`** — any JSON value, handed to the hook function as `payload.options`
  (default `{}`). Use it to reuse one hook module with different settings.

The two forms mix freely in the same array, and dedup is by the **whole** entry
(module plus its `test`/`exclude`/`mode`/`order`/`name`/`options`), so the same
module with different settings runs more than once by design.

## Behaviour

- **Merged, not overridden.** Every preset's hooks contribute; nothing shadows
  anything.
- **Base-first order.** Hooks run in the reverse of option precedence — the
  deepest referenced (base) presets execute first, the entry preset last — so a
  base can set things up before a more specific preset finishes.
- **Deduplicated.** The same module reached through two presets runs once, at its
  earliest position.
- **You only pay when you use them.** Resolution happens up front in native code;
  the Node bridge is engaged only for hooks that actually have modules registered.
  A build with no hooks is exactly as fast as one with no config at all.

## The module contract

A hook module default-exports (or `module.exports`) an async function. It receives
`{ hook, root, dev, options, name }` — plus `module` for the per-module hooks, and
`files` for the asset hooks — and may return a value the bundler applies. `options`
is the entry's `options` value (or `{}`), and `name` its label (if set). Unknown
hook names are ignored with a warning.

```js
// transform.mjs — strip // line comments from every JS bundle
export default async ({ files }) => ({
  files: files
    .filter((f) => f.name.endsWith(".js"))
    .map((f) => ({ name: f.name, text: f.text.replace(/^\s*\/\/.*$/gm, "") })),
});
```

Modules run over the Node bridge, so `@babel/core`, `terser`, or any npm package
they `import` must be installed in the project.

Two kinds carry extra payload and can return a value:

- **Asset hooks** (`additionalAssets`, `processAssets`, `afterProcessAssets`,
  `afterEmit` / the `afterBundling` alias) receive `files: [{ name, text }]` — text
  outputs (`.js`, `.css`, `.html`, `.json`, `.map`, …) as `text`, binary assets by
  `name` only. Return a `files` array to **replace** an asset's contents (or add a
  new one). This is your post-transformation slot.
- **`shouldEmit`** may return `{ emit: false }` to skip writing entirely.

The per-module hooks additionally receive the module's path as `module`.

## Lifecycle points

Every point in netpack's build maps to a hook name, mirroring the webpack/rspack
lifecycle. The build runs left to right; your post-transformation hooks most
commonly tap the **Emit** phase:

<svg viewBox="0 0 980 236" role="img" aria-labelledby="np-life-t np-life-d" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:auto;max-width:960px;font-family:ui-sans-serif,system-ui,sans-serif">
<title id="np-life-t">The netpack build lifecycle</title>
<desc id="np-life-d">Build phases from compiler start through per-module builds, graph completion, optimization, sealing, emit, and finish, each mapping to hook names.</desc>
<style>.np-life .ph{fill:currentColor;font-weight:600;font-size:13px}.np-life .hk{fill:currentColor;fill-opacity:0.7;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:10.5px}.np-life .sp{stroke:currentColor;stroke-opacity:0.35;stroke-width:2}.np-life .nd{fill:currentColor;fill-opacity:0.18;stroke:currentColor;stroke-opacity:0.5}.np-life .co{fill:#14b8a6;fill-opacity:0.08;stroke:#14b8a6;stroke-opacity:0.8}.np-life .ct{fill:currentColor;fill-opacity:0.85;font-size:11px}.np-life .em{fill:#14b8a6;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:10.5px}</style>
<g class="np-life">
<line class="sp" x1="70" y1="80" x2="910" y2="80"/>
<circle class="nd" cx="70" cy="80" r="8"/>
<circle class="nd" cx="210" cy="80" r="8"/>
<circle class="nd" cx="350" cy="80" r="8"/>
<circle class="nd" cx="490" cy="80" r="8"/>
<circle class="nd" cx="630" cy="80" r="8"/>
<circle cx="770" cy="80" r="9" fill="#14b8a6"/>
<circle class="nd" cx="910" cy="80" r="8"/>
<text class="ph" x="70" y="50" text-anchor="middle">Compiler</text>
<text class="ph" x="210" y="50" text-anchor="middle">Modules</text>
<text class="ph" x="350" y="50" text-anchor="middle">Graph</text>
<text class="ph" x="490" y="50" text-anchor="middle">Optimize</text>
<text class="ph" x="630" y="50" text-anchor="middle">Seal</text>
<text class="ph" x="770" y="50" text-anchor="middle" fill="#14b8a6">Emit</text>
<text class="ph" x="910" y="50" text-anchor="middle">Finish</text>
<text class="hk" x="70" y="108" text-anchor="middle">initialize</text>
<text class="hk" x="70" y="124" text-anchor="middle">compilation</text>
<text class="hk" x="210" y="108" text-anchor="middle">buildModule</text>
<text class="hk" x="210" y="124" text-anchor="middle">succeedModule</text>
<text class="hk" x="350" y="108" text-anchor="middle">finishModules</text>
<text class="hk" x="490" y="108" text-anchor="middle">optimizeModules</text>
<text class="hk" x="630" y="108" text-anchor="middle">moduleIds</text>
<text class="hk" x="630" y="124" text-anchor="middle">seal</text>
<text class="em" x="770" y="108" text-anchor="middle">processAssets</text>
<text class="em" x="770" y="124" text-anchor="middle">afterBundling</text>
<text class="hk" x="910" y="108" text-anchor="middle">done</text>
<line class="sp" x1="770" y1="89" x2="770" y2="158" style="stroke:#14b8a6;stroke-opacity:0.7"/>
<rect class="co" x="612" y="158" width="316" height="60" rx="8"/>
<text class="ct" x="770" y="182" text-anchor="middle">Post-transformation hooks tap here —</text>
<text class="ct" x="770" y="200" text-anchor="middle">replace or add output files before they're written.</text>
</g>
</svg>

| Phase | Hooks (in order) |
|---|---|
| Compiler start | `initialize`, `beforeRun`, `run` / `watchRun`, `beforeCompile` (alias `beforeCompilation`), `compile`, `thisCompilation`, `compilation`, `make` |
| Per module | `buildModule`, `stillValidModule`, `succeedModule`, `failedModule` |
| After the graph | `finishMake`, `finishModules` |
| Optimize (optimized builds) | `optimize`, `optimizeDependencies`, `afterOptimizeDependencies`, `optimizeModules`, `afterOptimizeModules`, `optimizeChunks`, `afterOptimizeChunks`, `optimizeTree`, `optimizeChunkModules` |
| Ids & seal | `moduleIds`, `chunkIds`, `seal`, `contentHash`, `afterCodeGeneration` |
| Emit | `shouldEmit`, `emit`, `additionalAssets`, `processAssets`, `afterProcessAssets`, `afterEmit` (alias `afterBundling`) |
| Finish | `afterSeal`, `afterCompile`, `done` |

The run-level hooks `invalid`, `watchClose`, `shutdown` and `failed` are
recognized (you can register them) but reserved — they aren't fired yet.

## .NET

Hooks map onto the `CompilerHooks` / `CompilationHooks` tap system in
`NetPack.Core`, which .NET plugins can tap directly (no Node bridge). See
[.NET libraries](./dotnet-libraries.md).
