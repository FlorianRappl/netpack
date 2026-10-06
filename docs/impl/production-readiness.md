# Production readiness: corpus harness & CJS-consumption audit

Internal notes tracking the work toward trusting netpack with serious projects.
Two things live here: the real-world bundling **corpus** (how to run it, what it
proves) and a **CommonJS-consumption audit** sizing the largest known ecosystem
gap. Not part of the published docs site (the loader skips `docs/impl/`).

## Corpus harness

Hand-written fixture tests exercise constructs we thought of; they can't catch the
parser/printer/minifier choking on code that actually ships on npm. The lucide
tree-shaking report (a barrel of ~1,800 `export … from` lines dragging every icon
into the bundle) is the archetype: every individual piece had a passing unit test,
but no test bundled a real package.

`./corpus.sh` assembles `./test-corpus` — one throwaway project with a spread of
real dependencies and one tiny entry per package that imports something real:

| Package | Why it's in the set |
| --- | --- |
| `react`, `react-dom` | large CommonJS with `__esModule` interop; JSX consumers |
| `lucide-react` | ESM barrel of ~1,800 re-exports (tree-shaking at scale) |
| `lodash` | big CommonJS module graph (parser + CJS stress) |
| `lodash-es` | ESM counterpart (named-export tree-shaking) |
| `date-fns` | many small ESM modules |
| `zod` | modern TypeScript-authored ESM |
| `classnames` | tiny ubiquitous CommonJS module |

`CorpusTests` bundles each entry with **minification on** (the hardest path —
mangler, tree-shaker and JS lowering all run) and asserts every emitted bundle
**re-parses with zero diagnostics**. That is the `NETPACK_VERIFY` idea turned into
a gate over real inputs.

```bash
./corpus.sh                                    # assemble (npm install) into ./test-corpus
dotnet test src/NetPack.Tests/NetPack.Tests.csproj --filter Corpus
```

The corpus is **not committed** (`node_modules` is large and churns) and is
git-ignored. When `./test-corpus` is absent the corpus test **skips**, so the
normal suite stays green without it; CI assembles it first. Override the location
with `NETPACK_CORPUS=/path`.

**Scope / what it does not do.** It validates *static* output correctness — the
emitted JavaScript is valid and re-parseable. It does **not** execute the bundles,
so it doesn't catch missing browser polyfills.

## Execution tests

`ExecutionTests` closes part of that gap without any corpus: it bundles small
fixtures in-process via `Bundler.WriteToDirectoryAsync` (targeting Node) and runs
the emitted entry under `node`, asserting on stdout. This proves the output
*executes*, not just parses — ESM-import linking, CommonJS `require`/default
interop, circular dependencies (the pre-cached-exports runtime), and ESM-format
output are all checked at runtime. The tests skip when `node` isn't on PATH, so the
suite still runs without it.

`BrowserExecutionTests` is the browser counterpart: it bundles a web target as UMD,
loads it as a classic `<script>` inside jsdom, and asserts the entry manipulated the
document (and that an imported helper linked and ran). jsdom is heavy, so it rides
along with the opt-in corpus — `./corpus.sh` installs it as a devDependency — and
the test skips (locating `test-corpus/node_modules` via `NETPACK_CORPUS` or an
upward search, and setting `NODE_PATH`) when the corpus isn't assembled.

Next candidates to add once green: a heavy CJS app (e.g. a build using
`readable-stream`/`buffer`), a Vue SFC app, and a couple of TypeScript-heavy
packages with decorators.

## CommonJS-consumption audit

How netpack consumes CJS dependencies today, and where it will bite.

**What works (and is correct).** Each module is wrapped in a
`(module, exports, require)` factory and registered by id; the `__r` runtime
(`Bundles/JsRuntime.cs`) caches a module's `exports` object *before* running its
factory (correct circular-dependency behaviour) and applies default interop:

```js
if (e && (typeof e == "object" || typeof e == "function") && e.default === void 0) e.default = e;
```

So the common shapes all resolve correctly:

- `module.exports = …` and `exports.foo = …` (including `Object.defineProperty`).
- `import D from 'cjs'` → the whole `module.exports` (via the `.default` fallback).
- `import { foo } from 'cjs'`, `import * as ns from 'cjs'`.
- Transpiled-ESM packages (`__esModule: true` with a real `.default`) — interop
  leaves their `.default` intact.
- Mixed ESM/CJS graphs (per-module wrapping) and `require()` with a string literal
  (discovered in `JsVisitor.VisitCallExpression`, lowered to `__r(id)`).
- CJS modules are never tree-shaken (`require` marks the whole target used in
  `ExportUsage`), which is the safe choice.

**Gaps, roughly in order of how often they bite:**

1. **Dynamic / computed `require(expr)`.** Only a single string-literal argument is
   discovered (`JsVisitor`). `require('./' + name)`, template-literal requires and
   conditional requires are not bundled; at runtime the target id isn't in the
   registry, so `__r(...)` returns undefined and the module throws. This is the
   most common real-world CJS failure and there's currently no build-time
   diagnostic — it fails silently at runtime.
2. **No Node built-in polyfills for the browser.** `process`, `Buffer`, `util`,
   `stream`, etc. aren't shimmed on the web platform (`Platforms/`), so any CJS
   package assuming Node globals breaks in the browser. `process.env.NODE_ENV`
   happens to fold via `--define`, but the rest don't.
3. **`browser` field — object form is ignored.** `Dependency.GetEntry` honours the
   *string* form (`"browser": "./browser.js"` main replacement) but not the object
   map (`"browser": { "./node.js": "./browser.js", "fs": false }`) that many CJS
   libraries use to swap Node implementations out for browser ones and to stub
   modules to `false`.
4. **`require.resolve`, `require.cache`, `module.require`, `typeof require`
   guards** are not recognised.
5. **`__dirname` / `__filename`** are undefined in browser bundles; packages that
   reference them break.

Suggested sequence: (1) emit a build-time warning for an unresolved dynamic
`require`/`import` so failures stop being silent; (2) implement the `browser`
object-map substitution (localized to `Dependency`); (3) add an opt-in set of
browser polyfills for the common Node built-ins. Each is independently shippable
and testable, and the corpus is where their regressions get caught.
