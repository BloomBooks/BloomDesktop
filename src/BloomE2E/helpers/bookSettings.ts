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
import { realClick } from "./realClick";

/** The book's appearance settings that tests here change. Only these fields. */
export interface IBookAppearance {
    /** The page theme, by the name of its file: "default", "edge-to-edge", "legacy-5-6", ... */
    cssThemeName: string;
    /** "Use full bleed page layout" on the dialog's Print Publishing page. */
    fullBleed: boolean;
}

/** What book/settings replies with. Only the part used here; the rest is posted back untouched. */
interface IBookSettings {
    appearance: IBookAppearance & Record<string, unknown>;
}

/** The book's current appearance settings, as the Book and Page Settings dialog would show them. */
export async function getBookAppearance(page: Page): Promise<IBookAppearance> {
    const settings = await apiGetJson<IBookSettings>(page, "book/settings");
    return {
        cssThemeName: settings.appearance.cssThemeName,
        fullBleed: !!settings.appearance.fullBleed,
    };
}

/**
 * Change the selected book's page theme and/or its full bleed setting, the way pressing OK in Book
 * and Page Settings does, and wait until the Edit tab has rebuilt the page it shows with them.
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
    Object.assign(settings.appearance, changes);
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
 * left, e.g. "Theme & Layout". Fails naming the pages the dialog offers when there is no such page.
 */
export async function showBookSettingsPage(
    page: Page,
    pageName: string,
): Promise<void> {
    const tab = bookSettingsTab(page, pageName);
    if ((await tab.count()) !== 1) {
        const offered = await bookSettingsDialog(page)
            .getByRole("tab")
            .allInnerTexts();
        throw new Error(
            `The Book and Page Settings dialog has no page "${pageName}"; it offers: ${offered.join(", ")}.`,
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

/** Close Book and Page Settings with Cancel, so nothing changed in it is saved. */
export async function cancelBookSettings(page: Page): Promise<void> {
    await realClick(bookSettingsDialog(page).getByTestId("dialog-cancel"));
    await expect(
        bookSettingsDialog(page),
        "Book and Page Settings did not close after Cancel",
    ).toHaveCount(0);
}
