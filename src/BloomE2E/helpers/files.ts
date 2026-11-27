// Look at files on disk from a test: fingerprints for "did Bloom change anything here", path
// containment, and which folder holds which book. Nothing here talks to Bloom.

import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as Path from "node:path";
import { expect } from "@playwright/test";
import { readBookId } from "./collection";

/**
 * A fingerprint of every file under `folder`: relative path to a SHA-1 of its bytes. Two calls
 * that return equal records mean nothing under the folder was added, removed, or rewritten; a
 * test uses that to show Bloom left a folder alone.
 */
export function fingerprintFolder(folder: string): Record<string, string> {
    const result: Record<string, string> = {};
    const walk = (dir: string) => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const full = Path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(full);
            } else {
                result[Path.relative(folder, full).replace(/\\/g, "/")] = crypto
                    .createHash("sha1")
                    .update(fs.readFileSync(full))
                    .digest("hex");
            }
        }
    };
    walk(folder);
    return result;
}

/** True when `child` is `parent` or lies somewhere beneath it, ignoring case and slash style. */
export function isInsideFolder(child: string, parent: string): boolean {
    const relative = Path.relative(
        Path.resolve(parent).toLowerCase(),
        Path.resolve(child).toLowerCase(),
    );
    return (
        relative === "" ||
        (!relative.startsWith("..") && !Path.isAbsolute(relative))
    );
}

/** The names of the .htm files directly in `bookFolder`. A book folder has one: the book itself. */
export function listBookHtmlFiles(bookFolder: string): string[] {
    return fs
        .readdirSync(bookFolder)
        .filter((name) => Path.extname(name).toLowerCase() === ".htm");
}

/**
 * The folders directly under `collectionDir` that hold the book with this id, by each folder's
 * meta.json. A folder with no meta.json, or one Bloom is in the middle of writing, is not counted.
 */
function findBookFoldersWithId(
    collectionDir: string,
    bookId: string,
): string[] {
    return fs
        .readdirSync(collectionDir, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => Path.join(collectionDir, entry.name))
        .filter((folder) => {
            try {
                return readBookId(folder) === bookId;
            } catch {
                return false;
            }
        });
}

/**
 * Wait until Bloom has renamed a book's folder, and return the folder the book is in now. Bloom
 * renames a book's folder to match its title the first time the book is saved with a new title,
 * so call this after the save, with the folder the book was in before it. The book is followed by
 * its id, so this works when the title alone does not say which folder is which.
 */
export async function waitForBookFolderRenamed(
    collectionDir: string,
    bookId: string,
    oldFolder: string,
    timeoutMs = 30000,
): Promise<string> {
    let folders: string[] = [];
    try {
        await expect
            .poll(
                () => {
                    folders = findBookFoldersWithId(collectionDir, bookId);
                    return (
                        folders.length === 1 &&
                        Path.resolve(folders[0]) !== Path.resolve(oldFolder)
                    );
                },
                { timeout: timeoutMs },
            )
            .toBe(true);
    } catch {
        // The message is built after the wait fails, so that it can name what was found.
        throw new Error(
            `Bloom never renamed the folder of book ${bookId} from ${oldFolder}. ` +
                `The folders holding that book are: [${folders.join(", ")}]. ` +
                `The collection holds: [${fs.readdirSync(collectionDir).join(", ")}].`,
        );
    }
    return folders[0];
}
