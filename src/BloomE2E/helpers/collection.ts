// Work with the collection Bloom has open, and with the book it has selected.
//
// Selecting a book is setup for nearly every test rather than the thing under test, so these take
// the fast, reliable path through Bloom's API (see the UI-vs-API policy in README.md). A test whose
// subject IS the collection grid clicks the book tile instead, with helpers/realClick.ts, because
// the tiles ignore a synthetic click.

import { expect, type Page } from "@playwright/test";
import { apiGet, apiPost } from "./api";

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
        .poll(
            async () => {
                try {
                    return (await apiGet(page, "e2e/isCollectionReady")).body;
                } catch (error) {
                    // The e2e endpoints are project-scoped, so they briefly answer 404 while
                    // the project is reopening (e.g. after a UI language change); that just
                    // means "not ready yet". Anything else - a closed page, a dead server -
                    // is a real failure, and hiding it inside a generic timeout would only
                    // obscure it.
                    if (/returned 404/.test(String(error)))
                        return "not ready yet";
                    throw error;
                }
            },
            {
                timeout: timeoutMs,
                message: "Bloom's editable collection never became ready.",
            },
        )
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
