import { defineConfig } from "astro/config";

// https://astro.build/config
export default defineConfig({
  site: "https://netpack.anglevisions.com/docs",
  base: "/docs",
  output: "static",
  compressHTML: true,

  // The docs collection is loaded from the repo-root `docs/` folder via the
  // Content Layer `glob()` loader (see src/content/config.ts) — that API is
  // stable behind this flag on Astro 4.x (default in Astro 5).
  experimental: {
    contentLayer: true,
  },

  markdown: {
    shikiConfig: {
      // Close to netpack's own near-black/violet palette, so code blocks
      // don't need much overriding in prose.css to fit the brand.
      theme: "vitesse-dark",
    },
  },
});
