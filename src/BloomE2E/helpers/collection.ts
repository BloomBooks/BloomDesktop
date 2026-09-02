// Work with the collection Bloom has open, and with the book it has selected.
//
// Selecting a book is setup for nearly every test rather than the thing under test, so these take
// the fast, reliable path through Bloom's API (see the UI-vs-API policy in README.md). A test whose
// subject IS the collection grid clicks the book tile instead, with helpers/realClick.ts, because
// the tiles ignore a synthetic click.

import { expect, type Page } from "@playwright/test";
import { apiGet, apiPost } from "./api";
import type { IBloomApp } from "../fixtures/bloomTest";

/**
 * Wait until the editable collection is loaded and its books can be enumerated. Switching to the
 * collection tab reloads its webview, and selecting a book during that window throws inside Bloom
 * and pops an error dialog, so call this before selectBook.
 */
export async function waitForCollectionReady(
    page: Page,
    timeoutMs = 60000,
): Promise<void> {
    await expect
        .poll(async () => (await apiGet(page, "e2e/isCollectionReady")).body, {
            timeout: timeoutMs,
            message: "Bloom's editable collection never became ready.",
        })
        .toBe("true");
}

/**
 * Select a book by its folder inside the collection Bloom has open. Pass a folder under the
 * fixture's `collectionDir`, never one under output/testing-inputs: Bloom rewrites the book it
 * selects, and the inputs are pinned and shared.
 *
 * Bloom keeps the Edit and Publish tabs hidden until a book is selected, so this is the
 * precondition for reaching either of them.
 */
export async function selectBook(
    page: Page,
    bookFolder: string,
): Promise<void> {
    await waitForCollectionReady(page);
    const collectionId = bookFolder.replace(/[\\/][^\\/]+$/, "");
    await apiPost(
        page,
        `collections/selected-book?path=${encodeURIComponent(bookFolder)}` +
            `&collection-id=${encodeURIComponent(collectionId)}`,
    );
}

/**
 * Set the collection's languages, and give Bloom back to the test with the change in effect.
 *
 * `tags` holds one to three language tags, for Language 1, Language 2 and Language 3. Fewer than
 * three leaves the rest empty, which is how a collection ends up with no Language 3.
 *
 * Bloom reads a collection's languages when it opens the collection, so this restarts it, exactly
 * as clicking OK in the Collection Settings dialog makes a user reopen the collection. The restart
 * kills the process, so leave the page being edited first or what was typed on it is lost, and use
 * the page this returns: the old one points at a dead target.
 *
 * The `e2e/setCollectionLanguages` hook writes the .bloomCollection with Bloom's own code, so
 * everything else in the collection's settings survives. See AUTOMATION-DEBT.md for why there is
 * no production API for this.
 */
export async function setCollectionLanguages(
    bloomApp: IBloomApp,
    tags: string[],
): Promise<Page> {
    if (tags.length < 1 || tags.length > 3)
        throw new Error(
            `A collection has one to three languages; setCollectionLanguages was given ${tags.length}.`,
        );
    await apiPost(
        bloomApp.page,
        "e2e/setCollectionLanguages",
        JSON.stringify(tags),
        "application/json",
    );
    return bloomApp.restart();
}
