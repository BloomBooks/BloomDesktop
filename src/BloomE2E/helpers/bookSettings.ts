// The Book and Page Settings dialog of the Edit tab, and the book's appearance settings it edits.
//
// Two routes to the same settings sit side by side here. setBookAppearance is SETUP: it posts what
// the dialog's OK button posts, so a test can put a book into a theme or turn full bleed on without
// driving the dialog, and then measure what the pages look like. The rest drive the dialog itself,
// for tests whose subject is the dialog.
//
// The dialog's pages are Material UI tabs named with the words Bloom shows (in English, which is
// what the suite runs in), like the Collection Settings dialog's (see collectionSettings.ts).
// Config-R names each check box after its setting's path, e.g. "appearance.fullBleed", which is a
// steadier handle than its label.

import { expect, type Locator, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { editablePageFrame, waitForEditablePage } from "./bookMaking";
import { chooseInList } from "./collectionSettings";
import { realClick, waitForClickToSettle } from "./realClick";

/** The book's appearance settings that tests here change. Only these fields. */
export interface IBookAppearance {
    /** The page theme, by the name of its file: "default", "edge-to-edge", "legacy-5-6", ... */
    cssThemeName: string;
    /** "Use full bleed page layout" on the dialog's Print Publishing page. */
    fullBleed: boolean;
    /** Where page numbers go: the "Page Numbers" choice on the dialog's Theme & Layout page. */
    pageNumberPosition: PageNumberPosition;
}

/** The choices of Page Numbers, by the values Bloom saves (AppearanceSettings.PageNumberPosition). */
export type PageNumberPosition =
    | "automatic"
    | "left"
    | "center"
    | "right"
    | "hidden";

/** What book/settings replies with. Only the part used here; the rest is posted back untouched. */
interface IBookSettings {
    appearance: {
        cssThemeName: string;
        fullBleed?: boolean;
        "pageNumber-position"?: PageNumberPosition;
    } & Record<string, unknown>;
}

/** The book's current appearance settings, as the Book and Page Settings dialog would show them. */
export async function getBookAppearance(page: Page): Promise<IBookAppearance> {
    const settings = await apiGetJson<IBookSettings>(page, "book/settings");
    return {
        cssThemeName: settings.appearance.cssThemeName,
        fullBleed: !!settings.appearance.fullBleed,
        // A book that never had Page Numbers changed has no value saved, which Bloom treats as
        // Automatic.
        pageNumberPosition:
            settings.appearance["pageNumber-position"] ?? "automatic",
    };
}

/**
 * Change the selected book's page theme, full bleed setting and/or Page Numbers choice, the way
 * pressing OK in Book and Page Settings does, and wait until the Edit tab has rebuilt the page it
 * shows with them.
 *
 * Full bleed only takes effect where Bloom allows it: a paper page size, and a collection whose
 * subscription includes it (Enterprise). Elsewhere the setting is saved but the page is drawn
 * without bleed, so a test that needs bleed checks for it (see isPageDrawnWithFullBleed).
 */
export async function setBookAppearance(
    page: Page,
    changes: Partial<IBookAppearance>,
): Promise<void> {
    const settings = await apiGetJson<IBookSettings>(page, "book/settings");
    if (changes.cssThemeName !== undefined)
        settings.appearance.cssThemeName = changes.cssThemeName;
    if (changes.fullBleed !== undefined)
        settings.appearance.fullBleed = changes.fullBleed;
    if (changes.pageNumberPosition !== undefined)
        settings.appearance["pageNumber-position"] = changes.pageNumberPosition;
    // Bloom rebuilds the page after saving, so the document showing now goes away. Mark it, and
    // wait for a document without the mark.
    await markPageDocument(page);
    await apiPost(
        page,
        "book/settings",
        JSON.stringify(settings),
        "application/json",
    );
    await expect
        .poll(async () => isPageDocumentMarked(page), {
            timeout: 60000,
            message:
                "Bloom never rebuilt the page after saving the book's settings.",
        })
        .toBe(false);
    await waitForEditablePage(page);
    const now = await getBookAppearance(page);
    for (const [key, value] of Object.entries(changes))
        expect(
            now[key as keyof IBookAppearance],
            `Bloom did not keep the book's ${key} setting`,
        ).toBe(value);
}

/**
 * Give the page being edited a background colour, e.g. "#fdf3c5", as the Book and Page Settings
 * dialog's page colour does: it sets --page-background-color on the page itself
 * (PageSettingsConfigrPages.tsx). SETUP: Bloom saves it with the page when the page is left.
 */
export async function setPageBackgroundColor(
    page: Page,
    color: string,
): Promise<void> {
    await editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .evaluate((element, value) => {
            (element as HTMLElement).style.setProperty(
                "--page-background-color",
                value,
            );
        }, color);
}

/**
 * True when the page being edited is drawn for full bleed: enlarged past the cut line, with the
 * 3mm bleed around it. False when the book has full bleed off, or Bloom does not allow it here.
 */
export async function isPageDrawnWithFullBleed(page: Page): Promise<boolean> {
    return editablePageFrame(page)
        .locator("body")
        .evaluate((body) => body.classList.contains("bloom-fullBleed"));
}

const kStaleMark = "data-e2e-stale-page-document";

async function markPageDocument(page: Page): Promise<void> {
    await editablePageFrame(page)
        .locator("html")
        .evaluate((html, mark) => html.setAttribute(mark, "true"), kStaleMark);
}

async function isPageDocumentMarked(page: Page): Promise<boolean> {
    const frame = page.frame({ name: "page" });
    if (!frame) return true;
    return frame
        .locator("html")
        .evaluate((html, mark) => html.hasAttribute(mark), kStaleMark)
        .catch(() => true);
}

/** The Book and Page Settings dialog, by the title it shows. */
function bookSettingsDialog(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ hasText: "Book and Page Settings" });
}

