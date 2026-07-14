// Creates a brand-new, never-committed local book via the real `collections/duplicateBook/` API
// (CollectionModel.DuplicateBook) rather than the "New Book" wizard, whose picker UI lives in yet
// another ReactDialog-hosted WebView2 (see README.md's CDP-reachability notes) -- duplicating the
// template book gives us a genuinely new bookInstanceId and folder without touching any dialog.
// `external/add-book` was considered and rejected: it explicitly refuses Team Collections
// (ExternalApi.RefuseIfTeamCollection), which `duplicateBook/` does not.
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { expect } from "@playwright/test";
import { postApi } from "./bloomApi";
import { readBookInstanceId } from "./selectBook";

/** Lists the top-level book folder names directly under a collection folder (directories only,
 * skipping the harness's own "Lost and Found" recovery folder so callers can diff before/after). */
const listBookFolders = async (collectionFolder: string): Promise<string[]> => {
    const entries = await fs.readdir(collectionFolder, { withFileTypes: true });
    return entries
        .filter(
            (entry) => entry.isDirectory() && entry.name !== "Lost and Found",
        )
        .map((entry) => entry.name);
};

/** Duplicates the book identified by `sourceBookInstanceId` (read from its own meta.json by the
 * caller, e.g. via `readBookInstanceId`) on the instance at `httpPort`, and returns the new
 * duplicate's folder name and bookInstanceId. Detected by diffing `collectionFolder`'s book-folder
 * listing before/after the call -- CollectionModel.DuplicateBook's own naming convention (today:
 * "<original> Copy", numbered on a second collision) is deliberately not hard-coded here. */
export const duplicateBook = async (
    httpPort: number,
    collectionFolder: string,
    sourceBookInstanceId: string,
): Promise<{ folderName: string; bookInstanceId: string }> => {
    const before = new Set(await listBookFolders(collectionFolder));
    const response = await postApi(
        httpPort,
        "collections/duplicateBook/",
        sourceBookInstanceId,
    );
    expect(response.status).toBe(200);

    const after = await listBookFolders(collectionFolder);
    const newFolders = after.filter((name) => !before.has(name));
    if (newFolders.length !== 1) {
        throw new Error(
            `Expected exactly one new book folder under ${collectionFolder} after duplicateBook, ` +
                `found ${newFolders.length}: ${JSON.stringify(newFolders)}`,
        );
    }
    const folderName = newFolders[0];
    const bookInstanceId = await readBookInstanceId(
        collectionFolder,
        folderName,
    );
    return { folderName, bookInstanceId };
};

export { listBookFolders };
