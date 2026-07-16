# Build-time code generation (`.codegen` files)

A file with a `.codegen` extension is executed at build time as a small
Node module, and whatever it returns becomes that module's JavaScript
source — which netpack then parses and bundles exactly like any other JS
module (imports, tree shaking, minification, all of it apply normally).

```js
// tokens.codegen
module.exports = function () {
  const tokens = require('./design-tokens.json');
  return `export default ${JSON.stringify(tokens)};`;
};
```

```js
import tokens from './tokens.codegen';
```

Like Sass/LESS/PostCSS, this runs through netpack's small long-lived Node
helper process — the one place the otherwise-native, no-runtime binary
reaches out to Node — so it needs Node.js available, and the module itself
can `require()` anything installed in your project.

## The loader context

Your exported function is called with `this` bound to a small
loader-style context, deliberately similar in shape to a webpack loader's:

```js
module.exports = function () {
  this.name;    // absolute path of the .codegen file being processed
  this.options; // always {} today — see below
  this.addDependency(); // present, but currently a no-op — see below
};
```

- **`this.name`** — the absolute file path of the `.codegen` file itself.
- **`this.options`** — always an empty object. There's currently no way to
  pass parameters into a codegen file from the importing side (no
  query-string config like `import x from './foo.codegen?opt=1'`, no
  companion config file) — every `.codegen` import is a bare import with
  no way to parameterize it externally.
- **`this.addDependency()`** — accepted for the call signature, but it's a
  no-op today: it doesn't register anything with the dev server's watcher.
  See the gotcha below.

## Return value

Return a string directly, or an object with a `value` string property —
both are accepted:

```js
module.exports = function () {
  return 'export default 42;';
};

// equivalently:
module.exports = function () {
  return { value: 'export default 42;' };
};
```

**Async is supported** — return (or resolve) a Promise and netpack awaits
it before treating the result as source:

```js
module.exports = async function () {
  const data = await fetch('https://example.com/config.json').then((r) => r.json());
  return `export default ${JSON.stringify(data)};`;
};
```

The generated source goes through the exact same pipeline as a regular JS
module, so it can `import`/`require` anything — including another
`.codegen` file. Circular imports are caught by the same general
protection every module gets; there's nothing codegen-specific to worry
about there.

## Gotcha: `addDependency()` doesn't drive rebuilds

Because `addDependency()` is currently a no-op, `netpack serve`'s watcher
only reacts to the `.codegen` file itself changing. If your codegen
function reads *other* files — a JSON file, a directory listing, anything
via `require()`/`fs` inside the function — editing **those** files will
not trigger a rebuild; only touching the `.codegen` file will. Keep this
in mind for anything beyond a static, self-contained transform: during
development you may need to save the `.codegen` file itself (even a
no-op whitespace edit) to pick up a change in something it reads.

## Gotcha: keep it defensive

An exception thrown inside a codegen function today isn't caught
gracefully by the Node helper process — it can affect not just that one
build but subsequent Sass/LESS/PostCSS/codegen calls in the same run.
Until this is hardened, wrap risky work in your own `try`/`catch` and
return a sensible fallback string rather than letting an error escape.

## A slightly bigger example

Generating a route manifest from a folder of pages, at build time:

```js
// routes.codegen
const fs = require('fs');
const path = require('path');

module.exports = function () {
  const pagesDir = path.join(__dirname, 'src/pages');
  const routes = fs
    .readdirSync(pagesDir)
    .filter((f) => f.endsWith('.tsx'))
    .map((f) => '/' + f.replace(/\.tsx$/, ''));

  return `export const routes = ${JSON.stringify(routes)};`;
};
```

```js
import { routes } from './routes.codegen';
```

Per the gotcha above, adding or removing a page file won't auto-rebuild
this in `serve` — re-save `routes.codegen` (or restart the dev server) to
pick up the change.
