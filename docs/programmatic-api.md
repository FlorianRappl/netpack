# Programmatic use (Node.js)

Besides the [CLI](./getting-started.md), the `netpack` npm package exposes a
small, fully-typed API for driving the bundler from a Node.js program — handy in
build scripts and custom tooling.

> Prefer bundling **in-process** from .NET (ASP.NET, MSBuild, custom tools)? Use
> the [.NET libraries](./dotnet-libraries.md) instead — no binary spawn.

## The `netpack` object

`netpack` mirrors the commands, with typed options and a Promise result. Commands
that produce data (`graph`, `analyze`) resolve with the parsed JSON; the others
resolve once the process exits (and reject on a non-zero exit code).

```js
import { netpack } from "netpack";

// Production build
await netpack.bundle("src/index.html", { minify: true, entryNames: "[name]-[hash]" });

// Inspect the dependency graph as JSON
const { graph } = await netpack.graph("src/index.html");

// Analyze the bundle, getting the analysis data back
const { analysis } = await netpack.analyze("src/index.html");
```

The available functions are `bundle`, `serve`, `graph`, `analyze`, and `inspect`.
Options are typed per command and mirror the CLI flags (camelCase, so
`--entry-names` becomes `entryNames`, `--public-path` becomes `publicPath`, and so
on).

## Long-running commands

`serve` (and `bundle` with `watch: true`) run until stopped. Pass an
`AbortSignal` to control their lifetime:

```js
import { netpack } from "netpack";

const controller = new AbortController();
netpack.serve("src/index.html", { port: 3000, signal: controller.signal });

// …later, to stop the dev server:
controller.abort();
```

## Low-level `run`

For full control there's the lower-level `run`, which spawns the binary with raw
arguments and returns the child process. Its stdio is inherited, so the bundler's
output appears in your terminal:

```js
import { run } from "netpack";

run(["bundle", "src/index.html", "--minify"]);
run("bundle", { minify: true }); // command + options form
```

The `netpack` object is built on top of `run`, so anything the CLI can do is
reachable either way.
