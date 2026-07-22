# Angular support — design & implementation notes

> **Status: deferred / not started.** This is an internal architecture note,
> not user documentation. It lives under `docs/impl/`, which is deliberately
> excluded from the docs website (the Astro content loader only publishes
> top-level `docs/*.md`; see `www/docs/src/content/config.ts`). Nothing here
> ships to users until Angular support is actually built.

This captures the analysis behind "what would it take for netpack to compile
Angular", so the reasoning isn't lost while the work is parked. One prerequisite
(conditional `exports` resolution) has already landed; everything else below is
still to do.

## TL;DR

Angular cannot be handled like Vue, Svelte or Astro. Those compile **per file**;
Angular's Ahead-of-Time (AOT / Ivy) compiler is a **whole-program, type-aware
TypeScript transformer**. netpack has no TypeScript program and no type checker
(it erases TS types natively), so the only realistic path is to **delegate all
Angular-specific compilation to `@angular/compiler-cli` over the Node bridge**
and let netpack do what it already does well — resolve, bundle, tree-shake,
minify, split, and emit — on the plain JS the Angular compiler produces. This is
the same division of labour esbuild and Vite use for Angular.

It is the heaviest framework integration on the roadmap, and it partly dilutes
netpack's "native speed" story because the Angular/TS compiler dominates build
time. That trade-off is inherent to Angular, not specific to netpack.

## Why Angular is different from every framework we already support

Everything netpack handles today is a **local** transform:

- **React / JSX** — lower a file's JSX in isolation.
- **Vue / Astro** — compile one SFC to one module (native C# compiler).
- **Svelte** — hand one `.svelte` file to `svelte.compile(source)` over the Node
  bridge.

Each is "source in, module out", independent of other files' types.

Angular inverts this:

- Component templates use Angular's own language (`*ngIf`, `*ngFor`, `[prop]`,
  `(event)`, `[(ngModel)]`, pipes `|`, template refs `#ref`, `<ng-content>`,
  structural directives) and compile to the **Ivy instruction set**
  (`ɵɵelementStart`, `ɵɵproperty`, `ɵɵtemplate`, …).
- Decorators (`@Component`, `@NgModule`, `@Injectable`, `@Directive`, `@Pipe`,
  `@Input`, `@Output`) are lowered into static class fields (`ɵcmp`, `ɵmod`,
  `ɵinj`, `ɵprov`, `ɵdir`).
- Doing this correctly **requires the TypeScript type checker**: to know which
  directives/pipes apply to a template, the types behind DI tokens, input/output
  types, `NgModule`/standalone `imports`, and (optionally) to type-check
  templates. The Angular compiler *is* a plugin over a TS `Program`; it runs
  during `program.emit()`. It is not a black box you can feed one file's text.

netpack, by contrast, **strips TypeScript types natively** — fast, but type-blind
— and doesn't even emit legacy decorator metadata. That is precisely the
machinery Angular depends on. The mismatch is fundamental.

### Why not a lighter path (JIT)?

Angular can run the compiler in the browser (JIT). This is a dead end for us:

- JIT needs `emitDecoratorMetadata` (`design:paramtypes`, which needs *types*)
  that netpack's erasing parser doesn't produce.
- It ships the compiler to the browser (large).
- Angular is removing JIT.

So AOT via the real Angular compiler is the only viable approach.

## The approach: delegate to `@angular/compiler-cli`

Reimplementing Angular's template compiler + DI + decorator lowering natively in
C# is not on the table — it's one of the largest, most type-entangled compilers
in the ecosystem and a moving target. Instead:

1. Run `@angular/compiler-cli` (ngtsc) over the project via the Node bridge,
   building an `NgtscProgram` (which wraps a TS `Program`) from the project's
   `tsconfig`.
2. Get plain emitted JS back — component classes with their templates compiled
   to Ivy instructions and decorators lowered, importing the Angular runtime.
3. Let netpack bundle that emitted JS normally.

netpack becomes an Angular-capable bundler by **orchestrating** the Angular
compiler, not by understanding Angular.

## What this requires from netpack

### Prerequisite — conditional `exports` resolution ✅ DONE

Angular ships as fesm2022 bundles addressed through `package.json` `exports`
maps (`@angular/core`, `@angular/common`, `@angular/platform-browser`, `rxjs`,
`tslib`, `zone.js`). Without conditional `exports` resolution the runtime can't
even resolve.

This is implemented (see `docs/platforms.md`, "Entry-point selection"):
- `PlatformTarget.Conditions` — per-platform condition sets (ESM-first;
  `require` omitted).
- `Dependency.HasExports` / `Dependency.ResolveExport(subpath, conditions)` —
  Node's algorithm: subpath maps, nested conditions, `"*"` wildcards, fallback
  arrays, `null` blocking.
- `Traverse.ResolveFromNodeModules` splits specifier → package + subpath and
  treats `exports` as authoritative.
- Tests in `NetPack.Tests/ExportsResolutionTests.cs` (including an Angular-style
  fesm manifest case).

Everything below is still outstanding.

### 1. A stateful Node bridge service

Every current bridge command (Sass, LESS, PostCSS, Svelte) is stateless
request → response. Angular needs a **persistent compiler service**: build the
`NgtscProgram` once, then answer per-file emit requests and file-change
invalidations. New lifecycle on `NodeJs.cs`, roughly:

- `angularInit(tsconfig)` — construct the program + compiler host.
- `angularEmit(file)` — return emitted JS for a source file (or emit-all to a
  map up front for the first cut).
- `angularInvalidate(file)` — for incremental dev rebuilds (later phase).

