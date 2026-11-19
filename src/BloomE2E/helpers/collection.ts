// Work with the collection Bloom has open, and with the book it has selected.
//
// Selecting a book is setup for nearly every test rather than the thing under test, so these take
// the fast, reliable path through Bloom's API (see the UI-vs-API policy in README.md). A test whose
// subject IS the collection grid clicks the book tile instead, with helpers/realClick.ts, because
// the tiles ignore a synthetic click.

import { expect, type Page } from "@playwright/test";
import { apiGet, apiPost } from "./api";
import { realClick } from "./realClick";

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

/**
 * Choose a command from the menu a person gets by right-clicking the selected book's tile on the
 * Collections tab, which must be showing. `menuPath` is the menu's own words, one entry per level,
 * e.g. ["More", "Export to Spreadsheet..."]. Clicking a command the subscription tier lacks opens
 * the Subscription page of the Settings dialog instead of doing it. Fails naming what the menu
 * offered when an entry is not there.
 */
export async function chooseFromSelectedBookMenu(
    page: Page,
    menuPath: string[],
): Promise<void> {
    await page
        .locator(".bookButton.selected")
        .click({ button: "right", timeout: 15000 });
    for (const entry of menuPath) {
        const items = page.getByRole("menuitem");
        // Each level opens in answer to the click before, so wait for it to appear.
        const item = items.filter({ hasText: entry }).last();
        try {
            await item.waitFor({ state: "visible", timeout: 15000 });
        } catch {
            const offered = (await items.allInnerTexts()).map((t) => t.trim());
            throw new Error(
                `The book menu has no "${entry}"; it offers: ${offered.join(", ")}.`,
            );
        }
        await realClick(item);
    }
}

/**
 * Close the selected book's menu with Escape, as a person does, and wait until it has gone. A
 * command the subscription tier lacks opens the Settings dialog but leaves this menu open behind
 * it, and the menu's backdrop then takes every click aimed at the Collections tab.
 */
export async function closeBookMenu(page: Page): Promise<void> {
    // The menu stays mounted when closed, so only showing entries count. Each Escape closes one
    // level (the More submenu, then the menu).
    const showingEntries = page.locator('[role="menuitem"]:visible');
    await expect(
        showingEntries.first(),
        "there is no book menu open to close",
    ).toBeVisible();
    await expect(async () => {
        await page.keyboard.press("Escape");
        await expect(showingEntries).toHaveCount(0, { timeout: 1000 });
    }, "the book menu did not close").toPass({ timeout: 10000 });
}
