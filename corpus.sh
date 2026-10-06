#!/usr/bin/env bash
#
# Assembles the real-world bundling corpus used by CorpusTests.
#
# It writes a single throwaway project under ./test-corpus with a spread of real
# npm packages (CJS and ESM, barrels, JSX, modern TS output) plus one tiny entry
# per package that imports something real from it. `dotnet test` then bundles each
# entry with minification and asserts the emitted JavaScript re-parses with zero
# diagnostics — i.e. the native parser/printer/minifier/tree-shaker survive code
# that actually ships on npm, not just hand-written fixtures.
#
# The corpus is intentionally NOT committed (node_modules is large and churns);
# run this once locally or in CI before the corpus tests. When ./test-corpus is
# absent the corpus tests skip, so the normal suite stays green without it.
#
# Usage:  ./corpus.sh            # assemble into ./test-corpus
#         NETPACK_CORPUS=/path ./corpus.sh   # assemble elsewhere

set -euo pipefail

ROOT="${NETPACK_CORPUS:-"$(cd "$(dirname "$0")" && pwd)/test-corpus"}"
ENTRIES="$ROOT/entries"

echo "Assembling corpus in $ROOT"
mkdir -p "$ENTRIES"

# A representative spread. Keep these reasonably sized but real:
#   react / react-dom  — large CJS with __esModule interop + JSX consumers
#   lucide-react       — ESM barrel of ~1800 re-exports (tree-shaking at scale)
#   lodash             — big CommonJS graph (parser + CJS stress)
#   lodash-es          — ESM counterpart (named-export tree-shaking)
#   date-fns           — many small ESM modules
#   zod                — modern TypeScript-authored ESM
#   classnames         — tiny ubiquitous CommonJS module
cat > "$ROOT/package.json" <<'JSON'
{
  "name": "netpack-corpus",
  "private": true,
  "version": "0.0.0",
  "dependencies": {
    "classnames": "^2.5.1",
    "date-fns": "^3.6.0",
    "lodash": "^4.17.21",
    "lodash-es": "^4.17.21",
    "lucide-react": "^0.400.0",
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "zod": "^3.23.8"
  },
  "devDependencies": {
    "jsdom": "^24.1.0"
  }
}
JSON

# One entry per package. Each imports a real export and uses it, so tree-shaking
# has a live root and lowering/minification actually run over the dependency.
cat > "$ENTRIES/react.tsx" <<'TSX'
import React, { useState } from "react";
export const App = () => {
  const [n, setN] = useState(0);
  return <button onClick={() => setN(n + 1)}>{n}</button>;
};
export default React;
TSX

cat > "$ENTRIES/react-dom.tsx" <<'TSX'
import { createRoot } from "react-dom/client";
export const mount = (el: Element) => createRoot(el);
TSX

cat > "$ENTRIES/lucide.tsx" <<'TSX'
import { ClockIcon } from "lucide-react";
export const Icon = ClockIcon;
TSX

cat > "$ENTRIES/lodash-cjs.js" <<'JS'
import _ from "lodash";
export const grouped = _.groupBy([1, 2, 3], (n) => n % 2);
JS

cat > "$ENTRIES/lodash-es.js" <<'JS'
import { debounce, chunk } from "lodash-es";
export const d = debounce(() => {}, 100);
export const c = chunk([1, 2, 3, 4], 2);
JS

cat > "$ENTRIES/date-fns.js" <<'JS'
import { format, addDays } from "date-fns";
export const when = format(addDays(new Date(), 3), "yyyy-MM-dd");
JS

cat > "$ENTRIES/zod.ts" <<'TS'
import { z } from "zod";
export const Schema = z.object({ id: z.number(), name: z.string() });
export type Model = z.infer<typeof Schema>;
TS

cat > "$ENTRIES/classnames.js" <<'JS'
import cx from "classnames";
export const cls = cx("a", { b: true }, ["c"]);
JS

echo "Installing dependencies (npm install)…"
( cd "$ROOT" && npm install --no-audit --no-fund --loglevel=error )

echo
echo "Corpus ready: $ROOT"
echo "Run the corpus tests with:  dotnet test src/NetPack.Tests/NetPack.Tests.csproj --filter Corpus"