/**
 * Open Book and Page Settings the way a person does, by clicking its button on the Edit tab's top
 * bar, and return once the dialog shows its pages. With `pageName`, e.g. "Theme & Layout", also
 * show that page of the dialog; otherwise the dialog shows whichever page it chose to open on.
 */
export async function openBookSettings(
    page: Page,
    pageName?: string,
): Promise<void> {
    await realClick(
        page.getByRole("button", {
            name: "Book and Page Settings",
            exact: true,
        }),
    );
    await expect(
        bookSettingsDialog(page).getByRole("tab").first(),
        "the Book and Page Settings dialog never showed its pages",
    ).toBeVisible({ timeout: 30000 });
    if (pageName) await showBookSettingsPage(page, pageName);
}

/**
 * Show one page of the open Book and Page Settings dialog by clicking its name in the list on the
 * left, e.g. "Theme & Layout". Fails naming the pages the dialog offers when no such page shows.
 */
export async function showBookSettingsPage(
    page: Page,
    pageName: string,
): Promise<void> {
    const tab = bookSettingsTab(page, pageName);
    // A page's name can change after the dialog shows its pages: each name is localized
    // asynchronously, and until its translation arrives the tab shows the English given in the
    // code, which can differ (the BloomPUB page shows "eBooks" until then). So wait for the name.
    try {
        await expect(tab).toHaveCount(1, { timeout: 10000 });
    } catch {
        const offered = await bookSettingsDialog(page)
            .getByRole("tab")
            .allInnerTexts();
        throw new Error(
            `The Book and Page Settings dialog showed no page "${pageName}" within 10 seconds; it now offers: ${offered.join(", ")}.`,
        );
    }
    await realClick(tab);
    await expectBookSettingsPageShowing(page, pageName);
}

/** Assert that the open Book and Page Settings dialog is showing the page with this name. */
export async function expectBookSettingsPageShowing(
    page: Page,
    pageName: string,
): Promise<void> {
    await expect(
        bookSettingsTab(page, pageName),
        `the Book and Page Settings dialog is not showing its "${pageName}" page`,
    ).toHaveAttribute("aria-selected", "true");
}

function bookSettingsTab(page: Page, pageName: string): Locator {
    return bookSettingsDialog(page).getByRole("tab", {
        name: pageName,
        exact: true,
    });
}

/**
 * Tick or untick "Use full bleed page layout" on the dialog's Print Publishing page, which must be
 * showing, and return once the box shows that state. Nothing is saved until OK.
 */
export async function setFullBleedInBookSettings(
    page: Page,
    checked: boolean,
): Promise<void> {
    const checkbox = bookSettingsDialog(page).locator(
        'input[type="checkbox"][name="appearance.fullBleed"]',
    );
    await expect(
        checkbox,
        "the showing Book and Page Settings page has no full bleed check box",
    ).toHaveCount(1);
    if ((await checkbox.isChecked()) !== checked) await realClick(checkbox);
    await expect(checkbox).toBeChecked({ checked });
}

/**
 * Choose a page theme in the "Page Theme" list on the dialog's Theme & Layout page, which must be
 * showing, by the name the list shows, e.g. "Rounded Border Ebook", as a person does by opening the
 * list and clicking it. Returns once the list shows it. Fails naming the themes the list offered
 * when it has no such theme. Nothing is saved until OK.
 */
