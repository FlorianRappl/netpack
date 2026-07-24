# NetPack.Build

[`NetPack.Build`](https://www.nuget.org/packages/NetPack.Build) wires netpack into
MSBuild: on `dotnet build` (or `publish`) it bundles your web entry point straight
into `wwwroot`. It's ideal for ASP.NET Core apps. See
[Using netpack from .NET](./dotnet-libraries.md) for how it compares to the CLI
and to the [`NetPack.Core`](./netpack-core.md) library.

It's built on `NetPack.Core` and ships a **pure-managed, cross-platform image
processor** (ImageSharp) — no SkiaSharp, no native/OS dependency. Everything the
task needs is bundled, so it adds no package references to your project.

## Install and enable

```sh
dotnet add package NetPack.Build
```

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

Bundling runs after `Build` whenever `NetpackEntry` is set; it's a no-op
otherwise, so the package is safe to reference from projects that don't use it.

## Options

All options are MSBuild properties with sensible defaults:

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

Relative paths (`NetpackEntry`, `NetpackOutputDirectory`) are resolved against the
project directory.

## Externals

Keep dependencies out of the bundle with an item group:

```xml
<ItemGroup>
  <NetpackExternal Include="react" />
  <NetpackExternal Include="react-dom" />
</ItemGroup>
```

## Image processing

Image resizing and re-encoding use ImageSharp — pure-managed and cross-platform,
with no native/OS dependency. Formats it doesn't cover (`.avif`, `.ico`) are
copied as-is. If you need those, use the [`NetPack.Core`](./netpack-core.md)
library and register your own `IAssetProcessor`.

## Build with the .NET SDK

The task targets .NET 8, so build via `dotnet build` / `dotnet msbuild` (the
SDK's .NET-based MSBuild). Node.js is only needed for the optional
Sass/Less/PostCSS/Svelte features; plain JS/TS/JSX/CSS/HTML bundling needs
nothing extra.
