// Creates scratch folder-collections for E2E scenarios to open, checkout/share, etc.
//
// Rather than checking a duplicate set of book images into this new e2e directory, each fixture
// is stamped out at runtime from the existing, already-known-good, source-controlled sample
// collection at src/BloomVisualRegressionTests/collections/basic/ (kept for visual-regression
// testing) — we only copy its "A5 Portrait" book (dropping the second book and the stale
// history.db files) to keep each scratch collection small and single-book-simple.
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { randomUUID } from "node:crypto";
import { repoRoot } from "./paths";
import {
    E2E_SCRATCH_ROOT,
    JOINED_COLLECTION_NAME_PREFIX,
    documentsBloomFolder,
} from "./reset";

const templateCollectionDir = path.join(
    repoRoot,
    "src",
    "BloomVisualRegressionTests",
    "collections",
    "basic",
);
const templateBookName = "A5 Portrait";

export interface ScratchCollection {
    /** Folder containing the .bloomCollection file (== the collection folder Bloom opens). */
    collectionFolder: string;
    /** Full path to the .bloomCollection file itself (what you pass to launchBloom). */
    collectionFilePath: string;
    collectionId: string;
    collectionName: string;
    /** Name of the one book copied in (folder name == book title, "A5 Portrait"). */
    bookName: string;
}

const copyDirExcluding = async (
    source: string,
    destination: string,
    excludeNames: string[],
): Promise<void> => {
    await fs.mkdir(destination, { recursive: true });
    const entries = await fs.readdir(source, { withFileTypes: true });
    for (const entry of entries) {
        if (excludeNames.includes(entry.name)) continue;
        const sourcePath = path.join(source, entry.name);
        const destPath = path.join(destination, entry.name);
        if (entry.isDirectory()) {
            await copyDirExcluding(sourcePath, destPath, excludeNames);
        } else {
            await fs.copyFile(sourcePath, destPath);
        }
    }
};

/**
 * Creates a fresh scratch collection folder under `E2E_SCRATCH_ROOT/<groupName>/<instanceName>/`
 * with a unique CollectionId and the single "A5 Portrait" template book. `groupName` should be
 * the scenario name (so a human can tell scratch folders apart / find them after a failure) and
 * `instanceName` a per-Bloom-instance discriminator (e.g. "alice", "bob").
 */
export const createScratchCollection = async (
    groupName: string,
    instanceName: string,
    // Defaults to a name carrying JOINED_COLLECTION_NAME_PREFIX: any collection this harness
    // shares to the cloud might later be pulled down by another instance, which always lands
    // in the real `%MyDocuments%\Bloom\<name>` folder (see reset.ts) — the prefix is what lets
    // cleanup find and remove it without ever touching a developer's real collections.
    collectionName = `${JOINED_COLLECTION_NAME_PREFIX}${groupName}`,
): Promise<ScratchCollection> => {
    const instanceRoot = path.join(E2E_SCRATCH_ROOT, groupName, instanceName);
    const collectionFolder = path.join(instanceRoot, collectionName);
    await fs.mkdir(collectionFolder, { recursive: true });

    // Collection-level files (skip the sibling book folders and the collection's own history.db).
    const topEntries = await fs.readdir(templateCollectionDir, {
        withFileTypes: true,
    });
    for (const entry of topEntries) {
        if (entry.isDirectory()) continue; // book folders copied explicitly below
        if (entry.name === "history.db") continue;
        if (entry.name.toLowerCase().endsWith(".bloomcollection")) continue; // written fresh below
        await fs.copyFile(
            path.join(templateCollectionDir, entry.name),
            path.join(collectionFolder, entry.name),
        );
    }

    // The one book.
    await copyDirExcluding(
        path.join(templateCollectionDir, templateBookName),
        path.join(collectionFolder, templateBookName),
        ["history.db"],
    );

    // Fresh .bloomCollection with a unique CollectionId (avoids collisions if two scratch
    // collections both somehow end up sharing state) and the requested display name.
    const templateXml = await fs.readFile(
        path.join(templateCollectionDir, "basic.bloomCollection"),
        "utf8",
    );
    const collectionId = randomUUID();
    const stampedXml = templateXml.replace(
        /<CollectionId>[^<]*<\/CollectionId>/,
        `<CollectionId>${collectionId}</CollectionId>`,
    );
    const collectionFilePath = path.join(
        collectionFolder,
        `${collectionName}.bloomCollection`,
    );
    await fs.writeFile(collectionFilePath, stampedXml, "utf8");

    return {
        collectionFolder,
        collectionFilePath,
        collectionId,
        collectionName,
        bookName: templateBookName,
    };
};

/** Where `collections/pullDown` puts a collection named `collectionName` (Bloom's own fixed,
 * non-configurable destination — see reset.ts's `documentsBloomFolder` doc comment). */
export const pulledDownCollectionFilePath = async (
    collectionName: string,
): Promise<string> => {
    const bloomDocsFolder = path.join(
        await documentsBloomFolder(),
        collectionName,
    );
    return path.join(bloomDocsFolder, `${collectionName}.bloomCollection`);
};
