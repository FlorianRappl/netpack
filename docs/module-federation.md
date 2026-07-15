# Module Federation

netpack can produce a [Module Federation](https://module-federation.io/)
remote container directly, without a separate plugin — it's a special entry
point, not a separate command.

## The `federation.json` convention

Point `bundle`/`serve`/`analyze` at a file literally named `federation.json`
and netpack treats it as a federation manifest instead of a module to
bundle:

```sh
npx netpack bundle src/federation.json --outdir dist
```

```json
{
  "name": "checkout",
  "filename": "remoteEntry.js",
  "shareScope": "default",
  "shareStrategy": "version-first",
  "exposes": {
    "./CheckoutForm": "./src/CheckoutForm.tsx",
    "./useCart": "./src/hooks/useCart.ts"
  },
  "shared": {
    "react": { "singleton": true, "requiredVersion": "^18.0.0" },
    "react-dom": { "singleton": true, "requiredVersion": "^18.0.0" }
  },
  "remotes": {
    "shell": { "name": "shell", "entry": "https://example.com/shell/remoteEntry.js" }
  }
}
```

| Field | Meaning |
| --- | --- |
| `name` | The container's federation name — how other remotes refer to it. |
| `kind` | `"module"` (default) for Module Federation, or `"native"` for [native federation](#native-federation). Any other value is an error. |
| `filename` | Output file name for the generated container (defaults to `remoteEntry.js`). |
| `shareScope` | Federation share scope, `"default"` unless you're isolating multiple federations on one page. |
| `shareStrategy` | `"version-first"` (default) or `"loaded-first"` — how the runtime picks between multiple copies of a shared dependency. |
| `exposes` | Map of public import name → local module. Each becomes a dynamic `import()` in the generated container so it's only fetched on demand. |
| `shared` | Dependencies this remote can share with the host/other remotes instead of bundling its own copy. `singleton: true` forces exactly one instance across the federation; `requiredVersion` is advertised to the runtime for version negotiation. |
| `remotes` | Other federated containers this one consumes, keyed by the alias used in `import()` calls. |

## What netpack generates

Given the manifest above, netpack:

1. resolves every `shared` dependency from your `node_modules` (the same
   resolution used for a normal import) and registers it under a
   `shared:<name>` alias, so the generated container can reference the
   locally installed copy;
2. reads the version of each shared dependency straight from your resolved
   `node_modules` metadata, so `requiredVersion`/negotiation matches what's
   actually installed;
3. writes a container script (`remoteEntry.js` or your configured
   `filename`) that wires up `exposes`, `shared` and `remotes` using the
   standard Module Federation runtime — the same `init()`/`get()` shape
   consumed by webpack, Rspack or Rsbuild federation hosts, so netpack
   remotes and host apps built with those tools can load each other.

The container is emitted as a regular bundle in `--outdir`, alongside
whatever else that build produces — there's nothing extra to wire up on the
consuming side beyond pointing a host's `remotes` config (or another
`federation.json`) at the emitted file.

## Native federation

Set `"kind": "native"` to emit a **native-federation** remote instead of a
Module Federation container. It uses the exact same `federation.json` entry —
only the output shape differs.

```json
{
  "name": "checkout",
  "kind": "native",
  "filename": "remoteEntry.js",
  "exposes": {
    "./CheckoutForm": "./src/CheckoutForm.tsx"
  },
  "shared": {
    "react": { "singleton": true },
    "react-dom": { "singleton": true }
  }
}
```

Where a Module Federation container ships the federation runtime and negotiates
shared dependencies through it, a native-federation remote is **just an ES
module**. Shared dependencies are imported directly, so the host resolves them
through a standard [import map](./importmaps-and-externals.md):

```js
// remoteEntry.js (native)
import * as __shared_0 from "react";
import * as __shared_1 from "react-dom";

export const shared = { "react": __shared_0, "react-dom": __shared_1 };
export const exposes = {
  "./CheckoutForm": () => import("./CheckoutForm.<hash>.js"),
};
export default { name: "checkout", exposes, shared };
```

netpack:

1. treats every `shared` dependency as an **external**, so each
   `import … from "react"` stays a bare specifier the host's import map
   resolves — no copy is inlined into the remote;
2. still emits each shared dependency as its **own standalone ES module**
   (`react.js`, `react-dom.js`, …), so the host can serve them and point its
   import map at them;
3. emits each `exposes` entry as its own ESM chunk, loaded on demand.

Everything stays ESM — there is no runtime container to load. The consuming host
provides an import map mapping the bare specifiers (`"react"`, the remote's own
name, …) to the emitted files.

## Combining with shared React etc.

`shared` in `federation.json` governs cross-remote sharing at the Module
Federation runtime level. If you *also* want the host page itself to load
React once via an import map (independent of federation), reach for
`--shared` on the host's own entry point instead — see
[Import maps & externals](./importmaps-and-externals.md). The two mechanisms
solve related but distinct problems: one is "don't duplicate this dependency
across federated remotes at runtime", the other is "don't duplicate this
dependency across bundles on one page".
