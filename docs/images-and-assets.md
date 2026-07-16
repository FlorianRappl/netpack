# Images & assets

Anything imported that isn't JS/TS, CSS, HTML or JSON is treated as an
asset: content-hashed, copied to the output directory, and handed back to
your code as a real URL. Images additionally get an optimization pass.
None of this needs configuration.

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

Worth setting expectations correctly: this is a **re-encode at a fixed
quality (90) in the same format**, not a resizing or format-conversion
pipeline. There's no width/height/quality option, and no way to request
e.g. a `.webp` output from a `.png` input yet — this lines up with
[netpack's own feature checklist](https://github.com/FlorianRappl/netpack#readme),
where "image/asset variants" is still listed as not yet implemented.

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
