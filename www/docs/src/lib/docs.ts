import { getCollection, render } from 'astro:content';
import { site } from './site';

export interface DocSummary {
  id: string;
  title: string;
}

export interface DocGroup {
  label: string;
  docs: DocSummary[];
}

/** The id of the doc that becomes the site's index ("/docs/") and the
 *  pinned "Overview" link at the top of the sidebar (its title comes from
 *  docs/README.md's own H1, same as every other doc). */
export const INDEX_ID = 'README';

/**
 * Sidebar sections, in reading order, each listing its docs' ids in the
 * order they should appear within that section. A doc that exists in
 * docs/*.md but isn't listed here still shows up — appended to a trailing
 * "More" section — so a new file is never silently dropped, it just won't
 * have a considered home until you add it here.
 */
const NAV_GROUPS: { label: string; ids: string[] }[] = [
  {
    label: 'General',
    ids: ['getting-started'],
  },
  {
    label: 'Use cases',
    ids: [
      'react-and-jsx',
      'vue',
      'astro',
      'module-federation',
      'native-federation',
      'styling-and-assets',
      'images-and-assets',
    ],
  },
  {
    label: 'Advanced',
    ids: ['importmaps-and-externals', 'codegen', 'other-features'],
  },
];

function titleCase(id: string): string {
  return id
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

/**
 * The display title for a doc: its own first-level heading (so it never
 * has to be maintained twice), falling back to a title-cased version of
 * the file name for a doc that somehow has no H1.
 */
export function titleFromHeadings(headings: { depth: number; text: string }[], id: string): string {
  const h1 = headings.find((h) => h.depth === 1);
  return h1?.text ?? titleCase(id);
}

/** Every doc (including the index), each with a display title. */
async function getAllDocSummaries(): Promise<DocSummary[]> {
  const entries = await getCollection('docs');

  return Promise.all(
    entries.map(async (entry) => {
      const { headings } = await render(entry);
      return {
        id: entry.id,
        title: titleFromHeadings(headings, entry.id),
      } satisfies DocSummary;
    })
  );
}

/** The index doc (docs/README.md), for the pinned "Overview" sidebar link. */
export async function getIndexDoc(): Promise<DocSummary | undefined> {
  const all = await getAllDocSummaries();
  return all.find((doc) => doc.id === INDEX_ID);
}

/**
 * Every non-index doc, grouped into sidebar sections per {@link NAV_GROUPS}.
 * Anything not covered by NAV_GROUPS lands in a trailing "More" section
 * (sorted alphabetically) instead of being dropped — see the comment there.
 */
export async function getGroupedDocs(): Promise<DocGroup[]> {
  const all = await getAllDocSummaries();
  const byId = new Map(all.map((doc) => [doc.id, doc]));
  const placed = new Set<string>([INDEX_ID]);

  const groups = NAV_GROUPS.map(({ label, ids }): DocGroup => {
    const docs: DocSummary[] = [];
    for (const id of ids) {
      const doc = byId.get(id);
      if (doc) {
        docs.push(doc);
        placed.add(doc.id);
      }
    }
    return { label, docs };
  }).filter((group) => group.docs.length > 0);

  const rest = all
    .filter((doc) => !placed.has(doc.id))
    .sort((a, b) => a.id.localeCompare(b.id));

  if (rest.length > 0) {
    groups.push({ label: 'More', docs: rest });
  }

  return groups;
}

/**
 * Rewrites the `.md` cross-links inside docs/*.md (written that way on
 * purpose, so the files are still correctly clickable on GitHub) into
 * routes on this site.
 *
 * - `./other-doc.md` / `./other-doc.md#heading` → that doc's page here
 *   (`/docs/other-doc/...`).
 * - `../README.md` (or anything reaching outside docs/) → there's no page
 *   for it here, so it's sent to the file on GitHub instead.
 */
export function rewriteMdLinks(html: string): string {
  const prefix = '/docs';

  return html.replace(
    /href="(\.\.?\/)?([A-Za-z0-9_-]+)\.md(#[^"]*)?"/g,
    (_full, up: string | undefined, name: string, anchor = '') => {
      if (up === '../') {
        // Points outside docs/ (e.g. the project root README) — no page
        // for that here, so send it to the source file on GitHub.
        return `href="${site.repoUrl}/blob/main/${name}.md${anchor}" target="_blank" rel="noopener noreferrer"`;
      }

      const target = name === INDEX_ID ? '' : `/${name}`;
      return `href="${prefix}${target}${anchor}"`;
    }
  );
}
