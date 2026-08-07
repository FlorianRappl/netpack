# Bundle analyzer

```sh
npx netpack analyze src/index.html --interactive
```

Starts a local server (default port `8080`) with a visual explorer of the
bundle graph — which modules ended up in which chunk, and how large each
one is — and keeps recompiling as you edit. Drop `--interactive` and add
`--outfile meta.json` instead to get the same metadata as a static file, or
neither to just print it to the console. See
[Getting started](./getting-started.md#analyze--inspect-the-bundle-graph).

## Dependency audit

The metadata carries an `audit` section: the dependencies that made it into
the graph are checked against known vulnerabilities (npm advisories / CVEs), and
each advisory (with severity, CVSS score, CWE and URL) is listed alongside a
per-severity summary. It surfaces as an **Audits** tab in the interactive
analyzer, grouped by package. Disable it with `--audit false`.

## Optimization recommendations

Alongside the raw graph, `analyze` inspects the *shape* of your chunks and points
out where they could be split more efficiently — the goal being fewer requests
and more predictable load order, not just smaller totals. The findings live in a
`savings` section of the metadata and are surfaced as an interactive **Savings**
tab in the analyzer, with a short summary printed to the console. Each
recommendation carries a machine-readable `kind`, a `severity`, the affected
modules and bundles, its byte and request impact, and a plain-language `message`
telling you what to do.

Findings are ranked HIGH / MEDIUM / LOW by impact, and the analysis is
ownership-aware: your own code is examined module by module (each with a concrete
fix you can make), while third-party packages are treated as a single unit — you
can swap a whole dependency, but not edit one file inside it.

### Chunk shape

- **Duplicated module** — a source module whose code physically lands in more
  than one output bundle. Moving it into a shared dependency (or importing it from
  a single module) removes the duplicated bytes outright. `potentialBytes` sums up
  this provably-wasted code across the whole graph.
- **Orphan shared chunk** — a `common.*` chunk that only one entry ever loads. It
  de-duplicates nothing and merely costs a request, so it should be merged into
  its single consumer.
- **Over-split chunk** — a small chunk shared by only a couple of entries.
  Inlining it into each trades a little duplicated code for one fewer request.

For example, rather than shipping two small entries plus one large shared chunk
(three requests), the analyzer will suggest folding the shared code back in so the
browser fetches two balanced bundles instead.

### Size & splitting

- **Oversized bundle** — an output above the recommended ~244 KB budget. Rather
  than a generic "split it", netpack points at the best lazy-load target: the
  top-level import whose dependency subtree is only reachable through it, so
  turning that import into a dynamic `import()` actually shrinks the entry. If no
  import detaches a large subtree (the modules are entangled), it says so — an
  oversized bundle can be the honest shape, and forcing a split wouldn't help.
- **Dominant module** — a single module that makes up most of a bundle. Loading
  it lazily (or swapping a heavy dependency for a lighter one) keeps it off the
  critical path.
- **Duplicate package versions** — the same npm package resolved at more than one
  version, so a full copy of the library ships per version. Aligning them on a
  single version (dedupe / lockfile update) is usually the biggest single win.

### Dead code & structure

- **Side-effect DCE trap** — one of your own modules has top-level side effects, so
  importing even one of its exports drags the whole file in (the unused exports
  can't be tree-shaken). Splitting the side-effectful code out — or making the
  module pure — lets the dead exports drop.
- **Package not tree-shaken** — the same trap inside a dependency, reported once per
  package. Try a subpath import, the package's ESM build, or a lighter alternative.
- **Widely-imported hub** — one of your modules is imported by many files (and,
  when heavy, spans several entry points): a natural refactor target and
  shared-chunk boundary.

### Assets

- **Inline this asset** — a small asset emitted as its own file but used in only
  one place: inlining it (raise `--inline-limit`, or add `?inline`) saves an
  immediate request for a few added bytes.
- **Stop inlining asset** — an asset baked into the bundle as a data URI that is
  either large or duplicated across several bundles: emitting it as a file removes
  the bytes (and duplication) and lets the browser cache it on its own.
