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

### What is NOT cached (limitations of Phase 1)

- **CSS stylesheets** — `AngleSharp.Css.IStyleSheet` holds a reference to the
  `BrowsingContext` (DOM engine), making it non-trivially shareable across builds.
- **HTML documents** — same reason (AngleSharp `IDocument`).
- **Node bridge output** (Sass, LESS, PostCSS, Svelte, Solid) — these involve
  an external Node.js process whose output depends on the project's
  `node_modules` state, not just the source file.
- **Resolved module paths** — these depend on the `exports` map, aliases, and
  loader configuration which may change between builds.
- **Rendered bundles** — these depend on the full module graph; any module
  change could cascade to multiple bundles.

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
when the process exits (memory-only for Phase 1).

### Future: shared ModuleIdMap

The current prototype does not share the `ModuleIdMap` between builds. This
means module IDs differ between cold and warm builds. For hot-module
replacement (HMR), stable module IDs across rebuilds are essential. The
existing dev server already passes a persistent `ModuleIdMap` to
`Traverse.From` — combining this with the `BuildCache` would give both stable
IDs and cached parses in watch mode.

## Roadmap

The current implementation (Phase 1) caches only parsed JS ASTs. The
following phases describe a progressive path toward a full incremental
pipeline.

### Architecture layers

netpack's build pipeline has these stages, each of which can be independently
cached or selectively rebuilt:

| Stage | What runs | Cached in Phase 1? | Cache key |
|-------|-----------|:-------------------:|-----------|
| **File I/O** | `File.ReadAllBytesAsync` | No | — |
| **Pre-process** | Sass/LESS/PostCSS/Solid/Svelte bridges | No | — |
| **Parse** | `Parser.ParseModule`, `CssParser`, `AngleSharp` | ✅ JS only | `(filePath, optionsHash, content)` |
| **Walk** | `JsVisitor.FindChildren`, `CssVisitor.FindChildren`, `HtmlVisitor.FindChildren` | No | — |
| **Bundle graph** | `Connected.Apply`, `CssChunkSplitter` | No | — |
| **Code generation** | `JsxToJavaScriptTranspiler.Transpile` (JSX lowering, import rewriting, runtime injection) | No | — |
| **Render** | `Bundle.CreateStream` / `Stringify` | No | — |
| **Write** | `ResultWriter.WriteOut` | No | — |

### Phase 2 — Code generation cache

Cache the output of `JsxToJavaScriptTranspiler.Transpile` per module, keyed by
a hash of the module's parsed AST and its dependency graph. Unchanged modules
skip both `Parser.ParseModule` AND the `JsVisitor` walk + JSX lowering +
import rewriting — the three most expensive per-module operations.

**Work items:**

- Add `CodeGenEntry` to `BuildCache`: stores the lowered AST body (list of
  `Ast.Statement`) ready for insertion into the bundle.
- Invalidation: when a module's imports change (new/removed/changed import
  specifier), invalidate the codegen cache for that module and all direct
  dependents.
- Compute a dependency hash: `hash(moduleContent, importedModules', importedModules'')`, so
  transitive dependency changes propagate.
- CLI flag: `--incremental-codegen` (on by default when `--incremental` is on).
- Tests: 10 modules, change 1 → verify 9 hit codegen cache; change a shared
  dependency → verify dependents miss cache; verify output is byte-identical to
  a full build.

### Phase 3 — Chunk render cache

Cache the rendered output of `Bundle.CreateStream` per chunk, keyed by a hash
of all module hashes within the chunk plus the chunk's own configuration
(format, public path, banner). When no module in a chunk has changed, skip the
entire `Stringify`/`CreateStream` call.

**Work items:**

- Add `RenderEntry` to `BuildCache`: stores the rendered `byte[]` or `Stream`.
- Compute `ChunkHash` from the hashes of all modules in the chunk, plus the
  chunk's format/options.
- In `ResultWriter.WriteOut`, check `BuildCache` before calling
  `bundle.CreateStream`.
- Invalidation: any module change in a chunk → chunk hash differs → miss.
- Tests: 3 chunks, change a module in chunk A → verify chunks B and C hit
  render cache; verify rendered output is byte-identical.

### Phase 4 — Multi-pass incremental architecture

Replace the linear "Traverse → Render → Write" pipeline with named passes that
can be selectively skipped or recovered from a previous build. This is modeled
directly on the compiler pipeline stages:

```csharp
enum IncrementalPass
{
    BuildModuleGraph,   // Traverse.From → build graph
    FinishModules,      // CSS modules, tree-shaking
    BuildChunkGraph,    // Connected.Apply, CssChunkSplitter
    ModulesCodegen,     // JsxToJavaScriptTranspiler
    ChunksHashes,       // Content hashing
    ChunkAsset,         // Bundle.CreateStream
    EmitAssets,         // ResultWriter.WriteOut
}
```

