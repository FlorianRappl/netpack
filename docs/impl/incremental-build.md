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

## Gap analysis: rspack vs netpack incremental build

### Architecture comparison

| Dimension | rspack | netpack (current) |
|-----------|--------|-------------------|
| **Passes** | 14 incremental passes (BUILD_MODULE_GRAPH → EMIT_ASSETS) | 1 pass: parse cache only |
| **Mutation tracking** | `Mutations` enum: ModuleAdd/Update/Remove, DependencyUpdate, ChunkAdd/Split/Remove/SetHashes | None — no diff between builds |
| **Artifact recovery** | `Cache.before_*/after_*` recovers 14 artifacts from old compilation | None — full rebuild per invocation |
| **Cache layers** | 3 layers: parse → codegen → render | 1 layer: parse (AST) |
| **Cache backends** | DisableCache, MemoryCache, MixedCache (memory + persistent disk) | Memory only |
| **Generational GC** | `MemoryGCStorage` with `max_generations` | No GC — entries live until process exit |
| **Chunk graph** | Incremental: invalidate only affected chunk groups | Full rebuild of chunk graph |
| **Code generation** | Cached per-module-per-chunk with content-hash key | Always regenerated |
| **Chunk render** | Cached per-chunk with content-hash key | Always re-rendered |
| **Module IDs** | Stable across rebuilds via artifact recovery | Reset each build (unless persistent `ModuleIdMap` passed) |
| **Snapshot** | File-system snapshot for persistent cache invalidation | N/A (memory only) |
| **Persistent disk cache** | Yes — `PersistentCache` with `rspack_storage` | No |

### Gap details

#### G1: Multi-pass incremental architecture

rspack defines 14 named passes, each with recovery hooks. netpack has no
pass system — it's a linear pipeline (Traverse → Render → Write).

**Impact**: Without passes, there's no granular way to recover or skip work. A
single file change causes a full rebuild except for JS parsing.

#### G2: Mutation tracking

rspack's `Mutations` tracks exactly what changed between builds: which modules
were added, updated, removed; which dependencies changed; which chunks split
or merged. This drives selective invalidation.

**Impact**: Without mutations, netpack can't selectively invalidate only
affected parts. Every module must be re-walked.

#### G3: Code generation cache

rspack caches `CodeGenerationResult` per module per runtime. The cache key
includes the module hash, source hash, and compilation hash. On rebuild,
unchanged modules reuse their codegen output.

**Impact**: netpack's `JsxToJavaScriptTranspiler` runs for every module on
every build. This includes JSX lowering, import rewriting, and runtime
injection — expensive for large projects.

#### G4: Chunk render cache

rspack caches rendered chunk sources keyed by content hash. If no module in a
chunk changed, the whole chunk render is skipped.

**Impact**: netpack's `JsBundle.Stringify` and `CSSBundle`/`HtmlBundle`
always re-render from scratch. For projects with many chunks, this is
significant.

#### G5: Generational memory GC

rspack's `MemoryGCStorage` keeps artifacts from the last N generations and
drops older ones. This prevents unbounded memory growth in long-running watch
sessions.

**Impact**: netpack's cache grows without bound during a session. In theory
this could exhaust memory, though in practice a single session's AST cache is
small (kilobytes per module).

#### G6: Persistent disk cache

rspack stores cache artifacts on disk using `rspack_storage`, enabling warm
builds to survive process restarts.

**Impact**: netpack always cold-starts after a process restart. The first
build after restart is always full-cost.

#### G7: Incremental chunk graph

rspack's `build_chunk_graph/incremental.rs` selectively rebuilds only chunk
groups affected by changed modules. Unchanged chunks and their graphs are
preserved.

**Impact**: netpack's `Connected.Apply` and `CssChunkSplitter` run from
scratch on every build.

#### G8: Stable module IDs

rspack recovers `ModuleIdsArtifact` so module IDs are identical between
rebuilds. This is crucial for HMR and for deterministic output.

**Impact**: netpack only has stable IDs when the caller passes a persistent
`ModuleIdMap`. The default is non-deterministic.

#### G9: Multi-target test harness

rspack tests incremental rebuilds across web, node, async-node, and worker
targets using `describeByWalk` + `createHotIncrementalCase`. Each test case
simulates the full HMR lifecycle: build → edit file → rebuild → assert output.

**Impact**: netpack's tests are unit-level (cache hit/miss counts). There's no
integration-level test that edits a file and verifies the rebuild produces
correct output.

#### G10: Snapshot-based invalidations in hot cases

rspack's hot test cases use `__snapshots__/` directories with `.snap.txt`
files that capture the expected output at each step (0.snap.txt = initial,
1.snap.txt = after first edit, etc.).

**Impact**: netpack has no snapshot-based output verification. Tests only
check that output is valid JS, not that it produces specific values.

### Implementation plan

#### Phase 1 — Stabilize current prototype (this PR)

- [x] G1 partial: JS AST cache layer (parsed Ast.SourceFile)
- [x] G2 partial: content-hash-based invalidation (implicit mutations via hash diff)
- [x] G5 alternative: manual cache reset (no GC, but callers can discard)
- [ ] Add `BuildCache.Clear()` for manual cache invalidation
- Tests: cache hit/miss, valid output, benchmark, module add/remove/reorder

