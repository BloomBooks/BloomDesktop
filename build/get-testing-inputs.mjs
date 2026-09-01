// Materialize the pinned commit of the shared test-input repository into output/testing-inputs/.
//
// The books and reference screenshots that src/BloomVisualRegressionTests renders live in
// https://github.com/BloomBooks/bloom-testing-inputs, not in this repository. build/testing-inputs.pin
// names that repository and the exact commit this branch tests against; this script fetches that
// commit and checks it out into output/testing-inputs/ (gitignored).
//
// Usage:
//   node build/get-testing-inputs.mjs            fetch and check out the pinned commit
//   node build/get-testing-inputs.mjs --check     report only; exit 1 if the folder is not at the pin
//
// The fetch is a shallow single-commit fetch, so it is small, and the whole script is a fast no-op
// when the folder is already at the pinned commit. It has no dependencies outside Node and git.

import { spawnSync } from "child_process";
import * as fs from "fs";
import * as Path from "path";
import { fileURLToPath } from "url";

// Resolve everything from this script's own location, never from the current working directory, so
// that the script behaves the same however it is invoked (repo root, a package folder, or CI).
const scriptDir = Path.dirname(fileURLToPath(import.meta.url));
const repoRoot = Path.resolve(scriptDir, "..");
const pinPath = Path.join(scriptDir, "testing-inputs.pin");
const targetDir = Path.join(repoRoot, "output", "testing-inputs");

/**
 * Read build/testing-inputs.pin and return its `repo` and `commit` values.
 * Throws if the file is missing or either key is absent, because every caller needs both.
 */
export function readPin() {
    if (!fs.existsSync(pinPath))
        throw new Error(`The pin file is missing: ${pinPath}`);
    const values = {};
    for (const rawLine of fs.readFileSync(pinPath, "utf8").split("\n")) {
        const line = rawLine.trim();
        if (!line || line.startsWith("#")) continue;
        const separator = line.indexOf("=");
        if (separator < 0)
            throw new Error(`${pinPath}: this line is not key=value: ${line}`);
        values[line.slice(0, separator).trim()] = line
            .slice(separator + 1)
            .trim();
    }
    for (const key of ["repo", "commit"])
        if (!values[key]) throw new Error(`${pinPath} has no ${key}=... line`);
    return { repo: values.repo, commit: values.commit };
}

/**
 * Run git with the given arguments and return its result, without throwing.
 * Output is inherited only when `quiet` is false, so a routine probe stays silent while a real
 * fetch shows git's own progress.
 */
function runGit(args, quiet) {
    return spawnSync("git", args, {
        encoding: "utf8",
        stdio: quiet ? "pipe" : ["ignore", "inherit", "inherit"],
    });
}

/**
 * Run git and throw a message naming the exact failing command if it fails.
 * This is what turns "something went wrong" into something a developer can paste into a terminal.
 */
function git(args) {
    const result = runGit(args, false);
    if (result.error)
        throw new Error(`Could not run git: ${result.error.message}`);
    if (result.status !== 0)
        throw new Error(
            `This command failed (exit ${result.status}): git ${args.join(" ")}`,
        );
}

/**
 * Return the commit currently checked out in the target folder, or null if the folder is not a
 * git working copy yet (first run) or has no commit checked out (an interrupted first run).
 */
export function currentCommit() {
    if (!fs.existsSync(Path.join(targetDir, ".git"))) return null;
    const result = runGit(["-C", targetDir, "rev-parse", "HEAD"], true);
    if (result.status !== 0) return null;
    return result.stdout.trim();
}

/**
 * Fetch and check out the pinned commit, unless the folder already holds it.
 * Handles both the first run (the folder does not exist) and advancing the pin (it holds an older
 * commit); in both cases the work is one shallow fetch plus a detached checkout.
 */
export function getTestingInputs() {
    const pin = readPin();
    if (currentCommit() === pin.commit) {
        console.log(
            `output/testing-inputs is already at the pinned commit ${pin.commit}.`,
        );
        return;
    }
    fs.mkdirSync(targetDir, { recursive: true });
    if (!fs.existsSync(Path.join(targetDir, ".git")))
        git(["init", "--quiet", targetDir]);
    console.log(`Fetching ${pin.commit} from ${pin.repo} into ${targetDir}`);
    git(["-C", targetDir, "fetch", "--depth", "1", pin.repo, pin.commit]);
    // --detach because this folder is a read-only materialization of one commit, not a branch
    // anybody works on. Deliberately not --force: a checkout that refuses because someone edited a
    // file here should say so rather than silently discard their work. Untracked files (the
    // suite's own *-current.png / *-diff.png) do not block a checkout, so a normal run is unaffected.
    git(["-C", targetDir, "checkout", "--detach", "FETCH_HEAD"]);
    console.log(`output/testing-inputs is now at ${pin.commit}.`);
}

/**
 * Report whether the target folder matches the pin, and exit non-zero if it does not.
 * For CI logs and for a developer wondering why the suite is rendering something unexpected.
 */
export function checkTestingInputs() {
    const pin = readPin();
    const have = currentCommit();
    if (have === pin.commit) {
        console.log(
            `output/testing-inputs is at the pinned commit ${pin.commit}.`,
        );
        return;
    }
    console.error(
        `output/testing-inputs is at ${have ?? "no commit (the folder is missing or empty)"}, ` +
            `but build/testing-inputs.pin names ${pin.commit}. ` +
            `Run: node build/get-testing-inputs.mjs`,
    );
    process.exitCode = 1;
}

try {
    if (process.argv.includes("--check")) checkTestingInputs();
    else getTestingInputs();
} catch (e) {
    console.error(e.message);
    process.exitCode = 1;
}
