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

> Hook **resolution** is in place (references are resolved, ordered, and
> deduplicated ahead of time); the specific lifecycle points and the module
> calling convention are being finalized, so no hook is invoked yet. The
> `hooks` object is already safe to author and share.

## .NET

Presets are part of `NetPack.Core` (`NetPack.Config.Presets`), so a managed host
can resolve the same files and read the merged options and hook list without the
CLI. See [.NET libraries](./dotnet-libraries.md).
