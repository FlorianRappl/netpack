# Output formats

A netpack build is a graph of modules wrapped in a single runtime. What changes
between formats is only the **envelope** — the linkage at the module boundary:
how a bundle imports its externals and shared siblings, how it exports the entry
module, and how it resolves dynamic imports and asset URLs. The module graph,
tree-shaking, shared-chunk splitting and dynamic-import code-splitting are
identical regardless of format.

```sh
npx netpack bundle src/main.js --format esm      # default
npx netpack bundle src/main.js --format cjs
npx netpack bundle src/main.js --format umd
npx netpack bundle src/main.js --format systemjs
```

| `--format` | Envelope | Primary target |
| --- | --- | --- |
| `esm` (default) | Native ES modules | Browsers, modern bundlers, Node ESM |
| `cjs` | `require` / `module.exports` | Node (CommonJS) |
| `umd` | IIFE adapting to CJS / AMD / global | `<script>` libraries, legacy loaders |
| `systemjs` | `System.register(…)` | The SystemJS loader |

Everything below assumes a shared mental model: each module becomes a factory
`(module, exports, require) => { … }` in a registry `__m`, and a tiny `__r`
runtime instantiates them lazily. Only the code *around* that registry differs.

## ESM — `--format esm` (default, recommended)

The native ECMAScript module format, and what netpack is built around.

```js
import __s0 from "./common.4f2a.js";       // shared sibling bundle
import { render } from "react-dom";         // external, left bare
const __m = { /* … module factories … */ };
Object.assign(__m, __s0);
// … __r runtime …
export default __r(0);
export { value };
```

- **Imports** — externals and shared chunks are real `import` statements the
  platform resolves (a browser, an import map, or Node).
- **Exports** — the entry's exports are real `export` / `export default`, so
  consumers get live, statically-analysable named bindings.
- **Dynamic imports** — `import(new URL("./chunk.js", import.meta.url).href)`:
  the spec-standard dynamic import, resolved relative to the module via
  `import.meta.url`. The same expression gives asset URLs.
- **No wrapper** — the bundle *is* a module; nothing is added around it.

**Why it's the best choice.** ESM is the only format here that is a language
feature rather than a convention layered on top of one:

- **Static structure.** `import`/`export` are analysable without running code, so
  tree-shaking, `import.meta`, top-level `await` and live bindings all work
  naturally. The other formats erase that structure into function calls.
- **No interop guesswork.** There is no default-vs-namespace ambiguity to paper
  over — CJS and UMD need an interop shim (below) precisely because ESM's shape
  has to be reconstructed at runtime.
- **First-class everywhere.** Browsers (`<script type="module">` and import
  maps), Node, Deno, CDNs and every modern bundler consume ESM directly. It is
  also the substrate for [native federation](./native-federation.md) and
  [import maps](./importmaps-and-externals.md).
- **Smallest, cleanest output.** No envelope, no adapter branches, no injected
  interop helpers — just the modules.

Reach for another format only when a specific consumer *cannot* load ESM.

## CommonJS — `--format cjs`

For Node consumers that use `require()`.

```js
function __cjsInterop(m) { return m && m.__esModule ? m : Object.assign({ default: m }, m); }
const __nurl = require("url").pathToFileURL(__filename).href;
const __s0 = require("./common.4f2a.js");
const { default: React } = __cjsInterop(require("react"));
const __m = { /* … */ };
// … __r runtime …
module.exports = __r(0);
```

- **Imports** — `require(…)`. Externals are wrapped in `__cjsInterop` so a
  default import (`import React from "react"`) still works against a CommonJS
  package that has no `.default`.
- **Exports** — `module.exports = <entry>`. All named exports and `default`
  become properties of `module.exports`.
- **Dynamic imports** — native `import()` (supported in Node CommonJS), targeting
  a `file:` URL built from `__filename`.
- **Asset URLs** — resolved against `__filename` (a `file:` URL).

**Limitations.**

- **Node-only.** It relies on `require`, `module`, `__filename` and
  `require("url")`; it does not run in a browser as-is.
