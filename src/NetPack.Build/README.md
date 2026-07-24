<p align="center">
  <a href="https://netpack.anglevisions.com"><img src="https://raw.githubusercontent.com/FlorianRappl/netpack/main/art/icon.png" alt="netpack" width="120" height="120" /></a>
</p>

# NetPack.Build

**Bundle your web assets as part of the .NET build.**

**[Website](https://netpack.anglevisions.com)** · **[Documentation](https://netpack.anglevisions.com/docs/netpack-build/)** · **[Source](https://github.com/FlorianRappl/netpack)** · **[CLI on npm](https://www.npmjs.com/package/netpack)**

This package wires [netpack](https://netpack.anglevisions.com) into MSBuild: on
`dotnet build` (or `publish`) it bundles and optimizes your JavaScript /
TypeScript / JSX / CSS / HTML entry point straight into `wwwroot` — ideal for
ASP.NET Core apps.

It's built on `NetPack.Core` and ships a **cross-platform, pure-managed image
processor** (ImageSharp) — no SkiaSharp, no native/OS dependency. Everything the
task needs is bundled, so it adds no package references to your project.

## Install

```sh
dotnet add package NetPack.Build
```

## Use

Point it at an entry file and build:

```xml
<PropertyGroup>
  <NetpackEntry>ClientApp/src/index.html</NetpackEntry>
</PropertyGroup>
```

```sh
dotnet build
# -> wwwroot/index.html, wwwroot/index.js, wwwroot/styles.css, …
```

That's it — bundling runs after `Build` whenever `NetpackEntry` is set.

## Options

All are MSBuild properties with sensible defaults:

| Property | Default | Purpose |
| --- | --- | --- |
| `NetpackEntry` | — | entry file (enables the integration when set) |
| `NetpackOutputDirectory` | `$(MSBuildProjectDirectory)/wwwroot` | output folder |
| `NetpackMinify` | `true` | minify + tree-shake |
| `NetpackSourceMaps` | `false` | emit source maps |
| `NetpackFormat` | `esm` | `esm` / `cjs` / `umd` / `systemjs` |
| `NetpackPlatform` | `web` | `web` / `node` / `deno` |
| `NetpackEntryNames` | `[name]` | naming template, e.g. `[name]-[hash]` |
| `NetpackPublicPath` | — | base URL/path for emitted-file references |

Keep dependencies external with an item group:

```xml
<ItemGroup>
  <NetpackExternal Include="react" />
  <NetpackExternal Include="react-dom" />
</ItemGroup>
```

## Notes

- **No native dependency.** Image resizing/re-encoding uses ImageSharp (Apache-2.0,
  the 2.1.x line). Formats it doesn't cover (`.avif`, `.ico`) are copied as-is.
- **Build with the .NET SDK.** The task targets .NET 8, so build via
  `dotnet build` / `dotnet msbuild` (the SDK's .NET-based MSBuild).
- **Node.js** is only needed for the optional Sass/Less/PostCSS/Svelte features;
  plain JS/TS/JSX/CSS/HTML bundling needs nothing extra.
- Prefer the API directly? Use [`NetPack.Core`](https://www.nuget.org/packages/NetPack.Core).
  Prefer a CLI? `npm i -D netpack`.

MIT licensed — <https://github.com/FlorianRappl/netpack>.
