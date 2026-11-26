// Work with the collection Bloom has open, and with the book it has selected.
//
// Selecting a book is setup for nearly every test rather than the thing under test, so these take
// the fast, reliable path through Bloom's API (see the UI-vs-API policy in README.md). A test whose
// subject IS the collection grid clicks the book tile instead, with helpers/realClick.ts, because
// the tiles ignore a synthetic click.

import * as fs from "fs";
import * as Path from "path";
import { expect, type FrameLocator, type Page } from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";
import {
    writeNewCollection,
    type ICollectionSpec,
} from "../fixtures/launchBloom";
import { apiGet, apiGetJson, apiPost, getUiString } from "./api";
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
 * Choose a top-level command from the menu a person gets by right-clicking the selected book's
 * tile on the Collections tab, which must be showing. `testId` is the command's `data-testid`, which
 * is its localization id (LocalizableMenuItem), e.g. "CollectionTab.MakeBookUsingThisTemplate", so
 * this works whatever language Bloom's UI is in. Fails naming what the menu offered when the
 * command is not there.
 */
export async function chooseFromSelectedBookMenuByTestId(
    page: Page,
    testId: string,
): Promise<void> {
    await page
        .locator(".bookButton.selected")
        .click({ button: "right", timeout: 15000 });
    // The menu stays mounted when closed, so only a showing entry counts. Only a menu item counts:
    // the book pane beside the grid can show a button with the same test id.
    const item = page.locator(
        `[role="menuitem"][data-testid="${testId}"]:visible`,
    );
    try {
        await item.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        const offered = await page
            .locator('[role="menuitem"]:visible')
            .evaluateAll((items) =>
                items.map((i) => i.getAttribute("data-testid") ?? "(no id)"),
            );
        throw new Error(
            `The book menu has no "${testId}"; it offers: ${offered.join(", ")}.`,
        );
    }
    await realClick(item);
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

/**
 * Make a new, empty collection beside the one Bloom has open, and return its folder. It is in the
 * launch's temp folder, so it is deleted with the rest of the run. For a test that changes
 * collections with openOtherCollection.
 */
export function makeAnotherCollection(
    bloomApp: IBloomApp,
    spec: ICollectionSpec,
): string {
    return writeNewCollection(Path.dirname(bloomApp.collectionDir), spec);
}

/**
 * Leave the collection Bloom has open for the one in `collectionDir`, without the dialog a person
 * uses: this makes the call that a click on the collection's card makes. For setup; a test whose
 * subject is the change of collection calls chooseOtherCollection.
 *
 * Bloom closes the whole workspace and builds it again on the other collection, in the same
 * process. This waits that out, finds the new shell page (bloomApp.page from here on, with
 * bloomApp.collectionDir the new collection), and returns it once Bloom reports that collection
 * as the open one and ready.
 */
export async function openOtherCollection(
    bloomApp: IBloomApp,
    collectionDir: string,
): Promise<Page> {
    const collectionFile = findCollectionFile(collectionDir);
    await waitForCollectionReady(bloomApp.page);
    return followToOtherCollection(bloomApp, collectionFile, (page) =>
        apiPost(page, "workspace/openCollection", collectionFile),
    );
}

/**
 * Leave the collection Bloom has open for the one in `collectionDir` the way a person does: click
 * Other Collection on the Collections tab's top bar, which must be showing, and then that
 * collection's card in the dialog. The dialog offers the collections Bloom opened recently, so
 * the collection must be one this Bloom has had open (openOtherCollection does that for a test).
 * Fails naming the collections the dialog offers when it is not among them.
 *
 * Returns the new shell page, as openOtherCollection does.
 */
export async function chooseOtherCollection(
    bloomApp: IBloomApp,
    collectionDir: string,
): Promise<Page> {
    const collectionFile = findCollectionFile(collectionDir);
    const collectionName = Path.basename(collectionFile, ".bloomCollection");
    const page = bloomApp.page;
    await waitForCollectionReady(page);
    // The button is found by what it says, in whatever language Bloom's UI is in.
    const buttonLabel = await getUiString(
        page,
        "CollectionTab.Open/CreateCollectionButton",
        "Other Collection",
    );
    await realClick(
        page.getByRole("button", { name: buttonLabel, exact: true }),
    );
    // A card is one button that holds the collection's name, in a paragraph of its own, and how
    // many books it has.
    const cards = page
        .getByRole("dialog")
        .getByRole("button")
        .filter({ has: page.locator("p") });
    const card = cards.filter({
        has: page.getByText(collectionName, { exact: true }),
    });
    try {
        await card.waitFor({ state: "visible", timeout: 30000 });
    } catch {
        const offered = (await cards.locator("p").allInnerTexts()).map((t) =>
            t.trim(),
        );
        throw new Error(
            `The Other Collection dialog has no card for "${collectionName}"; it offers: ` +
                `${offered.join(", ")}.`,
        );
    }
    return followToOtherCollection(bloomApp, collectionFile, async () => {
        // The click makes Bloom tear the page down, sometimes before Playwright has finished
        // with the click; a "closed" error then means the click landed. (If it never landed,
        // the wait for the page to close times out.)
        await realClick(card).catch((error) => {
            if (!/closed/i.test(String(error))) throw error;
        });
    });
}

/** The .bloomCollection file of the collection in `collectionDir`. */
function findCollectionFile(collectionDir: string): string {
    const collectionFile = fs
        .readdirSync(collectionDir)
        .find((f) => f.endsWith(".bloomCollection"));
    if (!collectionFile)
        throw new Error(
            `${collectionDir} has no .bloomCollection file, so Bloom cannot open it.`,
        );
    return Path.join(collectionDir, collectionFile);
}

/**
 * Run `askForCollection`, which asks Bloom to open the collection of `collectionFile`, and follow
 * Bloom there: wait for the old shell page to close, find the new one (bloomApp.page from here
 * on, with bloomApp.collectionDir the new collection), and return it once Bloom reports that
 * collection as the open one and ready.
 */
async function followToOtherCollection(
    bloomApp: IBloomApp,
    collectionFile: string,
    askForCollection: (page: Page) => Promise<unknown>,
): Promise<Page> {
    const page = bloomApp.page;
    const pageClosed = page.waitForEvent("close", { timeout: 60000 });
    // If asking fails, we rethrow and never await pageClosed; handling its rejection here keeps
    // that from surfacing as an unrelated unhandled-rejection error.
    pageClosed.catch(() => undefined);
    await askForCollection(page);
    await pageClosed;

    const newPage = await bloomApp.reattachToShell();
    const collectionName = Path.basename(collectionFile, ".bloomCollection");
    await expect
        .poll(() => getOpenCollectionName(newPage), {
            timeout: 60000,
            message: `Bloom never reported "${collectionName}" as the open collection.`,
        })
        .toBe(collectionName);
    await waitForCollectionReady(newPage);
    bloomApp.collectionDir = Path.dirname(collectionFile);
    return newPage;
}

/**
 * The name of the collection Bloom has open, or undefined while Bloom is between collections (its
 * project-scoped endpoints answer 404 then).
 */
async function getOpenCollectionName(page: Page): Promise<string | undefined> {
    try {
        return (await apiGet(page, "collections/name")).body;
    } catch (error) {
        if (/returned 404/.test(String(error))) return undefined;
        throw error;
    }
}

/**
 * The id of the book Bloom has selected, or undefined when no book is selected. This is the
 * answer the Collections tab's book pane asks Bloom for before it shows a preview.
 */
export async function getSelectedBookId(
    page: Page,
): Promise<string | undefined> {
    const info = await apiGetJson<{ id?: string | null }>(
        page,
        "app/selectedBookInfo",
    );
    return info.id ?? undefined;
}

/** The pane on the right of the Collections tab, which previews the selected book. */
const kBookPane = "collections-tab-book-pane";

const kBookPreviewFrame = 'iframe[src*="book-preview/index.htm"]';

/**
 * The preview of the selected book, on the right of the Collections tab. The pane has no such
 * frame while no book is selected.
 */
export function collectionBookPreview(page: Page): FrameLocator {
    return page.getByTestId(kBookPane).frameLocator(kBookPreviewFrame);
}

/**
 * Check that the Collections tab, which must be showing, previews the book whose cover carries
 * `coverTitle`: the preview on the right shows a cover with that title on it.
 */
export async function expectBookInPreview(
    page: Page,
    coverTitle: string,
): Promise<void> {
    await expect(
        collectionBookPreview(page)
            .locator(".bloom-page .bookTitle")
            .getByText(coverTitle, { exact: true })
            .first(),
        `the Collections tab's preview should show the book titled "${coverTitle}"`,
    ).toBeVisible({ timeout: 60000 });
}

/**
 * Check that the Collections tab, which must be showing, previews no book: Bloom has none
 * selected, and the pane on the right has no preview in it.
 *
 * The pane is drawn empty, and asks Bloom which book is selected as it is drawn; a preview comes
 * only with the answer. So an empty pane proves nothing until that answer has come. This waits for
 * the pane, then asks Bloom the same question from the same page: Bloom answers both in the order
 * they were asked, so once this one is answered the pane has had its own answer and drawn it.
 */
export async function expectNoBookInPreview(page: Page): Promise<void> {
    const pane = page.getByTestId(kBookPane);
    await expect(
        pane,
        "the Collections tab should be showing its book pane",
    ).toBeVisible({ timeout: 60000 });
    await expect
        .poll(() => getSelectedBookId(page), {
            timeout: 30000,
            message: "Bloom still has a book selected.",
        })
        .toBeUndefined();
    await expect(
        pane.locator(kBookPreviewFrame),
        "the Collections tab's preview should be empty",
    ).toHaveCount(0);
}
