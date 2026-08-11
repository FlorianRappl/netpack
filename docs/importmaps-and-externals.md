# Import maps & externals

netpack can leave a dependency out of the bundle entirely and let the
browser resolve it instead, via a native
[import map](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/script/type/importmap).
There are two ways into this, depending on who owns the mapping.

## You already have an import map

If your HTML entry point already contains a `<script type="importmap">`,
netpack reads it and automatically treats every key in `imports` as an
external — you don't need to repeat them with `--external`:

```html
<script type="importmap">
  {
    "imports": {
      "react": "https://esm.sh/react@18",
      "react-dom/client": "https://esm.sh/react-dom@18/client"
    }
  }
</script>
<script type="module" src="./main.tsx"></script>
```

Here, `import React from 'react'` in `main.tsx` is left as a real ESM import
in the output bundle — the browser resolves `react` using the import map
above, netpack never touches its contents. This is the escape hatch for
CDN-hosted or otherwise externally-served dependencies.

## `--external`: don't bundle this, but don't manage it either

```sh
npx netpack bundle src/index.html --external react --external react-dom
```

Use this when you already have your own import map (or a `<script>` tag
exposing a global) and just want netpack to stop trying to bundle the
import. netpack hoists the plain `import ... from 'react'` statement to the
top of the output bundle and leaves resolution entirely to the browser.

## `--shared`: don't bundle this, and wire it up for me

```sh
npx netpack bundle src/index.html --shared react --shared react-dom
```

`--shared` does everything `--external` does, plus:

1. it builds each shared name as its **own entry point**, producing a
   standalone output chunk (e.g. `react.js`, `react-dom.js`) from the actual
   package installed in `node_modules`;
2. it injects or extends the `<script type="importmap">` in your HTML,
   adding an entry per shared name that points at the generated chunk:

   ```html
   <script type="importmap">
     { "imports": { "react": "./react.js", "react-dom": "./react-dom.js" } }
   </script>
   ```

In other words, `--external` assumes someone else (a CDN, a prior
`<script>`) is going to serve the module; `--shared` makes netpack build and
serve it itself, as a separate cacheable chunk, without duplicating it
inside every bundle that imports it. This is the option to reach for when
you want one shared copy of React (or any other dependency) across several
independently-loaded entry points on the same page.

A shared name is sanitized into a file name by stripping characters that
can't appear in a path (so a scoped or sub-path import like
`react-dom/client` becomes something like `./react-domclient.js`) — if that
matters to you, prefer top-level package names for `--shared`.

## Choosing between them

The three modes differ in what happens to a dependency like `react` — whether
its code is bundled in, and who resolves it at runtime:

<svg viewBox="0 0 900 272" role="img" aria-labelledby="np-im-t np-im-d" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:auto;max-width:880px;font-family:ui-sans-serif,system-ui,sans-serif">
<title id="np-im-t">Bundled versus --external versus --shared</title>
<desc id="np-im-d">By default react is bundled inline; with --external it stays an import the browser resolves; with --shared netpack builds react.js as its own chunk and writes an import map entry.</desc>
<defs><marker id="np-im-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="currentColor" fill-opacity="0.55"/></marker></defs>
<style>.np-im .p{fill:currentColor;fill-opacity:0.03;stroke:currentColor;stroke-opacity:0.2}.np-im .b{fill:currentColor;fill-opacity:0.06;stroke:currentColor;stroke-opacity:0.35}.np-im .h{fill:currentColor;font-weight:600;font-size:14px}.np-im .hm{fill:currentColor;font-weight:600;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px}.np-im .t{fill:currentColor;font-weight:600;font-size:14px}.np-im .s{fill:currentColor;fill-opacity:0.8;font-size:12px}.np-im .c{fill:currentColor;fill-opacity:0.7;font-size:11.5px}.np-im .m{fill:currentColor;fill-opacity:0.85;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:11px}.np-im .l{stroke:currentColor;stroke-opacity:0.5}</style>
<g class="np-im">
<rect class="p" x="20" y="44" width="270" height="210" rx="12"/>
<rect class="p" x="315" y="44" width="270" height="210" rx="12"/>
<rect class="p" x="610" y="44" width="270" height="210" rx="12"/>
<text class="h" x="155" y="74" text-anchor="middle">default</text>
<text class="hm" x="450" y="74" text-anchor="middle">--external</text>
<text class="hm" x="745" y="74" text-anchor="middle" fill="#14b8a6">--shared</text>
<rect class="b" x="45" y="98" width="220" height="104" rx="8"/>
<text class="t" x="155" y="124" text-anchor="middle">app.js</text>
<rect x="95" y="142" width="120" height="40" rx="6" fill="#8b5cf6" fill-opacity="0.14" stroke="#8b5cf6" stroke-opacity="0.6"/>
<text class="m" x="155" y="167" text-anchor="middle">react</text>
<text class="c" x="155" y="234" text-anchor="middle">react is bundled inline</text>
<rect class="b" x="340" y="98" width="220" height="46" rx="8"/>
<text class="m" x="450" y="126" text-anchor="middle">import "react"</text>
<line class="l" x1="450" y1="146" x2="450" y2="166" marker-end="url(#np-im-arrow)"/>
<rect class="b" x="340" y="168" width="220" height="46" rx="8"/>
<text class="s" x="450" y="195" text-anchor="middle">browser resolves it</text>
<text class="c" x="450" y="234" text-anchor="middle">you own the import map</text>
<rect class="b" x="635" y="98" width="220" height="46" rx="8"/>
<text class="m" x="745" y="126" text-anchor="middle">import "react"</text>
<line class="l" x1="745" y1="146" x2="745" y2="166" marker-end="url(#np-im-arrow)"/>
<rect x="635" y="168" width="220" height="46" rx="8" fill="#14b8a6" fill-opacity="0.08" stroke="#14b8a6" stroke-opacity="0.8"/>
<text class="t" x="745" y="195" text-anchor="middle" fill="#14b8a6" font-size="13">react.js — own chunk</text>
<text class="c" x="745" y="234" text-anchor="middle">netpack writes the import map</text>
</g>
</svg>

| | Bundled | Own output chunk | Import map entry written |
| --- | --- | --- | --- |
| (default) | yes | — | — |
| `--external` | no | no | no (you provide it, or the browser already had one) |
| `--shared` | no | yes | yes, generated automatically |

`serve` and `analyze` accept the same `--external`/`--shared` flags as
`bundle`.