For a first cut, the simplest shape is a single `angularCompile(tsconfig)` that
returns **all** emitted files as a `{ path: jsCode }` map; netpack caches it and
serves modules from it during traversal. Tree-shaking prunes the unused
remainder.

### 2. A whole-program "Angular mode"

For Vue/Svelte only `.vue`/`.svelte` files are special; `.ts`/`.js` stay native.
For Angular the **`.ts` files *are* the components**, so `.ts` handling itself
changes: in an Angular project, `.ts` must route through the Angular compiler
instead of netpack's native TS stripping.

That means detecting an Angular project (presence of `angular.json` and/or an
`@angular/core` dependency) and flipping netpack into a mode where the compiler
drives compilation and netpack consumes its output. This is a bigger
architectural seam than any prior framework — a per-project mode, not a per-file
type. The entry point is typically `main.ts` (or `index.html` → `main.ts`); the
Angular compiler emits `main.js` and the component `.js` files, and netpack
bundles from the emitted `main.js`.

### 3. Resource hooks for templates & styles

Components use inline `template`/`styles` or external `templateUrl`/`styleUrls`,
and styles are frequently SCSS/LESS. The Angular compiler reads these through its
`readResource`/`transformResource` host hooks but does **not** preprocess SCSS by
default (the Angular *build system* provides that). netpack already has
Sass/LESS/PostCSS on the bridge; they'd need to be wired into the compiler's
resource pipeline. `ViewEncapsulation.Emulated` style scoping is handled by the
compiler itself — no work for us there.

For a first cut: support plain CSS/HTML resources only; add the SCSS/LESS hook in
a later phase.

### 4. `angular.json` build semantics (partial, later)

Real Angular apps expect a set of build behaviours the compiler alone doesn't do:
polyfills (`zone.js`), global `styles`, `assets` copying, `fileReplacements`
(e.g. `environment.ts` → `environment.prod.ts`), and lazy routes via
`loadChildren` dynamic imports. A first cut can ignore most of these and just
compile + bundle `main.ts`; honouring them is what makes netpack a drop-in
Angular builder.

## What netpack already has that carries over

Once the Angular compiler returns plain ES2022 JS (importing the Angular
runtime), the whole downstream pipeline is reusable with no Angular-specific
work:

- Module resolution & bundling (including the Angular runtime packages, now that
  `exports` resolution exists).
- Tree-shaking, the mangler, output formats.
- Code-splitting for lazy routes (Angular's router uses dynamic `import()`, which
  our dynamic-import splitting already handles — modulo Angular's specific
  expectations).
- `index.html` handling and asset optimization.
- Sass/LESS/PostCSS over the bridge (to be fed into the compiler's resource
  hooks).
- The native JS parser still parses the Ivy output for tree-shaking/mangling.

## Out of scope / genuinely hard

- Reimplementing the Angular template compiler natively (no).
- Full incremental + HMR integration with the Angular compiler's cross-file
  dependency tracking (advanced; first cut = full rebuild on change). Angular 19+
  has built-in template/style HMR that could be wired later.
- Surfacing template type-check diagnostics nicely.
- Zoneless mode, SSR/hydration, i18n, service worker (`ngsw`).

## Risks & trade-offs

- **Dilutes the speed thesis.** For an Angular app the Angular/TS compiler
  dominates build time; netpack's native-speed advantage is muted. Inherent to
  Angular (esbuild-based Angular builds spend their time there too). netpack would
  be "Angular-capable", not "Angular-fast".
- **Version fragility.** `@angular/compiler-cli`'s programmatic API
  (`NgtscProgram`, compiler host, resource hooks) is semi-public and shifts
  across majors. Needs a pinned support matrix and ongoing maintenance.
- **Requires Angular installed** (like Svelte, but heavier) — the value-add is
  compilation, so the toolchain must be present.
- **Effort profile is unusual:** less "write a compiler" (we delegate) and more
  build-system orchestration + a stateful bridge redesign. The hard part is the
  whole-program/stateful integration and incremental rebuilds, not cleverness.

## Phased plan

1. **Prerequisite — conditional `exports` resolution.** ✅ Done.
2. **Batch AOT.** Stateful bridge service builds the program from `tsconfig` and
   emits all JS to an in-memory map; netpack bundles from the emitted `main.js`.
   Scope: standalone components, AOT only, `bundle` command only, plain CSS/HTML
   resources. Goal: a hello-world Angular app builds and runs.
3. **Resources & styles.** Wire SCSS/LESS/PostCSS through the compiler's resource
   hooks; handle inline and external templates and styles.
4. **Project semantics.** Polyfills, global styles, assets, `fileReplacements`,
   lazy `loadChildren` splitting.
5. **Dev server.** Incremental re-emit + reload; later, Angular's own HMR.

## Open decisions (pin before building phase 2)

- **Target versions:** recommend standalone + Ivy, Angular v17+ only.
- **AOT only** for the first cut: yes.
- **Activation:** explicit "Angular mode"/flag vs auto-detection (angular.json /
  `@angular/core`). Leaning auto-detect with an override.
- **Compile granularity:** whole-program emit-all-to-map first (simpler), move to
  a stateful per-file `emit` cache when the dev server needs incrementality.

## References / prior art

- `@angular/compiler-cli` (ngtsc / `NgtscProgram`) — the whole-program AOT
  compiler this integration would drive.
- `@analogjs/vite-plugin-angular` — community Vite plugin; the clearest example
  of driving the Angular compiler from a non-Angular bundler.
- `@angular/build` (esbuild builder) — Angular's own esbuild integration; same
  "compiler produces JS, bundler bundles it" division of labour netpack would use.
- Node "Package entry points" (`exports`) spec — basis for the resolution
  prerequisite that already landed.
