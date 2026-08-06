# Other features

Assorted things netpack does today that don't warrant their own page yet.

## Output formats

By default netpack emits ES modules; `--format` (`esm`, `cjs`, `umd`, `systemjs`)
picks the envelope each JavaScript bundle is wrapped in. See
[Output formats](./output-formats.md) for the details, limitations, and why ESM
is the best choice.

## Tree shaking

netpack computes which exports of each module are actually used across the
whole graph (once per build, cached) and drops the rest — an `export` no
importer ever references doesn't make it into the output bundle.

## Source maps

```sh
npx netpack bundle src/index.html --sourcemap
```

Emits a `.js.map` next to each JavaScript bundle. `serve` always emits
source maps, regardless of this flag, since you're debugging live.

## Minification

```sh
npx netpack bundle src/index.html --minify
```

Optimizes JS, CSS and the HTML shell for size. `bundle`'s summary table
shows the effect directly — compare a build with and without `--minify`.

## Compile-time constants (`--define`)

Replaces a global identifier or member expression with a constant expression
before parsing — the value is inlined, so dead branches tree-shake away.

```sh
npx netpack bundle src/index.html --define __VERSION__=\"1.4.0\" --define DEBUG=false
```

The replacement text must be valid JavaScript, so a string constant keeps its
quotes (`--define API=\"/v2\"`). `process.env.NODE_ENV` is defined for you
(`development` under `serve`, `production` for an optimized build); a `--define`
of your own overrides it. Both `bundle` and `serve` accept the flag, repeatably.

## Import aliases (`--alias`)

Rewrites an import specifier to another package or a local file.

```sh
npx netpack bundle src/index.html --alias react=preact/compat --alias @=./src
```

A bare target (`preact/compat`) is resolved like any dependency; a path target
(`./src`) is resolved from the working directory. Matching is on the specifier,
so `import "@"` picks up the alias.

## Loaders (`--loader`)

Overrides how a file extension is turned into a module, replacing the built-in
handling.

```sh
npx netpack bundle src/index.html --loader .svg=text --loader .frag=text
```

Available loaders: `js`, `jsx`, `ts`, `tsx`, `json`, `css`, `text` (import the
file's contents as a string), `base64`, `dataurl` (inline as a `data:` URI),
`file`/`copy` (emit the file and import its URL), and `empty`. The
inline loaders (`text`/`base64`/`dataurl`/`empty`) produce a JS module, so they
apply to files imported from JavaScript.

## Cache-busting file names (`--entry-names`)

Adds a content hash to emitted bundle names so they can be served with a
long-lived cache. References from the HTML entry (and between bundles) are
rewritten to the hashed names automatically.

```sh
npx netpack bundle src/index.html --entry-names [name]-[hash]
```

The template understands `[name]` and `[hash]`; the default is `[name]` (no
hash). The entry HTML document keeps its own name so it stays linkable.
Imported assets are content-hashed already, independently of this flag. The hash
reflects each bundle's own contents, so a change confined to a shared bundle
re-hashes that bundle but not the entries that import it.

## Public path (`--public-path`)

Prepends a base path or URL to every reference to an emitted file — bundle
chunks, assets, and the `script`/`link`/`img` targets in the HTML shell — so the
output can be served from a CDN or a sub-path instead of next to the document.

```sh
npx netpack bundle src/index.html --public-path https://cdn.example.com/app
```

With no public path (the default) references stay document-relative
(`./app.js`); with one they become `https://cdn.example.com/app/app.js`. It
applies across every output format.

## Banner (`--banner`)

Places arbitrary text on the very first line of the entry JS bundle, followed by
a newline — typically a license/copyright header or a runtime pragma.

```sh
npx netpack bundle src/index.html --banner "// (c) 2026 Acme, Inc. — MIT"
```

The banner is emitted verbatim, so it is your responsibility to make it valid for
the position it lands in (a `//` or `/* … */` comment, a `"use client"`-style
directive, a shebang, …). It goes on top of every entry JS bundle; shared split
chunks are left untouched. An empty banner (the default) emits nothing. Source
maps stay accurate: mappings are shifted to account for the added lines. Both
`bundle` and `serve` accept the flag.

## Licenses (`--licenses`)

By default netpack collects the **legal comments** in your dependencies — the
`/*! … */`, `//! …`, `@license`, `@preserve` and `@copyright` blocks bundlers are
expected to preserve — and keeps the relevant ones in each bundle's head (after
any `--banner`). `--licenses` picks how that's handled:

| Value | Behaviour |
| --- | --- |
| `skip` (default) | Don't collect or emit any licenses. |
| `preamble` | Keep each module's legal comments at the top of the bundle it lands in, after the banner. |
| `json` | Write a `licenses.json` manifest (package name, version, license id, license text) to the output directory. |
| `spdx` | Write a `licenses.spdx` manifest in the SPDX tag-value format. |

```sh
npx netpack bundle src/index.html --licenses spdx
```

The `json`/`spdx` manifests list one entry per resolved dependency (deduplicated by
name+version). If a file with that name already exists in the output (for example
one copied from `public/`), a short suffix is added — `licenses-1a2b3c.json` — so
nothing is clobbered. The declared license comes from each package's
`package.json` `license` field; the license text, when present, from its `LICENSE`
file.

## Exports conditions (`--conditions`)

