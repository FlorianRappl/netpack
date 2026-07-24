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
- **[Astro components](./astro.md)** — native `.astro` compilation:
  frontmatter execution, the JSX-parsed template, components and slots, and
  the current scope (no hydration, no build-time static HTML generation yet).
- **[Svelte components](./svelte.md)** — `.svelte` compilation via the Svelte
  compiler over the Node bridge (requires `svelte` installed), with runtime
  style injection.
- **[Module Federation](./module-federation.md)** — exposing and consuming
  federated modules via a `federation.json` entry point.
- **[Native Federation](./native-federation.md)** — the same `federation.json`
  with `"kind": "native"`, emitting a plain-ESM remote whose shared deps are
  wired up through an import map.
- **[Styling & assets](./styling-and-assets.md)** — CSS, CSS Modules, and
  Sass/LESS/PostCSS (incl. Tailwind).
- **[Images & assets](./images-and-assets.md)** — importing images and other
  files, the SkiaSharp-based optimization pass, on-demand resized/re-encoded
  image variants, content hashing, and the `public/` folder.

## Advanced

- **[Import maps & externals](./importmaps-and-externals.md)** — leaving a
  dependency out of the bundle with `--external`, or having netpack build
  and wire it up itself with `--shared`.
- **[Build-time code generation](./codegen.md)** — `.codegen` files: the
  loader context, async support, and the current watch-mode limitations.
- **[Output formats](./output-formats.md)** — emitting ESM (default),
  CommonJS, UMD or SystemJS with `--format`, their trade-offs, and why ESM is
  the best choice.
- **[Platforms](./platforms.md)** — targeting web, Node or Deno with
  `--platform`: which modules stay external as runtime built-ins and how entry
  points are chosen.
- **[Other features](./other-features.md)** — tree shaking, source maps,
  minification, and the bundle analyzer.

## .NET

- **[Using netpack from .NET](./dotnet-libraries.md)** — overview of the two
  NuGet packages and when to reach for each.
- **[NetPack.Core](./netpack-core.md)** — the bundler engine as a managed
  library: bundle programmatically, or use the TypeScript/JSX parser/printer.
- **[NetPack.Build](./netpack-build.md)** — MSBuild integration: bundle into
  `wwwroot` on `dotnet build`, with cross-platform image processing.

This content is written to also serve as the source material for the
project's docs site (`www/docs`) — it's kept as plain, self-contained
Markdown for that reason. The site groups these same docs into the same
sections in its sidebar (see `www/docs/src/lib/docs.ts`).
