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