Adds custom [`exports`](./platforms.md#entry-point-selection) conditions on top
of the platform defaults, widening which conditional branches of a dependency's
`package.json` `exports` map are eligible.

```sh
npx netpack bundle src/index.html --conditions development --conditions browser
```

User conditions take priority over the platform's built-ins; `default` always
matches last.

## Externalizing packages (`--packages`)

`--packages external` keeps every bare (i.e. `node_modules`) import external
instead of bundling it — the standard way to build a library, or a Node app whose
dependencies are installed separately. Relative and absolute imports are still
bundled.

```sh
npx netpack bundle src/lib.ts --packages external --format esm
```

This is the bulk equivalent of listing every dependency with `--external`.

## Watch mode & HMR

`netpack serve` watches the filesystem and recompiles on every change with
no extra configuration — see
[Getting started](./getting-started.md#serve--dev-server-with-reloadhmr)
for how updates reach the browser (granular hot-swap vs. full reload), and
[React & JSX](./react-and-jsx.md#react-fast-refresh-in-the-dev-server) for
how React component state survives an edit when `react-refresh` is
installed.

For a build without a server, `netpack bundle --watch` rebuilds and rewrites the
output directory whenever a source file that took part in the build changes:

```sh
npx netpack bundle src/index.html --outdir dist --watch
```

It writes to disk (no dev server, no HMR) and runs until interrupted — handy when
another process serves `dist/`.

## Bundle analyzer

```sh
npx netpack analyze src/index.html --interactive
```

Starts a local server (default port `8080`) with a visual explorer of the
bundle graph — which modules ended up in which chunk, and how large each
one is — and keeps recompiling as you edit. Drop `--interactive` and add
`--outfile meta.json` instead to get the same metadata as a static file, or
neither to just print it to the console. See
[Getting started](./getting-started.md#analyze--inspect-the-bundle-graph).

The metadata also carries an `audit` section: the dependencies that made it into
the graph are checked against known vulnerabilities (npm advisories / CVEs), and
each advisory (with severity, CVSS score, CWE and URL) is listed alongside a
per-severity summary. Disable it with `--audit false`.

### Optimization recommendations

Alongside the raw graph, `analyze` inspects the *shape* of your chunks and points
out where they could be split more efficiently — the goal being fewer requests
and more predictable load order, not just smaller totals. The findings live in a
`savings` section of the metadata and are surfaced as an interactive **Savings**
tab in the analyzer, with a short summary printed to the console. Each
recommendation carries a machine-readable `kind`, a `severity`, the affected
modules and bundles, its byte and request impact, and a plain-language `message`
telling you what to do. Three situations are flagged:

- **Duplicated module** — a source module whose code physically lands in more
  than one output bundle. Moving it into a shared dependency (or importing it from
  a single module) removes the duplicated bytes outright. `potentialBytes` sums up
  this provably-wasted code across the whole graph.
- **Orphan shared chunk** — a `common.*` chunk that only one entry ever loads. It
  de-duplicates nothing and merely costs a request, so it should be merged into
  its single consumer.
- **Over-split chunk** — a small chunk shared by only a couple of entries.
  Inlining it into each trades a little duplicated code for one fewer request.
- **Duplicate package versions** — the same npm package resolved at more than one
  version, so a full copy of the library ships per version. Aligning them on a
  single version (dedupe / lockfile update) is usually the biggest single win.
- **Oversized bundle** — an output above the recommended ~244 KB budget. Rather
  than a generic "split it", netpack points at the best lazy-load target: the
  top-level import whose dependency subtree is only reachable through it, so
  turning that import into a dynamic `import()` actually shrinks the entry. If no
  import detaches a large subtree (the modules are entangled), it says so — an
  oversized bundle can be the honest shape, and forcing a split wouldn't help.
- **Dominant module** — a single module that makes up most of a bundle. Loading
  it lazily (or swapping a heavy dependency for a lighter one) keeps it off the
  critical path.
- **Inline this asset** — a small asset emitted as its own file but used in only
  one place: inlining it (raise `--inline-limit`, or add `?inline`) saves an
  immediate request for a few added bytes.
- **Stop inlining asset** — an asset baked into the bundle as a data URI that is
  either large or duplicated across several bundles: emitting it as a file removes
  the bytes (and duplication) and lets the browser cache it on its own.

For example, rather than shipping two small entries plus one large shared chunk
(three requests), the analyzer will suggest folding the shared code back in so the
browser fetches two balanced bundles instead.

## Build-time code generation

Covered in full in [Build-time code generation](./codegen.md) — a `.codegen`
file is executed as a small Node module at build time, and whatever it
returns becomes that module's JavaScript source.

## Import maps, externals & shared dependencies

Covered in full in [Import maps & externals](./importmaps-and-externals.md).

## Module Federation

Covered in full in [Module Federation](./module-federation.md).

## Native, npm-installable binary

netpack ships as a single Ahead-of-Time-compiled binary per platform
(`@netpack/linux-x64`, `@netpack/osx-arm64`, `@netpack/win-x64`), installed
through the `netpack` npm wrapper like any other JS build tool — no JIT
warmup, no separate runtime to install. This is also why the Node
dependency called out above (Sass/LESS/PostCSS/codegen) is opt-in rather
than a baseline requirement: it only spins up when you actually import
something that needs it.
