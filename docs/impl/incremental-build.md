# Incremental Build and Cache Architecture

> **Internal design note** (kept off the public docs site). Full conception of
> the incremental rebuild pipeline, from current prototype to full vision.

## 1. Problem

A full `Traverse.From` + `ResultWriter.WriteOut` rebuild touches every source
file — read bytes, parse, walk the AST, build the module graph, render bundles,
and write to disk. In watch mode, typically one or two files change. For a
project with hundreds of modules, re-parsing the other 98% is wasted work.

**Goal**: make watch-mode rebuilds feel instant by skipping work proportional to
how many files *didn't* change.

## 2. Architecture overview

netpack's build pipeline has seven stages. The cache strategy is to push the
caching boundary further right with each phase:

```
File → Parse → Walk → Bundle Graph → Codegen → Render → Write
 │       │      │          │            │         │       │
Phase 1  │      │          │            │         │       │
 ════════╝      │          │            │         │       │
Phase 2         │          │            │         │       │
 ═══════════════╝          │            │         │       │
Phase 3                    │            │         │       │
 ══════════════════════════╝            │         │       │
Phase 4+5 (multi-pass + mutation)       │         │       │
 ═══════════════════════════════════════╝         │       │
Phase 6+ (render cache)                           │       │
 ═════════════════════════════════════════════════╝       │
Phase 6+ (persistent disk cache)                          │
 ═════════════════════════════════════════════════════════╝
```

Each phase caches one more stage of the pipeline. When a file hasn't changed
(content hash match), all cached stages are skipped. When a file *has* changed,
the pipeline runs from that stage forward.

## 3. Design principles

1. **Content-addressable.** Every cache key is a hash of the thing being cached.
   No timestamps, no manual version numbers. Content changed → hash changed →
   cache miss. Correct by construction.

2. **Progressive adoption.** Each phase ships independently behind a flag. Users
   don't need to wait for the full roadmap to benefit. Phase 1 (parse cache)
   already provides ~50% speedup on warm rebuilds.

3. **Always-correct fallback.** If the cache is disabled, empty, or corrupted,
   the pipeline falls back to a full cold build. No state can become stale
   across sessions. No partial state to debug.

4. **Graph-safe by design.** Until Phase 5 (mutation tracking), the dependency
   graph is rebuilt fresh every time. This guarantees correctness at the cost
   of running the walk — a tradeoff that's acceptable because I/O + parse
   dominates over walk time.

5. **Minimal new API surface.** The `BuildCache` class has three methods:
   `Get`, `Put`, `ResetCounters`. The `Traverse.From` signature gains one
   optional parameter. No new configuration formats, no serialization formats.

## 4. Phase 1 — Parse cache (done, PR #21)

**What's cached**: `Ast.SourceFile` (parsed JS/TS/JSX AST) per file, keyed by
`hash(filePath + optionsKey + processedContent)`.

**What's skipped**: `Parser.ParseModule` for unchanged files. The `JsVisitor`
walk still runs to rebuild the dependency graph — correctness guarantee.

**Cache key components**:

| Component | Example | Purpose |
|-----------|---------|---------|
| `filePath` | `/src/main.js` | File identity |
| `optionsKey` | `"WebPlatform:1:0:0"` | Platform + define count + conditions + loaders |
| `processedContent` | Source after `--define` substitution | Catch define changes |

**Performance**: ~52% reduction in rebuild time for a 100-module project
with one file changed. Benchmark test (`Warm_build_is_faster_than_cold_build`)
verifies >10% improvement for 20 modules.

**Integration**: `Traverse.From` accepts optional `BuildCache` parameter.
`ServeCommand` and `BundleCommand` watch loops create one cache per session.

**Tests** (15 cases):

- Second build hits cache for unchanged files
- Changed file misses cache (content hash differs)
- Cache hit produces valid JS output
- Multiple modules (10+) benefit from cache
- Warm build measurably faster than cold (benchmark)
- New module added → fresh parse + valid output
- Module removed → rebuild succeeds
- Import order change → rebuild succeeds
- Non-source file addition doesn't mess cache
- `IncrementalTestHelper` wraps the full multi-step pattern

