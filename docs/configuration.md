# Configuration & presets

Every netpack option is a CLI flag, but you don't have to pass them by hand on
every build. A **preset** is a small JSON file that carries a set of options — and,
unlike the flags, it's transportable (share it as a file or an npm package) and
composable (build one on top of another without copying it).

```jsonc
// netpack.json
{
  "platform": "web",
  "minify": true,
  "external": ["react", "react-dom"],
  "define": { "process.env.NODE_ENV": "\"production\"" },
  "banner": "// (c) 2026 Acme, Inc."
}
```

Files are **JSONC** — comments and trailing commas are allowed, since these are
hand-authored.

## Where config comes from

Two entry points, and they stack:

- **`netpack.json`** in the working directory is picked up automatically.
- **`--preset <ref>`** loads an additional preset (repeatable). A `<ref>` is either
  a path (`./configs/prod.json`) or a package reference (`@acme/netpack-base`).

Both are resolved the same way as imports: a path resolves to a file; a package
reference resolves through `node_modules` (a subpath directly, or the package's
`package.json` `main`, which must point at a JSON file).

## Precedence

Options resolve **first-write-wins** — once something sets an option, later
sources can't override it. Sources are read highest priority first:

1. **CLI flags** — always win. A real `--minify` beats any preset.
2. **`--preset` presets**, in the order given.
3. The auto-discovered **`netpack.json`**.
4. Each preset's **referenced presets** (the `presets` array), in order,
   depth-first.

<svg viewBox="0 0 820 330" role="img" aria-labelledby="np-cfg-t np-cfg-d" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:auto;max-width:800px;font-family:ui-sans-serif,system-ui,sans-serif">
<title id="np-cfg-t">How netpack config is derived</title>
<desc id="np-cfg-d">Options resolve first-write-wins from highest to lowest priority: CLI flags, then --preset presets, then the auto-discovered netpack.json, then referenced base presets.</desc>
<defs><marker id="np-cfg-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="currentColor" fill-opacity="0.6"/></marker></defs>
<style>.np-cfg .b{fill:currentColor;fill-opacity:0.05;stroke:currentColor;stroke-opacity:0.3}.np-cfg .t{fill:currentColor;font-weight:600;font-size:14px}.np-cfg .tm{fill:currentColor;font-weight:600;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13.5px}.np-cfg .s{fill:currentColor;fill-opacity:0.7;font-size:11.5px}.np-cfg .c{fill:currentColor;fill-opacity:0.75;font-size:12px}.np-cfg .l{stroke:currentColor;stroke-opacity:0.55}.np-cfg .bg{fill:currentColor;fill-opacity:0.12;stroke:currentColor;stroke-opacity:0.4}.np-cfg .bn{fill:currentColor;font-weight:600;font-size:12px}</style>
<g class="np-cfg">
<rect x="150" y="40" width="560" height="56" rx="10" fill="#14b8a6" fill-opacity="0.09" stroke="#14b8a6" stroke-opacity="0.8"/>
<rect class="b" x="150" y="108" width="560" height="56" rx="10"/>
<rect class="b" x="150" y="176" width="560" height="56" rx="10"/>
<rect class="b" x="150" y="244" width="560" height="56" rx="10"/>
<circle class="bg" cx="180" cy="68" r="13"/><text class="bn" x="180" y="72" text-anchor="middle">1</text>
<circle class="bg" cx="180" cy="136" r="13"/><text class="bn" x="180" y="140" text-anchor="middle">2</text>
<circle class="bg" cx="180" cy="204" r="13"/><text class="bn" x="180" y="208" text-anchor="middle">3</text>
<circle class="bg" cx="180" cy="272" r="13"/><text class="bn" x="180" y="276" text-anchor="middle">4</text>
<text class="t" x="210" y="72" fill="#14b8a6">CLI flags</text>
<text class="s" x="690" y="72" text-anchor="end">always win</text>
<text class="tm" x="210" y="140">--preset</text>
<text class="s" x="690" y="140" text-anchor="end">in the order given</text>
<text class="tm" x="210" y="208">netpack.json</text>
<text class="s" x="690" y="208" text-anchor="end">auto-discovered</text>
<text class="t" x="210" y="276">referenced presets</text>
<text class="s" x="690" y="276" text-anchor="end">presets: […], depth-first</text>
<line class="l" x1="96" y1="298" x2="96" y2="46" marker-end="url(#np-cfg-arrow)"/>
<text class="s" x="66" y="172" text-anchor="middle" transform="rotate(-90 66 172)">priority · first write wins</text>
<text class="c" x="430" y="322" text-anchor="middle">Reading top-down, the first source to set an option wins — lower layers only fill the gaps.</text>
</g>
</svg>

