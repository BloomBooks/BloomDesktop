/* eslint-env node */
// Brings the parts of output/browser that the C# tests read up to date with this checkout:
// appearance themes, appearance migrations, and the template and branding files that the
// build:templates:files and build:branding:files scripts copy. build/agent-dotnet.sh and .ps1
// run it before every test run, so a worktree that was moved to another branch does not test
// against the old branch's copies.
//
// It must be safe while other test runs (which may run this script at the same moment) and a
// running Bloom read output/browser, and it must need nothing but node (a worktree may run C#
// tests without having installed the front-end's node_modules). So:
// - it writes only the files whose content differs, each through a temporary file and a rename,
//   so a reader sees the old file or the new one, never half of one;
// - it deletes only what this checkout does not have: folders with no counterpart in
//   src/content, and, in the theme and migration folders, files with none. The templates and
//   branding folders also hold CSS that the full build compiles from LESS sources beside them,
//   so a file there without a source counterpart is left alone; a folder without one is not,
//   since its LESS sources are gone too.
import path from "node:path";
import * as fs from "node:fs";
import { fileURLToPath } from "node:url";

const contentRoot = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "..",
);
const outputRoot = path.resolve(contentRoot, "../../output/browser");

// Another run of this script must never delete one of these while its writer is about to rename it.
const temporarySuffix = ".copyContentForCSharpTests.tmp";

const templateExtensions = new Set([
    ".png",
    ".jpg",
    ".svg",
    ".css",
    ".json",
    ".htm",
    ".html",
    ".txt",
    ".js",
    ".gif",
]);
const brandingExtensions = new Set([
    ".png",
    ".jpg",
    ".svg",
    ".css",
    ".json",
    ".htm",
]);

// Each entry matches the files that the src/content/package.json script named in its comment copies.
const folders = [
    {
        // build:appearance
        name: "appearanceThemes",
        include: (relativePath) => relativePath.endsWith(".css"),
        deleteExtraFiles: true,
    },
    {
        // build:appearanceMigrations
        name: "appearanceMigrations",
        include: () => true,
        deleteExtraFiles: true,
    },
    {
        // build:templates:files
        name: "templates",
        include: (relativePath) => {
            const extension = path.extname(relativePath);
            return (
                templateExtensions.has(extension) &&
                path.basename(relativePath, extension) !== "tsconfig"
            );
        },
        deleteExtraFiles: false,
    },
    {
        // build:branding:files: files at least one folder down, whose own folder is not "source"
        name: "branding",
        include: (relativePath) => {
            const parent = path.dirname(relativePath);
            return (
                parent !== "." &&
                path.basename(parent) !== "source" &&
                brandingExtensions.has(path.extname(relativePath))
            );
        },
        deleteExtraFiles: false,
    },
];

/**
 * Lists the files (or folders, if `wantFolders`) under `root` as paths relative to it. A folder
 * that is missing, or that another run of this script deletes while we walk it, counts as empty.
 */
function list(root, wantFolders, relativeFolder = "") {
    let entries;
    try {
        entries = fs.readdirSync(path.join(root, relativeFolder), {
            withFileTypes: true,
        });
    } catch (error) {
        if (error.code === "ENOENT") {
            return [];
        }
        throw error;
    }
    const result = [];
    for (const entry of entries) {
        const relativePath = path.join(relativeFolder, entry.name);
        if (entry.isDirectory()) {
            if (wantFolders) {
                result.push(relativePath);
            }
            result.push(...list(root, wantFolders, relativePath));
        } else if (!wantFolders && entry.isFile()) {
            result.push(relativePath);
        }
    }
    return result;
}

/** True if the file at `destination` exists and has exactly the bytes in `content`. */
function hasContent(destination, content) {
    if (!fs.existsSync(destination)) {
        return false;
    }
    if (fs.statSync(destination).size !== content.length) {
        return false;
    }
    return fs.readFileSync(destination).equals(content);
}

/**
 * Replaces `destination` with `content` in one rename, so a concurrent reader sees the old file or
 * the new one. On Windows the rename fails while a reader has the old file open, so it waits for
 * the reader for up to two seconds.
 */
function writeAtomically(destination, content) {
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    const temporary = `${destination}.${process.pid}${temporarySuffix}`;
    fs.writeFileSync(temporary, content);
    try {
        renameWhenFree(temporary, destination);
    } catch (error) {
        fs.rmSync(temporary, { force: true });
        throw error;
    }
}

/** Renames `from` to `to`, retrying for up to two seconds while Windows reports the file busy. */
function renameWhenFree(from, to) {
    for (let attempt = 1; ; attempt++) {
        try {
            fs.renameSync(from, to);
            return;
        } catch (error) {
            const busy = ["EPERM", "EBUSY", "EACCES"].includes(error.code);
            if (!busy || attempt === 20) {
                throw error;
            }
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 100);
        }
    }
}

/**
 * Deletes a stale folder. It is first renamed to a name of this process's own, which only one
 * concurrent run can do; two runs deleting the same tree at once fail with EPERM on Windows.
 * A folder that some process (a running Bloom, say) keeps busy is left, with a warning: the tests
 * should not be held up by a folder that this checkout does not use.
 */
function deleteFolder(folderPath) {
    const renamed = `${folderPath}.${process.pid}${temporarySuffix}`;
    try {
        renameWhenFree(folderPath, renamed);
    } catch (error) {
        if (error.code === "ENOENT") {
            return false; // another run got there first
        }
        if (["EPERM", "EBUSY", "EACCES"].includes(error.code)) {
            console.warn(
                `[agent-dotnet] left ${folderPath}, which this checkout does not have, because it is in use (${error.code})`,
            );
            return false;
        }
        throw error;
    }
    fs.rmSync(renamed, { recursive: true, force: true });
    return true;
}

let written = 0;
let deleted = 0;
for (const folder of folders) {
    const sourceRoot = path.join(contentRoot, folder.name);
    const destinationRoot = path.join(outputRoot, folder.name);

    const sourceFolders = new Set(list(sourceRoot, true));
    // Shallowest first, so a stale folder goes before anything inside it is visited.
    const destinationFolders = list(destinationRoot, true).sort(
        (a, b) => a.length - b.length,
    );
    for (const relativePath of destinationFolders) {
        const folderPath = path.join(destinationRoot, relativePath);
        if (
            !sourceFolders.has(relativePath) &&
            !relativePath.includes(temporarySuffix) &&
            fs.existsSync(folderPath) &&
            deleteFolder(folderPath)
        ) {
            deleted++;
        }
    }

    const sourceFiles = list(sourceRoot, false).filter(folder.include);
    for (const relativePath of sourceFiles) {
        const content = fs.readFileSync(path.join(sourceRoot, relativePath));
        const destination = path.join(destinationRoot, relativePath);
        if (!hasContent(destination, content)) {
            writeAtomically(destination, content);
            written++;
        }
    }

    if (folder.deleteExtraFiles) {
        const wanted = new Set(sourceFiles);
        for (const relativePath of list(destinationRoot, false)) {
            if (
                !wanted.has(relativePath) &&
                !relativePath.endsWith(temporarySuffix)
            ) {
                fs.rmSync(path.join(destinationRoot, relativePath), {
                    force: true,
                });
                deleted++;
            }
        }
    }
}
console.log(
    `[agent-dotnet] output/browser content from this checkout: ${written} file(s) written, ${deleted} file(s) or folder(s) removed`,
);
