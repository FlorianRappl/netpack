# Module Federation

netpack can produce a [Module Federation](https://module-federation.io/)
remote container directly, without a separate plugin — it's a special entry
point, not a separate command.

A host loads the generated `remoteEntry.js` at runtime and dynamically imports
the modules it exposes; dependencies marked `shared` are resolved through a
common share scope instead of being bundled twice:

<svg viewBox="0 0 880 300" role="img" aria-labelledby="np-fed-t np-fed-d" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:auto;max-width:860px;font-family:ui-sans-serif,system-ui,sans-serif">
<title id="np-fed-t">A Module Federation host and remote</title>
<desc id="np-fed-d">A host application dynamically imports modules exposed by a checkout remote's remoteEntry.js, while both resolve shared dependencies through a common share scope.</desc>
<defs><marker id="np-fed-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="currentColor" fill-opacity="0.55"/></marker></defs>
<style>.np-fed .b{fill:currentColor;fill-opacity:0.05;stroke:currentColor;stroke-opacity:0.35}.np-fed .t{fill:currentColor;font-weight:600;font-size:15px}.np-fed .m{fill:currentColor;fill-opacity:0.8;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:11px}.np-fed .s{fill:currentColor;fill-opacity:0.6;font-size:11px}.np-fed .l{stroke:currentColor;stroke-opacity:0.5}.np-fed .div{stroke:currentColor;stroke-opacity:0.2}</style>
<g class="np-fed">
<rect class="b" x="40" y="50" width="250" height="120" rx="12"/>
<rect class="b" x="590" y="40" width="250" height="160" rx="12" style="stroke:#14b8a6;stroke-opacity:0.85"/>
<rect x="300" y="212" width="280" height="72" rx="12" fill="#8b5cf6" fill-opacity="0.09" stroke="#8b5cf6" stroke-opacity="0.8"/>
<text class="t" x="165" y="84" text-anchor="middle">Host application</text>
<text class="m" x="165" y="112" text-anchor="middle">import("checkout/CheckoutForm")</text>
<text class="s" x="165" y="140" text-anchor="middle">loads remoteEntry.js at runtime</text>
<text class="t" x="715" y="72" text-anchor="middle" fill="#14b8a6">checkout remote</text>
<text class="m" x="715" y="97" text-anchor="middle">remoteEntry.js</text>
<line class="div" x1="606" y1="110" x2="824" y2="110"/>
<text class="s" x="715" y="130" text-anchor="middle">exposes</text>
<text class="m" x="715" y="151" text-anchor="middle">./CheckoutForm</text>
<text class="m" x="715" y="172" text-anchor="middle">./useCart</text>
<text class="t" x="440" y="242" text-anchor="middle" font-size="13">Shared scope (default)</text>
<text class="s" x="440" y="264" text-anchor="middle">react · react-dom — singleton, version-negotiated</text>
<line class="l" x1="292" y1="92" x2="588" y2="92" marker-end="url(#np-fed-arrow)"/>
<text class="m" x="440" y="82" text-anchor="middle" fill-opacity="0.6">dynamic import()</text>
<line class="l" x1="150" y1="172" x2="360" y2="216" marker-end="url(#np-fed-arrow)"/>
<line class="l" x1="720" y1="202" x2="512" y2="216" marker-end="url(#np-fed-arrow)"/>
</g>
</svg>

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

Set `"kind": "native"` to emit a plain-ESM **native-federation** remote from the
exact same `federation.json` entry, instead of a Module Federation container.
See [Native Federation](./native-federation.md) for the details.

## Combining with shared React etc.

`shared` in `federation.json` governs cross-remote sharing at the Module
Federation runtime level. If you *also* want the host page itself to load
React once via an import map (independent of federation), reach for
`--shared` on the host's own entry point instead — see
[Import maps & externals](./importmaps-and-externals.md). The two mechanisms
solve related but distinct problems: one is "don't duplicate this dependency
across federated remotes at runtime", the other is "don't duplicate this
dependency across bundles on one page".
