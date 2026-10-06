#!/usr/bin/env node
/**
 * Rolls CHANGELOG.md on a published GitHub release: inserts a new
 * `## [version] — date` section (from the release notes) below `## [Unreleased]`,
 * resets Unreleased, and fixes the link references. Run by the release pipeline
 * (.github/workflows/publish.yml) — the CHANGELOG is never edited by hand.
 *
 * Input via env:
 *   RELEASE_TAG   e.g. "v0.9.0"      (required)
 *   RELEASE_DATE  ISO timestamp      (optional; defaults to now)
 *   RELEASE_NOTES the release body   (optional; falls back to the current
 *                                     Unreleased contents, then a placeholder)
 *   CHANGELOG     path               (optional; defaults to ./CHANGELOG.md)
 *
 * Idempotent: if the version is already present, it exits 0 without changes.
 */

import { readFileSync, writeFileSync } from "node:fs";

const file = process.env.CHANGELOG || "CHANGELOG.md";
const version = (process.env.RELEASE_TAG || "").trim().replace(/^v/, "");
const date = (process.env.RELEASE_DATE || new Date().toISOString()).slice(0, 10);
const repo = "https://github.com/FlorianRappl/netpack";

if (!version) {
  console.error("update-changelog: RELEASE_TAG is required.");
  process.exit(1);
}

const original = readFileSync(file, "utf8");

if (new RegExp(`^## \\[${escapeRegExp(version)}\\] `, "m").test(original)) {
  console.log(`update-changelog: ${version} already present; nothing to do.`);
  process.exit(0);
}

const marker = "## [Unreleased]";
const markerAt = original.indexOf(marker);
if (markerAt < 0) {
  console.error("update-changelog: no '## [Unreleased]' section found.");
  process.exit(1);
}

// The Unreleased section spans from the heading to the next version heading (or
// the link-reference block at the bottom, whichever comes first).
const bodyStart = markerAt + marker.length;
const nextVersionAt = indexOrEnd(original, /\n## \[\d/, bodyStart);
const linksAt = indexOrEnd(original, /\n\[Unreleased\]:/, bodyStart);
const sectionEnd = Math.min(nextVersionAt, linksAt);

const unreleasedBody = original.slice(bodyStart, sectionEnd).trim();
const releaseNotes = (process.env.RELEASE_NOTES || "").trim();
const body = demoteHeadings(releaseNotes || unreleasedBody) || "_No release notes provided._";

const previous = (original.match(/^## \[(\d+\.\d+\.\d+)\] /m) || [])[1] || null;

const head = original.slice(0, bodyStart);
const rest = original.slice(sectionEnd).replace(/^\n+/, "\n");
const newSection = `\n\n## [${version}] — ${date}\n\n${body}\n`;

let text = `${head}\n${newSection}${rest}`;
text = rewriteLinks(text, version, previous);

writeFileSync(file, text);
console.log(`update-changelog: inserted ${version} (${date}).`);

// -- helpers ---------------------------------------------------------------

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function indexOrEnd(text, regex, from) {
  const match = regex.exec(text.slice(from));
  return match ? from + match.index : text.length;
}

/** Demote GitHub-generated headings one level so they nest under the version H2. */
function demoteHeadings(markdown) {
  return markdown.replace(/^(#{1,5})\s/gm, "#$1 ");
}

/** Point [Unreleased] at ...HEAD from the new version, and add the version link. */
function rewriteLinks(text, version, previous) {
  const unreleasedLink = `[Unreleased]: ${repo}/compare/v${version}...HEAD`;
  const versionLink = previous
    ? `[${version}]: ${repo}/compare/v${previous}...v${version}`
    : `[${version}]: ${repo}/releases/tag/v${version}`;

  if (/^\[Unreleased\]:.*$/m.test(text)) {
    return text.replace(/^\[Unreleased\]:.*$/m, `${unreleasedLink}\n${versionLink}`);
  }

  // No link block yet — append one.
  return `${text.replace(/\s*$/, "")}\n\n${unreleasedLink}\n${versionLink}\n`;
}
