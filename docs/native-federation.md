# Native Federation

netpack can produce a **native-federation** remote — the same idea as
[Module Federation](./module-federation.md), but the remote is a plain ES
module rather than a container that ships a federation runtime. Shared
dependencies are imported directly and resolved by the host through an
[import map](./importmaps-and-externals.md).

Like Module Federation, it's a special entry point (a `federation.json`), not a
separate command.

## The `federation.json` convention

Point `bundle`/`serve`/`analyze` at a file literally named `federation.json`
with `"kind": "native"`:

```sh
npx netpack bundle src/federation.json --outdir dist
```

```json
{
  "name": "checkout",
  "kind": "native",
  "filename": "remoteEntry.js",
  "exposes": {
    "./CheckoutForm": "./src/CheckoutForm.tsx",
    "./useCart": "./src/hooks/useCart.ts"
  },
  "shared": {
    "react": { "singleton": true, "requiredVersion": "^18.0.0" },
    "react-dom": { "singleton": true, "requiredVersion": "^18.0.0" }
  }
}
```

| Field | Meaning |
| --- | --- |
| `name` | The remote's federation name — how a host refers to it. |
| `kind` | `"native"` selects native federation. Omitted or `"module"` selects [Module Federation](./module-federation.md); any other value is an error. |
| `filename` | Output file name for the generated remote entry (defaults to `remoteEntry.js`). |
| `exposes` | Map of public import name → local module. Each becomes its own lazily-imported ESM chunk. |
| `shared` | Dependencies the host provides instead of the remote bundling its own copy. Each is imported directly as a bare specifier and also emitted as a standalone ESM file. |

The `shareScope`, `shareStrategy` and `remotes` fields that a Module Federation
manifest uses are not part of the native-federation model — sharing is handled
by the host's import map, and a host consumes a native remote by importing its
entry module directly.

## What netpack generates

Given the manifest above, netpack emits a remote entry that is **just an ES
module**:

```js
// remoteEntry.js
import * as __shared_0 from "react";
import * as __shared_1 from "react-dom";

export const shared = { "react": __shared_0, "react-dom": __shared_1 };
export const exposes = {
  "./CheckoutForm": () => import("./CheckoutForm.<hash>.js"),
  "./useCart": () => import("./useCart.<hash>.js"),
};
export default { name: "checkout", exposes, shared };
```

Concretely, netpack:

1. treats every `shared` dependency as an **external**, so each
   `import … from "react"` — in the remote entry, in the exposed modules and
   anywhere in between — stays a bare specifier the host's import map resolves.
   No copy of a shared dependency is inlined into the remote;
2. still emits each shared dependency as its **own standalone ES module**
   (`react.js`, `react-dom.js`, …), named after the package, so the host can
   serve them and point its import map at them;
3. emits each `exposes` entry as its own ESM chunk, referenced from the remote
   entry through a lazy `() => import(...)` so it's only fetched on demand;
4. pins the shared imports at the top of the remote entry (referenced through
   the exported `shared` map) so they are never tree-shaken away.

Everything stays ESM — there is no runtime container to download and boot before
the remote can be used.

## Consuming a native remote

Because the remote is a normal ES module, a host loads it with a dynamic
`import()` and reads its `exposes` map:

```js
const remote = await import("https://example.com/checkout/remoteEntry.js");
const { default: CheckoutForm } = await remote.exposes["./CheckoutForm"]();
```

For the bare specifiers inside the remote (`"react"`, `"react-dom"`, …) to
resolve, the host page provides an import map pointing at the shared ESM files
netpack emitted (served by whoever hosts them):

```html
<script type="importmap">
{
  "imports": {
    "react": "https://example.com/checkout/react.js",
    "react-dom": "https://example.com/checkout/react-dom.js"
  }
}
</script>
```

Because both the host and every remote resolve `"react"` through the same import
map entry, they share a single instance — the native-federation equivalent of a
Module Federation `singleton`.

## Native vs. Module Federation

Both start from the same `federation.json`; `kind` is the only switch.

| | Module Federation (`"module"`) | Native Federation (`"native"`) |
| --- | --- | --- |
| Remote entry | Container that ships the MF runtime | Plain ES module |
| Shared deps | Negotiated by the MF runtime at load time | Bare `import`s resolved by an import map |
| Interop | webpack / Rspack / Rsbuild federation hosts | Any ESM host with an import map |
| Extra runtime | Yes (federation runtime) | None |

Reach for `"module"` when you need to interop with an existing webpack/Rspack
federation host; reach for `"native"` when you control the host and want plain,
runtime-free ES modules wired together with an import map.
