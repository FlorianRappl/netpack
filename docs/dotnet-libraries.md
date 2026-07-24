# Using netpack from .NET

netpack is written in C#/.NET, so besides the [CLI](./getting-started.md) it's
also available as an embeddable engine on NuGet. Two packages cover the common
scenarios:

| Package | What it is | Reach for it when… |
| --- | --- | --- |
| `netpack` (npm) | the native CLI | you bundle from the terminal or npm scripts |
| [`NetPack.Core`](./netpack-core.md) | the bundler engine as a managed library | you want to bundle *programmatically* — from an app, a tool, or tests — or use the TypeScript/JSX parser directly |
| [`NetPack.Build`](./netpack-build.md) | MSBuild integration built on the core | you want your web assets bundled into `wwwroot` as part of `dotnet build`/`publish` (e.g. an ASP.NET Core app) |

Both packages target **.NET 8** and are trim/AOT-friendly. The core has a single
managed dependency (AngleSharp); Node.js is only needed for the optional
Sass/Less/PostCSS/Svelte features (plain JS/TS/JSX/CSS/HTML bundling is entirely
native).

## Where to next

- **[NetPack.Core](./netpack-core.md)** — bundle programmatically with the
  `Bundler` facade, the full options reference, using the parser/printer directly,
  and plugging in your own asset processing.
- **[NetPack.Build](./netpack-build.md)** — enable bundling into `wwwroot` on
  `dotnet build`, the `Netpack*` MSBuild options, and the built-in cross-platform
  image processing.
