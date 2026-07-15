import { defineCollection } from 'astro:content';
import { glob } from 'astro/loaders';
import { fileURLToPath } from 'node:url';

// The docs site has no content of its own — it renders the Markdown files
// that live in the repo-root `docs/` folder (one level above `www/`, two
// above this project), so that folder stays the single source of truth
// editors actually touch. Adding a new `docs/*.md` file makes it show up
// here automatically; see src/lib/docs.ts for how it gets ordered/titled.
const docsDir = fileURLToPath(new URL('../../../../docs', import.meta.url));

const docs = defineCollection({
  loader: glob({
    pattern: '**/*.md',
    base: docsDir,
    // Keep ids as the plain file name (no extension, case preserved) —
    // e.g. "getting-started", "README" — since src/lib/docs.ts and the
    // Markdown cross-links between docs both key off that.
    generateId: ({ entry }) => entry.replace(/\.md$/i, ''),
  }),
});

export const collections = { docs };