So a preset's own values take precedence over the ones it pulls in — `presets`
behaves like inheritance, but you can list several (more like layering plugins).

```jsonc
// prod.json — layer CDN + banner onto a shared base, override nothing else
{
  "presets": ["@acme/netpack-base", "./base.json"],
  "publicPath": "https://cdn.acme.com/app",
  "banner": "// (c) 2026 Acme, Inc."
}
```

Referenced presets are tracked by their fully-resolved path and loaded once, so a
diamond (two presets pulling in the same base) resolves it a single time and
reference **cycles are safe** — an already-seen preset is skipped.

## Options

The keys mirror the CLI flags (camelCase where the flag is hyphenated):
`outdir`, `minify`, `sourcemap`, `clean`, `external`, `shared`, `format`,
`platform`, `define`, `alias`, `loader`, `entryNames`, `publicPath`,
`conditions`, `packages`, `banner`, `licenses`, and `port`.
`external`/`shared`/`conditions` are arrays; `define`/`alias`/`loader` are objects.
See
[Getting started](./getting-started.md) and [Other features](./other-features.md)
for what each does.

## Hooks

Presets are also the only place to register **hooks** — extension points that run
a JavaScript module at a specific point in the build (post-transformation, asset
rewriting, and more):

```jsonc
{
  "hooks": {
    "afterBundling": ["./transform.mjs"]
  }
}
```

See [Hooks](./hooks.md) for the full lifecycle, the module contract, and how
hooks merge across a preset chain.

## .NET

Presets are part of `NetPack.Core` (`NetPack.Config.Presets`), so a managed host
can resolve the same files and read the merged options and hook list without the
CLI. See [.NET libraries](./dotnet-libraries.md).

## splitChunks

netpack already chooses sensible chunk boundaries by default — shared modules
are automatically extracted into separate chunks without any configuration. The
`splitChunks` option is an **expert setting** for when you need precise control
over chunk grouping (custom vendor bundles, size thresholds, priority rules).

```jsonc
// netpack.json
{
  "splitChunks": {
    "minSize": 20000,
    "minChunks": 1,
    "cacheGroups": {
      "vendors": {
        "test": "**/node_modules/**",
        "name": "vendors",
        "priority": -10,
        "enforce": true
      }
    }
  }
}
```

In practice the `splitChunks` object is most naturally authored inside a preset
rather than passed as a raw JSON string on the CLI — though `--split-chunks` is
available for one-off experiments:

```sh
npx netpack bundle src/index.html --split-chunks '{...}'
```

### Available options

| Option | Type | Default | Description |
|---|---|---|---|
| `chunks` | `string` | `"async"` | Which chunks to select: `"all"`, `"async"`, or `"initial"`. |
| `minSize` | `int` | `20000` | Minimum size in bytes for a chunk to be created. |
| `minChunks` | `int` | `1` | Minimum number of chunks that must share a module before splitting. |
| `maxSize` | `int` | `0` | Maximum chunk size before further splitting (0 = off). |
| `maxAsyncRequests` | `int` | `30` | Maximum parallel requests for async chunks. |
| `maxInitialRequests` | `int` | `30` | Maximum parallel requests for entry points. |

### cacheGroup options

| Option | Type | Description |
|---|---|---|
| `test` | `string` | Glob pattern matching module paths (`**/node_modules/**`, `**/lib/**`). |
| `name` | `string` | Name for the output chunk. Defaults to the cacheGroup key. |
| `priority` | `int` | Priority when a module matches multiple groups (higher wins). Default `0`. |
| `enforce` | `bool` | When `true`, creates the chunk regardless of `minSize` / `minChunks`. |
| `minChunks` | `int` | Overrides the top-level `minChunks` for this group. |
| `minSize` | `int` | Overrides the top-level `minSize` for this group. |
| `chunks` | `string` | Overrides the top-level `chunks` filter for this group. |

The `"default"` cacheGroup is built-in and can be disabled by setting it explicitly:

```jsonc
{
  "splitChunks": {
    "cacheGroups": {
      "default": {}
    }
  }
}
```
