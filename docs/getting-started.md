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

## How a build flows

Whatever the entry, the shape of a build is the same: netpack parses the entry,
follows what it references to build a module graph, groups that graph into chunks
(pulling shared code into its own chunk), and renders each chunk to disk.

<svg viewBox="0 0 920 172" role="img" aria-labelledby="np-pipe-t np-pipe-d" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:auto;max-width:900px;font-family:ui-sans-serif,system-ui,sans-serif">
<title id="np-pipe-t">The netpack build pipeline</title>
<desc id="np-pipe-d">A single entry point is parsed into a module graph, grouped into chunks, and emitted to the output directory.</desc>
<defs><marker id="np-pipe-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="currentColor" fill-opacity="0.55"/></marker></defs>
<style>.np-pipe .b{fill:currentColor;fill-opacity:0.05;stroke:currentColor;stroke-opacity:0.35}.np-pipe .t{fill:currentColor;font-weight:600;font-size:15px}.np-pipe .s{fill:currentColor;fill-opacity:0.7;font-size:11px}.np-pipe .c{fill:currentColor;fill-opacity:0.75;font-size:12px}.np-pipe .l{stroke:currentColor;stroke-opacity:0.5}</style>
<g class="np-pipe">
<rect class="b" x="20" y="38" width="160" height="74" rx="10"/>
<rect class="b" x="200" y="38" width="160" height="74" rx="10"/>
<rect class="b" x="380" y="38" width="160" height="74" rx="10"/>
<rect class="b" x="560" y="38" width="160" height="74" rx="10"/>
<rect class="b" x="740" y="38" width="160" height="74" rx="10" style="stroke:#14b8a6;stroke-opacity:0.85"/>
<text class="t" x="100" y="72" text-anchor="middle">Entry</text>
<text class="s" x="100" y="94" text-anchor="middle">html · js · ts</text>
<text class="t" x="280" y="72" text-anchor="middle">Parse</text>
<text class="s" x="280" y="94" text-anchor="middle">native, no Babel</text>
<text class="t" x="460" y="72" text-anchor="middle">Graph</text>
<text class="s" x="460" y="94" text-anchor="middle">resolve imports</text>
<text class="t" x="640" y="72" text-anchor="middle">Chunks</text>
<text class="s" x="640" y="94" text-anchor="middle">shared splitting</text>
<text class="t" x="820" y="72" text-anchor="middle">Emit</text>
<text class="s" x="820" y="94" text-anchor="middle">minify · maps</text>
<line class="l" x1="182" y1="75" x2="198" y2="75" marker-end="url(#np-pipe-arrow)"/>
<line class="l" x1="362" y1="75" x2="378" y2="75" marker-end="url(#np-pipe-arrow)"/>
<line class="l" x1="542" y1="75" x2="558" y2="75" marker-end="url(#np-pipe-arrow)"/>
<line class="l" x1="722" y1="75" x2="738" y2="75" marker-end="url(#np-pipe-arrow)"/>
<text class="c" x="460" y="150" text-anchor="middle">One entry point becomes a module graph, then chunks, then files in dist/ — tree-shaking runs over the graph before emit.</text>
</g>
</svg>

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
| `--banner <text>` | — | Text placed on top of the entry JS bundle, followed by a newline. Empty banners are discarded. See [Banner](./other-features.md#banner---banner). |
| `--licenses <mode>` | `skip` | Third-party license handling: `skip`, `preamble`, `json`, or `spdx`. See [Licenses](./other-features.md#licenses---licenses). |
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

Accepts `--port` (default `1234`), plus `--minify`, `--external`, `--shared`
and `--banner` from `bundle`. Source maps are always on in dev.

When the `react-refresh` package is resolvable from your project, the dev
server automatically enables React Fast Refresh instead of plain HMR for
component modules — see [React & JSX](./react-and-jsx.md).

### `analyze` — inspect the bundle graph

```sh
npx netpack analyze src/index.html
```

Compiles (optimized) and reports on the resulting bundles: what's in them,
how big they are, how many modules each one pulls in.

It also **audits the dependencies** that made it into the graph against known
vulnerabilities (npm advisories / CVEs) and includes the findings under an
`audit` key in the metadata JSON — one entry per raised advisory (package,
version, severity, CVSS score, CWE, advisory URL) plus a per-severity summary.
Only the packages actually present in the graph are checked (tree-shaken ones are
not), and a failed audit (offline, timeout) is recorded on the report rather than
failing the run.

| Option | Meaning |
| --- | --- |
| `--outfile <file>` | Write the metadata as JSON instead of printing it. |
| `--interactive` | Start a small local server (default port `8080`) with a visual explorer of the bundle graph, and keep recompiling on change. |
| `--audit` | Audit dependencies for known vulnerabilities (default on; `--audit false` to disable). |
| `--external`, `--shared` | Same meaning as in `bundle`. |

### `graph` / `inspect`

Lower-level commands for printing the raw dependency graph or inspecting a
single resolved module — mainly useful when debugging netpack itself or a
tricky resolution issue.

You can also drive all of these from a Node.js program — see
[Programmatic use](./programmatic-api.md).

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

## Metafile JSON

Use `analyze --outfile <path>` to emit a machine-readable build manifest (esbuild
format) in JSON. The metafile is useful for CI tooling, post-processing
scripts, and visualizers.

```sh
npx netpack analyze src/index.html --outfile meta.json
```

### Schema

The metafile has two top-level keys: `inputs` and `outputs`.

#### Inputs

Each key is a source file path (relative to the project root), mapping to:

| Field | Type | Description |
|-------|------|-------------|
| `bytes` | int | Size of the raw source file in bytes |
| `format` | string | Module format (`"esm"`) |
| `imports` | array | Dependency edges — each with `path` (resolved file), `kind` (`"import-statement"`, `"require-call"`, `"dynamic-import"`), and `original` (the specifier as written) |

#### Outputs

Each key is an emitted output file name, mapping to:

| Field | Type | Description |
|-------|------|-------------|
| `bytes` | int | Size of the emitted file in bytes |
| `entryPoint` | string? | The output file name when this is the entry bundle, `null` for shared chunks and assets |
| `flags` | string? | `"entry"` for entry bundles, `"shared"` for split/shared chunks, `null` for assets |
| `inputs` | object | Source files bundled into this output. Each key is a relative path mapping to `{ "bytesInOutput": int }` |
| `exports` | array | Exported names from this output (each with `path` and `kind`) |
| `imports` | array | Bundles this output imports (each with `path` and `kind`) |

### Example

For a project with `app.js` importing `helper.js`:

```sh
npx netpack analyze app.js --outfile meta.json
```

```json
{
  "inputs": {
    "app.js": {
      "bytes": 34,
      "format": "esm",
      "imports": [{ "path": "helper.js", "kind": "import-statement", "original": "./helper.js" }]
    },
    "helper.js": {
      "bytes": 38,
      "format": "esm",
      "imports": []
    }
  },
  "outputs": {
    "app.js": {
      "bytes": 412,
      "entryPoint": "app.js",
      "flags": "entry",
      "inputs": {
        "app.js": { "bytesInOutput": 184 },
        "helper.js": { "bytesInOutput": 228 }
      },
      "exports": [],
      "imports": []
    }
  }
}
```

Shared CSS chunks appear as non-entry outputs:

```json
{
  "outputs": {
    "common.0001.css": {
      "bytes": 42,
      "entryPoint": null,
      "flags": "shared",
      "inputs": { "shared.css": { "bytesInOutput": 42 } },
      "exports": [],
      "imports": []
    }
  }
}
```
