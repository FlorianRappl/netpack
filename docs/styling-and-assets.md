# Styling & assets

Everything below works with zero configuration — netpack detects what a
file needs from its extension (and, for CSS preprocessing, from what's
installed) rather than requiring a config file.

## CSS Ordering

CSS files imported from JavaScript are emitted in the order their importing
modules appear in the dependency graph — the cascade consistently matches
runtime execution across builds.

### How it works

During graph traversal each module receives a **post-order index** after its
children resolve, and imports are processed **sequentially** in declaration
order (not in parallel) so indices reflect source position exactly. When a
JS module imports multiple CSS files the CSS nodes inherit the relative
order of their importers through these indices.

This means:

```js
import './b.css';   // b.css appears first in output
import './c.css';   // c.css appears second
```

The bundle factory registry is sorted by post-order index so the runtime
injects styles in the same order the JS evaluated them, preserving the
intended cascade.

### Shared CSS chunks

When the same CSS file is imported by multiple entry bundles it is extracted
into a shared chunk (`common.NNNNN.css`). The order of shared chunks in
the HTML `<link>` tags follows the post-order of the first importing bundle,
producing a stable cross-entry cascade.

### CSS Modules

CSS module imports follow the same ordering as regular CSS imports —
the class-name maps are exported and injected in evaluation order.

### Conflict detection

A CSS file that appears before another in one entry but after it in another
creates an **ordering conflict** — the two modules have different relative
positions across chunk groups and the cascade cannot satisfy both at once.

netpack detects these conflicts during the build and warns on stderr:

```
[netpack] warning: Conflicting CSS order between shared.css and a.css.
These modules appear in different orders across chunk groups and the
output cascade may differ from source order.
```

No warning means the computed ordering is consistent across all entries.

### Debug output

Set `NETPACK_DEBUG_CSS_ORDER=1` to list every CSS file in computed
evaluation order:

```sh
NETPACK_DEBUG_CSS_ORDER=1 npx netpack bundle src/index.html
```

```
[netpack] CSS module order (by JS evaluation):
  1: shared.css
  2: a.css
  3: b.css
```

## CSS Modules

Whether a CSS import is treated as a **CSS module** depends on how you
import it, not the file name:

```js
import './app.css';           // plain global CSS — nothing hashed
import styles from './app.module.css'; // named/default binding — CSS module
```

Any import with named or default bindings (not just a bare side-effecting
import) marks that CSS file as a module: its class selectors get hashed, and
the generated JS module exports the original → hashed class name mapping,
so:

```jsx
import styles from './app.css';
// styles.button -> "button_a1b2c3"
<button className={styles.button}>Go</button>
```

## Sass / LESS / PostCSS (incl. Tailwind)

Import a `.scss`/`.sass` or `.less` file the same way as `.css` — netpack
detects the preprocessor from the extension and compiles it before the
usual CSS-module/bundling step. PostCSS (and, through it, Tailwind) is
picked up automatically when your project has a PostCSS config present;
no separate flag needed.

These three are the one place the otherwise-native, no-runtime netpack
binary reaches out to Node: preprocessing is delegated to a small
long-lived Node helper process that calls the real `sass`/`less`/`postcss`
packages. Everything else in this document (plain CSS, CSS Modules) has no
such dependency. Practically, this means `sass`, `less` or `postcss` (plus
a PostCSS config, for Tailwind) need to be installed in your project — and
Node.js available — the moment you import a file that needs them.

## Images, other assets and `public/`

Covered in full in [Images & assets](./images-and-assets.md) — importing an
image or any other non-CSS/JSON file, the SkiaSharp-based optimization pass,
content hashing, and the `public/` folder convention for files that should
bypass the bundler entirely.

## JSON

```js
import config from './config.json';
```

Imported directly as a parsed module — no plugin required.
