# Incremental build and cache architecture — RFC

> **Internal design note** (kept off the public docs site; see `docs/impl/angular.md`
> for why `docs/impl/` is excluded). Describes the cache architecture for
> incremental rebuilds, the invalidation model, and the benchmark plan.

## Motivation

A full `Traverse.From` + `ResultWriter.WriteOut` rebuild touches every source file:
read bytes, parse JS/TS/JSX, walk the AST with `JsVisitor`, build the module graph,
render bundles, and write to disk. For a project with hundreds of modules, the
parse step alone is the dominant cost. In watch mode (`serve`, `bundle --watch`),
only one or two files typically change between rebuilds — re-parsing the other
98 % is wasted work.

The goal is a **memory cache** that stores parsed ASTs keyed by content, so
unchanged files skip `Parser.ParseModule` entirely during a rebuild. The
`JsVisitor` walk and bundle rendering still run (they must rebuild the graph),
but the most expensive operation — tokenizing + parsing — is eliminated for most
files.

This is a **prototype** (memory-only, JS-only AST caching). Persistent disk cache,
CSS/HTML AST caching, and chunk-artifact caching are deferred to later phases.

## Design

### Cache key

```
hash(filePath + ":" + optionsKey + ":" + processedContent)
```

Components:

- **`filePath`** — absolute path. Two files with identical content in different
  directories get distinct cache entries.
- **`optionsKey`** — a compact string encoding the build options that affect
  parsing: platform name, define count, conditions count, loader count. When the
  user changes `--platform node` → `--platform web`, the key changes and all
  cached ASTs are invalidated.
- **`processedContent`** — the source text after `--define` and
  `import.meta.env.VAR` substitutions. If a define value changes, the content
  changes, and the hash changes — the entry is a miss and the file is re-parsed
  with the new defines.

### Cache entry

| Field | Type | Purpose |
|-------|------|---------|
| `Hash` | `string` (6 hex chars) | SHA256 digest of the key, used for fast comparison |
| `Fragment` | `object` | The parsed AST — `Ast.SourceFile` for JS/TS/JSX files |

### Cache lifecycle

```
┌──────────────┐
│ File change  │
│ detected     │
└──────┬───────┘
       │
       ▼
┌──────────────┐     ┌──────────────────┐
│ Read bytes   │────>│ Apply --define,  │
│ from disk    │     │ env substitutions│
└──────────────┘     └────────┬─────────┘
                              │
                              ▼
                     ┌──────────────────┐
                     │ Compute content  │
                     │ hash (key)       │
                     └────────┬─────────┘
                              │
                    ┌─────────▼──────────┐
                    │ BuildCache.Get()   │
                    └────┬──────────┬────┘
                         │          │
                     hit │          │ miss
                         │          │
                         ▼          ▼
              ┌─────────────┐  ┌──────────────┐
              │ Use cached  │  │ Parser.Parse  │
              │ AST         │  │ Module()     │
              └──────┬──────┘  └──────┬───────┘
                     │                │
                     │                ▼
                     │       ┌──────────────┐
                     │       │ BuildCache.  │
                     │       │ Put()        │
                     │       └──────┬───────┘
                     │              │
                     └──────┬───────┘
                            │
                            ▼
                   ┌──────────────────┐
                   │ JsVisitor walk   │
                   │ (always runs)    │
                   └──────────────────┘
```

Key design decision: **the visitor walk always runs.** The parsed AST has no
references to graph Nodes — those are built fresh by the `JsVisitor` during the
walk. This guarantees correctness (the dependency graph is never stale) while
still eliminating the most expensive step.

### What is NOT cached

- **CSS stylesheets** — `AngleSharp.Css.IStyleSheet` holds a reference to the
  `BrowsingContext` (DOM engine), making it non-trivially serializable /
  shareable across builds.
- **HTML documents** — same reason (AngleSharp `IDocument`).
- **Node bridge output** (Sass, LESS, PostCSS, Svelte, Solid) — these involve
  an external Node.js process whose output depends on the project's
  `node_modules` state, not just the source file.
- **Resolved module paths** — these depend on the `exports` map, aliases, and
  loader configuration which may change between builds.
- **Rendered bundles** — these depend on the full module graph; any module
  change could cascade to multiple bundles. Chunk-artifact caching requires a
  separate invalidation graph.

