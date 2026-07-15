# netpack documentation

Usage docs for netpack, in addition to the feature overview in the
[project README](../README.md).

- **[Getting started](./getting-started.md)** — install, entry points, and
  the `bundle` / `serve` / `analyze` commands.
- **[Import maps & externals](./importmaps-and-externals.md)** — leaving a
  dependency out of the bundle with `--external`, or having netpack build
  and wire it up itself with `--shared`.
- **[Module Federation](./module-federation.md)** — exposing and consuming
  federated modules via a `federation.json` entry point.
- **[React & JSX](./react-and-jsx.md)** — the default `React.createElement`
  factory, retargeting it project-wide or per file, and React Fast Refresh
  in the dev server.
- **[Vue single-file components](./vue.md)** — native `.vue` compilation:
  `<script setup>` and its macros, build-time template precompilation, scoped
  styles, and the runtime-compiler fallback.
- **[Styling & assets](./styling-and-assets.md)** — CSS, CSS Modules,
  Sass/LESS/PostCSS (incl. Tailwind), images, JSON and the `public/` folder.
- **[Other features](./other-features.md)** — tree shaking, source maps,
  minification, the bundle analyzer, and build-time codegen files.

This content is written to also serve as the source material for the
project's docs site (`www/docs`, coming next) — it's kept as plain,
self-contained Markdown for that reason.
