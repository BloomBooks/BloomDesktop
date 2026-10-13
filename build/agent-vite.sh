#!/usr/bin/env bash
#
# agent-vite.sh -- run a Vite production build of the web UI (src/BloomBrowserUI)
# WITHOUT colliding with a developer's running Bloom, Vite dev server, or watch.
#
# Why this exists:
#   Agents shouldn't run the full `pnpm build`: it runs clean.js (which wipes the shared
#   output\browser) and then a full build, disrupting a Bloom that is running against that
#   folder and any running dev server / `vite build --watch`. But you still sometimes need
#   to confirm the REAL production bundle compiles -- rollup bundling and CommonJS-interop
#   errors and the manifest post-build step that the lenient dev server never exercises.
#
#   This wrapper sets BLOOM_UI_OUTDIR so vite.config.mts redirects the whole build into a
#   private per-terminal tree under output/agent/<key>/browser, never touching the shared
#   output\browser. It is the front-end twin of build/agent-dotnet.sh.
#
#   Like the C# wrapper this is BUILD-ONLY: it confirms the bundle compiles. It does NOT
#   let a running Bloom load these bundles (Bloom reads the fixed output\browser / dev
#   server). The config also skips the pug/LESS/markdown/static-copy steps in this mode,
#   so it is a fast pure-bundle check.
#
# Usage (no args needed; extra args are passed through to `vite build`):
#   build/agent-vite.sh
#   build/agent-vite.sh --logLevel info
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
UI_DIR="$REPO_ROOT/src/BloomBrowserUI"

# Stable per-terminal key, matching agent-dotnet.sh: every terminal running `claude` has
# its own CLAUDE_CODE_SESSION_ID; a plain shell falls back to its parent pid. Different
# keys => different scratch trees => concurrent agent builds never share an outDir.
KEY="${CLAUDE_CODE_SESSION_ID:-shell-$PPID}"

# Node/vite on Windows accept the forward-slash, drive-letter form cygpath -m yields,
# which avoids backslash-escaping headaches (same trick as agent-dotnet.sh).
if command -v cygpath >/dev/null 2>&1; then
    OUTDIR="$(cygpath -m "$REPO_ROOT")/output/agent/$KEY/browser"
else
    OUTDIR="$REPO_ROOT/output/agent/$KEY/browser"
fi

echo "[agent-vite] isolated UI build dir: $OUTDIR" >&2

cd "$UI_DIR"
# --emptyOutDir: outDir is outside the project root, so Vite will not empty it without
# this explicit confirmation; passing it keeps stale files from accumulating in the
# (already isolated) scratch tree between runs.
BLOOM_UI_OUTDIR="$OUTDIR" exec pnpm exec vite build --emptyOutDir --logLevel warn "$@"
