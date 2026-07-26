# Solid components

Solid's JSX is not a `createElement`-style factory call like React or Preact —
it's a whole compile-time transform (`dom-expressions`) that turns markup into
fine-grained DOM operations and reactive updates. That transform ships as the
official `babel-preset-solid`, so rather than reimplement it, netpack drives the
real thing over the same Node bridge it uses for Sass, LESS, PostCSS and Svelte.

```sh
npm i solid-js
npm i -D @babel/core babel-preset-solid
```

```jsx
// App.jsx
import { createSignal } from 'solid-js';

export default function App() {
  const [count, setCount] = createSignal(0);
  return <button onClick={() => setCount(count() + 1)}>{count()}</button>;
}
```

```js
// main.jsx
import { render } from 'solid-js/web';
import App from './App';

render(() => <App />, document.getElementById('app'));
```

## How it works

netpack decides a project is a Solid project when `solid-js` is a dependency and
`react` is not. In that mode, every `.jsx`/`.tsx` file is sent to the Node bridge
before parsing:

1. **Compile.** The Node side runs `@babel/core` with `babel-preset-solid`
   (plus `@babel/preset-typescript` for `.tsx`, to parse and strip the types).
   The JSX becomes Solid's template/`insert`/`effect`/`createComponent` output —
   plain JavaScript with no JSX left.
2. **Runtime.** That output imports Solid's runtime from `solid-js` /
   `solid-js/web`. Those imports resolve from your `node_modules` and are bundled
   normally, so the runtime is shared across all components.
3. **Bundle.** The compiled module flows through the normal pipeline — resolution,
   tree-shaking, minification, output formats, source maps, and so on — like any
   other JavaScript module.

Because netpack's own `createElement` JSX lowering never runs in Solid mode, there
is no `React`/`Preact` auto-import and no factory retargeting; the whole JSX story
is handed to Solid's compiler.

## Requirements & limitations

- **`@babel/core` and `babel-preset-solid` must be installed** and resolvable from
  the project (`@babel/preset-typescript` too, if you use `.tsx`). Like Svelte,
  this is a Node round-trip — the compiler *is* the framework.
- **Detection is by dependency.** Solid mode turns on when `solid-js` is present
  and `react` is not. A project that needs both React and Solid at once isn't
  supported; React wins.
- **Applies to `.jsx`/`.tsx`.** Only these extensions are routed through the Solid
  transform; plain `.js`/`.ts` files are bundled as-is.
- **Client-side only.** Components compile for the browser; Solid's SSR /
  hydration transform options are not wired up.
- **No dedicated hot reload.** Edits rebuild the module like any other (the dev
  server reloads); `solid-refresh` is not integrated.
