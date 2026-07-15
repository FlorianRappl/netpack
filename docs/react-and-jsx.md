# React & JSX

netpack lowers JSX at compile time — `<div className="x">{child}</div>`
becomes a plain factory call — the same transform Babel/TypeScript/esbuild
do. What's configurable is *which* factory it calls, at three levels of
precedence.

## The default: React or Preact (auto-detected)

Out of the box, netpack picks the default JSX runtime from your dependencies:

- if `react` is present (or neither `react` nor `preact` is present), JSX
  lowers to `React.createElement` (fragments: `React.Fragment`);
- if `preact` is present and `react` is not, JSX lowers to `Preact.h`
  (fragments: `Preact.Fragment`).

In the Preact case, netpack also auto-injects `import Preact from "preact"`
for modules that contain JSX and don't already define a top-level `Preact`
binding.

React-style default output:

```jsx
// in:
export const a = <div />;

// out:
export const a = React.createElement("div");
```

Preact-only default output:

```jsx
// in (package.json has preact but no react):
export const a = <div />;

// out:
import Preact from "preact";
export const a = Preact.h("div");
```

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
or auto-detected dependency default.

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
