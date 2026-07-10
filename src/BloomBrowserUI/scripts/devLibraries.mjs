/* eslint-env node */
/* global process */

// Registry of the libraries we maintain in separate repos but develop alongside Bloom.
//
// Each entry describes how one library is consumed, and how `./go.sh --with <name>[=<path>]`
// links a LOCAL checkout of it for live development. Two kinds:
//
//   "bundled"    – imported into Bloom's own Vite bundle (by bare package name). Default:
//                  resolved from node_modules as installed. Linked: Bloom's vite.config adds
//                  a resolve.alias to the checkout (see BLOOM_LINKED_LIBS in vite.config.mts)
//                  and go.mjs runs the checkout's watch-build(s) as managed children so the
//                  alias target keeps rebuilding.
//
//   "iframe-app" – a standalone Vite app Bloom loads in an <iframe> (bloom-ai-image-tools).
//                  Default: copy the prebuilt dist-app/ out of the installed package into
//                  output/browser/. Linked: run the library's own Vite dev server and point
//                  Bloom at it via an env var (true HMR).
//
// Path notes: `checkoutCandidates` are relative to the Bloom repo root; `installedDistApp`
// is relative to src/BloomBrowserUI (where node_modules lives); `stageTarget`, `extraCopy.to`
// are relative to the repo root; `aliasTo`, `extraCopy.from` are relative to the checkout.

import fs from "node:fs";
import path from "node:path";

export const DEV_LIBRARIES = [
    {
        name: "bloom-ai-image-tools",
        kind: "iframe-app",
        checkoutEnv: "BLOOM_AI_IMAGE_TOOLS_DIR",
        checkoutCandidates: [
            "../bloom-ai-image-tools.worktrees/V2",
            "../../bloom-ai-image-tools.worktrees/V2",
            "../bloom-ai-image-tools",
            "../../bloom-ai-image-tools",
        ],
        // DEFAULT (a): the prebuilt app shipped in the installed package's dist-app/.
        installedDistApp: "node_modules/bloom-ai-image-tools/dist-app",
        stageTarget: "output/browser/aiImageEditor",
        // LINKED (b): the library's own Vite dev server (vite-plus). go.mjs reads the
        // chosen URL back from this env var and hands it to Bloom.
        devCommand: "pnpm dev",
        devUrlEnv: "BLOOM_AI_EDITOR_URL",
    },
    {
        name: "bloom-player",
        kind: "bundled",
        checkoutCandidates: ["../bloom-player", "../../bloom-player"],
        // Bare `import ... from "bloom-player"` resolves via the checkout's package.json
        // "module" field (lib/shared.es.js), so we alias the package to the checkout root.
        aliasTo: ".",
        // Two outputs: lib/ (what Bloom imports) and dist/ (the standalone player, copied
        // into output/browser/bloom-player/dist). Watch-build both.
        watchCommands: [
            "pnpm exec vite build --mode lib --watch",
            "pnpm exec vite build --watch",
        ],
        extraCopy: { from: "dist", to: "output/browser/bloom-player/dist" },
    },
    {
        name: "@sillsdev/config-r",
        kind: "bundled",
        checkoutCandidates: ["../config-r", "../../config-r"],
        aliasTo: ".",
        // build:dev = `tsc && vite build --mode development ... --watch` -> dist/configr.es.js
        watchCommands: ["yarn build:dev"],
    },
    // NOTE: we also consume a custom react-grid-layout fork (via a GitHub dependency),
    // but no entry is registered for it: no local checkout layout or watch command has
    // ever been verified. Whoever first links the fork for live development should add
    // (and test) an entry here.
];

// Look up a registry entry by package name (e.g. "bloom-player", "@sillsdev/config-r").
export const getLibrary = (name) =>
    DEV_LIBRARIES.find((lib) => lib.name === name);

// All valid --with names, for error messages.
export const libraryNames = () => DEV_LIBRARIES.map((lib) => lib.name);

// Resolve the local checkout directory for a library: an explicit path wins, then the
// entry's env-var override, then the sibling-checkout candidates. Returns an absolute path
// or null if none has a package.json.
export const resolveCheckoutDir = (entry, repoRoot, explicitPath) => {
    const candidates = [];
    if (explicitPath) {
        candidates.push(path.resolve(explicitPath));
    }
    if (entry.checkoutEnv && process.env[entry.checkoutEnv]) {
        candidates.push(path.resolve(process.env[entry.checkoutEnv]));
    }
    for (const rel of entry.checkoutCandidates ?? []) {
        candidates.push(path.resolve(repoRoot, rel));
    }
    for (const dir of candidates) {
        if (fs.existsSync(path.join(dir, "package.json"))) {
            return dir;
        }
    }
    return null;
};
