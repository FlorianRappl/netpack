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
`{ hook, root, dev }` — plus `module` for the per-module hooks, and `files` for the
asset hooks — and may return a value the bundler applies. Unknown hook names are
ignored with a warning.

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
lifecycle:

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
