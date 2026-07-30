# Multi-target build strategy

> **Internal design note** (kept off the public docs site). Describes how
> netpack supports building for multiple runtime targets in one command.

## 1. Motivation

A project may need to ship for both the browser and Node.js (or Deno) from
the same source. Rather than running netpack twice with different
`--platform` flags, `--target` accepts a comma-separated list and builds
for each target in one pass, sharing the parse cache.

## 2. Architecture

### 2.1 Per-target graph

Each target builds a **separate module graph** because platform-specific
resolution differs:

- **Web**: honours the `browser` field in `package.json`, resolves to ESM
  entries via `import`/`module`/`browser` conditions.
- **Node**: treats node core modules (`fs`, `path`, etc.) as `node:`-prefixed
  externals. Omits the `browser` condition.
- **Deno**: treats `node:`, `npm:`, and `jsr:` schemes as externals.

Building per-target graphs means a Node-specific import (e.g. `fs`) is
externalised only in the Node build, while the Web build bundles it (or
fails with an unresolved import).

### 2.2 Shared parse cache

The **parse cache** (Phase 1) is shared across targets. The AST for a given
source file is identical regardless of platform — only graph-level decisions
(externals, conditions) differ. Sharing the parse cache means the second
target's build skips `Parser.ParseModule` for every unchanged file.

The parse cache key is `hash(filePath + content)` — platform is deliberately
excluded.

### 2.3 Output layout

Each target emits to a subdirectory under the output root:

```
dist/
  web/
    app.js
    index.html
  node/
    app.js
  deno/
    app.js
```

The single-target path (`--platform web`) emits directly to `dist/` for
backward compatibility.

## 3. CLI

```
netpack bundle src/index.ts --target web,node --outdir dist
```

`--target` overrides `--platform`. When a single target is given, behaviour
is identical to `--platform`. When multiple targets are given:

1. The parse cache is pre-populated by the first target's build.
2. Subsequent targets reuse the cache (3ms vs 249ms in simple benchmarks).
3. Each target's output goes to `dist/<target>/`.

## 4. Limitations

- **No per-target defines or aliases.** All targets share the same `--define`
  and `--alias` values. Per-target overrides would require a config file.
- **No per-target conditions.** `--conditions` applies to all targets. In
  practice, platform-specific conditions (`browser`, `node`, `deno`) are
  already auto-selected per target.
- **Watch mode** is per-target (single `--platform` path only). Multi-target
  watch is not yet supported.
- **HMR** is web-only; multi-target serve is not yet supported.

## 5. Future work

- Per-target `--define` overrides (e.g. `--target:web:define API_URL=/api`)
- Multi-target watch mode
- Multi-target `netpack serve` with platform selector in the browser
