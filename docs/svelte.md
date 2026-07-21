# Svelte components

Svelte's value is its **compiler** — a `.svelte` file is turned into a small,
imperative JavaScript component ahead of time. That compiler is written in
JavaScript and ships with Svelte itself, so rather than reimplement it, netpack
drives the real thing over the same Node bridge it uses for Sass, LESS and
PostCSS.

```sh
npm i -D svelte
```

```js
// main.js
import App from './App.svelte';
new App({ target: document.getElementById('app') });   // Svelte 4
// (Svelte 5: import { mount } from 'svelte'; mount(App, { target }))
```

## How it works

When netpack encounters a `.svelte` file it sends the source to the Node bridge,
which calls `svelte/compiler`'s `compile()` and returns the generated ES module.
That module is then parsed and bundled like any other JavaScript:

1. **Compile.** The Node side `require('svelte/compiler')` and compiles the
   component. netpack reads the installed Svelte version and picks the matching
   options (`generate: 'client'` on Svelte 5, `generate: 'dom'` on Svelte 3/4).
2. **Styles.** The compiler is invoked with `css: 'injected'`, so each
   component's `<style>` is added to the document at runtime by the component
   itself — no separate CSS file to wire up.
3. **Runtime.** The generated module imports Svelte's runtime (e.g.
   `svelte/internal/client`). Those imports resolve from your `node_modules` and
   are bundled normally, so Svelte's runtime is included just like any other
   dependency (and shared across all components).
4. **Bundle.** The emitted module (`export default Component`) flows through the
   normal pipeline — tree-shaking, minification, output formats, and so on.

Because the compilation is a Node round-trip, `svelte` must be installed in the
project. This is the one framework integration that requires it: unlike Vue or
Astro (which netpack compiles natively), Svelte has no separate compiler to
reimplement — the compiler *is* the framework.

## Requirements & limitations

- **`svelte` must be installed** and resolvable from the project. Both Svelte 4
  and Svelte 5 are supported; the compile options are chosen from the detected
  version.
- **Styles are injected at runtime** (`css: 'injected'`). Extracting component
  CSS to standalone files is a possible follow-up.
- **TypeScript / preprocessors.** `<script lang="ts">` and other
  `svelte-preprocess` features are not run — netpack calls the compiler directly,
  not through a preprocessor chain. Use plain JS in components for now.
- **Client-side only.** Components are compiled for the browser (`dom`/`client`);
  SSR (`generate: 'server'`) is not wired up.
- **No dedicated hot reload.** Edits rebuild the component like any other module
  (the dev server reloads); Svelte's HMR plugin is not integrated.
