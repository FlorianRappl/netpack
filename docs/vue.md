# Vue single-file components

netpack compiles Vue single-file components (`.vue`) **natively** — there is no
Node round-trip through `@vue/compiler-sfc`. AngleSharp (already used for HTML)
splits the file into its top-level blocks, and a native compiler turns them into
a virtual JavaScript module. Templates are precompiled to render functions at
build time, so your app does **not** need a compiler-included Vue build.

Nothing to configure: importing a `.vue` file just works.

```js
// main.js
import { createApp } from 'vue';
import App from './App.vue';

createApp(App).mount('#app');
```

## The blocks

A `.vue` file is parsed into its top-level `<template>`, `<script>`,
`<script setup>` and `<style>` blocks (one template and script each; any number
of styles). Every block understands a `src` attribute, which loads its contents
from another file (resolved relative to the `.vue` file):

```html
<template src="./app.html" />
<script src="./app.js" />
<style src="./app.css" />
```

`<script>` (and `<script setup>`) may be written in TypeScript with `lang="ts"`;
the types are erased the same way `.ts` files are.

## The `<script>` block

A classic `<script>` block's `default` export becomes the component. netpack
rebinds it to a local so it can attach the template and styles, keeping every
other statement (imports, helpers) verbatim:

```html
<!-- in: -->
<script>
import { defineComponent } from 'vue';
export default defineComponent({ name: 'Hello' });
</script>
```

```js
// out (simplified):
import { defineComponent } from 'vue';
const __sfc_main = defineComponent({ name: 'Hello' });
__sfc_main.render = function (_ctx, _cache) { /* … */ };
export default __sfc_main;
```

## The `<script setup>` block

`<script setup>` is compiled to a `setup()` function. Imports are hoisted to
module scope, every top-level binding is returned so the template can see it,
and the compiler macros are expanded:

```html
<!-- in: -->
<script setup>
import { ref } from 'vue';
const props = defineProps({ msg: String });
const emit = defineEmits(['change']);
const count = ref(0);
function inc() { count.value++; emit('change'); }
</script>
```

```js
// out (simplified):
import { ref } from 'vue';
const __sfc_main = {
  props: { msg: String },
  emits: ['change'],
  setup(__props, __ctx) {
    const props = __props;
    const emit = __ctx.emit;
    const count = ref(0);
    function inc() { count.value++; emit('change'); }
    return { ref, props, emit, count, inc };
  }
};
export default __sfc_main;
```

The supported macros:

- **`defineProps(...)`** — hoisted to the `props` option; the call evaluates to `__props`.
- **`withDefaults(defineProps(...), { … })`** — merged into `props` (via a tiny generated `__mergeDefaults` helper).
- **`defineEmits(...)`** — hoisted to the `emits` option; the call evaluates to the emit function.
- **`defineExpose({ … })`** — becomes `__ctx.expose({ … })`.
- **`defineOptions({ … })`** — spread into the component options.

A classic `<script>` **alongside** `<script setup>` is allowed: its default
export contributes base options that the setup component is spread on top of
(`{ ...__sfc_base, setup() { … } }`).

## Template precompilation

The `<template>` is compiled to a render function built from Vue's public
runtime helpers (`h`, `toDisplayString`, `renderList`, …), imported under
`_vue_*` aliases. Template expressions are rewritten so free identifiers resolve
against the render context — `count` becomes `_ctx.count`, while `v-for` items,
inline arrow parameters and JavaScript globals (`Math`, `JSON`, `true`, …) are
left alone.

```html
<!-- in: -->
<template>
  <p v-if="count">{{ label }}: {{ count }}</p>
  <button @click="inc">+</button>
  <li v-for="item in items" :key="item.id">{{ item.name }}</li>
</template>
```

```js
// out (simplified body of render):
_ctx.count
  ? _vue_h("p", null, _vue_toDisplayString(_ctx.label) + ": " + _vue_toDisplayString(_ctx.count))
  : _vue_createCommentVNode("v-if", true),
_vue_h("button", { onClick: _ctx.inc }, "+"),
_vue_h(_vue_Fragment, null, _vue_renderList(_ctx.items, (item) =>
  _vue_h("li", { key: item.id }, _vue_toDisplayString(item.name))))
```

Supported in the render compiler:

- Text interpolation `{{ … }}`.
- `v-bind` / `:attr`, `v-on` / `@event`.
- `v-if` / `v-else-if` / `v-else`, `v-for`.
- `v-show`, `v-html`, `v-text`.
- `v-model` — both native form controls (via the `vModel*` directives) and components (`modelValue` + `onUpdate:modelValue`).
- `key`, `ref`, static and dynamic attributes.
- Components, default and named slots, and `<slot>` outlets.

### Graceful fallback

Anything outside that subset makes netpack **fall back to Vue's runtime
compiler** for that component: it attaches the raw template string as
`.template` instead of a precompiled `.render`. Your app keeps working (a
compiler-included Vue build is only needed for components that actually fall
back). Constructs that currently fall back: `v-on` / `v-bind` / `v-model`
modifiers (`@click.stop`, `:x.prop`, `v-model.trim`), custom directives, and
`<component :is="…">`.

### Component resolution

Component tags are resolved with `resolveComponent`, matching Vue's own runtime
semantics (it looks up the `components` option and globally registered
components, camelizing/capitalizing as needed). For `<script setup>`, imported
components used by the template are **auto-registered** into a `components`
option so `resolveComponent` finds them.

Because the template is parsed as HTML, tag names are lowercased — so **use
kebab-case for child components** in templates (`<my-widget>`, not
`<MyWidget>`). `resolveComponent("my-widget")` still resolves a component
imported/registered as `MyWidget`.

## The `<style>` blocks

Each `<style>` block's CSS is injected at runtime via a `<style>` element.

- **`scoped`** — netpack computes a `data-v-*` scope id, rewrites the block's
  selectors to require it (`.box` → `.box[data-v-1a2b3c]`) and sets the
  component's `__scopeId`, so the runtime stamps the attribute onto the elements
  it renders.
- **`lang="scss" | "sass" | "less"`** — preprocessed with the same pipeline as
  standalone stylesheets (when the corresponding tool is enabled via
  `package.json`; see [Styling & assets](./styling-and-assets.md)).
- **`src`** — loads the CSS from a file.

## Requirements & limits

- `vue` must be available to the bundle — either bundled normally, or left
  external (see [Import maps & externals](./importmaps-and-externals.md)) when
  you load it from a CDN.
- Because templates are precompiled, a runtime-compiler Vue build is only needed
  for components that fall back to runtime compilation.
- Not yet supported: `defineModel`, type-only props (`defineProps<T>()` with no
  runtime argument produces empty props), and `<style module>`.
