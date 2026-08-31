// Enables this repo's git pre-commit hook by pointing core.hooksPath at the
// transitional dispatcher in .githooks/ (committed on every maintained branch).
//
// Background: during the yarn+husky 4 -> pnpm+vite-plus migration, different
// branches use different hook systems. The .githooks/pre-commit dispatcher routes,
// at commit time, to whichever checker the current branch ships -- this project's
// vite-plus checks (src/BloomBrowserUI/.vite-hooks/pre-commit) on pnpm branches, or
// husky's default hook on yarn branches -- and fails loudly if it finds neither.
// See .githooks/README.md.
//
// We do NOT use `vp config` to install hooks: the dispatcher runs
// .vite-hooks/pre-commit directly (it carries its own shebang and is committed
// mode 0755), so vite-plus's own hook runner is unnecessary. vite-plus remains the
// node/pnpm toolchain, just not the git-hook installer.
//
// core.hooksPath is set repo-wide on purpose: the dispatcher exists on every
// maintained branch, so the same value is correct for every worktree, and routing
// is decided per-branch by the dispatcher rather than per-worktree by git config.
//
// Runs from the package.json "prepare" script, so it executes on every install.

import { execSync } from "node:child_process";

const git = (args) =>
    execSync(`git ${args}`, { stdio: ["ignore", "pipe", "pipe"] })
        .toString()
        .trim();

// Bail quietly if we're not inside a git working tree (e.g. installed as a tarball).
try {
    git("rev-parse --is-inside-work-tree");
} catch {
    process.exit(0);
}

try {
    // Point git at the dispatcher, repo-wide.
    git("config core.hooksPath .githooks");

    // Remove any leftover per-worktree override from the earlier (pre-dispatcher)
    // approach, so it can't shadow the repo-wide value set above.
    try {
        git("config --worktree --unset core.hooksPath");
    } catch {
        // worktree config not enabled, or nothing worktree-scoped to remove
    }

    console.log(
        "[setup-git-hooks] pre-commit hook enabled (core.hooksPath -> .githooks).",
    );
} catch (e) {
    console.warn(
        `[setup-git-hooks] could not configure git hooks: ${e.message}. ` +
            "The pre-commit hook may not run; this does not block install.",
    );
}
