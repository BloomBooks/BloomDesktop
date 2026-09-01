// Drive the Edit tab's page thumbnail list: select a page, open its menu, and run a command.
//
// Three facts about this pane shape everything here, and each one has cost someone time:
//
//  1. The list lives in the Edit tab's `#pageList` iframe, but React renders it into
//     `#pageGridWrapper`, replacing the `#pageGrid` div the pug file declares. So the
//     thumbnails are under `#pageGridWrapper`, not `#pageGrid`.
//  2. The menu itself is rendered with a portal into the PARENT document, so that it can
//     extend past the narrow iframe. A test therefore clicks in the iframe to open it and
//     then queries the shell page for the items.
//  3. Opening the menu on a thumbnail that is not already selected does nothing at all
//     (pageThumbnailList.tsx bails out). Selection is a round trip through C# and back over
//     a websocket, so a test has to wait for the selection to arrive before it opens the menu.
//
// The menu items are matched by their English labels because they carry no test ids; see the
// entry in AUTOMATION-DEBT.md.

import {
    expect,
    type FrameLocator,
    type Locator,
    type Page,
} from "@playwright/test";
import { apiGet } from "./api";

/** A command in a page thumbnail's context menu, named as the menu shows it in English. */
export type PageMenuCommand =
    | "Copy Page"
    | "Paste Page"
    | "Duplicate Page"
    | "Choose Different Layout"
    | "Remove Page";

// Bloom's own name for each command, which is what the enabled/clicked APIs speak.
const COMMAND_ID: Record<PageMenuCommand, string> = {
    "Copy Page": "copyPage",
    "Paste Page": "pastePage",
    "Duplicate Page": "duplicatePage",
    "Choose Different Layout": "chooseDifferentLayout",
    "Remove Page": "removePage",
};

/** The page-thumbnail pane's iframe inside the Edit tab. */
export function pageListFrame(page: Page): FrameLocator {
    return page.frameLocator("#pageList");
}

/** Every real page thumbnail, in the order the pane shows them. */
function thumbnails(page: Page): Locator {
    return pageListFrame(page).locator(
        "#pageGridWrapper .gridItem:not(.placeholder)",
    );
}

/**
 * Wait until the thumbnail pane has finished loading and report the page ids it shows, in
 * order. A thumbnail's element id is the page's own id, which is what the saved book HTML
 * uses too, so this is how a test ties a thumbnail to a page in the file.
 */
export async function getPageIds(
    page: Page,
    timeoutMs = 60000,
): Promise<string[]> {
    await thumbnails(page).first().waitFor({ timeout: timeoutMs });
    return thumbnails(page).evaluateAll((elements) =>
        elements.map((element) => element.id),
    );
}

/** Wait until the thumbnail pane shows exactly `count` pages. */
export async function waitForPageCount(
    page: Page,
    count: number,
    timeoutMs = 60000,
): Promise<void> {
    await expect
        .poll(async () => (await getPageIds(page, timeoutMs)).length, {
            timeout: timeoutMs,
            message: `The page thumbnail list never showed ${count} pages.`,
        })
        .toBe(count);
}

/**
 * Click a page's thumbnail and wait for Bloom to report it selected. The click target is the
 * transparent cover over the thumbnail, which is what a person hits; the thumbnail's own
 * content ignores clicks.
 */
export async function selectPage(
    page: Page,
    pageId: string,
    timeoutMs = 60000,
): Promise<void> {
    // An attribute selector, not `#id`: Bloom's page ids are GUIDs that may start with a
    // digit, which a bare id selector cannot express.
    const thumbnail = pageListFrame(page).locator(
        `#pageGridWrapper .gridItem[id="${pageId}"]`,
    );
    await thumbnail.waitFor({ timeout: timeoutMs });
    await thumbnail
        .locator(".invisibleThumbnailCover")
        .click({ timeout: timeoutMs });
    // Selection goes to C# and comes back over a websocket, so it is not done when the click is.
    await expect(thumbnail).toHaveClass(/gridSelected/, { timeout: timeoutMs });
    await waitForEditablePage(page, pageId, timeoutMs);
}

/**
 * Wait until the Edit tab is actually showing `pageId` and has finished loading it.
 *
 * This matters more than it looks. While the page is still loading, Bloom's editing model is in
 * its Navigating state, and several commands — Copy Page among them — quietly do nothing at all
 * in that state rather than failing. A test that clicks Copy Page too early gets no error and an
 * empty clipboard.
 */
