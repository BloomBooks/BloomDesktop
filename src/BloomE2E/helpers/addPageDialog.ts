// Drive and read the Add Page dialog: the one the Add Page button in the Edit tab's page list opens.
//
// The dialog is a React dialog that Bloom mounts into whichever document the Edit tab is showing,
// so these helpers find it by looking through every frame rather than assuming one. What the dialog
// shows is a list of groups, one per template book, each a row of thumbnails; the thumbnails carry
// no visible label, so the label and the template book of each one are read out of its image URL,
// which is how the dialog itself identifies them (see getTemplatePageImageSource in
// PageChooserDialog.tsx).
//
// helpers/bookMaking.ts has addPage(), the API route to the same result; a test that only needs a
// page in its book uses that. These helpers are for tests whose subject is the dialog.

import { expect, type Locator, type Page } from "@playwright/test";
import * as Path from "node:path";
import { getPages, waitForEditablePage } from "./bookMaking";

/** One thumbnail in the Add Page dialog. */
export interface IAddPageThumbnail {
    /** The template page's label, e.g. "Basic Text & Image". */
    label: string;
    /** The folder of the template book the page comes from, as an OS path. */
    templateBookFolder: string;
    /** True when the browser has the image and it has a size; false while loading or on error. */
    thumbnailLoaded: boolean;
}

/**
 * One group of thumbnails in the Add Page dialog: one template book. The dialog groups by title,
 * so two template books of the same title share one group, and `templateBookFolder` is then the
 * folder of the first of them.
 */
export interface IAddPageGroup {
    /**
     * The group heading as shown. It is the template book's title, localized where Bloom has a
     * translation: Basic Book is headed "Basic Pages" in English. Identify a group by its folder.
     */
    title: string;
    /** The folder of the template book, as an OS path; its last segment is e.g. "Basic Book". */
    templateBookFolder: string;
    pages: IAddPageThumbnail[];
}

/**
 * Click the real Add Page button in the Edit tab's page list, and wait until the dialog is showing
 * its template pages with every thumbnail loaded. A book must be showing in the Edit tab.
 */
export async function openAddPageDialog(page: Page): Promise<void> {
    await waitForEditablePage(page);
    const pageList = page.frame({ name: "pageList" });
    if (!pageList)
        throw new Error(
            "There is no 'pageList' frame, so the Edit tab is not showing its page list. " +
                `Frames: ${page
                    .frames()
                    .map((f) => f.name() || "(main)")
                    .join(", ")}.`,
        );
    const button = pageList.getByRole("button", { name: "Add Page" });
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.click();
    await findAddPageDialog(page);
    await waitForAddPageThumbnails(page);
}

/**
 * The open Add Page dialog, wherever Bloom mounted it. Throws, naming the frames it looked in, if
 * no dialog appears within the timeout.
 */
async function findAddPageDialog(
    page: Page,
    timeoutMs = 30000,
): Promise<Locator> {
    let found: Locator | undefined;
    await expect
        .poll(
            async () => {
                for (const frame of page.frames()) {
                    const dialog = frame
                        .locator('[role="dialog"]')
                        .filter({ hasText: "Add Page" });
                    if ((await dialog.count().catch(() => 0)) > 0) {
                        found = dialog.first();
                        return true;
                    }
                }
                return false;
            },
            {
                timeout: timeoutMs,
                message:
                    "The Add Page dialog never appeared. Frames: " +
                    page
                        .frames()
                        .map((f) => f.name() || "(main)")
                        .join(", "),
            },
        )
        .toBe(true);
    return found!;
}

/** True while an Add Page dialog is open in any frame. */
async function isAddPageDialogOpen(page: Page): Promise<boolean> {
    for (const frame of page.frames()) {
        const count = await frame
            .locator('[role="dialog"]')
            .filter({ hasText: "Add Page" })
            .count()
            .catch(() => 0);
        if (count > 0) return true;
    }
    return false;
}

/**
 * Wait until the dialog has at least one group of thumbnails and every thumbnail image has loaded.
 * Fails naming the images that never loaded, which is what a broken template page looks like from
 * the dialog's side.
 */
