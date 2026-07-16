# Images & assets

Anything imported that isn't JS/TS, CSS, HTML or JSON is treated as an
asset: content-hashed, copied to the output directory, and handed back to
your code as a real URL. Images additionally get an optimization pass,
and can be resized and/or re-encoded into on-demand variants based on how
they're used. None of this needs configuration.

## Importing an asset

```js
import logoUrl from './logo.png';
```

```js
// what that compiles to, roughly:
const logoUrl = new URL('./logo.a1b2c3.png', import.meta.url).href;
```

`logoUrl` is a real string, usable directly as `<img src={logoUrl}>`. It's
resolved **at runtime, relative to the importing module's own URL** rather
than baked in as an absolute path — so it keeps working regardless of
where the final build ends up deployed (a subpath, a CDN, whatever). This
works for any file type netpack doesn't otherwise understand, not just
images — fonts, video, PDFs, and so on all import the same way.

## Content hashing

The emitted file name is `<original-name>.<hash><ext>` — e.g. `logo.png`
becomes `logo.a1b2c3.png`. The hash is SHA-256 over the file's contents,
truncated to the first 6 hex characters: deterministic, and it changes
whenever the file's contents change, so emitted assets are safe to cache
aggressively.

Files copied from a `public/` folder (see below) are the one exception —
they keep their original name, unhashed.

## Image optimization

Whenever a build is optimized — `--minify` on `bundle`/`serve`, or
`analyze` (which always optimizes) — netpack re-encodes images through
[SkiaSharp](https://github.com/mono/SkiaSharp) as part of processing them.
Today that covers:

- **Optimized**: `.png`, `.jpg`/`.jpeg`, `.webp`, `.gif`, `.bmp`, `.exif`.
- **Not (yet) optimized**: `.avif` and `.ico` currently pass through
  unmodified, copied like any other generic asset.
- **SVG is never touched** — it's always a generic hashed asset (copied
  byte-for-byte). netpack doesn't rasterize it or do anything
  SVGR-style (importing it as a component); if you want that, you'd bring
  your own tooling for it today.

Worth setting expectations correctly: on its own, this pass is a
**re-encode at a fixed quality (90) in the same format** — not a resizing
or format-conversion pipeline. For that, see the next section: variants
are always resized/re-encoded on demand, independent of `--minify`.

## Image variants

Beyond the blanket optimization pass above, netpack can produce a
**resized and/or re-encoded variant** of an image, generated on demand
from how it's actually used — no config file, no separate CLI flag. Three
places trigger it:

- **HTML** — `width`/`height` attributes on an `<img>`:

  ```html
  <img src="./logo.png" width="200" height="100">
  ```

- **CSS** — a `background-size` declared in the same rule as a
  `background-image`/`background` that uses `url(...)`:

  ```css
  .logo {
    background-image: url('./logo.png');
    background-size: 200px 100px;
  }
  ```

  Only literal `px` values are understood here. Keywords (`cover`,
  `contain`) and relative units (`%`, `vw`, …) can't be resolved to an
  absolute pixel size at build time, so a rule using those keeps the
  image at its original size.

- **JS/TS** — query parameters on the import specifier itself:

  ```js
  import logoUrl from './logo.png?width=200&height=100';
  ```

  The query string is irrelevant for locating the file (it's stripped
  before resolution) but tells the asset pipeline which variant to
  produce. This also works on an `<img src="...">` or CSS `url(...)`
  directly, for the same reason.

In every case, **omitting one dimension scales the other automatically**,
preserving the source image's aspect ratio — `width="200"` alone on an
`<img>` behaves like the browser's own single-attribute scaling. Giving
both dimensions uses them exactly as specified (no aspect-ratio
correction).

### Switching the output format

The same query-parameter mechanism also accepts `format`, independent of
width/height:

```js
import heroWebp from './hero.png?format=webp';
import heroSmallJpeg from './hero.png?width=400&format=jpeg';
```

Recognized values: **`png`**, **`jpg`**/**`jpeg`**, **`webp`**, **`gif`**,
**`bmp`**. An unrecognized value is ignored (treated as if no `format`
were given) rather than failing the build. The output file's extension
follows the requested format, so `./hero.png?format=webp` emits a
`hero.<hash>.webp` — a genuine `.png` → `.webp` conversion, not just a
renamed copy.

As with plain optimization, format conversion is only available for the
source types netpack already knows how to decode (`.png`, `.jpg`/`.jpeg`,
`.webp`, `.gif`, `.bmp`, `.exif`) — `.avif`/`.ico` sources and SVG are
unaffected by `?format=`/`?width=`/`?height=`, same as in plain
optimization above. `avif` and `ico` also aren't offered as **target**
formats, since encoder support for them is less reliably available than
for the five formats above.

### How variants are named and deduplicated

A variant is content-hashed like any other asset, but the hash also folds
in the requested width/height/format — so `logo.png`, `logo.png?width=200`,
and `logo.png?width=200&format=webp` each get their own distinct output
file (`logo.a1b2c3.png`, `logo.d4e5f6.png`, `logo.7a8b9c.webp`, …) instead
of colliding. Requesting the exact same variant from multiple places in
the app reuses the one generated file rather than duplicating it.

## No inlining threshold

Unlike Vite or webpack's asset modules, netpack never inlines a small
asset as a base64 data URI — every import always becomes a real emitted
file plus a URL, regardless of size. If you specifically want a data URI
for something tiny, you'd construct that yourself rather than relying on
an automatic threshold.

## JSON

Not an "asset" in the sense above, but the same "just works" spirit
applies:

```js
import config from './config.json';
```

Imported directly as a parsed module — no plugin required.

## Static files: `public/`

For an HTML entry point, a `public/` folder next to it is copied verbatim
into the output directory — the same convention as Vite/Parcel/CRA, useful
for a `favicon.ico`, `robots.txt`, or anything else that shouldn't go
through the bundler (and shouldn't get a content hash) at all.
