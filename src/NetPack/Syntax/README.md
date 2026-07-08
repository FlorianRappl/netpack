# NetPack.Syntax — native JS/TS/JSX front-end

This directory contains NetPack's own JavaScript / TypeScript / JSX front-end.
It **replaces the external Acornima dependency** with an in-house tokenizer,
parser, AST, tree rewriter and code generator — matching esbuild's model where
one pipeline handles JS(X)/TS(X) all the way from source to printed output.

**Acornima has been removed** from `NetPack.csproj`. The whole bundler now runs
on this module: `Traverse` parses with `Parser`, `JsVisitor` collects
dependencies over the AST, `JsBundle` transforms it with `AstRewriter`, and
`JsPrinter` renders the result (replacing Acornima's `ToJsx()`).

## What is implemented (this iteration)

**Tokenizer** (`Tokenizer.cs`, `Token.cs`, `TokenKind.cs`, `Keywords.cs`,
`CharUtil.cs`) — a hand-written, allocation-light lexer:

- Full punctuator/operator set, including `?.`, `??`, `??=`, `**`, `**=`,
  `>>>`, optional-chaining and logical-assignment operators.
- Numeric literals: hex/octal/binary, `BigInt` (`n`), numeric separators
  (`1_000`), exponents, legacy octal detection.
- Strings with full escape cooking (`\n`, `\xNN`, `\uNNNN`, `\u{…}`, legacy
  octal, line continuations, lone surrogates).
- Template literals (`` `…` ``, `` `…${ ``, `}…${`, `}…` ``) with parser-driven
  continuation re-scans.
- Regex-vs-division disambiguation via the Acorn/esbuild previous-token
  heuristic, plus an explicit `ReScanAsRegex` hook.
- Comments, hashbang, all Unicode line terminators, `PrecededByNewLine`
  tracking for ASI, and identifier Unicode escapes.
- JS reserved words **and** TS contextual keywords (`type`, `interface`,
  `enum`, `satisfies`, `as`, `readonly`, `namespace`, `declare`, …), plus JSX
  scan modes (`ScanJsxText`, `ScanJsxIdentifier`).
- Tolerant: lexical errors become `Diagnostic`s instead of exceptions.

**AST** (`Ast/`) — a fresh, allocation-friendly node model with source spans and
a flat `NodeKind` discriminator for cheap `switch`/pattern matching. Covers
statements, the full expression grammar, ES module import/export forms, JSX,
and type-erased TypeScript declarations.

**Parser** (`Parser*.cs`) — a recursive-descent + precedence-climbing parser:

- Modules: every `import` / `export` shape (default, named, namespace,
  side-effect, `export * as`, `export … from`, `export default`).
- Statements: blocks, `var`/`let`/`const`, functions, classes (body captured
  as a balanced block for now), `if`/`for`/`for-in`/`for-of`/`while`,
  `try`/`catch`/`finally`, `switch`, `throw`, `break`/`continue`.
- Expressions: assignment, conditional, all binary/logical operators with
  correct precedence and `**` right-associativity, unary/update, optional
  chaining, `new`, tagged templates, spreads, arrow functions (including
  `async`), object/array literals, dynamic `import()` and `import.meta`.
- **TypeScript erasure**: type annotations, generics, `as`/`satisfies`,
  non-null `!`, parameter properties, and whole type-only declarations
  (`interface`, `type`, `declare …`) are consumed and dropped so the resulting
  tree is plain JavaScript — no `tsc` subprocess required.
- **JSX**: elements, fragments, member/namespaced names, attributes (string,
  expression container, spread) and children.

## Tests

```
dotnet test src/NetPack.Tests/NetPack.Tests.csproj
```

`TokenizerTests`, `ParserTests` and `PrinterTests` cover the lexer, the parser
surface (imports/exports/`require`/dynamic import, TS erasure, JSX, templates,
arrows, real class bodies), and the code generator (precedence-correct
parenthesization, minified output, print → parse → print round-trip stability).

## Pipeline

```
source ──Tokenizer──▶ tokens ──Parser──▶ AST (SourceFile)
                                          │
                    JsVisitor (AstRewriter, read-only) ──▶ dependencies + export names
                                          │
                    JsBundle transform (AstRewriter) ──▶ lowered module-system AST
                                          │
                                     JsPrinter ──▶ JavaScript
```

## Done

- Tokenizer, AST, recursive-descent parser (JS/TS/JSX).
- Real class bodies (fields, methods, accessors, `static` blocks, decorators),
  `for-in`/`for-of`, `do/while`, labeled statements.
- `AstRewriter` (in-place tree rewriter) powering both dependency collection
  and the bundler transform.
- `JsPrinter` code generator with pretty + whitespace-minified modes.
- `Mangler` — a scope-aware identifier minifier that shortens local bindings
  (globally-unique, capture-safe renaming) while preserving globals, property
  names and the module interface. Runs automatically on optimizing builds.
- **TypeScript `enum` lowering** — enums are emitted as the runtime IIFE form
  (numeric reverse-mapping + string members), including `const enum` and
  `export enum`.
- **Acornima removed**: `Traverse`, `JsVisitor`, `JsBundle`, `JsFragment` and
  `JsExternalFragment` all run on this module; the package references are gone.
- **Optimized bundle runtime** (`JsBundle` / `JsRuntime`): modules become
  `(module, exports, require) => { … }` factories in a compact registry keyed by
  small integer ids (`BundlerContext.GetModuleId`). A cache-before-run `require`
  gives correct circular-dependency semantics; ESM/CJS default interop is inlined.
- **Working hot-module replacement** (dev server): stable module ids across
  recompiles (`ModuleIdMap`), a dependency-graph-aware runtime that records
  importers and bubbles a changed module up to the nearest `module.hot.accept`
  boundary (re-running it, running dispose handlers, transferring `hot.data`),
  and a full-reload fallback. The dev server diffs each module's factory source
  between compiles and pushes only the changed factories over SSE; the injected
  client applies them via `globalThis.__netpack.apply`. Non-JS changes and
  module add/remove fall back to a reload.

## Next (opportunities)

1. **Cross-scope name reuse** — the mangler currently assigns globally-unique
   names; reusing names across sibling scopes (as esbuild does) shrinks output
   further.
2. **Dead-code elimination / tree shaking** — drop unreferenced module exports.
3. **TS `namespace` with runtime semantics** — currently erased; emit the IIFE
   form where a runtime value is produced.
4. **Source maps** — the printer tracks node spans; emit mappings.
5. **Drop the `tsc` path** entirely (the parser already strips types, so the
   `FeatureFlags.TypeScript` subprocess branch in `Traverse` is redundant).