async function waitForAddPageThumbnails(
    page: Page,
    timeoutMs = 60000,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const groups = await getAddPageDialogGroups(page);
                if (groups.length === 0) return "no groups yet";
                const pending = groups
                    .flatMap((g) => g.pages)
                    .filter((p) => !p.thumbnailLoaded)
                    .map((p) => `${p.label} (${p.templateBookFolder})`);
                return pending.length === 0 ? "" : pending.join(", ");
            },
            {
                timeout: timeoutMs,
                message:
                    "The Add Page dialog never showed all of its thumbnails loaded.",
            },
        )
        .toBe("");
}

/**
 * Every group the Add Page dialog is showing, in order, with the thumbnails in each. Reads the
 * dialog's DOM: the heading of each group and the image URL of each thumbnail, which names the
 * template book folder and the page label.
 */
export async function getAddPageDialogGroups(
    page: Page,
): Promise<IAddPageGroup[]> {
    const dialog = await findAddPageDialog(page);
    const raw = await dialog.evaluate((dialogElement) => {
        const result: {
            title: string;
            pages: { src: string; loaded: boolean }[];
        }[] = [];
        for (const group of Array.from(
            dialogElement.querySelectorAll(".templateBookGroup"),
        )) {
            // The heading is the group's previous sibling: a Typography h6 holding a Span.
            const heading = group.previousElementSibling;
            const title = heading?.textContent?.trim() ?? "";
            const pages = Array.from(group.querySelectorAll("img")).map(
                (img) => ({
                    src: img.getAttribute("src") ?? "",
                    loaded: img.complete && img.naturalWidth > 0,
                }),
            );
            result.push({ title, pages });
        }
        return result;
    });
    return raw.map((group) => {
        const pages = group.pages.map((p) => ({
            ...parseThumbnailSource(p.src),
            thumbnailLoaded: p.loaded,
        }));
        if (pages.length === 0)
            throw new Error(
                `The Add Page dialog group "${group.title}" has no thumbnails.`,
            );
        return {
            title: group.title,
            templateBookFolder: pages[0].templateBookFolder,
            pages,
        };
    });
}

/**
 * Read the template book folder and page label out of a thumbnail's image URL. The dialog builds
 * the URL as <api>pageTemplateThumbnail?path=<encoded folder>/template/<encoded label>[-landscape].svg...
 */
function parseThumbnailSource(src: string): {
    label: string;
    templateBookFolder: string;
} {
    const match = src.match(/path=(.*?)\/template\/(.*?)(-landscape)?\.svg/);
    if (!match)
        throw new Error(
            `An Add Page thumbnail has an image URL this helper cannot read: ${src}`,
        );
    return {
        templateBookFolder: Path.normalize(decodeURIComponent(match[1])),
        label: decodeURIComponent(match[2]),
    };
}

/**
 * Scroll the dialog's list of template pages to the bottom and back to the top, `times` times,
 * with real mouse wheel events over the list. Returns how far the list can scroll, in pixels: zero
 * means every group fit without scrolling, so there was nothing to scroll.
 */
export async function scrollAddPageDialog(
    page: Page,
    times = 1,
): Promise<number> {
    const dialog = await findAddPageDialog(page);
    const list = dialog.locator(".groupDisplay");
    await list.waitFor({ state: "visible", timeout: 30000 });
    const maxScroll = await list.evaluate(
        (element) => element.scrollHeight - element.clientHeight,
    );
    if (maxScroll <= 0) return 0;

    const box = await list.boundingBox();
    if (!box)
        throw new Error(
            "The Add Page dialog's list of pages has no on-screen box to scroll.",
        );
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    const scrollTop = () => list.evaluate((element) => element.scrollTop);
    for (let i = 0; i < times; i++) {
        await page.mouse.wheel(0, maxScroll + 200);
        await expect
            .poll(scrollTop, {
                timeout: 15000,
                message: `Scrolling the Add Page dialog down (round ${i + 1}) never reached the bottom.`,
            })
            .toBeGreaterThanOrEqual(maxScroll - 1);
        await page.mouse.wheel(0, -(maxScroll + 200));
        await expect
            .poll(scrollTop, {
                timeout: 15000,
                message: `Scrolling the Add Page dialog up (round ${i + 1}) never reached the top.`,
            })
            .toBe(0);
    }
    return maxScroll;
}

/** Close the Add Page dialog with its title-bar Close button, and wait until it is gone. */
export async function closeAddPageDialog(page: Page): Promise<void> {
    const dialog = await findAddPageDialog(page);
    await dialog.getByRole("button", { name: "Close" }).click();
    await expect
        .poll(() => isAddPageDialogOpen(page), {
            timeout: 30000,
            message: "The Add Page dialog never closed.",
        })
        .toBe(false);
}

