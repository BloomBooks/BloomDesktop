// Change the book's page size and orientation.
//
// This is SETUP: a test uses it to get a page drawn landscape, or at another size, so that it can
// then ask whether what is on the page still looks right. It therefore goes by the same route the
// Edit tab's size menu posts on, rather than opening that menu: the menu's entries carry no id of
// their own, so clicking one would mean matching the localized words Bloom shows, and the words
// are not the subject. A test whose subject IS the size menu should click the menu instead.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { waitForEditablePage, waitForEditTabSettled } from "./bookMaking";

/** One of the sizes Bloom offers for the book, as its layout-choice API describes it. */
interface ILayoutChoice {
    /** The layout id, which is also the class Bloom puts on the page, e.g. "A5Landscape". */
    id: string;
    /** The name the menu shows, localized. */
    label: string;
}

interface ILayoutChoiceData {
    choices: ILayoutChoice[];
    currentLayoutChoiceId: string;
}

/** The sizes this book can be drawn at, and which one it is at now. */
export async function getPageSizeChoices(
    page: Page,
): Promise<ILayoutChoiceData> {
    return apiGetJson<ILayoutChoiceData>(
        page,
        "editView/topBar/layoutChoiceData",
    );
}

/**
 * Draw the book at a page size and orientation, by its layout id, e.g. "A5Portrait" or
 * "A5Landscape", and wait until Bloom has rebuilt the page at it.
 *
 * The change is to the whole book, and Bloom rebuilds the page being edited, so anything a test
 * measured before this call has to be measured again after it.
 */
export async function setPageSize(page: Page, layoutId: string): Promise<void> {
    const data = await getPageSizeChoices(page);
    // The endpoint answers with the sizes this book's template allows, and only those, so being
    // in the list is the whole test of whether a size can be asked for.
    if (!data.choices.some((choice) => choice.id === layoutId))
        throw new Error(
            `This book cannot be drawn at "${layoutId}". It offers: ` +
                `${data.choices.map((c) => c.id).join(", ")}.`,
        );
    await apiPost(
        page,
        "editView/topBar/layoutChoiceChange",
        JSON.stringify({ layoutChoiceId: layoutId }),
        "application/json",
    );
    await expect
        .poll(async () => getPageSize(page), {
            timeout: 60000,
            message: `Asking for "${layoutId}" did not change the page's size.`,
        })
        .toBe(layoutId);
    await waitForEditablePage(page);
    // The rebuilt page announces itself more than once, and the Edit tab drops a request that
    // changes the page (such as adding one) if it comes between those announcements.
    await waitForEditTabSettled(page);
}

/**
 * Ask for a page size as setPageSize() does, and return at once, without waiting for Bloom to
 * draw the page at it. For a test of what happens while Bloom is still busy with the change.
 */
export async function askForPageSize(
    page: Page,
    layoutId: string,
): Promise<void> {
    await apiPost(
        page,
        "editView/topBar/layoutChoiceChange",
        JSON.stringify({ layoutChoiceId: layoutId }),
        "application/json",
    );
}

/**
 * The layout the page being edited is drawn at, as the page div's own class records it: Bloom puts
 * the layout id, e.g. "A5Portrait", on the page element.
 */
export async function getPageSize(page: Page): Promise<string> {
    const frame = page.frame({ name: "page" });
    if (!frame) return "";
    return frame
        .locator(".bloom-page")
        .first()
        .evaluate(
            (element) =>
                [...element.classList].find((c) =>
                    /^[A-Za-z0-9]+(Portrait|Landscape|Square)$/.test(c),
                ) ?? "",
        )
        .catch(() => "");
}

/**
 * The page layout update level the book's file records, as last saved: 0 (or missing, read as 0)
 * when its pages need the page layout update, as they do after a change of page size, and higher
 * once Bloom has run it (BookProcessor.RecordPageLayoutUpdateDone). Publish and the AI image editor
 * run the update, loading every page off-screen, only for a book below the current level. Read from
 * disk, so it says what Bloom last wrote: a change of page size is written at once, and so is the
 * update.
 */
export function getSavedPageLayoutUpdateLevel(bookFolder: string): number {
    const htmFiles = fs
        .readdirSync(bookFolder)
        .filter((name) => name.toLowerCase().endsWith(".htm"));
    if (htmFiles.length !== 1)
        throw new Error(
            `Expected one .htm file in ${bookFolder}, found: ${htmFiles.join(", ") || "none"}.`,
        );
    const html = fs.readFileSync(Path.join(bookFolder, htmFiles[0]), "utf8");
    const match =
        /<meta[^>]*name="pageLayoutUpdateLevel"[^>]*content="(\d+)"/.exec(html);
    return match ? Number(match[1]) : 0;
}
