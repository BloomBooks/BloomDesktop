// The font chooser: the dropdown of installed fonts that shows, beside each font, whether Bloom may
// use it in a published book (a check mark), may not (an exclamation mark), or cannot tell (a
// question mark), and the information pane that hovering one of those marks opens. In the source
// it is FontSelectComponent, with FontDisplayBar for each row and FontInformationPane for the pane.
//
// The chooser appears in two places: the Format dialog in the Edit tab (see formatDialog.ts), which
// is where these helpers drive it, and the Book Making tab of the collection Settings dialog, a
// WinForms surface CDP cannot reach (see AUTOMATION-DEBT.md). The component is the same in both.
//
// Everything here lives in the Edit tab's page frame, where the Format dialog is: the chooser sits
// in the dialog, and its list and its information pane are portals in that frame's body. Bloom
// marks the font it may not use as dimmed in the list but still lets it be chosen, and the mark of
// the chosen font in the closed chooser is coloured (red, gold, or Bloom blue) where the same mark
// in the list is grey.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGetJson } from "./api";
import { editablePageFrame } from "./bookMaking";

/** The container the Format dialog renders the chooser into (StyleEditor.pug). */
const CHOOSER_CONTAINER = "#fontSelectComponent";
/** The closed chooser: the control showing the chosen font and its mark. */
const CHOOSER = `${CHOOSER_CONTAINER} .MuiSelect-select`;
/** The dropdown list, which MUI portals into the frame's body while it is open. */
const LIST = ".MuiMenu-root [role='listbox']";
/** One font in the list. Its data-value is the font's name. */
const LIST_ITEM = "[role='option']";
/** The information pane's popover: the one MUI popover in the frame that is not the list's menu. */
const PANE = ".MuiPopover-root:not(.MuiMenu-root) .MuiPaper-root";
/** The information icon in the pane, which shows the font's raw metadata. Not shown for a usable font. */
const PANE_DETAILS_ICON = "svg[data-testid='InfoOutlinedIcon']";
/** The pane's close button (the X). Clicking anywhere on the pane closes it; this is the visible way. */
const PANE_CLOSE_BUTTON = "button";
/** The pane's main message, the first paragraph. */
const PANE_MESSAGE = ".MuiTypography-body2";
/** The font's name in the pane, set in bold. */
const PANE_FONT_NAME = ".MuiTypography-subtitle2";

/** What the mark beside a font says: usable, not usable, or Bloom cannot tell. */
export type FontMark = "check" | "exclamation" | "question";

/** The colours Bloom draws a mark or a font name in, named as the theme names them. */
export type FontChooserColor = "bloom-blue" | "gold" | "red" | "grey" | "black";

/** One font's metadata as GET fonts/metadata reports it (FontMetadata.cs). Only the fields tests use. */
export interface IFontMetadata {
    name: string;
    version?: string;
    license?: string;
    licenseURL?: string;
    designer?: string;
    designerURL?: string;
    manufacturer?: string;
    manufacturerURL?: string;
    variants?: string[];
    determinedSuitability: "ok" | "unknown" | "unsuitable" | "invalid";
    determinedSuitabilityNotes?: string;
}

/** One row of the font list, or the closed chooser, as shown. */
export interface IFontMarkDisplay {
    name: string;
    mark: FontMark;
    markColor: FontChooserColor;
    /** The colour of the font's name: black for a usable font, grey for a dimmed one. */
    textColor: FontChooserColor;
    /** False when the row is disabled and cannot be chosen. Bloom dims rows but never disables them. */
    enabled: boolean;
}

/** What the information pane shows for one font. */
export interface IFontInformation {
    /** The verdict at the top of the pane, e.g. that the font is legal for all Bloom purposes. */
    message: string;
    /** The font's name, set in bold. */
    name: string;
    /** Every link in the pane's summary: the designer, the manufacturer, the license. */
    links: { text: string; href: string }[];
    /** True when the pane offers the information icon that shows the font's raw metadata. */
    hasDetailsIcon: boolean;
    /** The whole pane as text, for checks on the summary block (styles, version). */
    text: string;
}

/** Which kind of font a test wants, by what the chooser says about it. */
export type FontKind = "usable" | "microsoft" | "not-embeddable" | "unknown";

/** How the four kinds are recognised in the metadata. */
const MICROSOFT_LICENSE_URL =
    "https://learn.microsoft.com/en-us/typography/fonts/font-faq";