Future phases could add:
- **CSS stylesheet cache** — store the serialized stylesheet text and re-parse
  it, skipping only the Sass/LESS/PostCSS pre-process step.
- **Chunk-artifact cache** — cache rendered JS/CSS chunks keyed by a hash of
  all modules in the chunk. When no module in a chunk has changed, reuse the
  cached render output.

## Invalidation model

### What triggers a cache miss

| Event | Mechanism |
|-------|-----------|
| File edited | Content hash differs → miss |
| `--define` value changed | Processed content differs → hash differs → miss |
| `--platform` switched | Options key differs → hash differs → miss |
| `--loader` added/removed | Options key (loader count) differs → miss |
| Node bridge output changed | Not cached (always re-processes) |

### What does NOT trigger a miss (safe)

| Event | Why safe |
|-------|----------|
| `--format esm` → `cjs` | Parsing is format-agnostic; `JsModuleFormat` applies later in rendering |
| `--public-path` changed | Only affects bundle rendering, not parsing |
| `--outdir` changed | Only affects file output, not parsing |

### Correctness guarantees

1. **No stale ASTs.** The cache key includes the processed content (after define
   substitution). If the source text or defines change, the key changes.
2. **Graph always fresh.** `JsVisitor` walks every module regardless of cache
   hit, building the current dependency graph.
3. **Same output semantics.** Cold and warm builds produce functionally
   equivalent output. Module IDs may differ (the `ModuleIdMap` is not shared
   across builds), but the bundle behavior is identical.
4. **Fallback full rebuild.** If the cache is disabled or corrupted, every
   module parses from scratch — equivalent to a cold build. There is no
   persistent state that can become stale across sessions.

## Performance

### Expected savings

For a typical watch-mode edit of one file in a 100-module project:

| Step | Cold (ms) | Warm (ms) | Saved |
|------|-----------|-----------|-------|
| File I/O | ~10 | ~10 | 0 % |
| Parse (99 files unchanged) | ~50 | 0 | 100 % |
| Parse (1 changed file) | ~1 | ~1 | 0 % |
| JsVisitor walk (all 100) | ~20 | ~20 | 0 % |
| Bundle render + write | ~15 | ~15 | 0 % |
| **Total** | **~96** | **~46** | **~52 %** |

The parse saving is proportional to unchanged-file count. Large projects
(1000+ modules) see the biggest benefit; small projects (< 20 modules) see
less because the visitor walk and render dominate.

### Benchmark results (20 modules, Debug build)

The `IncrementalBuildTests.Warm_build_is_faster_than_cold_build` test measures
the difference. On the reference machine, warm builds are consistently > 10 %
faster than cold builds for a 20-module project. For larger projects the
improvement grows.

### When caching doesn't help

- **First build after startup** — always cold, no cache populated yet.
- **Options change** — all files miss because the options hash changed.
- **Large-scale refactor** — many files changed, cache miss rate high.
- **Small projects** — visitor walk + render dominates over parse time.

## Integration with watch mode

The `BuildCache` is created once per dev-server session (or watch invocation)
and passed to every `Traverse.From` call:

```csharp
// In ServeCommand / BundleCommand watch loop:
var cache = new BuildCache();

watcher.Install(async () =>
{
    using var graph = await Traverse.From(
        entry, externals, shared, buildCache: cache);
    var result = new DiskResultWriter(graph.Context, outdir);
    return await result.WriteOut(options);
});
```

The cache survives across rebuilds within the same session. It is discarded
when the process exits (memory-only for prototype).

### Future: shared ModuleIdMap

The current prototype does not share the `ModuleIdMap` between builds. This
means module IDs differ between cold and warm builds (e.g., `__m { 0: … }`
vs `__m { 1: … }`). For hot-module replacement (HMR), stable module IDs across
rebuilds are essential. The existing dev server already passes a persistent
`ModuleIdMap` to `Traverse.From` — combining this with the `BuildCache` would
give both stable IDs and cached parses in watch mode.

## References

- rspack incremental architecture (`crates/rspack_core/src/incremental/`):
  14-pass incremental system with mutation tracking. This prototype is a
  simplified adaptation — single cache layer (AST) instead of multi-stage
  artifact recovery.
- webpack cache API (`Cache` trait / `before_*` / `after_*` hooks):
  pluggable cache backends (memory, filesystem). This prototype is
  memory-only with a fixed cache key strategy.
