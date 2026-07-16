# Overview

Usage docs for netpack, in addition to the feature overview in the
[project README](../README.md).

## General

- **[Getting started](./getting-started.md)** — install, entry points, and
  the `bundle` / `serve` / `analyze` commands.

## Use cases

- **[React & JSX](./react-and-jsx.md)** — the default `React.createElement`
  factory, retargeting it project-wide or per file, and React Fast Refresh
  in the dev server.
- **[Vue single-file components](./vue.md)** — native `.vue` compilation:
  `<script setup>` and its macros, build-time template precompilation, scoped
  styles, and the runtime-compiler fallback.
- **[Module Federation](./module-federation.md)** — exposing and consuming
  federated modules via a `federation.json` entry point.
- **[Native Federation](./native-federation.md)** — the same `federation.json`
  with `"kind": "native"`, emitting a plain-ESM remote whose shared deps are
  wired up through an import map.
- **[Styling & assets](./styling-and-assets.md)** — CSS, CSS Modules, and
  Sass/LESS/PostCSS (incl. Tailwind).
- **[Images & assets](./images-and-assets.md)** — importing images and other
  files, the SkiaSharp-based optimization pass, content hashing, and the
  `public/` folder.

## Advanced

- **[Import maps & externals](./importmaps-and-externals.md)** — leaving a
  dependency out of the bundle with `--external`, or having netpack build
  and wire it up itself with `--shared`.
- **[Build-time code generation](./codegen.md)** — `.codegen` files: the
  loader context, async support, and the current watch-mode limitations.
- **[Other features](./other-features.md)** — tree shaking, source maps,
  minification, and the bundle analyzer.

This content is written to also serve as the source material for the
project's docs site (`www/docs`) — it's kept as plain, self-contained
Markdown for that reason. The site groups these same docs into the same
three sections in its sidebar (see `www/docs/src/lib/docs.ts`).