- **Interop is heuristic.** `__cjsInterop` reconstructs a default export by
  convention (`__esModule` or "the module *is* the default"). This matches how
  bundlers generally interop, but pathological packages can still surprise it.
- **No live bindings.** Exports are a plain snapshot object, not ESM live
  bindings.

## UMD — `--format umd`

One file that loads as CommonJS, AMD, or a browser global — handy for shipping a
library consumed by unknown/legacy toolchains.

```js
(function (root, factory) {
  if (typeof exports === "object" && typeof module !== "undefined") module.exports = factory(require("react"));
  else if (typeof define === "function" && define.amd) define(["react"], factory);
  else root["main"] = factory(root["react"]);
})(typeof self !== "undefined" ? self : this, function (__dep_0) {
  function __umdInterop(m) { return m && m.__esModule ? m : Object.assign({ default: m }, m); }
  const { default: React } = __umdInterop(__dep_0);
  const __m = { /* … */ };
  // … __r runtime …
  return __r(0);
});
```

- **Imports** — externals and shared chunks become **factory parameters**,
  resolved three ways: `require(spec)` (CommonJS), a `define([spec], …)`
  dependency (AMD), or `root[spec]` (browser global). Default interop is applied,
  as in CommonJS.
- **Exports** — the factory `return`s the entry; the header assigns it to
  `module.exports`, the AMD module, or `root[<global>]`.
- **Global name** — derived from the entry file name (e.g. `main.js` → `main`).
- **Dynamic imports / assets** — best-effort base URL: the running script's URL
  (`document.currentScript.src`) in the browser, else `__filename`.

**Limitations.**

- **Global names are literal specifiers.** A browser-global dependency is looked
  up as `root["react"]`, not `root.React` — netpack has no package-name → global
  map, so the host must expose externals under their specifier name.
- **The global export name is derived, not configured.** It comes from the file
  name.
- **Base-URL resolution is best-effort.** Dynamic imports and asset URLs assume a
  browser `document.currentScript` or a Node `__filename`; other loaders (bare
  AMD) may not resolve them.
- **Largest envelope.** The three-way adapter and interop helper add overhead
  ESM/CJS don't have.

## SystemJS — `--format systemjs`

For apps loaded through the [SystemJS](https://github.com/systemjs/systemjs)
loader, which polyfills modules (and import maps) on older browsers.

```js
System.register(["./common.4f2a.js", "react"], function (_export, _context) {
  var __dep_0, __dep_1;
  return {
    setters: [
      function (m) { __dep_0 = m; },
      function (m) { __dep_1 = m; }
    ],
    execute: function () {
      const __s0 = __dep_0.default;          // shared registry (default export)
      const { default: React } = __dep_1;    // external
      const __m = { /* … */ };
      // … __r runtime …
      _export(__r(0));
    }
  };
});
```

- **Imports** — every dependency is declared in the `System.register` array and
  delivered through a **setter**; the loader handles interop, so `.default` and
  named members are already correct.
- **Exports** — published with `_export(<entry>)`.
- **Dynamic imports** — `_context.import(…)`, the loader's dynamic import.
- **Asset URLs** — resolved against `_context.meta.url`.

**Limitations.**

- **Requires the SystemJS runtime.** The output is inert without the loader on
  the page; it is not a standalone module.
- **Loader-specific.** It targets SystemJS's semantics rather than a platform
  standard.

## Externals and shared chunks

External dependencies (see [Import maps & externals](./importmaps-and-externals.md))
stay bare in every format and are wired up the format's way — a `require("react")`
in CJS, a `define(["react"], …)` dependency in UMD, a setter in SystemJS, a real
`import` in ESM. Shared chunks (`--shared`, or automatic common chunks) are
emitted in the *same* format as the bundle that imports them, and linked the same
way, so a CommonJS build's shared chunks are themselves CommonJS, and so on.

## Choosing a format

- **Default to `esm`.** It is the standard, produces the smallest and cleanest
  output, and is what every modern target consumes.
- **`cjs`** when you publish for Node consumers still on `require()`.
- **`umd`** when you ship a browser library for unknown/legacy loaders and need
  one file that "just works" via a `<script>` tag.
- **`systemjs`** when your app is already loaded through the SystemJS loader.