/**
 * In the open Add Page dialog, select the thumbnail with this label and click the dialog's own
 * Add Page button, then wait for the book to have one more page and for the Edit tab to show it.
 * Pass `templateBookFolderName`, e.g. "Basic Book", when more than one group offers a page with
 * the same label.
 */
export async function addPageFromDialog(
    page: Page,
    label: string,
    templateBookFolderName?: string,
): Promise<void> {
    const dialog = await selectPageInAddPageDialog(
        page,
        label,
        templateBookFolderName,
    );
    const before = (await getPages(page)).length;
    await dialog.getByRole("button", { name: "Add Page", exact: true }).click();
    await expect
        .poll(() => isAddPageDialogOpen(page), {
            timeout: 30000,
            message: "The Add Page dialog did not close after Add Page.",
        })
        .toBe(false);
    await expect
        .poll(async () => (await getPages(page)).length, {
            timeout: 60000,
            message: `Bloom never added the "${label}" page from the dialog.`,
        })
        .toBe(before + 1);
    await waitForEditablePage(page);
}

/**
 * In the open Add Page dialog, select the thumbnail with this label and wait until the dialog's
 * right-hand pane is showing it. Returns the dialog, so a caller can go on to read what the pane
 * offers for the selected page. Adds nothing to the book.
 *
 * This is separate from addPageFromDialog because a page whose feature the collection's
 * subscription tier does not include can be selected but not added: the pane replaces the Add
 * Page button with a notice saying what subscription it needs.
 */
export async function selectPageInAddPageDialog(
    page: Page,
    label: string,
    templateBookFolderName?: string,
): Promise<Locator> {
    const dialog = await findAddPageDialog(page);
    const groups = await getAddPageDialogGroups(page);
    const group = groups.find(
        (g) =>
            (!templateBookFolderName ||
                Path.basename(g.templateBookFolder) ===
                    templateBookFolderName) &&
            g.pages.some((p) => p.label === label),
    );
    if (!group)
        throw new Error(
            `The Add Page dialog offers no page called "${label}"` +
                (templateBookFolderName
                    ? ` from "${templateBookFolderName}"`
                    : "") +
                `. It offers: ` +
                groups
                    .map(
                        (g) =>
                            `${g.title} (${Path.basename(g.templateBookFolder)}): ` +
                            g.pages.map((p) => p.label).join(", "),
                    )
                    .join("; ") +
                ".",
        );
    const groupIndex = groups.indexOf(group);
    const pageIndex = group.pages.findIndex((p) => p.label === label);
    // Each thumbnail is an img inside a frame div; a transparent overlay div, its previous sibling,
    // takes the click.
    const thumbnail = dialog
        .locator(".templateBookGroup")
        .nth(groupIndex)
        .locator("img")
        .nth(pageIndex);
    const overlay = thumbnail.locator("xpath=../preceding-sibling::div[1]");
    await overlay.click();
    // The right-hand pane shows the selected page's label as its caption.
    await expect(
        dialog.getByText(label, { exact: true }),
        `Selecting "${label}" never showed it in the dialog's preview pane.`,
    ).toBeVisible({ timeout: 15000 });
    return dialog;
}

/** What the Add Page dialog offers for the page that is selected. */
export interface IAddPageOffer {
    /** True when the dialog is offering its Add Page button, so the page can be added. */
    addButtonOffered: boolean;
    /**
     * True when the pane is showing the notice that names the subscription the selected page's
     * feature needs. That notice takes the place of the Add Page button.
     */
    requiresSubscriptionNotice: boolean;
}

/**
 * What the dialog is offering for the page that is selected: the Add Page button, or the notice
 * that says what subscription the page's feature needs. Select a page first.
 *
 * The notice is found by the class its own markup carries rather than by its words, which are
 * localized (RequiresSubscriptionNotice in react_components/requiresSubscription.tsx).
 */
export async function getAddPageOffer(page: Page): Promise<IAddPageOffer> {
    const dialog = await findAddPageDialog(page);
    return {
        addButtonOffered:
            (await dialog
                .getByRole("button", { name: "Add Page", exact: true })
                .count()) > 0,
        requiresSubscriptionNotice:
            (await dialog.locator(".messageSettingsDialogWrapper").count()) > 0,
    };
}