export async function choosePageThemeInBookSettings(
    page: Page,
    themeName: string,
): Promise<void> {
    // Config-R puts a setting's label in an h4 in its row; the list itself is an MUI Select,
    // whose clickable part is the element that announces a listbox.
    const list = bookSettingsDialog(page)
        .locator("li")
        .filter({
            has: page.locator("h4").getByText("Page Theme", { exact: true }),
        })
        .locator('[aria-haspopup="listbox"]');
    await expect(
        list,
        'the showing Book and Page Settings page has no "Page Theme" list',
    ).toHaveCount(1);
    await chooseInList(page, list, "Page Theme", themeName);
}

/** The warning on the Theme & Layout page that Edge to Edge needs full bleed on paper. */
function edgeToEdgeFullBleedWarning(page: Page): Locator {
    return bookSettingsDialog(page).getByTestId(
        "edge-to-edge-needs-full-bleed-warning",
    );
}

/**
 * Assert whether the open dialog shows the warning that pictures cannot reach the edge of a
 * printed page because the book uses Edge to Edge without full bleed, waiting for the dialog to
 * settle. The dialog updates the warning as soon as the theme or full bleed changes, without
 * saving.
 */
export async function expectEdgeToEdgeFullBleedWarning(
    page: Page,
    showing: boolean,
): Promise<void> {
    const warning = edgeToEdgeFullBleedWarning(page);
    if (showing)
        await expect(
            warning,
            "Book and Page Settings should warn that Edge to Edge needs full bleed",
        ).toBeVisible();
    else
        await expect(
            warning,
            "Book and Page Settings should not show the Edge to Edge full bleed warning",
        ).toHaveCount(0);
}

/**
 * Click the link in the Edge to Edge full bleed warning, which takes the dialog to the page where
 * full bleed is turned on, and return once that page is showing.
 */
export async function followEdgeToEdgeFullBleedWarningLink(
    page: Page,
): Promise<void> {
    const warning = edgeToEdgeFullBleedWarning(page);
    await expect(
        warning,
        "the Edge to Edge full bleed warning is not showing",
    ).toBeVisible();
    await realClick(warning.getByRole("button"));
    await expectBookSettingsPageShowing(page, "Print Publishing");
}

/**
 * Press the Book and Page Settings button with a real mouse click, then wait two animation frames
 * in the shell document, and no longer. For a test of whether the button responds at all, such as
 * while a game is being played and the top bar's editing controls are disabled. The button's click
 * handler opens the dialog by setting React state in this same document, so by then the dialog is
 * in the DOM if the click reached the button: isBookSettingsDialogShowing can be asked at once,
 * for either answer. Found by its test id: disabled (inert) controls are out of the accessibility
 * tree, where openBookSettings looks.
 */
export async function pressBookSettingsButton(page: Page): Promise<void> {
    await realClick(page.getByTestId("book-settings-button"));
    await waitForClickToSettle(page);
}

/** True while the Book and Page Settings dialog is open. */
export async function isBookSettingsDialogShowing(
    page: Page,
): Promise<boolean> {
    return (await bookSettingsDialog(page).count()) > 0;
}

/**
 * One Config-R check box of the open dialog's showing page, by its setting's path, e.g.
 * "appearance.cover-topic-show".
 */
export function bookSettingsCheckbox(page: Page, path: string): Locator {
    return bookSettingsDialog(page).locator(
        `input[type="checkbox"][name="${path}"]`,
    );
}

/**
 * Tick or untick one check box of the open dialog's showing page, by its setting's path, and
 * return once the box shows that state. Nothing is saved until OK.
 */
export async function setBookSettingsCheckbox(
    page: Page,
    path: string,
    checked: boolean,
): Promise<void> {
    const checkbox = bookSettingsCheckbox(page, path);
    await expect(
        checkbox,
        `the showing Book and Page Settings page has no "${path}" check box`,
    ).toHaveCount(1);
    if ((await checkbox.isChecked()) !== checked) await realClick(checkbox);
    await expect(checkbox).toBeChecked({ checked });
}

/**
 * Close Book and Page Settings with OK and, when a book setting was changed, wait until the Edit
 * tab has rebuilt the page it shows with the saved settings.
 *
 * The dialog saves the book's settings only when one of them changed
 * (BookAndPageSettingsDialog.tsx, saveSettingsAndCloseDialog), and only that save makes Bloom
 * rebuild the page. So a test that presses OK with no book setting changed, or with only a page
 * setting changed, passes `bookSettingsChanged: false`; waiting for a rebuild that never comes
 * would fail it after a minute.
 */
