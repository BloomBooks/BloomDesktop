// Drive and read the Publish tab's ePUB screen: its ePUB mode, and its preview.
//
// The preview is the ePUB itself. Clicking Preview has Bloom stage the ePUB's files, exactly the
// ones Save... then zips, and show them in a reader (Readium's D2 Reader) inside the device frame.
// The reader shows one page of a fixed-layout ePUB at a time, each in a frame of its own, so a test
// reads a page of the publication from that frame.
//
// Saving the .epub file is not here: Save... opens the operating system's save dialog, which no
// test can answer (see AUTOMATION-DEBT.md).

import { expect, type Frame, type Page } from "@playwright/test";
import { apiGet } from "./api";
import { selectPublishDestination } from "./publish";

/** How an ePUB lays its pages out: as Bloom shows them, or however the reader likes. */
export type EpubMode = "fixed" | "flowable";

/** The ePUB screen's Preview button. Found by its aria-label, which is not localized. */
const PREVIEW_BUTTON = '[aria-label="refresh preview"]';

/** The dialog that shows how making the ePUB is going; see PublishProgressDialogInner.tsx. */
const PROGRESS_DIALOG = ".MuiDialog-root.progress-dialog";
/**
 * That dialog's Close button, which it has once the ePUB is made. It is the dialog's only filled
 * button, and its label is not localized, so it is found by how it is drawn.
 */
const PROGRESS_DIALOG_CLOSE_BUTTON =
    ".MuiDialogActions-root button.MuiButton-contained";

/**
 * The reader's button that turns to the next page, found by what it does: it is the right-hand
 * button for a book read left to right (Readium/index.html), and the left-hand one for a book
 * read right to left (indexRtl.html).
 */
const NEXT_PAGE_BUTTON = 'button[onclick*="nextPage"]';

/** Go to the Publish tab's ePUB screen, and wait until it offers its Preview button. */
export async function openEpubPublishScreen(page: Page): Promise<void> {
    await selectPublishDestination(page, "ePUB");
    await page
        .locator(PREVIEW_BUTTON)
        .waitFor({ state: "visible", timeout: 60000 });
}

/**
 * The mode Bloom will publish the selected book's ePUB in, which is the one the ePUB screen's
 * "ePUB mode" list shows. A book with comic bubbles is always "fixed", whatever was chosen for it.
 */
export async function getEpubMode(page: Page): Promise<EpubMode> {
    const mode = (await apiGet(page, "publish/epub/epubMode")).body.trim();
    if (mode !== "fixed" && mode !== "flowable")
        throw new Error(
            `Bloom reported the ePUB mode as "${mode}", which is neither fixed nor flowable.`,
        );
    return mode;
}

/**
 * Click Preview on the ePUB screen, wait until the reader is showing the ePUB's first page, and
 * close the "Creating ePUB" dialog if it is waiting to be closed, so that the preview can be
 * clicked. The ePUB screen must be showing (openEpubPublishScreen).
 *
 * Bloom closes that dialog by itself only when making the ePUB had nothing to tell the user. It
 * usually has, such as which fonts it embedded, and then the dialog stays, over the whole screen,
 * until the user clicks its Close button.
 */
export async function showEpubPreview(page: Page): Promise<void> {
    await page.locator(PREVIEW_BUTTON).click();
    await expect
        .poll(async () => (await getIdOfEpubPreviewPage(page)) !== undefined, {
            timeout: 180000,
            message: "The ePUB preview never showed the book.",
        })
        .toBe(true);

    const dialog = page.locator(PROGRESS_DIALOG);
    const close = dialog.locator(PROGRESS_DIALOG_CLOSE_BUTTON);
    await expect
        .poll(
            async () =>
                (await dialog.count()) === 0 || (await close.isVisible()),
            {
                timeout: 30000,
                message:
                    'The "Creating ePUB" dialog neither went away nor offered its Close ' +
                    "button, though the preview is showing.",
            },
        )
        .toBe(true);
    if ((await dialog.count()) > 0) await close.click();
    await expect(
        dialog,
        'The "Creating ePUB" dialog should be gone.',
    ).toHaveCount(0, { timeout: 30000 });
}

/**
 * Turn the pages of the ePUB preview forward with the reader's own next-page button until it shows
 * the book page whose id in the book is `pageId`, wait until that page has its fonts and pictures,
 * and return the frame the page is in, together with the id the page has there.
 *
 * That id can differ from the book's: an ePUB validator objects to an id that starts with a digit,
 * so Bloom puts an "i" before one (EpubMaker.FixIllegalIds).
 *
 * Only forward: the preview opens on the cover, so every other page is ahead of it.
 */
