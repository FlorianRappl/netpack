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

| | Bundled | Own output chunk | Import map entry written |
| --- | --- | --- | --- |
| (default) | yes | — | — |
| `--external` | no | no | no (you provide it, or the browser already had one) |
| `--shared` | no | yes | yes, generated automatically |

`serve` and `analyze` accept the same `--external`/`--shared` flags as
`bundle`.