export async function okBookSettings(
    page: Page,
    { bookSettingsChanged = true }: { bookSettingsChanged?: boolean } = {},
): Promise<void> {
    if (bookSettingsChanged) await markPageDocument(page);
    await realClick(bookSettingsDialog(page).getByTestId("dialog-ok"));
    await expect(
        bookSettingsDialog(page),
        "Book and Page Settings did not close after OK",
    ).toHaveCount(0);
    if (!bookSettingsChanged) return;
    await expect
        .poll(async () => isPageDocumentMarked(page), {
            timeout: 60000,
            message:
                "Bloom never rebuilt the page after saving the book's settings.",
        })
        .toBe(false);
    await waitForEditablePage(page);
}

/** Close Book and Page Settings with Cancel, so nothing changed in it is saved. */
export async function cancelBookSettings(page: Page): Promise<void> {
    await realClick(bookSettingsDialog(page).getByTestId("dialog-cancel"));
    await expect(
        bookSettingsDialog(page),
        "Book and Page Settings did not close after Cancel",
    ).toHaveCount(0);
}

/**
 * The image resolutions the BloomPUB page of Book and Page Settings offers on its slider, in the
 * slider's order, by the name the slider shows, with the largest picture each allows, landscape
 * (BloomResolutionSliderInner in BookSettingsConfigrPages.tsx). A portrait picture gets the same
 * box turned on its side.
 */
export const kBloomPubResolutions = [
    { name: "Small", maxWidth: 600, maxHeight: 600 },
    { name: "HD", maxWidth: 1280, maxHeight: 720 },
    { name: "Full HD", maxWidth: 1920, maxHeight: 1080 },
    { name: "4K", maxWidth: 3840, maxHeight: 2160 },
] as const;

/** One of the BloomPUB image resolutions, by the name the slider shows. */
export type BloomPubResolutionName =
    (typeof kBloomPubResolutions)[number]["name"];

/** The largest picture, landscape, that the book's BloomPUBs carry, as the book has it saved. */
export async function getBloomPubImageSettings(
    page: Page,
): Promise<{ maxWidth: number; maxHeight: number }> {
    const settings = await apiGetJson<{
        publish?: {
            bloomPUB?: {
                imageSettings?: { maxWidth: number; maxHeight: number };
            };
        };
    }>(page, "book/settings");
    const imageSettings = settings.publish?.bloomPUB?.imageSettings;
    if (!imageSettings)
        throw new Error(
            "book/settings has no publish.bloomPUB.imageSettings for the book: " +
                JSON.stringify(settings.publish),
        );
    return {
        maxWidth: imageSettings.maxWidth,
        maxHeight: imageSettings.maxHeight,
    };
}

/** The image resolution slider on the BloomPUB page of Book and Page Settings. */
function bloomPubResolutionSlider(page: Page): Locator {
    return bookSettingsDialog(page).locator(".MuiSlider-root");
}

/**
 * The image resolution the slider on the BloomPUB page of the open Book and Page Settings dialog is
 * set to, e.g. "HD". The page must be showing (see showBookSettingsPage).
 *
 * This reads the slider's position, not the name written above it: the name is translated, so it
 * is one of kBloomPubResolutions' names only when Bloom's UI is in English.
 */
export async function getBloomPubResolutionInBookSettings(
    page: Page,
): Promise<BloomPubResolutionName> {
    const slider = bloomPubResolutionSlider(page);
    await expect(
        slider,
        "the showing Book and Page Settings page has no image resolution slider",
    ).toHaveCount(1);
    const position = await slider.locator('input[type="range"]').inputValue();
    const resolution = kBloomPubResolutions[Number(position)];
    if (!resolution)
        throw new Error(
            `The image resolution slider is at "${position}", which is none of its ${kBloomPubResolutions.length} positions.`,
        );
    return resolution.name;
}

/**
 * Move the image resolution slider on the BloomPUB page of the open Book and Page Settings dialog
 * to `resolution`, the way a person does with the keyboard: the slider's thumb takes the arrow
 * keys, one step per press. Returns once the slider is there. Nothing is saved until OK.
 */
export async function setBloomPubResolutionInBookSettings(
    page: Page,
    resolution: BloomPubResolutionName,
): Promise<void> {
    const names: string[] = kBloomPubResolutions.map((r) => r.name);
    const from = names.indexOf(await getBloomPubResolutionInBookSettings(page));
    const to = names.indexOf(resolution);
    const thumb = bloomPubResolutionSlider(page).locator('input[type="range"]');
    await thumb.focus();
    for (let i = 0; i < Math.abs(to - from); i++)
        await thumb.press(to > from ? "ArrowRight" : "ArrowLeft");
    await expect
        .poll(async () => getBloomPubResolutionInBookSettings(page), {
            timeout: 10000,
            message: `The image resolution slider never moved to ${resolution}.`,
        })
        .toBe(resolution);
}
