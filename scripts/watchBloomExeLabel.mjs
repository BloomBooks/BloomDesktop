import { execFileSync } from "node:child_process";
import path from "node:path";

export const formatStartupLabel = (repoLabel, branchName, isGitWorktree) => {
    if (!repoLabel) {
        return undefined;
    }

    if (!branchName) {
        return `/${repoLabel}/`;
    }

    if (!isGitWorktree) {
        return `/${branchName}/`;
    }

    return repoLabel === branchName
        ? `/${repoLabel}/`
        : `/${repoLabel} (${branchName})/`;
};

const tryGetGitOutput = (repoRoot, args) => {
    try {
        const output = execFileSync("git", args, {
            cwd: repoRoot,
            encoding: "utf8",
            stdio: ["ignore", "pipe", "ignore"],
        }).trim();

        return output || undefined;
    } catch {
        return undefined;
    }
};

const normalizeGitPath = (repoRoot, gitPath) =>
    path.resolve(repoRoot, gitPath).toLowerCase();

const isGitWorktree = (repoRoot) => {
    const gitDir = tryGetGitOutput(repoRoot, ["rev-parse", "--git-dir"]);
    const commonDir = tryGetGitOutput(repoRoot, [
        "rev-parse",
        "--git-common-dir",
    ]);

    if (!gitDir || !commonDir) {
        return false;
    }

    return (
        normalizeGitPath(repoRoot, gitDir) !==
        normalizeGitPath(repoRoot, commonDir)
    );
};

export const getHelpfulStartupLabel = (repoRoot) => {
    const resolvedRepoRoot = path.resolve(repoRoot);
    const repoLabel = path.basename(resolvedRepoRoot);
    const branchName = tryGetGitOutput(resolvedRepoRoot, [
        "branch",
        "--show-current",
    ]);

    return formatStartupLabel(
        repoLabel,
        branchName,
        isGitWorktree(resolvedRepoRoot),
    );
};
