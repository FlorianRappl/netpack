# React & JSX

netpack lowers JSX at compile time — `<div className="x">{child}</div>`
becomes a plain factory call — the same transform Babel/TypeScript/esbuild
do. What's configurable is *which* factory it calls, at three levels of
precedence.

## The default: `React.createElement`

Out of the box, a `.jsx`/`.tsx` file compiles JSX to `React.createElement`
(and fragments to `React.Fragment`):

```jsx
// in:
export const a = <div />;

// out:
export const a = React.createElement("div");
```

netpack does **not** inject an import for `React` — it only emits the call.
`React` has to already be in scope, exactly as with any other JSX toolchain:
either `import React from 'react'` in the file (bundled normally, or left
external — see [Import maps & externals](./importmaps-and-externals.md)), or
a global `window.React` provided some other way (e.g. a CDN `<script>` plus
`--external react`).

Static children are passed as separate trailing arguments, not as an array —
`React.createElement("ul", null, child1, child2)`, never
`React.createElement("ul", null, [child1, child2])` — so React doesn't emit
spurious "each child needs a key" warnings for markup that was never a real
list.

## Custom JSX factory — project-wide

Set `jsxFactory` (and optionally `jsxFragmentFactory`) in `tsconfig.json` to
retarget JSX for every TypeScript source file in the project — the usual way
to use netpack with Preact, or any other `h`-style factory:

```json
{
  "compilerOptions": {
    "jsxFactory": "h",
    "jsxFragmentFactory": "Fragment"
  }
}
```

```jsx
// in (app.tsx):
export const a = <div />;

// out:
export const a = h("div");
```

Two things worth knowing:

- **`tsconfig.json`'s `jsxFactory` only applies to TypeScript sources**
  (`.ts`/`.tsx`). A plain `.jsx` file in the same project still lowers to
  `React.createElement` unless it opts in itself (see below).
- netpack finds the nearest `tsconfig.json` by walking up from the entry
  point, the same way it finds `package.json` for root resolution.

## Custom JSX factory — per file

Any file — `.js`, `.jsx`, `.ts` or `.tsx` — can override the factory for
just itself with a leading pragma comment, before any code:

```jsx
/** @jsx h */
/** @jsxFrag Fragment */

export const a = <>{child}</>;
// compiles using h(...) / Fragment, regardless of tsconfig or the default
```

- `@jsx <factory>` — e.g. `@jsx h`, or a dotted path like `@jsx Preact.h`.
- `@jsxFrag <factory>` — the fragment factory, e.g. `@jsxFrag Fragment`.
- Either can appear alone; they don't have to be paired.
- The pragma must be in a comment that appears before the first line of
  code — netpack stops scanning at the first non-comment, non-whitespace
  character.

**Precedence, most specific wins:** a file-local `@jsx` pragma overrides
`tsconfig.json`'s `jsxFactory`, which overrides the `React.createElement`
default.

## React Fast Refresh in the dev server

Running `netpack serve` with `react-refresh` installed and resolvable from
your project automatically upgrades component hot-reloading from a plain
module swap to real Fast Refresh: component state survives an edit instead
of the whole module (and its subtree) being torn down and rebuilt. Nothing
to configure beyond having the package available:

```sh
npm i -D react-refresh
npx netpack serve src/index.html
```

If `react-refresh` isn't resolvable, `serve` still hot-updates modules —
component instances just remount instead of preserving state, and it falls
back to a full page reload whenever a change can't be applied granularly
(a module was added/removed, or something non-JS changed).