const isKind = (font: IFontMetadata, kind: FontKind): boolean => {
    switch (kind) {
        case "usable":
            return font.determinedSuitability === "ok";
        case "microsoft":
            return (
                font.determinedSuitability === "unsuitable" &&
                font.licenseURL === MICROSOFT_LICENSE_URL
            );
        case "not-embeddable":
            return (
                font.determinedSuitability === "unsuitable" &&
                font.licenseURL !== MICROSOFT_LICENSE_URL
            );
        case "unknown":
            return font.determinedSuitability === "unknown";
    }
};

/** The computed colours Bloom's theme constants come out as (bloomMaterialUITheme.ts). */
const COLOR_NAMES: Record<string, FontChooserColor> = {
    "rgb(29, 148, 164)": "bloom-blue", // kBloomBlue #1d94a4
    "rgb(243, 170, 24)": "gold", // kBloomGold #f3aa18
    "rgb(255, 0, 0)": "red", // kErrorColor
    "rgb(187, 187, 187)": "grey", // kDisabledControlGray #bbb
    "rgb(0, 0, 0)": "black",
};

/** The MUI icon each mark is drawn with (FontDisplayBar.tsx). MUI stamps the icon's name on the svg. */
const MARK_BY_ICON: Record<string, FontMark> = {
    CheckCircleIcon: "check",
    ErrorIcon: "exclamation",
    HelpIcon: "question",
};

/** Name a computed colour, or throw so that a new theme colour is noticed rather than misread. */
const nameColor = (computed: string, what: string): FontChooserColor => {
    const name = COLOR_NAMES[computed];
    if (!name)
        throw new Error(
            `${what} is drawn in ${computed}, which is not a colour the font chooser is known to use.`,
        );
    return name;
};

/** Every font Bloom offers, with its metadata, as fonts/metadata reports them. */
export async function getFontsMetadata(page: Page): Promise<IFontMetadata[]> {
    return apiGetJson<IFontMetadata[]>(page, "fonts/metadata");
}

/** The metadata of one font by name. Throws, naming the fonts there are, when Bloom has no such font. */
export async function getFontMetadata(
    page: Page,
    fontName: string,
): Promise<IFontMetadata> {
    const fonts = await getFontsMetadata(page);
    const font = fonts.find((f) => f.name === fontName);
    if (!font)
        throw new Error(
            `Bloom offers no font called "${fontName}". It offers: ${fonts.map((f) => f.name).join(", ")}.`,
        );
    return font;
}

/**
 * The name of a font of the given kind, from the fonts Bloom offers on this machine: the first of
 * `preferred` that is there and is of that kind, otherwise the first such font. Which fonts a
 * machine has varies, so a test names the fonts it would like, in order, and takes what it gets.
 * The nightly runner has the fonts bloom-testing-inputs ships under fonts/ (installed by
 * scripts/install-test-fonts.ps1), so a test names those first to behave the same everywhere.
 *
 * Throws when the machine has no font of the kind, listing how many of each kind it does have: a
 * test of the mark for that kind cannot run on such a machine, and the failure should say so.
 */
export async function pickFont(
    page: Page,
    kind: FontKind,
    preferred: string[] = [],
): Promise<string> {
    const fonts = await getFontsMetadata(page);
    const candidates = fonts.filter((f) => f.name && isKind(f, kind));
    const chosen =
        preferred
            .map((name) => candidates.find((f) => f.name === name))
            .find((f) => f) ?? candidates[0];
    if (!chosen) {
        const kinds: FontKind[] = [
            "usable",
            "microsoft",
            "not-embeddable",
            "unknown",
        ];
        throw new Error(
            `This machine has no ${kind} font for the test to use. Of its ${fonts.length} fonts, ` +
                kinds
                    .map(
                        (k) =>
                            `${fonts.filter((f) => isKind(f, k)).length} are ${k}`,
                    )
                    .join(", ") +
                ".",
        );
    }
    return chosen.name;
}

/** What is read off one font display bar in the page, before the colours and icon are named. */
interface IRawMarkDisplay {
    name: string;
    icon: string;
    markColor: string;
    textColor: string;
    enabled: boolean;
}

/**
 * Read font display bars (list rows, or the closed chooser) in the page, in one round trip. This
 * is the page function for Locator.evaluateAll, so it runs in the browser and can refer to nothing
 * outside itself. A row's name is its data-value when it has one, else the text shown.
 */
