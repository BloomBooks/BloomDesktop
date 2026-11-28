// Work with the collection Bloom has open, and with the book it has selected.
//
// Selecting a book is setup for nearly every test rather than the thing under test, so these take
// the fast, reliable path through Bloom's API (see the UI-vs-API policy in README.md). A test whose
// subject IS the collection grid clicks the book tile instead, with helpers/realClick.ts, because
// the tiles ignore a synthetic click.

import * as fs from "fs";
import * as Path from "path";
import {
    expect,
    type FrameLocator,
    type Locator,
    type Page,
} from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";
import {
    writeNewCollection,
    type ICollectionSpec,
} from "../fixtures/launchBloom";
import { apiGet, apiGetJson, apiPost, getUiString } from "./api";
import { FIRST_IMAGE } from "./images";
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

/** One book as collections/books reports it. Only the fields used here. */
export interface IBookInfo {
    id: string;
    title: string;
    folderPath: string;
}

/** One collection as collections/list reports it. Only the fields used here. */
export interface ICollectionInfo {
    id: string;
    name: string;
}

/** The books in the collection Bloom has open. */
export async function listEditableBooks(page: Page): Promise<IBookInfo[]> {
    const name = (
        await apiGet(page, "collections/getCurrentEditableCollectionName")
    ).body;
    const collections = await apiGetJson<ICollectionInfo[]>(
        page,
        "collections/list",
    );
    const editable = collections.find((c) => c.name === name) ?? collections[0];
    return apiGetJson<IBookInfo[]>(
        page,
        `collections/books?collection-id=${encodeURIComponent(editable.id)}`,
    );
}

/** The two ways a person opens the selected book's menu on the Collections tab. */
export type BookMenuOpener = "right-click" | "triangle";

/** The triangle at the bottom right of the selected book's tile, which opens the book's menu. */
const kBookMenuTriangle = "book-menu-triangle";

/** The entries of the selected book's menu that are showing; the menu stays mounted when closed. */
const kShowingBookMenuEntries = '[role="menuitem"]:visible';

/** The tile of every book on the Collections tab, in any of its collections. */
const kBookTiles = ".book-button[data-book-id]";

/**
 * Select a book the way a person does, by clicking its tile on the Collections tab, which must be
 * showing. Returns once Bloom reports the book selected and its tile is drawn as the selected one.
 * selectBook does the same through the API, for a test whose subject is not the tile.
 */
export async function clickBookTile(page: Page, bookId: string): Promise<void> {
    await waitForCollectionReady(page);
    const tile = page
        .locator(`${kBookTiles}[data-book-id="${bookId}"]`)
        .locator(".bookButton");
    await realClick(tile);
    await expect
        .poll(() => getSelectedBookId(page), {
            timeout: 30000,
            message: `Bloom never reported book ${bookId} selected after a click on its tile.`,
        })
        .toBe(bookId);
    await expect(
        tile,
        `the tile of book ${bookId} should be drawn as the selected one`,
    ).toHaveClass(/\bselected\b/, { timeout: 15000 });
}

/**
 * The ids of the books whose tiles on the Collections tab, which must be showing, show the
 * triangle that opens the book's menu.
 */
export async function getBooksShowingMenuTriangle(
    page: Page,
): Promise<string[]> {
    return page
        .locator(kBookTiles)
        .filter({
            has: page.getByTestId(kBookMenuTriangle).filter({ visible: true }),
        })
        .evaluateAll((tiles) =>
            tiles.map((tile) => tile.getAttribute("data-book-id")!),
        );
}

/**
 * Open the selected book's menu on the Collections tab, which must be showing, by right-clicking
 * the book's tile or by clicking the triangle at the tile's bottom right corner. Returns once the
 * menu shows its entries.
 */
export async function openSelectedBookMenu(
    page: Page,
    openWith: BookMenuOpener = "right-click",
): Promise<void> {
    const selectedTile = page.locator(".bookButton.selected");
    if (openWith === "right-click") {
        await selectedTile.click({ button: "right", timeout: 15000 });
    } else {
        // The triangle is a sibling of the tile's button, inside the same book tile.
        await realClick(
            selectedTile.locator("xpath=..").getByTestId(kBookMenuTriangle),
        );
    }
    await expect(
        page.locator(kShowingBookMenuEntries).first(),
        `the selected book's menu should open on a ${openWith}`,
    ).toBeVisible({ timeout: 15000 });
}