export async function waitForEditablePage(
    page: Page,
    pageId: string,
    timeoutMs = 60000,
): Promise<void> {
    await page
        .frameLocator("#page")
        .locator(`.bloom-page[id="${pageId}"]`)
        .waitFor({ state: "attached", timeout: timeoutMs });
    // The page's own script tells Bloom it is ready to edit once its DOM has loaded, so wait for
    // the document to be fully loaded rather than merely parsed.
    await expect
        .poll(
            () =>
                page.evaluate(() => {
                    const frame = document.querySelector(
                        "#page",
                    ) as HTMLIFrameElement | null;
                    return frame?.contentDocument?.readyState ?? "none";
                }),
            {
                timeout: timeoutMs,
                message: `The Edit tab never finished loading page ${pageId}.`,
            },
        )
        .toBe("complete");
}

// Property name put on the editable page's document so a later poll can tell whether it is still
// the same document or a reload has replaced it. Bloom reloads a page to the SAME url (the
// in-memory file is named after the page id), so the url cannot answer that question.
const RELOAD_MARKER = "__bloomE2eEditablePageMarker";

/**
 * Mark the document now showing in the Edit tab, so waitForEditablePageReload can tell when
 * Bloom has replaced it. Call this before a command that reloads the page.
 */
export async function markEditablePage(page: Page): Promise<void> {
    await page.evaluate((marker) => {
        const document_ = (
            document.querySelector("#page") as HTMLIFrameElement | null
        )?.contentDocument;
        if (!document_)
            throw new Error(
                "The Edit tab is not showing a page, so there is nothing to mark.",
            );
        (document_ as unknown as Record<string, boolean>)[marker] = true;
    }, RELOAD_MARKER);
}

/**
 * Wait out the page reload that follows a command which saves the book, and for the reloaded
 * page to finish loading. Copy Page is one such command: it saves first, so that unsaved typing
 * is copied too, and Bloom then navigates back to the page. Until that navigation finishes the
 * editing model is in its Navigating state, in which Paste Page silently does nothing while the
 * menu still offers it.
 *
 * Call markEditablePage() before the command.
 */
export async function waitForEditablePageReload(
    page: Page,
    pageId: string,
    timeoutMs = 60000,
): Promise<void> {
    await expect
        .poll(
            () =>
                page.evaluate(
                    (options) => {
                        const document_ = (
                            document.querySelector(
                                "#page",
                            ) as HTMLIFrameElement | null
                        )?.contentDocument;
                        if (!document_) return "no document";
                        if (
                            (document_ as unknown as Record<string, boolean>)[
                                options.marker
                            ]
                        )
                            return "not reloaded yet";
                        if (
                            !document_.querySelector(
                                `.bloom-page[id="${options.pageId}"]`,
                            )
                        )
                            return "showing some other page";
                        return document_.readyState;
                    },
                    { marker: RELOAD_MARKER, pageId },
                ),
            {
                timeout: timeoutMs,
                message: `The Edit tab never reloaded page ${pageId}.`,
            },
        )
        .toBe("complete");
}

/**
 * Wait until Bloom would enable `command` for `pageId`. This is the same question the menu asks
 * as it opens, and it has to be settled first, because the commands run asynchronously: Copy
 * Page returns long before the page is on Bloom's clipboard, so a menu opened straight after it
 * shows Paste Page still greyed out.
 */
export async function waitForPageMenuCommandEnabled(
    page: Page,
    pageId: string,
    command: PageMenuCommand,
    timeoutMs = 30000,
): Promise<void> {
    await expect
        .poll(
            async () =>
                (
                    await apiGet(
                        page,
                        `pageList/contextMenuItemEnabled?commandId=${COMMAND_ID[command]}` +
                            `&pageId=${encodeURIComponent(pageId)}`,
                    )
                ).body,
            {
                timeout: timeoutMs,
                message: `Bloom never enabled the page menu's "${command}" command.`,
            },
        )
        .toBe("true");
}

/**
 * Open a page's context menu with the chevron button the pane shows on it, and run one command.
 * The page must already be selected (see selectPage): the menu refuses to open on any other one.
 *
 * This is the real user gesture for Copy Page and Paste Page, which is why the copy-page test
 * goes through here rather than posting pageList/contextMenuItemClicked.
 */
export async function runPageMenuCommand(
    page: Page,
    pageId: string,
    command: PageMenuCommand,
    timeoutMs = 30000,
): Promise<void> {
    await waitForPageMenuCommandEnabled(page, pageId, command, timeoutMs);
    await pageListFrame(page)
        .locator("#menuIconHolder")
        .click({ timeout: timeoutMs });
    const item = pageMenuItem(page, command);
    await item.waitFor({ timeout: timeoutMs });
    await expect(
        item,
        `The page menu's "${command}" command is disabled.`,
    ).toBeEnabled({ timeout: timeoutMs });
    await item.click();
    await expect(page.getByRole("menu")).toHaveCount(0, { timeout: timeoutMs });
}

/**
 * The menu item for a command, in the shell page (the menu is portaled out of the iframe).
 * Exported so a test can assert on a command's enabled state without running it.
 */
export function pageMenuItem(page: Page, command: PageMenuCommand): Locator {
    return page.getByRole("menuitem", { name: command, exact: true });
}
