// Build the folio test collection: the books in helpers/folio.ts, plus two folios, one holding the
// books a folio can publish and one that also holds a book of another page size. The result is
// copied to output/folio-test-inputs/collections/folio-test-collection, laid out like the shared
// test inputs, so it can be opened in a Bloom of one's own either way:
//
//   pnpm exec playwright test --config scripts/playwright.config.ts make-folio-test-collection
//   ./go.sh --collection output/folio-test-inputs/collections/folio-test-collection
//   BLOOM_TESTING_INPUTS_DIR=<repo>/output/folio-test-inputs, and test.use({ collectionName:
//     "folio-test-collection" }) in a script under scripts/
//
// Running it again replaces the collection.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "../fixtures/bloomTest";
import { kFlowTextFeatures } from "../helpers/flowText";
import {
    kFolioTestBooks,
    makeFolio,
    makeFolioTestBook,
    readBookId,
    setFolioBooks,
} from "../helpers/folio";

const repoRoot = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
    "..",
);
const destination = Path.join(
    repoRoot,
    "output",
    "folio-test-inputs",
    "collections",
    "folio-test-collection",
);

// Folio needs a Pro subscription and, until flow text is ready, the flow-text experimental feature.
// makeFolio gives this run's collection the Pro tier; whoever opens the saved collection needs a
// subscription of their own, and the feature turned on in Settings.
test.use({
    collectionSpec: {
        name: "Folio Test Collection",
        languages: ["en"],
    },
    experimentalFeatures: kFlowTextFeatures,
});

test("make the folio test collection", async ({ page, bloomApp }) => {
    test.setTimeout(30 * 60 * 1000);
    const folders = new Map<string, string>();
    for (const book of kFolioTestBooks)
        folders.set(book.title, await makeFolioTestBook(page, book));
    const folio = await makeFolio(page, "Test Folio");
    const folioWithA4Book = await makeFolio(page, "Folio With A4 Book");

    const idOf = (title: string) => readBookId(folders.get(title)!);
    await bloomApp.restart(() => {
        setFolioBooks(folio, [
            idOf("Three Pages"),
            idOf("Big Print"),
            idOf("Crédits Ñandú"),
        ]);
        setFolioBooks(folioWithA4Book, [idOf("Three Pages"), idOf("Also A4")]);
    });

    fs.rmSync(destination, { recursive: true, force: true });
    fs.cpSync(bloomApp.collectionDir, destination, { recursive: true });
    console.log(`Folio test collection written to ${destination}`);
});