/** One entry of the selected book's menu. */
export interface IBookMenuEntry {
    /** The entry's `data-testid`, which is its localization id. */
    testId: string;
    /** The words the entry shows, in whatever language Bloom's UI is in. */
    label: string;
    /** False for an entry that is greyed out, such as one the book's checkout forbids. */
    enabled: boolean;
    /**
     * True for an entry whose command the subscription tier lacks. That alone does not grey it
     * out: while `enabled` is true, a click on it opens the Subscription page of the Settings
     * dialog instead of doing the command.
     */
    needsSubscription: boolean;
}

/** What the selected book's menu offers: its own entries, and those of its More submenu. */
export interface IBookMenu {
    entries: IBookMenuEntry[];
    more: IBookMenuEntry[];
}

/**
 * Read what the selected book's menu offers when opened with `openWith`, on the Collections tab,
 * which must be showing: every entry of the menu and of its More submenu, in the order shown. Opens
 * the menu and closes it again.
 */
export async function readSelectedBookMenu(
    page: Page,
    openWith: BookMenuOpener,
): Promise<IBookMenu> {
    await openSelectedBookMenu(page, openWith);
    const entries = await readShowingBookMenuEntries(page);
    await realClick(
        page.locator(
            `${kShowingBookMenuEntries}[data-testid="CollectionTab.ContextMenu.More"]`,
        ),
    );
    // The submenu opens in answer to the click, so wait until more entries are showing.
    await expect
        .poll(() => page.locator(kShowingBookMenuEntries).count(), {
            timeout: 15000,
            message: "the book menu's More submenu never opened",
        })
        .toBeGreaterThan(entries.length);
    const withSubmenu = await readShowingBookMenuEntries(page);
    // While the pointer rests on More its submenu stays open, and the menu then ignores Escape:
    // the key goes to the menu, which has the focus, and only the topmost menu, the submenu,
    // answers it. So move the pointer to another entry, which shuts the submenu, before closing.
    // A greyed-out entry sits under the tooltip that explains it, which takes the pointer, so the
    // entry must be one that is not greyed out.
    await page
        .locator(
            `${kShowingBookMenuEntries}:not([aria-disabled="true"]):not([data-testid="CollectionTab.ContextMenu.More"])`,
        )
        .first()
        .hover();
    await expect(
        page.locator(kShowingBookMenuEntries),
        "the More submenu should shut when the pointer leaves More",
    ).toHaveCount(entries.length, { timeout: 15000 });
    await closeBookMenu(page);
    return {
        entries,
        more: withSubmenu.filter(
            (entry) => !entries.some((e) => e.testId === entry.testId),
        ),
    };
}

/**
 * Every entry the open book menu shows, in the order shown. Waits first until each entry that
 * depends on the subscription tier has its answer, since until then it looks like any other.
 */
