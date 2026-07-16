# Astro components

netpack compiles `.astro` single-file components natively — no Node round-trip.
A `.astro` file has two parts: a `---`-fenced **frontmatter** (plain JS/TS,
executed on every render) and a **template** (JSX-like markup) below it. The
whole file compiles down to a JS module with one export:

```js
export default async function render(props, slots) {
  // ...frontmatter...
  return html`...`;
}
```

Importing a `.astro` file gets you that `render` function directly — call it
yourself (`await render(props, slots)`) to get the rendered HTML back as a
string. netpack doesn't (yet) execute this itself at build time to produce a
static `.html` file; see [Scope](#scope) below.

## The frontmatter

Everything between the two `---` lines is parsed as an ordinary module body —
imports, exports, top-level `await`, functions, classes, TypeScript types, or
any other statement:

```astro
---
import Layout from "./Layout.astro";

const title = "Hello";
const posts = await getPosts();

function greet(name) {
  return `Hello ${name}`;
}
---

<Layout title={title}>
  <h1>{greet("World")}</h1>
</Layout>
```

Imports are hoisted to the compiled module's top level, so `import Layout from
'./Layout.astro'` is resolved exactly like any other import — this is how
`.astro` files import and use each other as components. Everything else in
the frontmatter (the `const`s, the function, the top-level `await`) is moved
into the generated `render` function's body, so it re-runs on every call
rather than once at module load.

A file doesn't need frontmatter at all — a template-only `.astro` file (no
leading `---` block) is perfectly valid.

## The template

The template is parsed **as JSX**, not as HTML. This is deliberate: JSX is
case-sensitive in exactly the way Astro's own syntax needs — `<Layout>`
(capitalized) is a reference to the imported `Layout` component, while
`<div>` (lowercase) is a literal HTML element. An HTML parser would silently
lowercase `<Layout>` to `<layout>`, losing that distinction entirely. Since
the template is a genuine JSX parse, it follows JSX's own rules, not lenient
HTML5 ones — most notably, void elements need an explicit self-close
(`<img />`, not `<img>`), same as in React.

### Expressions

`{expr}` works in text and attribute position, evaluated fresh on every
render:

```astro
<h1>{title}</h1>
<img src={imageUrl} width={200} />
```

Values are HTML-escaped by default — a `title` containing `<` or `&`
renders safely rather than being interpreted as markup.

### Components

A capitalized (or dotted, e.g. `Astro.Self`-style) tag is a component
reference — the imported binding is called directly as a function (an
imported `.astro` file's default export *is* its `render` function), awaited,
and its already-rendered output is inlined without being re-escaped:

```astro
<Layout title={title}>
  <h1>Hello</h1>
</Layout>
```

A component's children become `slots.default` — a single pre-rendered HTML
value the component can interpolate whenever it likes. Only the default slot
is supported; there's no named-slot syntax yet.

### Attributes

```astro
<input disabled />
<div class={active ? "on" : "off"} />
<div {...rest} />
```

A bare attribute (`disabled`) renders as a boolean HTML attribute. An
expression attribute (`class={...}`) is escaped and interpolated. A spread
(`{...rest}`) expands an object into `key="value"` pairs at render time —
`null`/`undefined`/`false` entries are dropped, and `true` renders the key
alone (matching how JSX/React treat boolean props).

### Directives

Attribute names with a colon — `client:load`, `client:visible`, `set:html`,
and so on — parse fine (JSX has always supported namespaced attribute names
like `xlink:href`), but none of them carry special meaning yet. They're
emitted as inert, literal HTML attributes. There is no hydration/islands
runtime in netpack today, so a `client:*` directive doesn't cause anything to
ship to the client beyond the attribute itself.

## Scope

What's implemented: frontmatter execution, component composition, expressions,
attributes (including spread and boolean), and default-by-escaped /
opt-in-raw HTML output.

What's deliberately not (yet):

- **No build-time static HTML generation.** netpack compiles `.astro` files
  to importable `render` functions; actually calling one to produce a static
  page is left to you (or a future netpack feature) rather than happening
  automatically during `bundle`.
- **No hydration/islands.** `client:*` directives are inert, as noted above.
- **No `<style>`/`<script>` blocks inside the template.** Unlike
  [Vue SFCs](./vue.md), which get real block-splitting and scoped styles,
  a `.astro` file's own `<style>`/`<script>` tags are stripped during
  compilation rather than given special handling.
- **No named slots** — only `slots.default`.
- **No `Astro.*` global** (`Astro.props`, `Astro.request`, etc.) — `props`
  and `slots` are the generated function's own parameters instead.

## How rendering composes safely

Every compiled `.astro` module gets a small inline runtime (a handful of
lines, private to that module — nothing shared or importable). Its `html`
tag function escapes each interpolated value by default; a nested component
call's output is a marked "already HTML" value that's inlined as-is instead
of being escaped a second time. This is what lets `<Layout><h1>{title}</h1>
</Layout>` compose correctly: `title` gets escaped, `Layout`'s own rendered
markup doesn't.