#### Phase 2 — Code generation cache

| Work item | Description | rspack equivalent |
|-----------|-------------|-------------------|
| **CodegenCache** | Cache `JsFragment` (AST + replacements) keyed by `(filePath, contentHash)`. Skip both parse AND `JsVisitor` walk for unchanged modules. | `CodeGenerateCacheArtifact` |
| **Codegen invalidation** | When a module's imports change, invalidate the codegen cache for that module and all its dependents. | `Mutation::DependencyUpdate` |
| **Test: codegen cache hit** | 10 modules, only 1 changed. Verify 9 modules skip both parse and walk. | `hotCases/code-generation` |

#### Phase 3 — Multi-pass architecture

| Work item | Description | rspack equivalent |
|-----------|-------------|-------------------|
| **Pass enum** | Define `IncrementalPass` enum: `BUILD_MODULE_GRAPH`, `FINISH_MODULES`, `BUILD_CHUNK_GRAPH`, `MODULES_CODEGEN`, `CHUNK_ASSET` | `IncrementalPasses` bitflags |
| **Artifact trait** | `IArtifact { IncrementalPass Pass { get; } void Recover(IArtifact old); }` | `ArtifactExt` trait + `recover_artifact` |
| **MemoryCache** | Store old `Compilation`; before each pass, recover artifacts for the pass if incremental is enabled | `MemoryCache` struct |
| **Test: artifact recovery** | Build → change 1 file → rebuild → verify artifact recovery hits | `MemoryCache.before_build_module_graph` tests |

#### Phase 4 — Chunk render cache

| Work item | Description | rspack equivalent |
|-----------|-------------|-------------------|
| **ChunkHash** | Compute hash of all modules in a chunk. Cache key for render. | `chunk.content_hash_by_source_type` |
| **RenderCache** | Cache `Stream` output of `Bundle.CreateStream` by chunk hash. | `ChunkRenderCacheArtifact` |
| **Test: chunk render cache hit** | Multiple chunks, only 1 changed. Verify unchanged chunks skip rendering. | `hotCases/chunks` |

#### Phase 5 — Persistent disk cache

| Work item | Description | rspack equivalent |
|-----------|-------------|-------------------|
| **Storage backend** | Filesystem-based store for cache artifacts (JSON serialized). | `rspack_storage` crate |
| **Snapshot** | File-system snapshot of source directories for invalidation. | `snapshot/strategy/` |
| **MixedCache** | Layer `PersistentCache` under `MemoryCache`. Cold starts check disk; warm builds use memory. | `MixedCache` |
| **Test: cold start from disk** | Build → restart process → build again → verify disk cache hits | `cacheCases/` |

#### Phase 6 — Mutation tracking

| Work item | Description | rspack equivalent |
|-----------|-------------|-------------------|
| **Mutations** | Track added/updated/removed modules between builds. | `Mutations` + `Mutation` enum |
| **Selective invalidation** | Use mutations to skip passes that have no affected modules. | `Incremental::passes_enabled` |
| **Dependency tracking** | When module A imports module B, and B changes, invalidate A's codegen. | `DependencyUpdate` mutation |
| **Test: selective invalidation** | Change leaf module → verify only its chunk and ancestors are rebuilt. | `hotCases/make/clean-isolated-module` |

#### Phase 7 — Test harness parity with rspack

| Work item | Description | rspack equivalent |
|-----------|-------------|-------------------|
| **Snapshot tests** | Write expected output per build step (0.snap.txt, 1.snap.txt, …). | `__snapshots__/web/` |
| **Multi-target** | Run the same test cases with different platforms (web, node). | `Incremental-web.test.js` + `Incremental-node.test.js` |
| **HMR cycle tests** | Simulate the full HMR lifecycle: build → edit → rebuild → assert. | `hotCases/` per subdirectory |
| **Error recovery tests** | Broken syntax → fix → rebuild succeeds. Already partially covered. | `hotCases/make/rebuild-abnormal-module` |
| **Cycle detection tests** | Module cycles survive rebuild correctly. | `hotCases/make/clean-isolated-cycle` |

### Summary matrix

| Gap | Phase | Effort | Impact |
|-----|-------|--------|--------|
| G1 Multi-pass | Phase 3 | 🔴 Large | Full incremental pipeline |
| G2 Mutation tracking | Phase 6 | 🟡 Medium | Selective invalidation |
| G3 Codegen cache | Phase 2 | 🟡 Medium | Skip JsVisitor for unchanged modules |
| G4 Chunk render cache | Phase 4 | 🟡 Medium | Skip rendering for unchanged chunks |
| G5 Generational GC | Phase 1 | 🟢 Small | `BuildCache.Clear()` only |
| G6 Persistent cache | Phase 5 | 🔴 Large | Disk-based storage |
| G7 Incremental chunk graph | Phase 6 | 🟡 Medium | Skip chunk-graph rebuild |
| G8 Stable module IDs | Phase 3 | 🟢 Small | Share `ModuleIdMap` with cache |
| G9 Multi-target harness | Phase 7 | 🟡 Medium | Snapshot + platform tests |
| G10 Snapshot assertions | Phase 7 | 🟢 Small | `__snapshots__/` test files |