## 5. Phase 2 — Code generation cache

**What's cached**: The output of `JsxToJavaScriptTranspiler.Transpile` per
module — the lowered AST body (list of `Ast.Statement`) ready for insertion
into the bundle registry.

**What's skipped**: `JsVisitor` walk + JSX lowering + import rewriting +
define/env substitution + runtime injection — the three most expensive
per-module operations.

**Invalidation**: When a module's imports change (new, removed, or changed
specifier), invalidate the codegen cache for that module and all modules
that import it transitively. Compute a dependency hash from the module's
import map: `hash(moduleContent, importedModules...)`.

**Estimated effort**: Medium. Touches `JsBundle.JsxToJavaScriptTranspiler`
and `JsVisitor`.

**Key types**:
```csharp
class CodeGenEntry {
    string Hash;           // dependency-aware hash
    List<Ast.Statement> Body; // lowered code ready for registry
}
```

## 6. Phase 3 — Chunk render cache

**What's cached**: The rendered output of `Bundle.CreateStream` per chunk.
Keyed by a hash of all module hashes within the chunk plus the chunk's
configuration (format, public path, banner).

**What's skipped**: `Bundle.Stringify` / `CreateStream` for unchanged chunks.
The entire render → memory stream → UTF-8 encoding chain.

**Invalidation**: Any module in the chunk changes → chunk hash differs →
render cache miss.

**Estimated effort**: Medium. Touches `Bundle`, `JsBundle`, `CssBundle`,
`HtmlBundle`, and `ResultWriter.WriteOut`.

**Key types**:
```csharp
class RenderEntry {
    string ChunkHash;
    byte[] RenderedBytes;
}
```

## 7. Phase 4 — Multi-pass architecture

Replace the linear pipeline with named passes that can be selectively
skipped or recovered from a previous build.

```csharp
enum IncrementalPass {
    BuildModuleGraph,   // Traverse.From
    FinishModules,      // CSS modules, tree-shaking
    BuildChunkGraph,    // Connected.Apply, CssChunkSplitter
    ModulesCodegen,     // JsxToJavaScriptTranspiler
    ChunksHashes,       // Content hashing
    ChunkAsset,         // Bundle.CreateStream
    EmitAssets,         // ResultWriter.WriteOut
}
```

Each pass produces artifacts. The cache stores artifacts from the previous
build. On rebuild, each pass checks whether its artifacts are valid and
recovers them instead of recomputing.

```csharp
interface IArtifact {
    IncrementalPass Pass { get; }
    void Recover(IArtifact old);
}
```

**Estimated effort**: Large. Requires extracting artifact objects from
`BundlerContext` and restructuring the pipeline.

## 8. Phase 5 — Mutation tracking

Track exactly what changed between builds:

- `ModuleAdded(moduleId)` — new file
- `ModuleUpdated(moduleId)` — content changed
- `ModuleRemoved(moduleId)` — file deleted
- `DependencyChanged(from, to)` — import specifier changed

Use mutations to selectively invalidate only affected modules and chunks.
Unchanged subtrees skip all passes.

**Estimated effort**: Medium. Touches `Traverse` and graph diff logic.

## 9. Phase 6 — Persistent disk cache

Store cache artifacts on disk so warm builds survive process restarts.
`netpack serve` restarts benefit from the previous session's cache.

- `IPersistentStorage` interface with `Read`/`Write`
- `JsonFileStorage` → `node_modules/.cache/netpack/`
- `SnapshotStore` for cross-session file change detection
- `MixedCache` layer: memory + disk

**Estimated effort**: Large. Requires serialization, I/O, and invalidation
across sessions.

## 10. Test infrastructure

✅ Done — 29 tests across 2 classes, covering:

