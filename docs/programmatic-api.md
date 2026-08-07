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

### Reacting to (re)builds

`serve` and `bundle({ watch: true })` accept three callbacks so a script can
react to the build lifecycle — most usefully, to know when a rebuild has
finished after a file change:

```js
import { netpack } from "netpack";

const controller = new AbortController();
netpack.serve("src/index.html", {
  signal: controller.signal,
  onStart: () => console.log("build started…"),
  onBuild: ({ initial, durationMs }) =>
    console.log(initial ? "first build done" : `rebuilt in ${durationMs ?? "?"} ms`),
  onError: (err) => console.error("rebuild failed:", err.message),
});
```

- **`onStart()`** — a build or rebuild has begun.
- **`onBuild({ initial, durationMs })`** — a build finished successfully.
  `initial` is `false` for rebuilds triggered by a file change; `durationMs` is
  the reported build time when available.
- **`onError(error)`** — a rebuild failed. Under `serve`/`watch` the process
  keeps running, so more builds can still follow.

These are implemented purely in the npm layer by watching the binary's normal
stdout/stderr for its build markers — there's no extra IPC channel, and all
output still reaches your terminal. They're a no-op for one-shot `bundle` calls
except that `onBuild`/`onError` fire once for that single build.

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
