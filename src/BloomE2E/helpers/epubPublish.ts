// Drive and read the Publish tab's ePUB screen: its ePUB mode, and its preview.
//
// The preview is the ePUB itself. Clicking Preview has Bloom stage the ePUB's files, exactly the
// ones Save... then zips, and show them in a reader (Readium's D2 Reader) inside the device frame.
// The reader shows one page of a fixed-layout ePUB at a time, each in a frame of its own, so a test
// reads a page of the publication from that frame.
//
// Saving the .epub file is not here: Save... opens the operating system's save dialog, which no
// test can answer (see AUTOMATION-DEBT.md).

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGet, getUiString } from "./api";
import { selectPublishDestination } from "./publish";

/** How an ePUB lays its pages out: as Bloom shows them, or however the reader likes. */
export type EpubMode = "fixed" | "flowable";

/**
 * The Publish screen that is showing, with its settings beside its preview; see
 * PublishScreenTemplate.tsx. Each destination draws its own screen in it.
 */
const PUBLISH_SCREEN = "#publishScreenTemplate";

/** The "ePUB mode" list, which is the screen's only drop-down list. */
const MODE_LIST = '[role="combobox"]';

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
 * Choose a mode in the ePUB screen's "ePUB mode" list, the way a person does, and wait until Bloom
 * has it as the book's mode. Does nothing when the list already shows that mode. The ePUB screen
 * must be showing (openEpubPublishScreen).
 *
 * The preview does not follow the change by itself: click Preview again (showEpubPreview).
 */
export async function chooseEpubMode(
    page: Page,
    mode: EpubMode,
): Promise<void> {
    if ((await getEpubMode(page)) === mode) return;
    await page.locator(`${PUBLISH_SCREEN} ${MODE_LIST}`).click();
    // MUI draws the list's choices in a popup outside the screen's own element; each carries the
    // mode it stands for in data-value.
    const choice = page.locator(`li[role="option"][data-value="${mode}"]`);
    await choice.waitFor({ state: "visible", timeout: 15000 });
    if ((await choice.getAttribute("aria-disabled")) === "true")
        throw new Error(
            `The ePUB screen offers the "${mode}" mode only as a disabled choice for this book.`,
        );
    await choice.click();
    await expect
        .poll(() => getEpubMode(page), {
            timeout: 15000,
            message: `Bloom never took "${mode}" as the book's ePUB mode.`,
        })
        .toBe(mode);
}

/**
 * The ePUB screen's "Include image descriptions on page" check box, found by its label in Bloom's
 * UI language, which is the Windows UI language and so not always English.
 */
async function includeImageDescriptionsBox(page: Page): Promise<Locator> {
    return page.locator(PUBLISH_SCREEN).getByRole("checkbox", {
        name: await getUiString(
            page,
            "PublishTab.Epub.IncludeOnPage",
            "Include image descriptions on page",
        ),
        exact: true,
    });
}

/**
 * Whether Bloom will put the pictures' descriptions on the pages of the selected book's ePUB, as
 * the book's publish settings hold it. The ePUB screen's check box can show otherwise: it is
 * unticked and disabled while the mode is "fixed", whatever the setting.
 */
async function getIncludeImageDescriptionsSetting(
    page: Page,
): Promise<boolean> {
    return (
        (
            await apiGet(page, "publish/epub/imageDescriptionSetting")
        ).body.trim() === "true"
    );
}

/**
 * Tick or untick the ePUB screen's "Include image descriptions on page" check box, the way a person
 * does, and wait until Bloom has saved the setting. Does nothing when the box is already as asked.
 * Throws when the box is disabled, which it is while the ePUB mode is "fixed" (chooseEpubMode).
 *
 * The preview does not follow the change by itself: click Preview again (showEpubPreview).
 */
export async function setIncludeImageDescriptionsOnPage(
    page: Page,
    include: boolean,
): Promise<void> {
    const box = await includeImageDescriptionsBox(page);
    await box.waitFor({ state: "attached", timeout: 30000 });
    if (!(await box.isEnabled()))
        throw new Error(
            'The ePUB screen\'s "Include image descriptions on page" check box is disabled. ' +
                `The ePUB mode is "${await getEpubMode(page)}"; the box can be changed only in flowable mode.`,
        );
    if ((await box.isChecked()) !== include) await box.click();
    await expect(box).toBeChecked({ checked: include, timeout: 15000 });
    await expect
        .poll(() => getIncludeImageDescriptionsSetting(page), {
            timeout: 15000,
            message: `Bloom never saved "Include image descriptions on page" as ${include ? "on" : "off"}.`,
        })
        .toBe(include);
}

/**
 * The picture descriptions that a page of the ePUB preview shows on the page (a frame
 * showEpubPreviewPage returned), one for each language of each described picture, in document
 * order. Bloom puts them there, after the picture, only when "Include image descriptions on page"
 * is ticked (EpubMaker.HandleImageDescriptions); otherwise this matches nothing.
 */
export function getEpubPreviewImageDescriptions(frame: Frame): Locator {
    return frame.locator("aside.imageDescription");
}

/**
 * Whether a picture description on a page of the ePUB preview (one of
 * getEpubPreviewImageDescriptions) comes after a picture of its page and before any text box of
 * the page that follows that picture: the place a reader that lays the page out itself gives it,
 * straight after the picture it describes. Throws when the page has no picture.
 */
export async function isEpubPreviewImageDescriptionAfterItsPicture(
    description: Locator,
): Promise<boolean> {
    return description.evaluate((aside) => {
        const page = aside.closest(".bloom-page");
        if (!page)
            throw new Error("The description is not on a page of the book.");
        const pictures = [...page.querySelectorAll("img")];
        if (pictures.length === 0)
            throw new Error(
                "The page of the ePUB has no picture: " + page.outerHTML,
            );
        const follows = (later: Element, earlier: Element) =>
            (earlier.compareDocumentPosition(later) &
                Node.DOCUMENT_POSITION_FOLLOWING) !==
            0;
        // The last picture before the description is the one it describes.
        const picture = pictures.filter((p) => follows(aside, p)).pop();
        if (!picture) return false;
        const textBetween = [...page.querySelectorAll(".bloom-editable")].some(
            (box) =>
                !aside.contains(box) &&
                !box.contains(aside) &&
                follows(box, picture) &&
                follows(aside, box) &&
                (box.textContent ?? "").trim() !== "",
        );
        return !textBetween;
    });
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
