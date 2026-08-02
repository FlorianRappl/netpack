# Configuration & presets

Every netpack option is a CLI flag, but you don't have to pass them by hand on
every build. A **preset** is a small JSON file that carries a set of options — and,
unlike the flags, it's transportable (share it as a file or an npm package) and
composable (build one on top of another without copying it).

```jsonc
// netpack.json
{
  "platform": "web",
  "minify": true,
  "external": ["react", "react-dom"],
  "define": { "process.env.NODE_ENV": "\"production\"" },
  "banner": "// (c) 2026 Acme, Inc."
}
```

Files are **JSONC** — comments and trailing commas are allowed, since these are
hand-authored.

## Where config comes from

Two entry points, and they stack:

- **`netpack.json`** in the working directory is picked up automatically.
- **`--preset <ref>`** loads an additional preset (repeatable). A `<ref>` is either
  a path (`./configs/prod.json`) or a package reference (`@acme/netpack-base`).

Both are resolved the same way as imports: a path resolves to a file; a package
reference resolves through `node_modules` (a subpath directly, or the package's
`package.json` `main`, which must point at a JSON file).

## Precedence

Options resolve **first-write-wins** — once something sets an option, later
sources can't override it. Sources are read highest priority first:

1. **CLI flags** — always win. A real `--minify` beats any preset.
2. **`--preset` presets**, in the order given.
3. The auto-discovered **`netpack.json`**.
4. Each preset's **referenced presets** (the `presets` array), in order,
   depth-first.

So a preset's own values take precedence over the ones it pulls in — `presets`
behaves like inheritance, but you can list several (more like layering plugins).

```jsonc
// prod.json — layer CDN + banner onto a shared base, override nothing else
{
  "presets": ["@acme/netpack-base", "./base.json"],
  "publicPath": "https://cdn.acme.com/app",
  "banner": "// (c) 2026 Acme, Inc."
}
```

Referenced presets are tracked by their fully-resolved path and loaded once, so a
diamond (two presets pulling in the same base) resolves it a single time and
reference **cycles are safe** — an already-seen preset is skipped.

## Options

The keys mirror the CLI flags (camelCase where the flag is hyphenated):
`outdir`, `minify`, `sourcemap`, `clean`, `external`, `shared`, `format`,
`platform`, `define`, `alias`, `loader`, `entryNames`, `publicPath`,
`conditions`, `packages`, `banner`, and `port`. `external`/`shared`/`conditions`
are arrays; `define`/`alias`/`loader` are objects. See
[Getting started](./getting-started.md) and [Other features](./other-features.md)
for what each does.

## Hooks

Presets are the *only* place you can register **hooks** — extension points that
run a JavaScript module through netpack's Node bridge. Each hook name maps to an
array of module references, so several callbacks can attach, and the arrays merge
across the whole preset chain.

```jsonc
{
  "presets": ["@myorg/base"],
  "hooks": {
    "afterBundling": ["./transform.mjs", "@myorg/tools/stamp.js"]
  }
}
```

Hook modules are resolved with the same mechanism as presets. A few properties by
design:

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

### Lifecycle points

Two hook names are recognized today; others are ignored with a warning:

| Hook | When it runs | Receives | Can return |
|---|---|---|---|
| `beforeCompilation` | before any module is processed | `{ hook, root, dev }` | — (side effects) |
| `afterBundling` | after the bundles are emitted | `{ hook, root, dev, files: [{ name, text }] }` | `{ files: [{ name, text }] }` |

A hook module default-exports (or `module.exports`) an async function that
receives the payload above. For `afterBundling`, text outputs (`.js`, `.css`,
`.html`, `.json`, `.map`, …) are passed as `text` and binary assets by `name`
only; return a `files` array to **replace** any asset's contents (or add a new
one) — this is your post-transformation slot. Return nothing to leave the output
untouched.

```js
// transform.mjs — minify-ish: strip // line comments from every JS bundle
export default async ({ files }) => ({
  files: files
    .filter((f) => f.name.endsWith(".js"))
    .map((f) => ({ name: f.name, text: f.text.replace(/^\s*\/\/.*$/gm, "") })),
});
```

Modules run over the Node bridge, so `@babel/core`, `terser`, or any npm package
they `import` must be installed in the project.

## .NET

Presets are part of `NetPack.Core` (`NetPack.Config.Presets`), so a managed host
can resolve the same files and read the merged options and hook list without the
CLI. See [.NET libraries](./dotnet-libraries.md).

## splitChunks

netpack already chooses sensible chunk boundaries by default — shared modules
are automatically extracted into separate chunks without any configuration. The
`splitChunks` option is an **expert setting** for when you need precise control
over chunk grouping (custom vendor bundles, size thresholds, priority rules).

```jsonc
// netpack.json
{
  "splitChunks": {
    "minSize": 20000,
    "minChunks": 1,
    "cacheGroups": {
      "vendors": {
        "test": "**/node_modules/**",
        "name": "vendors",
        "priority": -10,
        "enforce": true
      }
    }
  }
}
```

In practice the `splitChunks` object is most naturally authored inside a preset
rather than passed as a raw JSON string on the CLI — though `--split-chunks` is
available for one-off experiments:

```sh
npx netpack bundle src/index.html --split-chunks '{...}'
```

### Available options

| Option | Type | Default | Description |
|---|---|---|---|
| `chunks` | `string` | `"async"` | Which chunks to select: `"all"`, `"async"`, or `"initial"`. |
| `minSize` | `int` | `20000` | Minimum size in bytes for a chunk to be created. |
| `minChunks` | `int` | `1` | Minimum number of chunks that must share a module before splitting. |
| `maxSize` | `int` | `0` | Maximum chunk size before further splitting (0 = off). |
| `maxAsyncRequests` | `int` | `30` | Maximum parallel requests for async chunks. |
| `maxInitialRequests` | `int` | `30` | Maximum parallel requests for entry points. |

### cacheGroup options

| Option | Type | Description |
|---|---|---|
| `test` | `string` | Glob pattern matching module paths (`**/node_modules/**`, `**/lib/**`). |
| `name` | `string` | Name for the output chunk. Defaults to the cacheGroup key. |
| `priority` | `int` | Priority when a module matches multiple groups (higher wins). Default `0`. |
| `enforce` | `bool` | When `true`, creates the chunk regardless of `minSize` / `minChunks`. |
| `minChunks` | `int` | Overrides the top-level `minChunks` for this group. |
| `minSize` | `int` | Overrides the top-level `minSize` for this group. |
| `chunks` | `string` | Overrides the top-level `chunks` filter for this group. |

The `"default"` cacheGroup is built-in and can be disabled by setting it explicitly:

```jsonc
{
  "splitChunks": {
    "cacheGroups": {
      "default": {}
    }
  }
}
```
