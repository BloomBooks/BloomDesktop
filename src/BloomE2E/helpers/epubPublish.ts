// Drive and read the Publish tab's ePUB screen: its ePUB mode, its Book Metadata dialog, and its
// preview.
//
// The preview is the ePUB itself. Clicking Preview has Bloom stage the ePUB's files, exactly the
// ones Save... then zips, and show them in a reader (Readium's D2 Reader) inside the device frame.
// The reader shows one page of a fixed-layout ePUB at a time, each in a frame of its own, so a test
// reads a page of the publication from that frame.
//
// Saving the .epub file is not here: Save... opens the operating system's save dialog, which no
// test can answer (see AUTOMATION-DEBT.md).

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGet, apiGetJson, getUiString } from "./api";
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
 * How the ePUB screen's "Include image descriptions on page" check box shows: ticked or not, and
 * whether it can be changed. The ePUB screen must be showing (openEpubPublishScreen).
 */
export async function getIncludeImageDescriptionsBoxState(
    page: Page,
): Promise<{ ticked: boolean; enabled: boolean }> {
    const box = await includeImageDescriptionsBox(page);
    await box.waitFor({ state: "attached", timeout: 30000 });
    return { ticked: await box.isChecked(), enabled: await box.isEnabled() };
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

/** One thing a reader meets on a page of an ePUB, and what it holds. */
export interface IEpubPageItem {
    kind: "picture" | "description" | "text";
    /** A picture's file name; the words of a description or of a text box. */
    content: string;
}

/**
 * What a reader meets on a page of the ePUB preview (a frame showEpubPreviewPage returned), in
 * the order of the page's document, which is the order a reader that lays the page out itself
 * shows it in: each picture, each picture description that is on the page
 * (getEpubPreviewImageDescriptions), and each text box that is showing and has words in it.
 */
export async function getEpubPreviewPageItems(
    frame: Frame,
): Promise<IEpubPageItem[]> {
    return frame.evaluate(() => {
        const items: {
            kind: "picture" | "description" | "text";
            content: string;
        }[] = [];
        const isShowing = (element: Element) =>
            (element as HTMLElement).getClientRects().length > 0 &&
            getComputedStyle(element).visibility !== "hidden";
        const words = (element: Element) =>
            ((element as HTMLElement).innerText ?? "").trim();
        for (const element of document.querySelectorAll(
            ".bloom-page img, .bloom-page aside.imageDescription, .bloom-page .bloom-editable",
        )) {
            if (!isShowing(element)) continue;
            if (element.tagName.toLowerCase() === "img") {
                const src = element.getAttribute("src") ?? "";
                items.push({
                    kind: "picture",
                    content: decodeURIComponent(src.split("/").pop() ?? ""),
                });
            } else if (element.tagName.toLowerCase() === "aside") {
                items.push({ kind: "description", content: words(element) });
            } else if (
                !element.closest("aside.imageDescription") &&
                words(element)
            ) {
                items.push({ kind: "text", content: words(element) });
            }
        }
        return items;
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
 * The address of the ePUB content document (one page of the book) that the preview is showing,
 * without any fragment, or undefined while it shows none: before the reader has loaded the book,
 * and between one page and the next.
 */
export async function getEpubPreviewPageAddress(
    page: Page,
): Promise<string | undefined> {
    if ((await getIdOfEpubPreviewPage(page)) === undefined) return undefined;
    return getEpubPreviewPageFrame(page)?.url().split("#")[0];
}

/** The reader's page-turn arrows, which it draws at its left and right edges. */
export type EpubPreviewArrow = "left" | "right";

/**
 * Click the reader's page-turn arrow drawn at its left or right edge, as a person does, and return
 * the address of the page it turned to (getEpubPreviewPageAddress), once that page has loaded.
 * Throws when the arrow turned to no other page.
 *
 * The arrows are found by where the reader draws them, not by their ids or what they call, since
 * which way each one turns is what a test of them checks. Only click an arrow that should turn the
 * page: on the first or last page one of them does nothing, and this would wait for a turn that
 * never comes.
 */
export async function clickEpubPreviewArrow(
    page: Page,
    side: EpubPreviewArrow,
): Promise<string> {
    let before: string | undefined;
    await expect
        .poll(async () => (before = await getEpubPreviewPageAddress(page)), {
            timeout: 30000,
            message:
                "The ePUB preview is not showing a page whose arrows can be clicked.",
        })
        .toBeDefined();
    const arrows = readerFrame(page).locator(".page-switch-overlay-icon");
    await expect(
        arrows,
        "The ePUB reader should draw two page-turn arrows.",
    ).toHaveCount(2);
    const boxes = await Promise.all(
        [0, 1].map((i) => arrows.nth(i).boundingBox()),
    );
    if (!boxes[0] || !boxes[1])
        throw new Error("A page-turn arrow of the ePUB reader is not drawn.");
    const leftIndex = boxes[0].x < boxes[1].x ? 0 : 1;
    const arrow = arrows.nth(side === "left" ? leftIndex : 1 - leftIndex);
    await arrow.click();
    let after: string | undefined;
    await expect
        .poll(
            async () => {
                const now = await getEpubPreviewPageAddress(page);
                if (now === undefined || now === before) return false;
                after = now;
                return true;
            },
            {
                timeout: 30000,
                message: `The ${side} arrow of the ePUB preview did not turn away from ${before}.`,
            },
        )
        .toBe(true);
    const frame = getEpubPreviewPageFrame(page);
    if (frame) await waitForEpubPreviewPageToFinishLoading(frame);
    return after!;
}

/**
 * The reading order of the ePUB the preview is showing, as its package's spine gives it to a
 * reader app: the address of each page in order, and the page-progression-direction the spine
 * declares ("rtl" or "ltr"), or undefined when it declares none, which the ePUB standard leaves to
 * the reader (EpubMaker writes "rtl" when the book's first language is right to left).
 */
export async function getEpubPreviewSpine(
    page: Page,
): Promise<{ direction: string | undefined; pages: string[] }> {
    const packageUrl = epubPreviewPackageUrl(page);
    const packageText = await fetchText(page, packageUrl);
    return page.evaluate(
        ({ text, packageAddress }) => {
            const doc = new DOMParser().parseFromString(
                text,
                "application/xml",
            );
            const spines = doc.getElementsByTagNameNS("*", "spine");
            if (spines.length !== 1)
                throw new Error(
                    `The ePUB package has ${spines.length} <spine> elements, not one.`,
                );
            const items = new Map(
                [...doc.getElementsByTagNameNS("*", "item")].map((item) => [
                    item.getAttribute("id") ?? "",
                    item.getAttribute("href") ?? "",
                ]),
            );
            const pages = [
                ...spines[0].getElementsByTagNameNS("*", "itemref"),
            ].map((itemref) => {
                const idref = itemref.getAttribute("idref") ?? "";
                const href = items.get(idref);
                if (href === undefined)
                    throw new Error(
                        `The ePUB package's spine names "${idref}", which its manifest does not list.`,
                    );
                return new URL(href, packageAddress).href;
            });
            return {
                direction:
                    spines[0].getAttribute("page-progression-direction") ??
                    undefined,
                pages,
            };
        },
        { text: packageText, packageAddress: packageUrl },
    );
}

/**
 * Whether the ePUB preview's reader is the one for a book read right to left (Readium's
 * indexRtl.html, which PublishEpubApi.SetupEpubControlContent picks when the book's first language
 * is right to left) rather than the one for a book read left to right (index.html).
 */
export function isEpubPreviewReaderRightToLeft(page: Page): boolean {
    return /\/Readium\/indexRtl\.html/.test(readerFrame(page).url());
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

/** The ePUB screen's Book Metadata dialog; see BookMetadataDialog.tsx. */
const BOOK_METADATA_DIALOG = ".bookMetadataDialog";

/**
 * Click the ePUB screen's "Book Metadata" link and wait until its dialog shows the book's
 * metadata. The ePUB screen must be showing (openEpubPublishScreen).
 */
export async function openBookMetadataDialog(page: Page): Promise<Locator> {
    await page.locator("#bookMetadataDialogLink").click();
    const dialog = page.locator(BOOK_METADATA_DIALOG);
    await bookMetadataField(page, "summary").waitFor({
        state: "visible",
        timeout: 30000,
    });
    return dialog;
}

/**
 * One text box of the Book Metadata dialog, by the key Bloom gives it in book/metadata, such as
 * "summary" or "author".
 */
export function bookMetadataField(page: Page, key: string): Locator {
    return page
        .locator(BOOK_METADATA_DIALOG)
        .getByTestId(`book-metadata-${key}`);
}

/**
 * Click the Book Metadata dialog's OK button and wait until the dialog is gone. The dialog then
 * saves what it holds; the caller waits for the value it changed (getBookMetadataValue).
 */
export async function clickBookMetadataOk(page: Page): Promise<void> {
    const dialog = page.locator(BOOK_METADATA_DIALOG);
    await dialog.locator("#okButton").click();
    await expect(
        dialog,
        "The Book Metadata dialog should close on OK.",
    ).toHaveCount(0, { timeout: 15000 });
}

/** A value of the book's metadata as Bloom holds it, by the key book/metadata gives it. */
export async function getBookMetadataValue(
    page: Page,
    key: string,
): Promise<string> {
    const answer = await apiGetJson<{
        metadata: Record<string, { value: unknown }>;
    }>(page, "book/metadata");
    return String(answer.metadata[key].value ?? "");
}

/**
 * The package document (content/content.opf) of the ePUB the preview is showing, as text: the
 * file that Save... zips with the rest of the staged ePUB, and that holds the ePUB's metadata.
 * Click Preview first (showEpubPreview).
 *
 * The reader's own address names the staged ePUB's manifest.json in its "url" parameter, which
 * Bloom writes beside the ePUB's content folder (PublishEpubApi.SetupEpubControlContent).
 */
export async function readEpubPreviewPackage(page: Page): Promise<string> {
    return fetchText(page, epubPreviewPackageUrl(page));
}

/** Where the package document of the ePUB the preview is showing is served (readEpubPreviewPackage). */
function epubPreviewPackageUrl(page: Page): string {
    const manifest = new URL(readerFrame(page).url()).searchParams.get("url");
    if (!manifest)
        throw new Error(
            `The ePUB reader's address names no manifest: ${readerFrame(page).url()}`,
        );
    return new URL("content/content.opf", manifest).toString();
}

/** A file of the staged ePUB, as text, fetched the way the reader fetches it. */
async function fetchText(page: Page, url: string): Promise<string> {
    return page.evaluate(async (address) => {
        const response = await fetch(address);
        if (!response.ok)
            throw new Error(`Fetching ${address} answered ${response.status}.`);
        return response.text();
    }, url);
}

/** One clip of a page's read-aloud: the text a reader highlights, and the audio it plays then. */
export interface IEpubReadAloudClip {
    /** The id, in the page's document, of the text the clip reads. */
    textId: string;
    /** The audio file the clip plays, as the ePUB's address of it. */
    audioUrl: string;
    /** Where in that file the clip starts and ends, in seconds. */
    clipBegin: number;
    clipEnd: number;
}

/**
 * What a reader app reads aloud on a page of the ePUB preview (a frame showEpubPreviewPage
 * returned), in the order it reads it: the clips of the page's media overlay, the SMIL file that
 * the package's manifest names as the page's "media-overlay" (EpubMaker.AddAudioOverlay). Empty
 * when the page has none, which is when nothing on it has a recording.
 */
export async function getEpubPreviewPageReadAloud(
    page: Page,
    frame: Frame,
): Promise<IEpubReadAloudClip[]> {
    const packageUrl = epubPreviewPackageUrl(page);
    const packageText = await fetchText(page, packageUrl);
    const pageUrl = frame.url().split("#")[0];
    const overlayUrl = await page.evaluate(
        ({ text, packageAddress, pageAddress }) => {
            const doc = new DOMParser().parseFromString(
                text,
                "application/xml",
            );
            const items = [...doc.getElementsByTagNameNS("*", "item")];
            const pageItem = items.find(
                (item) =>
                    new URL(item.getAttribute("href") ?? "", packageAddress)
                        .href === pageAddress,
            );
            if (!pageItem)
                throw new Error(
                    `The ePUB package's manifest lists no item for the page at ${pageAddress}.`,
                );
            const overlayId = pageItem.getAttribute("media-overlay");
            if (!overlayId) return undefined;
            const overlay = items.find(
                (item) => item.getAttribute("id") === overlayId,
            );
            if (!overlay)
                throw new Error(
                    `The ePUB package's manifest names "${overlayId}" as the media overlay of ` +
                        `${pageAddress}, but has no item with that id.`,
                );
            return new URL(overlay.getAttribute("href") ?? "", packageAddress)
                .href;
        },
        { text: packageText, packageAddress: packageUrl, pageAddress: pageUrl },
    );
    if (!overlayUrl) return [];
    const smilText = await fetchText(page, overlayUrl);
    return page.evaluate(
        ({ text, smilAddress, pageAddress }) => {
            const seconds = (clock: string | null) => {
                const parts = (clock ?? "").split(":").map(Number);
                if (parts.length !== 3 || parts.some(Number.isNaN))
                    throw new Error(
                        `A clip of ${smilAddress} has the time "${clock}", not h:mm:ss.fff.`,
                    );
                return parts[0] * 3600 + parts[1] * 60 + parts[2];
            };
            const doc = new DOMParser().parseFromString(
                text,
                "application/xml",
            );
            return [...doc.getElementsByTagNameNS("*", "par")].map((par) => {
                const textSrc =
                    par
                        .getElementsByTagNameNS("*", "text")[0]
                        ?.getAttribute("src") ?? "";
                const audio = par.getElementsByTagNameNS("*", "audio")[0];
                const target = new URL(textSrc, smilAddress);
                if (target.href.split("#")[0] !== pageAddress)
                    throw new Error(
                        `A clip of ${smilAddress} reads ${textSrc}, which is not on the page ${pageAddress}.`,
                    );
                const audioSrc = audio?.getAttribute("src");
                if (!audioSrc)
                    throw new Error(
                        `The clip of ${smilAddress} that reads ${textSrc} names no audio file.`,
                    );
                return {
                    textId: decodeURIComponent(target.hash.slice(1)),
                    audioUrl: new URL(audioSrc, smilAddress).href,
                    clipBegin: seconds(audio.getAttribute("clipBegin")),
                    clipEnd: seconds(audio.getAttribute("clipEnd")),
                };
            });
        },
        { text: smilText, smilAddress: overlayUrl, pageAddress: pageUrl },
    );
}

/** Whether the staged ePUB has a file at this address (one getEpubPreviewPageReadAloud gave). */
export async function epubPreviewHasFile(
    page: Page,
    url: string,
): Promise<boolean> {
    return page.evaluate(async (address) => (await fetch(address)).ok, url);
}

/** The reader's button that starts reading the page aloud (Readium/index.html). */
const PLAY_BUTTON = "#menu-button-play";

/**
 * The class the preview's reader puts on the text it is reading aloud: the "active-class" that
 * Bloom gives it in the Readium manifest it writes beside the ePUB (ReadiumManifest.cs), taken from
 * the package's media:active-class (EpubMaker). Bloom's style sheets give that class a background
 * (basePage-sharedRules.less).
 */
export async function getEpubPreviewReadAlongClass(
    page: Page,
): Promise<string> {
    const manifestUrl = new URL(readerFrame(page).url()).searchParams.get(
        "url",
    );
    if (!manifestUrl)
        throw new Error(
            `The ePUB reader's address names no manifest: ${readerFrame(page).url()}`,
        );
    const manifest = JSON.parse(await fetchText(page, manifestUrl)) as {
        metadata?: { "media-overlay"?: { "active-class"?: string } };
    };
    const activeClass = manifest.metadata?.["media-overlay"]?.["active-class"];
    if (!activeClass)
        throw new Error(
            `The ePUB preview's manifest, ${manifestUrl}, gives the reader no class to mark ` +
                "the text it reads aloud with.",
        );
    return activeClass;
}

/** One thing that happened while the reader read a page of the preview aloud. */
export type EpubReadAlongEvent =
    | {
          /** The reader marked an element as the text it is reading, or unmarked it. */
          kind: "highlighted" | "unhighlighted";
          /** The element's id in the page's document. */
          id: string;
          /** The element's background color right after it was marked ("" when unmarked). */
          background: string;
          /** Whether the reader's audio was playing at that moment, and where in it. */
          audioPlaying: boolean;
          audioTime: number;
      }
    | {
          /** The reader's audio started playing, paused (which is also how it stops), or ended. */
          kind: "playing" | "pause" | "ended";
          /** The audio file, as the ePUB's address of it. */
          audioUrl: string;
          audioTime: number;
      };

/**
 * The ids of the elements of a page of the ePUB preview (a frame showEpubPreviewPage returned) that
 * the reader shows as being read aloud: those with the class getEpubPreviewReadAlongClass gives.
 */
export async function getEpubPreviewReadAlongHighlights(
    page: Page,
    frame: Frame,
): Promise<string[]> {
    const activeClass = await getEpubPreviewReadAlongClass(page);
    return frame
        .locator(`[class~="${activeClass}"]`)
        .evaluateAll((elements) => elements.map((element) => element.id));
}

/**
 * Click the reader's Play button over a page of the ePUB preview (a frame showEpubPreviewPage
 * returned), the way a person does, and return what happened, in order, until the reader has
 * read the page and stopped: every element it marked or unmarked as being read, and every time its
 * audio started, paused or ended.
 *
 * The reader plays the page's media overlay (getEpubPreviewPageReadAloud) through one audio element
 * of its own document, and marks each clip's text with its active class
 * (getEpubPreviewReadAlongClass) while it plays that clip. It takes the class off and puts it back
 * on every frame; an element counts as unmarked only once it is left without the class. It is
 * told not to turn the page by itself (Readium/index.html), so it stops at the end of the page.
 *
 * Play does nothing until the reader has loaded the page's first clip into its audio element,
 * which it does by itself after showing the page; so this waits for that before clicking.
 */
export async function playEpubPreviewReadAlong(
    page: Page,
    frame: Frame,
): Promise<EpubReadAlongEvent[]> {
    const reader = readerFrame(page);
    const activeClass = await getEpubPreviewReadAlongClass(page);
    const clips = await getEpubPreviewPageReadAloud(page, frame);
    if (clips.length === 0)
        throw new Error(
            `The page at ${frame.url()} has nothing to read aloud: its media overlay has no clip.`,
        );
    await expect
        .poll(
            () =>
                reader.evaluate(() => {
                    const audio = document.getElementById(
                        "AUDIO_MO_ID",
                    ) as HTMLAudioElement | null;
                    return audio ? audio.src : "";
                }),
            {
                timeout: 30000,
                message:
                    "The ePUB preview's reader never loaded the page's first clip into its audio " +
                    "element, so Play would do nothing.",
            },
        )
        .toBe(clips[0].audioUrl);
    // The reader replaces its audio element for each new audio file, so the audio events are
    // caught on its document (media events do not bubble, but they do pass through the capture
    // phase), and the page's frame finds the element by its id whenever it needs it.
    await reader.evaluate(() => {
        const log: unknown[] = [];
        (window as unknown as { e2eReadAlong: unknown[] }).e2eReadAlong = log;
        for (const kind of ["playing", "pause", "ended"])
            document.addEventListener(
                kind,
                (event) => {
                    const audio = event.target as HTMLAudioElement;
                    log.push({
                        kind,
                        audioUrl: audio.currentSrc,
                        audioTime: audio.currentTime,
                    });
                },
                true,
            );
    });
    await frame.evaluate((activeClass) => {
        const log = (window.parent as unknown as { e2eReadAlong: unknown[] })
            .e2eReadAlong;
        const marked = new Set<Element>();
        new MutationObserver((mutations) => {
            for (const mutation of mutations) {
                const element = mutation.target as HTMLElement;
                const isMarked = element.classList.contains(activeClass);
                if (isMarked === marked.has(element)) continue;
                if (isMarked) marked.add(element);
                else marked.delete(element);
                const audio = window.parent.document.getElementById(
                    "AUDIO_MO_ID",
                ) as HTMLAudioElement | null;
                log.push({
                    kind: isMarked ? "highlighted" : "unhighlighted",
                    id: element.id,
                    background: isMarked
                        ? getComputedStyle(element).backgroundColor
                        : "",
                    audioPlaying: !!audio && !audio.paused && !audio.ended,
                    audioTime: audio?.currentTime ?? 0,
                });
            }
        }).observe(document.documentElement, {
            subtree: true,
            attributes: true,
            attributeFilter: ["class"],
        });
    }, activeClass);

    const readLog = () =>
        reader.evaluate(
            () =>
                (window as unknown as { e2eReadAlong: EpubReadAlongEvent[] })
                    .e2eReadAlong,
        );
    await reader.locator(PLAY_BUTTON).click();
    let events: EpubReadAlongEvent[] = [];
    await expect
        .poll(
            async () => {
                events = await readLog();
                const started = events.findIndex((e) => e.kind === "playing");
                return (
                    started >= 0 &&
                    events
                        .slice(started)
                        .some(
                            (e) => e.kind === "pause" || e.kind === "ended",
                        ) &&
                    (await getEpubPreviewReadAlongHighlights(page, frame))
                        .length === 0
                );
            },
            {
                timeout: 60000,
                message:
                    "After Play, the ePUB preview's reader did not play the page's audio to its end " +
                    "and stop.",
            },
        )
        .toBe(true);
    return readLog();
}

/** Whether the ePUB screen's "ePUB mode" list can be changed. */
export async function isEpubModeListEnabled(page: Page): Promise<boolean> {
    const list = page.locator(`${PUBLISH_SCREEN} ${MODE_LIST}`);
    await list.waitFor({ state: "visible", timeout: 30000 });
    return (await list.getAttribute("aria-disabled")) !== "true";
}

/**
 * The layout of the ePUB the preview is showing, as its package declares it to a reader app:
 * "fixed" when it says rendition:layout is pre-paginated, "flowable" when it says nothing, which
 * the ePUB standard makes reflowable (EpubMaker writes the property only for a fixed ePUB).
 */
export async function getEpubPreviewLayout(page: Page): Promise<EpubMode> {
    const packageText = await readEpubPreviewPackage(page);
    const layouts = await page.evaluate((text) => {
        const doc = new DOMParser().parseFromString(text, "application/xml");
        return [...doc.getElementsByTagNameNS("*", "meta")]
            .filter(
                (meta) => meta.getAttribute("property") === "rendition:layout",
            )
            .map((meta) => (meta.textContent ?? "").trim());
    }, packageText);
    if (layouts.length === 0) return "flowable";
    if (layouts.length === 1 && layouts[0] === "pre-paginated") return "fixed";
    throw new Error(
        `The ePUB package declares rendition:layout as ${JSON.stringify(layouts)}.`,
    );
}

/**
 * The id that an element of the book (such as a recorded sentence) has in the ePUB: an ePUB
 * validator objects to an id that starts with a digit, so Bloom puts an "i" before one
 * (EpubMaker.FixIllegalIds).
 */
export function idInEpub(bookId: string): string {
    return /^\d/.test(bookId) ? `i${bookId}` : bookId;
}

/** A picture on a page of the ePUB preview, and the alternative text a screen reader gives it. */
export interface IEpubPicture {
    /** The picture's file name. */
    file: string;
    /** Its alt attribute; undefined when it has none. */
    alt: string | undefined;
}

/**
 * The pictures a page of the ePUB preview (a frame showEpubPreviewPage returned) shows, in
 * document order, with their alternative text. Bloom gives a described picture its description
 * as its alt (EpubMaker.HandleImageDescriptions), whether or not the description is on the page.
 */
export async function getEpubPreviewPictures(
    frame: Frame,
): Promise<IEpubPicture[]> {
    return frame.locator(".bloom-page img").evaluateAll((images) =>
        images
            // A picture that is display:none, or visibility:hidden anywhere up its ancestors, is
            // not shown, though the latter still takes up room on the page.
            .filter((image) =>
                image.checkVisibility({ visibilityProperty: true }),
            )
            .map((image) => ({
                file: decodeURIComponent(
                    (image.getAttribute("src") ?? "").split("/").pop() ?? "",
                ),
                alt: image.getAttribute("alt") ?? undefined,
            })),
    );
}

/**
 * The picture description that a page of the ePUB preview keeps in its document without putting
 * it on the page, which is what Bloom does when "Include image descriptions on page" is unticked:
 * its words, whether any of it is drawn, and whether each of the recordings named by
 * `recordingIds` (ids in the book) is in it, so that a reader app can still read it aloud. Throws
 * unless the page has exactly one such description.
 */
export async function getEpubPreviewKeptImageDescription(
    frame: Frame,
    recordingIds: string[],
): Promise<{ text: string; showing: boolean; recordingsFound: boolean }> {
    return frame.evaluate(
        ({ ids }) => {
            const groups = [
                ...document.querySelectorAll(".bloom-imageDescription"),
            ];
            if (groups.length !== 1)
                throw new Error(
                    `The page keeps ${groups.length} picture descriptions, not one.`,
                );
            const group = groups[0];
            const showing = (element: Element) =>
                element.getClientRects().length > 0 &&
                getComputedStyle(element).visibility !== "hidden";
            return {
                text: (group.textContent ?? "").trim(),
                showing:
                    showing(group) ||
                    [...group.querySelectorAll("*")].some(showing),
                recordingsFound: ids.every(
                    (id) => !!group.querySelector(`[id="${id}"]`),
                ),
            };
        },
        { ids: recordingIds.map(idInEpub) },
    );
}

/**
 * The texts of the entries of an ePUB package document's metadata (readEpubPreviewPackage) that
 * have the Dublin Core name `name`, such as "description" or "title", in document order. Only the
 * children of the package's <metadata> count, since that is where a reader app looks for them.
 */
export async function getEpubPackageDublinCore(
    page: Page,
    packageText: string,
    name: string,
): Promise<string[]> {
    return page.evaluate(
        ({ text, localName }) => {
            const doc = new DOMParser().parseFromString(
                text,
                "application/xml",
            );
            const error = doc.querySelector("parsererror");
            if (error)
                throw new Error(
                    `The ePUB package is not well-formed XML: ${error.textContent}`,
                );
            const opf = "http://www.idpf.org/2007/opf";
            const root = doc.documentElement;
            if (root.namespaceURI !== opf || root.localName !== "package")
                throw new Error(
                    `The ePUB package's root is <${root.tagName}>, not an OPF <package>.`,
                );
            const metadata = [...root.children].filter(
                (child) =>
                    child.namespaceURI === opf &&
                    child.localName === "metadata",
            );
            if (metadata.length !== 1)
                throw new Error(
                    `The ePUB package has ${metadata.length} <metadata> elements, not one.`,
                );
            return [...metadata[0].children]
                .filter(
                    (child) =>
                        child.namespaceURI ===
                            "http://purl.org/dc/elements/1.1/" &&
                        child.localName === localName,
                )
                .map((element) => element.textContent ?? "");
        },
        { text: packageText, localName: name },
    );
}