Each pass produces one or more **artifacts** (parsed ASTs, codegen results,
rendered chunks). The cache stores artifacts from the previous build. On
rebuild, each pass checks whether its artifacts are still valid and recovers
them instead of recomputing.

**Work items:**

- Define `IArtifact` interface: `IncrementalPass Pass { get; } void Recover(IArtifact old);`
- Extract artifacts from `BundlerContext` into separate artifact objects
  (`ModuleGraphArtifact`, `CodeGenArtifact`, `ChunkRenderArtifact`).
- Store old `BundlerContext` in `BuildCache` for artifact recovery.
- Before each pass, call `Recover` on the pass's artifacts from the old context.
- CLI flag: `--incremental` controls which passes are enabled.
- Tests: build → change 1 file → rebuild → count how many passes were skipped
  by artifact recovery. Verify output correctness.

### Phase 5 — Mutation tracking and selective invalidation

Track exactly what changed between builds — which modules were added,
updated, or removed — and use this information to selectively invalidate only
the affected parts of the pipeline.

**Mutation types:**

- `ModuleAdded(moduleId)` — new file, needs full processing
- `ModuleUpdated(moduleId)` — content changed, re-parse + re-codegen
- `ModuleRemoved(moduleId)` — file deleted, remove from graph
- `DependencyChanged(fromModuleId, toModuleId)` — import specifier changed

**Work items:**

- Compute a diff between the current and previous module graph.
- In `Traverse.From`, only process modules that appear in the diff.
- Propagate invalidation: when module A changes, invalidate codegen for all
  modules that import A transitively.
- Skip entire passes when no mutations affect their artifacts.
- Tests: change a leaf module → verify only that module and its ancestors are
  rebuilt; verify unchanged sibling subtrees hit cache.

### Phase 6 — Persistent disk cache

Store cache artifacts on disk so warm builds survive process restarts. The
first build after `netpack serve` or `netpack bundle --watch` restarts should
benefit from the previous session's cache.

**Work items:**

- Add `IPersistentStorage` interface with `Read`/`Write` methods.
- Implement `JsonFileStorage`: serializes artifacts as JSON to
  `node_modules/.cache/netpack/`.
- Add `SnapshotStore`: stores file hashes for source files to detect changes
  between sessions without re-reading all files.
- Layer `PersistentCache` under the memory cache: on cold start, pre-populate
  from disk; on rebuild, write back to disk in the background.
- CLI flag: `--cache-dir` to customize the cache directory.
- Tests: build → stop process → restart → build again → verify disk cache
  hits. Verify cache survives machine reboots (use temp dir as cache dir).

### Phase 7 — Test harness improvements

Expand the test coverage to match the maturity of the cache pipeline:

- **Snapshot-based output verification**: capture expected bundle output per
  build step in `__snapshots__/` directories. Each step appends a file
  (`0.snap.txt`, `1.snap.txt`, …) to verify the exact output after each edit.
- **Multi-platform**: run the same incremental test scenarios with
  `Platform.Web`, `Platform.Node`, and `Platform.Deno`.
- **Full HMR cycle**: simulate the complete hot-reload lifecycle:
  1. Initial build → assert output
  2. Edit a source file → trigger rebuild → assert output changed correctly
  3. Edit again → assert cumulative changes
  4. Delete a file → assert references cleaned up
  5. Re-add the file → assert recovered
- **Error recovery**: inject syntax errors → verify watcher survives → fix
  syntax → verify rebuild succeeds.
- **Cycle resilience**: module A imports B, B imports A. Edit A → verify
  rebuild produces correct output without infinite recursion.
- **Concurrent edits**: simulate multiple files changing within the debounce
  window → verify exactly one rebuild with all changes batched.

### Summary matrix

| Phase | Scope | Effort | Key benefit |
|-------|-------|--------|-------------|
| 1 | Parse cache (JS AST) | ✅ Done | Skip `Parser.ParseModule` for unchanged files |
| 2 | Codegen cache | 🟡 Medium | Skip `JsVisitor` walk + JSX lowering for unchanged modules |
| 3 | Chunk render cache | 🟡 Medium | Skip `Bundle.CreateStream` for unchanged chunks |
| 4 | Multi-pass architecture | 🔴 Large | Granular pass control + artifact recovery |
| 5 | Mutation tracking | 🟡 Medium | Selective invalidation — only rebuild affected modules |
| 6 | Persistent disk cache | 🔴 Large | Warm builds survive process restarts |
| 7 | Test harness | 🟡 Medium | Snapshot tests + HMR cycles + error/cycle/concurrent tests |