async function readShowingBookMenuEntries(
    page: Page,
): Promise<IBookMenuEntry[]> {
    const showing = page.locator(kShowingBookMenuEntries);
    await expect(
        showing.and(page.locator("[data-feature-status-pending]")),
        "every entry of the book menu should know whether the subscription allows it",
    ).toHaveCount(0, { timeout: 15000 });
    return showing.evaluateAll((items) =>
        items.map((item) => ({
            testId: item.getAttribute("data-testid") ?? "(no id)",
            label: (item as HTMLElement).innerText.trim(),
            enabled: item.getAttribute("aria-disabled") !== "true",
            needsSubscription:
                item.getAttribute("data-subscription-gated") === "true",
        })),
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
    await openSelectedBookMenu(page);
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
 * Choose a top-level command from the selected book's menu on the Collections tab, which must be
 * showing. The menu is opened with `openWith`: a right-click on the book's tile, or a click on the
 * triangle at the tile's bottom right corner. `testId` is the command's `data-testid`, which
 * is its localization id (LocalizableMenuItem), e.g. "CollectionTab.MakeBookUsingThisTemplate", so
 * this works whatever language Bloom's UI is in. Fails naming what the menu offered when the
 * command is not there.
 */
export async function chooseFromSelectedBookMenuByTestId(
    page: Page,
    testId: string,
    openWith: BookMenuOpener = "right-click",
): Promise<void> {
    await openSelectedBookMenu(page, openWith);
    // The menu stays mounted when closed, so only a showing entry counts. Only a menu item counts:
    // the book pane beside the grid can show a button with the same test id.
    const item = page.locator(
        `${kShowingBookMenuEntries}[data-testid="${testId}"]`,
    );
    try {
        await item.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        const offered = await page
            .locator(kShowingBookMenuEntries)
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
 * Choose a command from the More submenu of the selected book's menu on the Collections tab, which
 * must be showing, as a person does: open the menu with a right-click, click More, then the
 * command. `testId` is the command's localization id, e.g.
 * "CollectionTab.BookMenu.ExportToSpreadsheet". Waits until the command knows whether the
 * subscription allows it, since until then it looks like any other. Fails naming what the menu
 * offered when the command is not there.
 */
export async function chooseFromSelectedBookMoreMenuByTestId(
    page: Page,
    testId: string,
): Promise<void> {
    await openSelectedBookMenu(page);
    await realClick(
        page.locator(
            `${kShowingBookMenuEntries}[data-testid="CollectionTab.ContextMenu.More"]`,
        ),
    );
    const item = page.locator(
        `${kShowingBookMenuEntries}[data-testid="${testId}"]`,
    );
    try {
        await item.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        const offered = await page
            .locator(kShowingBookMenuEntries)
            .evaluateAll((items) =>
                items.map((i) => i.getAttribute("data-testid") ?? "(no id)"),
            );
        throw new Error(
            `The book menu's More submenu has no "${testId}"; the menu offers: ${offered.join(", ")}.`,
        );
    }
    await expect(
        item.and(page.locator("[data-feature-status-pending]")),
        `"${testId}" should know whether the subscription allows it`,
    ).toHaveCount(0, { timeout: 15000 });
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
    const showingEntries = page.locator(kShowingBookMenuEntries);
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
 * Open the collection's own menu on the Collections tab, which must be showing, as a person does:
 * click the triangle beside the collection's name above its books. Returns once its entries show.
 */
export async function openCollectionMenu(page: Page): Promise<void> {
    await waitForCollectionReady(page);
    const name = (
        await apiGet(page, "collections/getCurrentEditableCollectionName")
    ).body;
    // The name heading holds the triangle, the one button in it.
    await realClick(
        page
            .locator("h1")
            .filter({ has: page.getByText(name, { exact: true }) })
            .getByRole("button"),
    );
    await page
        .locator(kShowingBookMenuEntries)
        .first()
        .waitFor({ state: "visible", timeout: 15000 });
}

/**
 * The test ids (their localization ids) of the entries the open collection menu shows, at the top
 * level, or in the submenu of the entry whose test id is `submenuTestId`, which this opens by
 * hovering it, as a person does.
 */
export async function readCollectionMenu(
    page: Page,
    submenuTestId?: string,
): Promise<string[]> {
    const showing = page.locator(kShowingBookMenuEntries);
    const ids = () =>
        showing.evaluateAll((items) =>
            items.map((i) => i.getAttribute("data-testid") ?? "(no id)"),
        );
    if (!submenuTestId) return ids();
    const before = await ids();
    await page
        .locator(`${kShowingBookMenuEntries}[data-testid="${submenuTestId}"]`)
        .hover();
    // The submenu adds its entries to the ones showing.
    await expect
        .poll(async () => (await ids()).length, {
            timeout: 15000,
            message: `The collection menu's "${submenuTestId}" did not open its submenu.`,
        })
        .toBeGreaterThan(before.length);
    const after = await ids();
    return after.filter((id) => !before.includes(id));
}

/**
 * Close the open collection menu with Escape, as a person does, and wait until it has gone. A
 * submenu that was opened by hovering stays open, and keeps the menu open, while the pointer is on
 * it (BL-17198), so the pointer moves off the menu first.
 */
export async function closeCollectionMenu(page: Page): Promise<void> {
    const showingEntries = page.locator(kShowingBookMenuEntries);
    await expect(
        showingEntries.first(),
        "there is no collection menu open to close",
    ).toBeVisible();
    // The top left corner of the page, the Collections tab's button, which no menu covers.
    await page.mouse.move(1, 1);
    await expect(async () => {
        await page.keyboard.press("Escape");
        await expect(showingEntries).toHaveCount(0, { timeout: 1000 });
    }, "the collection menu did not close").toPass({ timeout: 10000 });
}

/**
 * Choose a command from the collection's own menu on the Collections tab, which must be showing:
 * open the menu, hover each submenu entry of `testIds` but the last, and click the last. Each is
 * an entry's test id, its localization id, e.g. ["CollectionTab.ContextMenu.Troubleshooting",
 * "CollectionTab.CollectionMenu.doChecksOfAllBooks"]. Fails naming what the menu offered when an
 * entry is not there.
 */
export async function chooseFromCollectionMenu(
    page: Page,
    testIds: string[],
): Promise<void> {
    await openCollectionMenu(page);
    for (const [index, testId] of testIds.entries()) {
        const item = page.locator(
            `${kShowingBookMenuEntries}[data-testid="${testId}"]`,
        );
        try {
            await item.waitFor({ state: "visible", timeout: 15000 });
        } catch {
            const offered = await page
                .locator(kShowingBookMenuEntries)
                .evaluateAll((items) =>
                    items.map(
                        (i) => i.getAttribute("data-testid") ?? "(no id)",
                    ),
                );
            throw new Error(
                `The collection menu has no "${testId}"; it offers: ${offered.join(", ")}.`,
            );
        }
        if (index < testIds.length - 1) await item.hover();
        else await realClick(item);
    }
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

/** The bookInstanceId in the meta.json of the book in `bookFolder`. */
export function readBookId(bookFolder: string): string {
    return JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
    ).bookInstanceId;
}

/**
 * The folder of the book whose meta.json has this bookInstanceId, among the books in
 * `collectionDir`. For a book whose folder Bloom may have renamed since, as it does when the
 * book's title changes.
 */
export function findBookFolderById(
    collectionDir: string,
    bookId: string,
): string {
    const folders = fs
        .readdirSync(collectionDir, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => Path.join(collectionDir, entry.name))
        .filter((folder) => fs.existsSync(Path.join(folder, "meta.json")));
    const match = folders.find((folder) => readBookId(folder) === bookId);
    if (!match)
        throw new Error(
            `No book in ${collectionDir} has the id ${bookId}. Folders: ` +
                folders.map((f) => Path.basename(f)).join(", "),
        );
    return match;
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

/** The front cover in the Collections tab's preview of the selected book. */
export function collectionPreviewCover(page: Page): Locator {
    return collectionBookPreview(page)
        .locator(".bloom-page.coverColor")
        .first();
}

/** The picture in the front cover's picture slot, in the Collections tab's preview. */
export function collectionPreviewCoverPicture(page: Page): Locator {
    return collectionPreviewCover(page).locator(FIRST_IMAGE).first();
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

// The thumbnail a book's tile shows. The tile's <img> asks collections/book/thumbnail, which
// answers with the book's thumbnail.png, or with an icon from Bloom's resources while that file is
// missing. Bloom serves a book's files with no caching, so the picture the tile loaded cannot be
// fetched again; instead readBookTileThumbnail draws the tile's own <img> and the thumbnail.png
// read from disk, and waits until they are the same picture, pixel for pixel.

/** The thumbnail a book's tile shows. */
export interface ITileThumbnail {
    width: number;
    height: number;
    /** Its pixels, RGBA, row by row. */
    pixels: number[];
    /**
     * For the top row (left to right) and the left column (top to bottom): the lengths of the
     * runs of black and of other pixels, alternating, starting with black (so a first run of 0
     * means the edge starts with a pixel that is not black).
     */
    topRuns: number[];
    leftRuns: number[];
}

/** The tile's picture of the book with this id, among the tiles of every collection shown. */
function tileImage(page: Page, bookId: string) {
    return page
        .locator(
            `.book-button[data-book-id="${bookId}"] .thumbnail-wrapper img`,
        )
        .first();
}

/**
 * Wait until the tile of the book in `bookFolder` shows that book's thumbnail.png, and return that
 * picture. The Collections tab must be showing.
 */
export async function readBookTileThumbnail(
    page: Page,
    bookFolder: string,
    bookId: string,
): Promise<ITileThumbnail> {
    const thumbnailFile = Path.join(bookFolder, "thumbnail.png");
    const image = tileImage(page, bookId);
    await expect(
        image,
        `the Collections tab shows no tile picture for the book in ${bookFolder}`,
    ).toBeVisible({ timeout: 30000 });

    let result: ITileThumbnail | string = "not read yet";
    try {
        await expect
            .poll(
                async () => {
                    if (!fs.existsSync(thumbnailFile)) {
                        result = `Bloom has not made ${thumbnailFile}`;
                        return false;
                    }
                    let fileBase64: string;
                    try {
                        fileBase64 = fs
                            .readFileSync(thumbnailFile)
                            .toString("base64");
                    } catch (error) {
                        // Bloom may be writing it.
                        result = `could not read ${thumbnailFile}: ${error}`;
                        return false;
                    }
                    result = await image.evaluate(async (img, fileBase64) => {
                        const tileImg = img as HTMLImageElement;
                        if (!tileImg.complete || tileImg.naturalWidth === 0)
                            return "the tile's picture has not loaded";
                        const fileImg = new Image();
                        fileImg.src = `data:image/png;base64,${fileBase64}`;
                        try {
                            await fileImg.decode();
                        } catch {
                            return "thumbnail.png on disk is not a picture yet";
                        }
                        const draw = (source: HTMLImageElement) => {
                            const canvas = document.createElement("canvas");
                            canvas.width = source.naturalWidth;
                            canvas.height = source.naturalHeight;
                            const context = canvas.getContext("2d")!;
                            context.drawImage(source, 0, 0);
                            return context.getImageData(
                                0,
                                0,
                                canvas.width,
                                canvas.height,
                            );
                        };
                        const shown = draw(tileImg);
                        const onDisk = draw(fileImg);
                        if (
                            shown.width !== onDisk.width ||
                            shown.height !== onDisk.height ||
                            shown.data.some((v, i) => v !== onDisk.data[i])
                        )
                            return (
                                `the tile shows a ${shown.width}x${shown.height} picture ` +
                                `that is not thumbnail.png (${onDisk.width}x${onDisk.height})`
                            );
                        const data = shown.data;
                        const isBlack = (x: number, y: number) => {
                            const i = (y * shown.width + x) * 4;
                            return (
                                data[i + 3] > 200 &&
                                data[i] < 40 &&
                                data[i + 1] < 40 &&
                                data[i + 2] < 40
                            );
                        };
                        const runs = (
                            count: number,
                            black: (n: number) => boolean,
                        ) => {
                            const result = [0];
                            let inBlack = true;
                            for (let n = 0; n < count; n++) {
                                if (black(n) !== inBlack) {
                                    inBlack = !inBlack;
                                    result.push(0);
                                }
                                result[result.length - 1]++;
                            }
                            return result;
                        };
                        return {
                            width: shown.width,
                            height: shown.height,
                            pixels: Array.from(data),
                            topRuns: runs(shown.width, (x) => isBlack(x, 0)),
                            leftRuns: runs(shown.height, (y) => isBlack(0, y)),
                        };
                    }, fileBase64);
                    return typeof result !== "string";
                },
                { timeout: 60000 },
            )
            .toBe(true);
    } catch (error) {
        // The poll fails either on its timeout, when `result` says what the last try saw, or
        // because reading the tile threw, which `error` says.
        throw new Error(
            `The tile of the book in ${bookFolder} never showed its thumbnail.png. ` +
                `Last try: ${String(result)}. ${error}`,
        );
    }
    if (typeof result === "string") throw new Error(result);
    return result;
}

/** The number of black dashes in a run list from readBookTileThumbnail. */
export function countDashes(runs: number[]): number {
    return runs.filter((length, i) => i % 2 === 0 && length > 0).length;
}

/**
 * How many pixels differ between two thumbnails of the same size, leaving out a band `margin`
 * pixels wide around the edge (where a template's border is drawn).
 */
export function countThumbnailPixelsDifferingInside(
    a: ITileThumbnail,
    b: ITileThumbnail,
    margin: number,
): number {
    if (a.width !== b.width || a.height !== b.height)
        throw new Error(
            `the thumbnails differ in size: ${a.width}x${a.height} and ${b.width}x${b.height}`,
        );
    let count = 0;
    for (let y = margin; y < a.height - margin; y++)
        for (let x = margin; x < a.width - margin; x++) {
            const i = (y * a.width + x) * 4;
            if ([0, 1, 2, 3].some((c) => a.pixels[i + c] !== b.pixels[i + c]))
                count++;
        }
    return count;
}

/** What the info icon above the Collections tab's preview tells about the selected book. */
export interface IBookInfoIndicator {
    /** The book's folder, under "Path on disk". */
    path: string;
    /** The book's id (its bookInstanceId), under "Book ID". */
    id: string;
    /** The book's theme, under "Theme"; undefined when the tip shows no theme. */
    theme: string | undefined;
}

/**
 * Point at the info icon (a circled i) at the top right of the Collections tab's book pane, which
 * must be showing a book, and read the tip it opens: the selected book's path on disk, its id and
 * its theme (BookInfoIndicator.tsx). The tip opens on hover; a click on the icon does nothing more.
 * The headings of the tip are not localized.
 *
 * The pane asks Bloom for these facts again when another book is selected, and shows the last
 * book's until the answer comes. So this waits until the tip shows `expectedBookId` as its book id,
 * and fails if it never does. Only text that is drawn counts: a heading or value that is hidden,
 * transparent or empty is read as missing. Moves the pointer away afterwards, which closes the tip.
 */
export async function readBookInfoIndicator(
    page: Page,
    expectedBookId: string,
): Promise<IBookInfoIndicator> {
    const icon = page
        .getByTestId(kBookPane)
        .locator("svg[data-testid='InfoOutlinedIcon']");
    await expect(
        icon,
        "the book pane should show the info icon above the preview",
    ).toBeVisible({ timeout: 30000 });
    await icon.hover();
    const tip = page.getByRole("tooltip").filter({ hasText: "Path on disk" });
    await expect(
        tip,
        "the info icon should open a tip with the book's path on disk",
    ).toBeVisible({ timeout: 15000 });
    const readShownSections = () =>
        tip.locator("p").evaluateAll((paragraphs) => {
            const options = {
                opacityProperty: true,
                visibilityProperty: true,
                contentVisibilityAuto: true,
            };
            const drawn = (element: Element) => {
                const box = element.getBoundingClientRect();
                return (
                    element.checkVisibility(options) &&
                    box.width > 0 &&
                    box.height > 0
                );
            };
            return paragraphs
                .filter((p) => drawn(p))
                .map((p) => {
                    const headingElement = p.querySelector("b");
                    const heading =
                        headingElement && drawn(headingElement)
                            ? (headingElement.textContent ?? "").trim()
                            : "";
                    // The value is everything in the paragraph after the heading: text, or an
                    // element (the path is a link) that is itself drawn.
                    const value = Array.from(p.childNodes)
                        .filter((node) => node !== headingElement)
                        .filter(
                            (node) => !(node instanceof Element) || drawn(node),
                        )
                        .map((node) => node.textContent ?? "")
                        .join("")
                        .trim();
                    return { heading, value };
                });
        });
    let sections: { heading: string; value: string }[] = [];
    const valueUnder = (heading: string) =>
        sections.find((s) => s.heading === heading)?.value || undefined;
    await expect
        .poll(
            async () => {
                sections = await readShownSections();
                return valueUnder("Book ID");
            },
            {
                timeout: 30000,
                message: "the info tip never showed the selected book's id",
            },
        )
        .toBe(expectedBookId);
    const path = valueUnder("Path on disk");
    if (!path)
        throw new Error(
            `The info tip showed no path on disk: ${JSON.stringify(sections)}`,
        );
    await page.mouse.move(0, 0);
    await expect(tip, "the info tip should close").toBeHidden({
        timeout: 15000,
    });
    return { path, id: expectedBookId, theme: valueUnder("Theme") };
}
