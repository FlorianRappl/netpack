# Multi-target build strategy

> **Internal design note** (kept off the public docs site). Describes how
> netpack supports building for multiple runtime targets via presets.

## 1. Motivation

A project may need to ship for both the browser and Node.js (or Deno) from
the same source. Rather than running netpack twice with different
`--platform` flags, a netpack preset with **variants** defines named
build configurations that each produce a separate output.

## 2. Configuration

In `netpack.json`, add a `variants` object whose keys name each target:

```json
{
  "outdir": "dist",
  "variants": {
    "web":  { "platform": "web",  "minify": true },
    "node": { "platform": "node" }
  }
}
```

Each variant inherits the base options and can override any field.
When the preset is resolved, `PresetArgs.Apply` returns one CLI arg
set per variant (filtered by any explicit CLI flags), and `Program.Main`
iterates them, running the bundler once per target.

Output follows the variant names:

```
dist/
  web/
    app.js
    index.html
  node/
    app.js
```

A single `--platform web` (no variants) emits directly to `dist/` for
backward compatibility.

## 3. Architecture

### 3.1 Per-target graph

Each target builds a **separate module graph** because platform-specific
resolution differs:

- **Web**: honours the `browser` field in `package.json`, resolves to ESM
  entries via `import`/`module`/`browser` conditions.
- **Node**: treats node core modules (`fs`, `path`, etc.) as `node:`-prefixed
  externals. Omits the `browser` condition.
- **Deno**: treats `node:`, `npm:`, and `jsr:` schemes as externals.

### 3.2 Shared parse cache

The **parse cache** (Phase 1) is shared across targets because the caches
in `BundleCommand` are `static` — all `BundleCommand` instances (one per
variant) reuse the same cache. The AST for a given source file is identical
regardless of platform, so the second target's build skips re-parsing.

The parse cache key is `hash(filePath + content)` — platform is deliberately
excluded.

### 3.3 Type-safe variants

`PresetConfig` extends `BasePresetConfig`, which has every option field
except `Variants`. A variant's value is `BasePresetConfig`, so variants
cannot recursively define sub-variants. The `variants` property is
deserialized manually via `JsonDocument` to avoid AOT source-gen issues
with nested generics.

## 4. Limitations

- **No per-variant defines or aliases in the current implementation.**
- **Watch mode** is single-target only.
- **HMR** is web-only; multi-target serve is not yet supported.
