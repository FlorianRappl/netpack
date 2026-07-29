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

(Partially merged — PR #22)

The `IncrementalTestHelper` provides a reusable multi-step rebuild pattern
for verifying correctness across all phases:

```
Setup → Build → Assert → Edit → Rebuild → Assert → ...
```

6 rebuild tests (add/remove modules, import order change, consecutive edits)
exercise the pattern. Future phases add:

- Snapshot-based output verification (`__snapshots__/`)
- Multi-platform (web, node, deno)
- Full HMR cycle (edit → rebuild → hot-update)
- Error recovery (broken syntax → fix → rebuild)
- Module cycle resilience
- Concurrent edit batching

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

| Phase | Status | PR |
|-------|--------|----|
| 1 — Parse cache | ✅ Merged | #21 |
| Test infrastructure | ✅ Merged | #22 |
| 2 — Codegen cache | Planned | — |
| 3 — Chunk render | Planned | — |
| 4 — Multi-pass | Planned | — |
| 5 — Mutations | Planned | — |
| 6 — Disk cache | Planned | — |
