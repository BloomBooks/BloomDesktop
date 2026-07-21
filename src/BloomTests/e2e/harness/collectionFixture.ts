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
    const stampedXml = templateXml
        .replace(
            /<CollectionId>[^<]*<\/CollectionId>/,
            `<CollectionId>${collectionId}</CollectionId>`,
        )
        // Stamp a valid LocalCommunity-tier subscription code. This is load-bearing, not
        // decorative: TeamCollectionManager.CheckDisablingTeamCollections disconnects ANY Team
        // Collection when FeatureName.TeamCollection isn't enabled for the collection's
        // subscription tier (requires LocalCommunity; the template's empty code = Basic).
        // DISCOVERED LIVE (E2E-9 name-race): whether that check actually fires for a CLOUD TC is
        // a startup RACE -- the check early-returns when TCManager.CurrentCollection is still
        // null, and the cloud connection usually isn't established yet at that point in workspace
        // load, so most launches sail through... but a launch where sign-in/connection completes
        // fast enough gets disconnected with "Team Collections require a Bloom subscription tier
        // of at least 'LocalCommunity'" (observed once in ~40 launches; broke every subsequent
        // teamCollection/* call in that instance with empty-body 503s). The same test code the
        // unit suite uses ("Fake-LC-006273-1463", SubscriptionTests.cs) encodes an expiry of
        // ~Sep 2026 -- when it expires, SubscriptionTests will start failing too and both must
        // be updated together.
        .replace(
            /<SubscriptionCode\s*\/>|<SubscriptionCode>[^<]*<\/SubscriptionCode>/,
            "<SubscriptionCode>Fake-LC-006273-1463</SubscriptionCode>",
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

/**
 * Seeds a SECOND book folder named `bookName` directly into an already-existing collection
 * folder, by copying the same template book used for the collection's original book, stamped
 * with a fresh bookInstanceId and its main .htm file renamed to match (mirroring
 * `BookStorage.Duplicate`'s own folder-name/htm-name convention).
 *
 * Used by E2E-9's concurrent-same-name-creation scenario: `CollectionModel.DuplicateBook`
 * deliberately gives every duplicate a GUID-suffixed folder name specifically so two
 * Team-Collection members' independent duplicates of the SAME source book never collide
 * locally (see BookStorage.Duplicate's own comment) -- which means it can't be used to
 * construct a genuine same-*proposed*-name race. Seeding two collections' book folders with
 * the IDENTICAL name directly on disk (each with its own distinct bookInstanceId, so they are
 * unrelated books that merely happen to share a display name) reproduces the race
 * `CloudTeamCollection.PutBookInRepo`'s name-conflict retry loop exists to resolve. Must be
 * done while the collection's Bloom instance is NOT running (or before its first launch) --
 * the caller is responsible for relaunching afterward so the new folder is picked up by the
 * normal collection-load scan, since this only writes files and does not talk to any running
 * Bloom process.
 */
export const seedAdditionalBookIntoCollection = async (
    collectionFolder: string,
    bookName: string,
): Promise<{ bookInstanceId: string }> => {
    const bookFolder = path.join(collectionFolder, bookName);
    await copyDirExcluding(
        path.join(templateCollectionDir, templateBookName),
        bookFolder,
        ["history.db"],
    );

    // Rename the main .htm file to match the new folder name (BookStorage.Duplicate's own
    // convention -- Bloom expects <folder>/<folder>.htm) AND stamp the book's TITLE (the
    // data-book="bookTitle" divs in the htm, plus meta.json's "title") to match too.
    // The title stamping is load-bearing, not cosmetic: Bloom renames a book's folder to match
    // its title on save (Book.Save -> SetBookName), and checkInCurrentBook saves first -- so a
    // seeded folder named "RaceBook" whose content still says "A5 Portrait" would get renamed
    // (uniquified against the collection's existing "A5 Portrait" book) BEFORE the Send, and
    // the server would never see the "RaceBook" name this helper's caller is trying to race
    // (discovered live: E2E-9's name-race test committed title-derived names instead).
    const htmPath = path.join(bookFolder, `${bookName}.htm`);
    await fs.rename(path.join(bookFolder, `${templateBookName}.htm`), htmPath);
    const htmContent = await fs.readFile(htmPath, "utf8");
    await fs.writeFile(
        htmPath,
        htmContent.split(templateBookName).join(bookName),
        "utf8",
    );

    // Stamp a fresh bookInstanceId so this counts as an unrelated book that merely shares a
    // display name with its counterpart on the other side, not a duplicate/re-import of it.
    const metaPath = path.join(bookFolder, "meta.json");
    const meta = JSON.parse(await fs.readFile(metaPath, "utf8"));
    const bookInstanceId = randomUUID();
    meta.bookInstanceId = bookInstanceId;
    meta.title = bookName;
    await fs.writeFile(metaPath, JSON.stringify(meta, null, 2), "utf8");

    return { bookInstanceId };
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
