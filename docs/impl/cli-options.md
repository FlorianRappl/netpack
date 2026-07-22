# CLI options — esbuild parity gap analysis

> **Internal implementation note** (kept off the public docs site; see
> `docs/impl/angular.md` for why `docs/impl/` is excluded). Tracks how netpack's
> CLI surface compares to esbuild's and what's worth adding, ranked by impact.

## What netpack exposes today

- `bundle` — entry, `--outdir`, `--minify`, `--sourcemap`, `--clean`,
  `--external`, `--shared`, `--format` (esm/cjs/umd/systemjs), `--platform`
  (web/node/deno).
- `serve` — entry, `--port`, `--minify`, `--external`, `--shared`.
- Auxiliary commands: `graph`, `inspect`, `analyze`.

## Missing vs esbuild, ranked

### Tier 1 — table stakes for real apps

1. **`--define`** — compile-time constant substitution
   (`--define:process.env.NODE_ENV="production"`, feature flags, `__VERSION__`).
   Only a single hardcoded `process.env.NODE_ENV` replacement exists internally.
2. **`--target` + syntax downleveling** (`es2017`, `chrome110`, `node18`, …).
   The heavyweight: netpack does no syntax lowering, so it cannot guarantee output
   runs on older engines. A real new capability, not just a flag. **(Deferred.)**
3. **`--loader:.ext=…`** (`file`, `dataurl`, `base64`, `binary`, `text`, `json`,
   `copy`, `empty`, `js/ts/jsx/css`). Per-type handling is currently fixed.
4. **Content-hashed output names** — `--entry-names`/`--chunk-names`/
   `--asset-names` with `[name]`/`[hash]` templates. No cache-busting today.
5. **`--alias`** (`@/→src`, swap a package). An internal `Aliases` map exists but
   has no CLI surface.

### Tier 2 — frequently needed

6. Optimization knobs: `--drop:console`/`--drop:debugger`, `--pure:FN`,
   `--tree-shaking=true|false`, `--keep-names`, `--ignore-annotations`.
7. Source-map modes: `--sourcemap=linked|inline|external|both`,
   `--sources-content=false` (currently boolean only).
8. `--splitting` + shared-chunk control (dynamic `import()` is recognized for
   tree-shaking but there's no explicit shared-chunk splitting/naming).
9. JSX controls: `--jsx=automatic|transform`, `--jsx-import-source`, `--jsx-dev`,
   `--jsx-factory`/`--jsx-fragment` (derived from tsconfig/heuristics only today).
10. `--outfile` / `--outbase` (only `--outdir` exists).
11. `--public-path` (asset URL base for CDN/subpath).
12. `--packages=external` (mark all `node_modules` external in one shot).
13. `--conditions` (user `exports` conditions atop the platform defaults).
14. `bundle --watch` (watch-to-disk without a server) and richer `serve`
    (`--servedir`, host binding, HTTPS `--certfile`/`--keyfile`).

### Tier 3 — nice-to-have / advanced

`--banner`/`--footer` · `--legal-comments` · `--metafile` (partly covered by
`analyze`) · `--inject` · granular minify (`--minify-whitespace`/`-identifiers`/
`-syntax`) · `--charset=utf8` · explicit `--tsconfig`/`--tsconfig-raw` ·
`--main-fields` · `--resolve-extensions` · `--out-extension` ·
`--allow-overwrite` · `--preserve-symlinks` · `--node-paths` · `--global-name`
(IIFE) · `--log-level`/`--color` · `--supported:feature` ·
`--mangle-props`/`--reserve-props`.

## Implementation order

First batch: Tier 1 items **1, 3, 4, 5** (define, loader, hashed names, alias) —
all fit the existing architecture cleanly. `--target` (item 2) is deferred; it
requires a syntax-lowering pass. Tier 2 follows.

### Progress

- [x] `--define` — generalized the built-in NODE_ENV replacement into a
  context define table (longest-key-first substitution); `bundle`/`serve` flags.
- [x] `--loader` — per-extension override (js/jsx/ts/tsx/json/css/text/base64/
  dataurl/file/copy/empty); inline loaders emit a synthetic JS module.
- [x] content-hashed output names — `--entry-names [name]-[hash]`; a two-phase
  naming pass in `ResultWriter` sets `Bundle.OutputName`, references pick it up.
  Known limitation: self-content hashing (no cross-bundle hash propagation).
- [x] `--alias` — populates `context.Aliases` (already consulted in
  `InnerProcess`); path targets resolve from the CWD, bare targets stay bare.

Documented for users in `docs/other-features.md`. Tests in
`NetPack.Tests/CliOptionsTests.cs`. Next: Tier 2.
