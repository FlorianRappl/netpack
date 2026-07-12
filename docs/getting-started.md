# Getting started

netpack is a single native binary, distributed through npm. There's no
runtime to install and nothing to warm up — the CLI starts and behaves the
same whether it's bundling a two-file script or a large app.

## Install

```sh
npm i -D netpack
```

This pulls in the wrapper package (`netpack`) plus the platform package for
your OS/architecture (`@netpack/linux-x64`, `@netpack/osx-arm64` or
`@netpack/win-x64`). The wrapper just forwards to the native binary.

## Entry points

netpack takes a single entry point and follows whatever it imports/references
from there — same convention as Vite or Parcel:

- an **HTML file** (`index.html`) — script/link/img/etc. references are
  resolved, bundled and rewritten in place;
- a **JavaScript/TypeScript file** (`main.mjs`, `app.tsx`, …) — bundled
  directly, no HTML wrapper required;
- a file literally named **`federation.json`** — treated specially, see
  [Module Federation](./module-federation.md).

You don't need a build config. If your project needs externals, shared
dependencies, or has a `tsconfig.json` with JSX options, netpack picks that up
automatically (see the other docs in this folder).

## Commands

### `bundle` — one-shot production build

```sh
npx netpack bundle src/index.html
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--outdir <dir>` | `dist` | Where to write the output. |
| `--minify` | off | Minify the emitted JS/CSS/HTML. |
| `--sourcemap` | off | Emit a `.js.map` next to each JS bundle. |
| `--clean` | off | Delete `--outdir` before writing. |
| `--external <name>` | — | Repeatable. Don't bundle this import; leave it as a real `import` for the browser/import map to resolve. |
| `--shared <name>` | — | Repeatable. Like `--external`, but also builds the dependency as its own output chunk and wires it into an import map. See [Import maps & externals](./importmaps-and-externals.md). |

### `serve` — dev server with reload/HMR

```sh
npx netpack serve src/index.html
```

Watches the filesystem, recompiles on change, and pushes updates to the
browser over a small SSE-based client:

- if only module bodies changed, it sends a granular `update` and hot-swaps
  the affected module factories in place (no full reload);
- if a module was added/removed, or something non-JS changed, it falls back
  to a full page reload.

Accepts `--port` (default `1234`), plus `--minify`, `--external` and
`--shared` from `bundle`. Source maps are always on in dev.

When the `react-refresh` package is resolvable from your project, the dev
server automatically enables React Fast Refresh instead of plain HMR for
component modules — see [React & JSX](./react-and-jsx.md).

### `analyze` — inspect the bundle graph

```sh
npx netpack analyze src/index.html
```

Compiles (optimized) and reports on the resulting bundles: what's in them,
how big they are, how many modules each one pulls in.

| Option | Meaning |
| --- | --- |
| `--outfile <file>` | Write the metadata as JSON instead of printing it. |
| `--interactive` | Start a small local server (default port `8080`) with a visual explorer of the bundle graph, and keep recompiling on change. |
| `--external`, `--shared` | Same meaning as in `bundle`. |

### `graph` / `inspect`

Lower-level commands for printing the raw dependency graph or inspecting a
single resolved module — mainly useful when debugging netpack itself or a
tricky resolution issue.

## Output

For an HTML entry point, netpack writes:

- the HTML file itself, with `<script>`/`<link>`/etc. `src`/`href`
  attributes rewritten to point at the emitted bundle files;
- one JS bundle per connected component of the module graph (so code that's
  never reached from more than one entry gets its own chunk automatically);
- one CSS bundle when styles are imported from JS, or referenced directly
  from HTML;
- anything placed in a `public/` folder next to the entry file, copied
  as-is next to the output.

`bundle` prints a summary table of every emitted file, its size, and (for
JS/CSS bundles) how many modules went into it.
