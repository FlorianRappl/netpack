import { getCollection, render } from 'astro:content';
import { site } from './site';

export interface DocSummary {
  id: string;
  title: string;
}

/** The id of the doc that becomes the site's index ("/"). */
export const INDEX_ID = 'README';

/**
 * Deliberate reading order for the sidebar. Anything in docs/*.md that
 * isn't listed here still shows up — appended alphabetically after these —
 * so a new file is never silently dropped, it just won't have a considered
 * position until you add it here.
 */
const NAV_ORDER = [
  INDEX_ID,
  'getting-started',
  'importmaps-and-externals',
  'module-federation',
  'react-and-jsx',
  'vue',
  'styling-and-assets',
  'other-features',
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

/** All docs, each with a display title — see {@link titleFromHeadings}. */
export async function getAllDocs(): Promise<DocSummary[]> {
  const entries = await getCollection('docs');

  const withTitles = await Promise.all(
    entries.map(async (entry) => {
      const { headings } = await render(entry);
      return {
        id: entry.id,
        title: titleFromHeadings(headings, entry.id),
      } satisfies DocSummary;
    })
  );

  return sortDocs(withTitles);
}

export function sortDocs(docs: DocSummary[]): DocSummary[] {
  return [...docs].sort((a, b) => {
    const ai = NAV_ORDER.indexOf(a.id);
    const bi = NAV_ORDER.indexOf(b.id);

    if (ai === -1 && bi === -1) return a.id.localeCompare(b.id);
    if (ai === -1) return 1;
    if (bi === -1) return -1;
    return ai - bi;
  });
}

/**
 * Rewrites the `.md` cross-links inside docs/*.md (written that way on
 * purpose, so the files are still correctly clickable on GitHub) into
 * routes on this site.
 *
 * - `./other-doc.md` / `./other-doc.md#heading` → a relative link to that
 *   doc's page here, honouring the current page's depth in the site.
 * - `../README.md` (or anything reaching outside docs/) → there's no page
 *   for it here, so it's sent to the file on GitHub instead.
 *
 * `isIndexPage` should be true only for the page rendering docs/README.md
 * (served at the site root); every other doc is served one level deep.
 */
export function rewriteMdLinks(html: string, isIndexPage: boolean): string {
  const prefix = isIndexPage ? '' : '../';

  return html.replace(
    /href="(\.\.?\/)?([A-Za-z0-9_-]+)\.md(#[^"]*)?"/g,
    (_full, up: string | undefined, name: string, anchor = '') => {
      if (up === '../') {
        // Points outside docs/ (e.g. the project root README) — no page
        // for that here, so send it to the source file on GitHub.
        return `href="${site.repoUrl}/blob/main/${name}.md${anchor}" target="_blank" rel="noopener noreferrer"`;
      }

      const target = name === INDEX_ID ? '' : `${name}/`;
      return `href="${prefix}${target}${anchor}"`;
    }
  );
}