const readRawMarkDisplays = (elements: Element[]): IRawMarkDisplay[] =>
    elements.map((element) => {
        const svg = element.querySelector("svg");
        const text = element.querySelector(".MuiTypography-root");
        if (!svg || !text)
            throw new Error(
                "The font row has no mark or no name; is this a font display bar?",
            );
        return {
            name:
                element.getAttribute("data-value") ??
                text.textContent?.trim() ??
                "",
            icon: svg.getAttribute("data-testid") ?? "",
            markColor: getComputedStyle(svg).color,
            textColor: getComputedStyle(text).color,
            enabled: element.getAttribute("aria-disabled") !== "true",
        };
    });

/** Name the icon and colours of a raw reading, throwing on anything the chooser is not known to draw. */
const nameMarkDisplay = (raw: IRawMarkDisplay): IFontMarkDisplay => {
    const mark = MARK_BY_ICON[raw.icon];
    if (!mark)
        throw new Error(
            `The mark beside "${raw.name}" is a ${raw.icon || "(unnamed icon)"}, which is not one of the font chooser's marks.`,
        );
    return {
        name: raw.name,
        mark,
        markColor: nameColor(raw.markColor, `The mark beside "${raw.name}"`),
        textColor: nameColor(raw.textColor, `The name "${raw.name}"`),
        enabled: raw.enabled,
    };
};

/** The frame the Format dialog, and so the chooser, is in. */
const chooserFrame = (page: Page): Frame => editablePageFrame(page);

/**
 * Pull down the font list in the Format dialog, the way a person does, and wait until it is showing
 * its fonts. The Format dialog must be open (see openFormatDialog).
 */
export async function openFontList(page: Page): Promise<void> {
    const frame = chooserFrame(page);
    await frame.locator(CHOOSER).click({ timeout: 30000 });
    await expect(
        frame.locator(LIST),
        "Clicking the font chooser did not open its list.",
    ).toBeVisible({ timeout: 30000 });
    await expect
        .poll(() => frame.locator(`${LIST} ${LIST_ITEM}`).count(), {
            timeout: 30000,
            message: "The font list opened but never listed a font.",
        })
        .toBeGreaterThan(0);
}

/** True while the font list is pulled down. */
export async function isFontListOpen(page: Page): Promise<boolean> {
    return chooserFrame(page).locator(LIST).isVisible();
}

/** Every font in the pulled-down list, in its order, with the mark and colours each is shown with. */
export async function getFontListItems(
    page: Page,
): Promise<IFontMarkDisplay[]> {
    const rows = chooserFrame(page).locator(`${LIST} ${LIST_ITEM}`);
    if ((await rows.count()) === 0)
        throw new Error(
            "The font list is not open, or lists no font. Open it with openFontList first.",
        );
    // One round trip for the whole list: a machine has hundreds of fonts.
    return (await rows.evaluateAll(readRawMarkDisplays)).map(nameMarkDisplay);
}

/** The list row for one font. */
const listRow = (page: Page, fontName: string): Locator =>
    chooserFrame(page).locator(
        `${LIST} ${LIST_ITEM}[data-value="${fontName}"]`,
    );

/** Wait for the information pane to open, then read it. */
const waitForPane = async (
    page: Page,
    what: string,
): Promise<IFontInformation> => {
    const pane = chooserFrame(page).locator(PANE);
    await expect(
        pane,
        `Hovering ${what} did not open the font information pane.`,
    ).toBeVisible({ timeout: 30000 });
    return readFontInformationPane(page);
};

/**
 * Hover the mark beside one font in the pulled-down list, the way a person asks what the mark means,
 * and wait for the information pane to open. Returns what the pane says.
 *
 * Bloom opens the pane a moment after the pointer arrives (a 700ms debounce in FontDisplayBar), and
 * the pane is modal: close it with closeFontInformationPane before hovering or clicking anything
 * else in the list.
 */
export async function hoverFontMarkInList(
    page: Page,
    fontName: string,
): Promise<IFontInformation> {
    const row = listRow(page, fontName);
    await expect(
        row,
        `The font list has no row for "${fontName}".`,
    ).toHaveCount(1, { timeout: 30000 });
    await row.locator("svg").hover({ timeout: 30000 });
    return waitForPane(page, `the mark beside "${fontName}" in the list`);
}

