/* eslint-env node */
// Brings the parts of output/browser that the C# tests read up to date with this checkout:
// appearance themes, appearance migrations, and the template and branding files that the
// build:templates:files and build:branding:files scripts copy. build/agent-dotnet.sh and .ps1
// run it before every test run, so a worktree that was moved to another branch does not test
// against the old branch's copies.
//
// It must be safe while other test runs and a running Bloom are reading output/browser, and it
// must need nothing but node (a worktree may run C# tests without having installed the
// front-end's node_modules). So it never deletes a folder: it writes only the files whose
// content differs, each through a temporary file and a rename so a reader never sees half a
// file, and it deletes only the files in the theme and migration folders that this checkout
// does not have. The templates and branding folders also hold CSS that the full build compiles
// from LESS, so nothing is deleted from them.
import path from "node:path";
import * as fs from "node:fs";
import { fileURLToPath } from "node:url";

const contentRoot = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "..",
);
const outputRoot = path.resolve(contentRoot, "../../output/browser");

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
        deleteExtras: true,
    },
    {
        // build:appearanceMigrations
        name: "appearanceMigrations",
        include: () => true,
        deleteExtras: true,
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
        deleteExtras: false,
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
        deleteExtras: false,
    },
];

/** Lists every file under `root`, as paths relative to it; an empty list if `root` is missing. */
function listFiles(root) {
    if (!fs.existsSync(root)) {
        return [];
    }
    return fs
        .readdirSync(root, { recursive: true, withFileTypes: true })
        .filter((entry) => entry.isFile())
        .map((entry) =>
            path.relative(root, path.join(entry.parentPath, entry.name)),
        );
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

/** Replaces `destination` with `content` in one rename, so a concurrent reader sees the old file or the new one. */
function writeAtomically(destination, content) {
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    const temporary = `${destination}.${process.pid}.tmp`;
    fs.writeFileSync(temporary, content);
    fs.renameSync(temporary, destination);
}

let written = 0;
let deleted = 0;
for (const folder of folders) {
    const sourceRoot = path.join(contentRoot, folder.name);
    const destinationRoot = path.join(outputRoot, folder.name);
    const sourceFiles = listFiles(sourceRoot).filter(folder.include);
    for (const relativePath of sourceFiles) {
        const content = fs.readFileSync(path.join(sourceRoot, relativePath));
        const destination = path.join(destinationRoot, relativePath);
        if (!hasContent(destination, content)) {
            writeAtomically(destination, content);
            written++;
        }
    }
    if (folder.deleteExtras) {
        const wanted = new Set(sourceFiles);
        for (const relativePath of listFiles(destinationRoot)) {
            if (!wanted.has(relativePath)) {
                fs.rmSync(path.join(destinationRoot, relativePath));
                deleted++;
            }
        }
        removeEmptyFolders(destinationRoot);
    }
}

/** Removes the empty folders below `root`, deepest first, so a folder emptied of extras goes too. */
function removeEmptyFolders(root) {
    const folderPaths = fs
        .readdirSync(root, { recursive: true, withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => path.join(entry.parentPath, entry.name))
        .sort((a, b) => b.length - a.length);
    for (const folderPath of folderPaths) {
        if (fs.readdirSync(folderPath).length === 0) {
            fs.rmdirSync(folderPath);
        }
    }
}
console.log(
    `[agent-dotnet] output/browser content from this checkout: ${written} file(s) written, ${deleted} removed`,
);