### IncrementalBuildTests (16 tests — cache correctness)
| Test | Coverage |
|------|----------|
| Second_build_hits_cache | Phase 1 cache hit on warm rebuild |
| Changed_file_misses_cache | Content change → Phase 1 miss |
| Cache_hit_produces_valid_output | Phase 1 cache round-trips valid JS |
| Multiple_modules_benefit_from_cache | 11 modules → Phase 1 hits |
| Warm_build_is_faster_than_cold | Phase 1 speedup benchmark |
| New_module_leads_to_fresh_parse | Module addition → rebuild succeeds |
| Removed_module_is_detected | Module deletion → rebuild succeeds |
| Rebuild_handles_import_order_change | Import order swap → valid output |
| Cache_survives_non_source_file_addition | Non-JS files don't break cache |
| Second_build_hits_codegen_cache | Phase 2 codegen cache hit |
| Changed_file_invalidates_codegen_cache | Content change → codegen miss |
| Codegen_cache_hit_produces_valid_output | Phase 2 cache round-trips valid JS |
| Multiple_modules_benefit_from_codegen_cache | 11 modules → Phase 2 hits |
| Warm_build_with_codegen_is_faster | Phase 2 benchmark |
| Codegen_cache_survives_module_addition | New module → unchanged deps still hit |
| Codegen_cache_survives_import_order_change | Import swap → unchanged deps still hit |

### IncrementalRebuildTests (13 tests — rspack-mirroring scenarios)
| Test | rspack equivalent |
|------|------------------|
| Snapshot_based_output_verification | HotStep `toMatchFileSnapshotSync` |
| Cascading invalidation (4-module chain) | Module dependency propagation |
| Broken syntax → fix → rebuild succeeds | Per-step error/warning arrays |
| Multi-file edit batching (3 files) | Multiple file changes in one step |
| Identical content → identical output | Hash stability (`LAST_HASH`/`CURRENT_HASH`) |
| Circular dependencies survive rebuild | Cycle resilience |
| Shared dependency rebuild (2 entries) | Code-split chunk rebuild |
| Single_edit_changes_output | HotStep content change |
| Adding / removing modules | File addition / deletion |
| Three consecutive edits | Multi-step mutation sequence |
| Import order change | Dependency reordering |
| Non-imported file addition | Unrelated file immunity |

### Snapshot system
- `EnableSnapshots(className, methodName)` — stores under `__snapshots__/<Class>.<Method>/step_N.js`
- `AssertMatchesSnapshot(step)` — compares or writes snapshots
- `NETPACK_UPDATE_SNAPSHOTS=1` — regenerate all snapshots
- `AssertCacheStatsSnapshot(step, hits, misses, codegenHits, codegenMisses)` — per-step cache audit

## 11. Success metrics

| Metric | Phase 1 target | Phase 2+ target |
|--------|:-------------:|:---------------:|
| Warm rebuild time (100 modules, 1 changed) | <60ms | <30ms |
| Cold build overhead from cache | <5% | <5% |
| Stale output bugs | 0 | 0 |
| Cache hit rate (typical edit) | >90% | >95% |
| Memory overhead (1000 modules) | <50MB | <100MB |

## 12. Risks and mitigation

| Risk | Mitigation |
|------|-----------|
| Cache grows unbounded in long sessions | `Clear()` method, future generational GC |
| Module IDs differ between cold/warm | Persistent `ModuleIdMap` already in dev server |
| Cache key collision (hash collision) | SHA-256 — astronomically unlikely |
| Options change invalidates entire cache | Acceptable — user explicitly changed build config |
| Breakage from upstream AST changes | Test suite catches; `BuildCache` API is small surface area |
| CSS/HTML not cacheable (Phase 1 limitation) | Phases 4+ introduce artifact recovery for non-JS types |

## 13. Current status

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Parse cache | ✅ Done | PR #21 |
| Test infrastructure | ✅ Done | PR #22 |
| 2 — Codegen cache | ✅ Done | — |
| 3 — Chunk render | ✅ Done | — |
| 4 — Multi-pass | ✅ Done | — |
| 5 — Mutations | ✅ Done | — |
| 6 — Disk cache | ✅ Done | — |
| CLI integration | ✅ Done | ServeCommand + BundleCommand |
