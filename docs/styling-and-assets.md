# Styling & assets

Everything below works with zero configuration — netpack detects what a
file needs from its extension (and, for CSS preprocessing, from what's
installed) rather than requiring a config file.

## CSS Ordering

CSS files imported from JavaScript are emitted in the order their importing
modules appear in the dependency graph — the cascade consistently matches
runtime execution across builds.

<svg viewBox="0 0 860 292" role="img" aria-labelledby="np-css-t np-css-d" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:auto;max-width:840px;font-family:ui-sans-serif,system-ui,sans-serif">
<title id="np-css-t">CSS output order follows JS evaluation order</title>
<desc id="np-css-d">The order CSS files are imported in JavaScript becomes their order in the output cascade, via each module's post-order index.</desc>
<defs><marker id="np-css-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="currentColor" fill-opacity="0.55"/></marker></defs>
<style>.np-css .p{fill:currentColor;fill-opacity:0.03;stroke:currentColor;stroke-opacity:0.2}.np-css .b{fill:currentColor;fill-opacity:0.06;stroke:currentColor;stroke-opacity:0.3}.np-css .hd{fill:currentColor;font-weight:600;font-size:14px}.np-css .hm{fill:currentColor;font-weight:600;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:14px}.np-css .m{fill:currentColor;fill-opacity:0.85;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px}.np-css .lb{fill:currentColor;font-weight:600;font-size:12.5px}.np-css .s{fill:currentColor;fill-opacity:0.7;font-size:11.5px}.np-css .c{fill:currentColor;fill-opacity:0.75;font-size:12px}.np-css .l{stroke:currentColor;stroke-opacity:0.5}.np-css .dv{stroke:currentColor;stroke-opacity:0.18}.np-css .bg{fill:#14b8a6;fill-opacity:0.16;stroke:#14b8a6;stroke-opacity:0.7}.np-css .bn{fill:currentColor;font-weight:600;font-size:11px}</style>
<g class="np-css">
<rect class="p" x="40" y="56" width="330" height="190" rx="12"/>
<text class="hm" x="205" y="88" text-anchor="middle">app.js</text>
<line class="dv" x1="56" y1="100" x2="354" y2="100"/>
<circle class="bg" cx="74" cy="128" r="12"/><text class="bn" x="74" y="132" text-anchor="middle">1</text>
<circle class="bg" cx="74" cy="164" r="12"/><text class="bn" x="74" y="168" text-anchor="middle">2</text>
<circle class="bg" cx="74" cy="200" r="12"/><text class="bn" x="74" y="204" text-anchor="middle">3</text>
<text class="m" x="96" y="132">import "shared.css"</text>
<text class="m" x="96" y="168">import "b.css"</text>
<text class="m" x="96" y="204">import "c.css"</text>
<line class="l" x1="372" y1="150" x2="486" y2="150" marker-end="url(#np-css-arrow)"/>
<text class="s" x="429" y="140" text-anchor="middle">post-order</text>
<rect class="p" x="490" y="56" width="330" height="190" rx="12"/>
<text class="hd" x="655" y="88" text-anchor="middle">output cascade</text>
<line class="dv" x1="506" y1="100" x2="814" y2="100"/>
<rect class="b" x="520" y="112" width="256" height="36" rx="6"/>
<rect class="b" x="520" y="154" width="256" height="36" rx="6"/>
<rect class="b" x="520" y="196" width="256" height="36" rx="6"/>
<circle class="bg" cx="546" cy="130" r="12"/><text class="bn" x="546" y="134" text-anchor="middle">1</text>
<circle class="bg" cx="546" cy="172" r="12"/><text class="bn" x="546" y="176" text-anchor="middle">2</text>
<circle class="bg" cx="546" cy="214" r="12"/><text class="bn" x="546" y="218" text-anchor="middle">3</text>
<text class="lb" x="572" y="135">shared.css</text>
<text class="lb" x="572" y="177">b.css</text>
<text class="lb" x="572" y="219">c.css</text>
<line class="l" x1="800" y1="118" x2="800" y2="226" marker-end="url(#np-css-arrow)"/>
<text class="s" x="822" y="176" text-anchor="middle" transform="rotate(90 822 176)">later wins</text>
<text class="c" x="430" y="274" text-anchor="middle">CSS is emitted in JS-evaluation order (the post-order index) — stable across every build.</text>
</g>
</svg>

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
