# Styling & assets

Everything below works with zero configuration — netpack detects what a
file needs from its extension (and, for CSS preprocessing, from what's
installed) rather than requiring a config file.

## CSS

Importing a `.css` file — from JS/TS or with a `<link>` in HTML — bundles it
into a CSS output file and, for a JS/TS import, injects the styles at
runtime via a small generated module.

```js
import './app.css';
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
packages. Everything else in this document (plain CSS, CSS Modules, images,
JSON, `public/`) has no such dependency. Practically, this means `sass`,
`less` or `postcss` (plus a PostCSS config, for Tailwind) need to be
installed in your project — and Node.js available — the moment you import
a file that needs them.

## Images & other assets

Importing an image gives you back its final URL, and netpack optimizes the
image (via SkiaSharp) as part of the build:

```js
import logoUrl from './logo.png';
```

Any other file type netpack doesn't specifically understand is still
handled as a generic asset: copied to the output with a content hash in its
name, with the import resolving to that final URL.

## JSON

```js
import config from './config.json';
```

Imported directly as a parsed module — no plugin required.

## Static files: `public/`

For an HTML entry point, a `public/` folder next to it is copied verbatim
into the output directory — the same convention as Vite/Parcel/CRA, useful
for a `favicon.ico`, `robots.txt`, or anything else that shouldn't go
through the bundler at all.
