# Other features

Assorted things netpack does today that don't warrant their own page yet.

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

## Watch mode & HMR

`netpack serve` watches the filesystem and recompiles on every change with
no extra configuration — see
[Getting started](./getting-started.md#serve--dev-server-with-reloadhmr)
for how updates reach the browser (granular hot-swap vs. full reload), and
[React & JSX](./react-and-jsx.md#react-fast-refresh-in-the-dev-server) for
how React component state survives an edit when `react-refresh` is
installed.

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
