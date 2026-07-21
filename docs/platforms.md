# Platforms

`--platform` tells netpack which runtime a bundle targets. Like esbuild's option
of the same name, it decides two things: which bare specifiers are **runtime
built-ins** (provided by the platform, so netpack keeps them external instead of
bundling a copy) and how a dependency's `package.json` entry point is chosen.

```sh
npx netpack bundle src/main.js --platform web    # default
npx netpack bundle src/server.js --platform node
npx netpack bundle src/main.ts --platform deno
```

| `--platform` | Built-ins kept external | `browser` field |
| --- | --- | --- |
| `web` (default) | none | used |
| `node` | `node:*` and Node core modules (`fs`, `path`, `http`, `crypto`, …, incl. subpaths like `fs/promises`) | ignored |
| `deno` | `node:*`, `npm:*`, `jsr:*` | ignored |

## Built-ins stay external

A built-in for the target platform is left as a bare import — it is never bundled,
because the runtime supplies it:

```js
// in (built with --platform node):
import { readFile } from 'node:fs/promises';
import path from 'path';

// out (esm): the imports are hoisted verbatim, nothing is bundled for them
import { readFile } from "node:fs/promises";
import path from "path";
```

Under `--platform node`, both the `node:` scheme and the classic bare names (`fs`,
`path`, `crypto`, …) — including subpaths such as `fs/promises` — are recognised.
A local package that happens to share a core module's name does not shadow the
built-in, matching Node's own resolution.

Under `--platform deno`, Deno's URL-like schemes (`node:`, `npm:`, `jsr:`) are kept
external for the Deno runtime to resolve.

Under `--platform web` (the default) nothing is treated as a built-in: everything
resolves through `node_modules` (or an [import map / external](./importmaps-and-externals.md)),
so importing a Node core module on the web is a resolution error rather than a
silent external — which is what you want when shipping to a browser.

This composes with the output [format](./output-formats.md): the external is wired
up the format's way, e.g. a real `import` in ESM, a `require("node:fs/promises")`
in CommonJS, and so on.

## Entry-point selection

The platform also controls whether the `browser` field of a dependency's
`package.json` is honoured when picking its entry point:

- **`web`** prefers `browser`, then `module`, then `main` — so a package that
  ships a browser-specific build is used on the web.
- **`node` / `deno`** ignore `browser` and prefer `module`, then `main` — so the
  same package resolves to its Node/universal build instead.

## Not covered yet

- **Conditional `exports`.** netpack selects an entry from the top-level
  `browser` / `module` / `main` fields; it does not yet evaluate the
  `exports` map's `import` / `require` / `browser` / `node` conditions.
- **Platform globals / defines.** A platform does not (yet) inject or gate
  runtime globals (for example service-worker or `Deno.*` APIs) beyond the
  built-in resolution described here; netpack does not type-check, so these are
  left to the runtime.