/**
 * Hover the mark in the closed chooser, beside the name of the chosen font, and wait for the
 * information pane to open. Returns what the pane says. Close it with closeFontInformationPane.
 */
export async function hoverChosenFontMark(
    page: Page,
): Promise<IFontInformation> {
    await chooserFrame(page)
        .locator(`${CHOOSER} svg`)
        .hover({ timeout: 30000 });
    return waitForPane(page, "the mark in the font chooser");
}

/** Read the open information pane. Throws when no pane is open. */
export async function readFontInformationPane(
    page: Page,
): Promise<IFontInformation> {
    const pane = chooserFrame(page).locator(PANE);
    if (!(await pane.isVisible()))
        throw new Error(
            "The font information pane is not open, so there is nothing to read.",
        );
    return pane.evaluate(
        (element, selectors) => ({
            message:
                element.querySelector(selectors.message)?.textContent?.trim() ??
                "",
            name:
                element.querySelector(selectors.name)?.textContent?.trim() ??
                "",
            // A font with no manufacturer at all still gets a manufacturer link, with an empty
            // href (FontInformationPane renders the link when neither name nor URL is there).
            // That is not a link a person can follow, so leave it out.
            links: Array.from(element.querySelectorAll("a[href]"))
                .filter((a) => a.getAttribute("href"))
                .map((a) => ({
                    text: a.textContent?.trim() ?? "",
                    href: a.getAttribute("href") ?? "",
                })),
            hasDetailsIcon: !!element.querySelector(selectors.detailsIcon),
            text: element.textContent ?? "",
        }),
        {
            message: PANE_MESSAGE,
            name: PANE_FONT_NAME,
            detailsIcon: PANE_DETAILS_ICON,
        },
    );
}

/** Close the information pane with its X button, and wait for it to go. */
export async function closeFontInformationPane(page: Page): Promise<void> {
    const pane = chooserFrame(page).locator(PANE);
    await pane.locator(PANE_CLOSE_BUTTON).click({ timeout: 30000 });
    await expect(
        pane,
        "The font information pane stayed open after its close button was clicked.",
    ).toBeHidden({ timeout: 30000 });
}

/**
 * Click the information icon in the open pane, which shows the font's raw metadata in a message box,
 * and return that message. The box is the browser's own alert; this accepts it, so nothing is left
 * open. Only a font Bloom may not use, or cannot judge, offers the icon.
 */
export async function showFontDetails(page: Page): Promise<string> {
    const icon = chooserFrame(page).locator(`${PANE} ${PANE_DETAILS_ICON}`);
    await expect(
        icon,
        "The open font information pane offers no information icon.",
    ).toBeVisible({ timeout: 30000 });
    // Listen before clicking: Playwright dismisses a dialog nobody is listening for.
    const message = new Promise<string>((resolve) => {
        page.once("dialog", async (dialog) => {
            const text = dialog.message();
            await dialog.accept();
            resolve(text);
        });
    });
    await icon.click({ timeout: 30000 });
    return message;
}

/**
 * Choose a font from the pulled-down list, the way a person does, and wait until the chooser shows
 * it. The list closes. What the choice does to the text box is the caller's to check (see
 * getFontFamilyInGroup in bookMaking.ts).
 */
export async function chooseFont(page: Page, fontName: string): Promise<void> {
    const row = listRow(page, fontName);
    await expect(
        row,
        `The font list has no row for "${fontName}".`,
    ).toHaveCount(1, { timeout: 30000 });
    await row.click({ timeout: 30000 });
    const frame = chooserFrame(page);
    await expect(
        frame.locator(LIST),
        `The font list stayed open after "${fontName}" was chosen.`,
    ).toBeHidden({ timeout: 30000 });
    await expect(
        frame.locator(CHOOSER),
        `The font chooser does not show "${fontName}" after it was chosen.`,
    ).toContainText(fontName, { timeout: 30000 });
}

/** The font the closed chooser shows, with the mark and colours beside its name. */
export async function getChosenFont(page: Page): Promise<IFontMarkDisplay> {
    const chooser = chooserFrame(page).locator(CHOOSER);
    await chooser.waitFor({ state: "visible", timeout: 30000 });
    const [raw] = await chooser.evaluateAll(readRawMarkDisplays);
    return nameMarkDisplay(raw);
}