export async function showEpubPreviewPage(
    page: Page,
    pageId: string,
): Promise<{ frame: Frame; pageId: string }> {
    const wanted = [pageId, `i${pageId}`];
    const seen: string[] = [];
    let shown = await getIdOfEpubPreviewPage(page);
    if (shown === undefined)
        throw new Error(
            "The ePUB preview is not showing a page. Click Preview first (showEpubPreview).",
        );
    // A book has far fewer pages than this; the bound only stops a preview that is not turning.
    for (let turns = 0; turns < 500; turns++) {
        if (wanted.includes(shown)) {
            const frame = getEpubPreviewPageFrame(page);
            if (!frame)
                throw new Error(
                    "The ePUB preview's page went away while it was being read.",
                );
            await waitForEpubPreviewPageToFinishLoading(frame);
            return { frame, pageId: shown };
        }
        seen.push(shown);
        const before = shown;
        await readerFrame(page).locator(NEXT_PAGE_BUTTON).click();
        // On its last page the reader's button does nothing, so this is also how the end of the
        // book shows.
        await expect
            .poll(
                async () => {
                    const now = await getIdOfEpubPreviewPage(page);
                    if (now === undefined || now === before) return false;
                    shown = now;
                    return true;
                },
                {
                    timeout: 30000,
                    message:
                        `The ePUB preview did not turn to another page, and has not shown page ` +
                        `"${pageId}". It showed: ${seen.join(", ")}.`,
                },
            )
            .toBe(true);
    }
    throw new Error(
        `The ePUB preview never showed page "${pageId}", after 500 page turns.`,
    );
}

/** The reader's own document, inside the ePUB screen's device frame, if it is showing. */
function findReaderFrame(page: Page): Frame | undefined {
    return page
        .frames()
        .find((frame) => /\/Readium\/index(Rtl)?\.html/.test(frame.url()));
}

/** The reader's own document. Throws when the ePUB screen is not showing it. */
function readerFrame(page: Page): Frame {
    const reader = findReaderFrame(page);
    if (!reader)
        throw new Error(
            "The ePUB screen is not showing its reader. Click Preview first (showEpubPreview).",
        );
    return reader;
}

/**
 * Whether the reader is loading a page: it is then covering the page with its "Loading" sheet
 * (#reader-loading), from the moment the reader opens, and again from each page turn, until the
 * page is up.
 *
 * Do not click a page-turn button while this is true. The reader works out which page comes next
 * only as it finishes loading one, so a click before that turns nothing, and says nothing.
 */
async function isEpubReaderLoading(page: Page): Promise<boolean> {
    const reader = findReaderFrame(page);
    if (!reader) return true;
    return reader
        .evaluate(() => {
            const sheet = document.getElementById("reader-loading");
            return !sheet || getComputedStyle(sheet).display !== "none";
        })
        .catch(() => true);
}

/**
 * The frame holding the page of the ePUB that the preview is showing: one of the ePUB's own
 * content documents, which the reader loads into a frame inside its own.
 */
function getEpubPreviewPageFrame(page: Page): Frame | undefined {
    return findReaderFrame(page)
        ?.childFrames()
        .find((frame) => /\.xhtml/.test(frame.url()));
}

/**
 * The id of the book page the preview is showing, or undefined while it shows none: before the
 * reader has loaded the book, and between one page and the next.
 */
async function getIdOfEpubPreviewPage(page: Page): Promise<string | undefined> {
    if (await isEpubReaderLoading(page)) return undefined;
    const frame = getEpubPreviewPageFrame(page);
    if (!frame) return undefined;
    return frame
        .evaluate(() => document.querySelector(".bloom-page")?.id)
        .catch(() => undefined);
}

/**
 * Wait until a page of the preview has loaded, with its fonts and every picture it shows. Throws,
 * naming the picture, when one of them failed to load: the browser then calls it complete, but it
 * has no picture, and the page would be measured without it.
 */
async function waitForEpubPreviewPageToFinishLoading(
    frame: Frame,
): Promise<void> {
    let broken: string[] = [];
    await expect
        .poll(
            async () => {
                const state = await frame.evaluate(() => ({
                    loaded:
                        document.readyState === "complete" &&
                        document.fonts.status === "loaded" &&
                        [...document.images].every((image) => image.complete),
                    broken: [...document.images]
                        .filter(
                            (image) =>
                                image.complete && image.naturalWidth === 0,
                        )
                        .map(
                            (image) => image.getAttribute("src") ?? "(no src)",
                        ),
                }));
                broken = state.broken;
                return state.loaded;
            },
            {
                timeout: 30000,
                message:
                    "A page of the ePUB preview never finished loading its fonts and pictures.",
            },
        )
        .toBe(true);
    if (broken.length > 0)
        throw new Error(
            `The ePUB preview could not load ${broken.length === 1 ? "a picture" : "pictures"} ` +
                `of the page at ${frame.url()}: ${broken.join(", ")}.`,
        );
}
