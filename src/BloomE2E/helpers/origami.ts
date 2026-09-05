// Drive the Edit tab's "Change Layout" mode, which is Bloom's own name for origami: the page's
// sections, the "+" buttons that split them, and the list of types an empty section offers.
//
// The toggle and the sections are all in the page's own iframe: the toggle is drawn above the page
// but rendered into that same document.
//
// The type list is found by each link's `data-i18n` key rather than by the word it shows, because
// origami localizes those words.

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame, waitForEditablePage } from "./bookMaking";

/** The types origami offers an empty section, by the i18n key each link carries. */
const TYPE_KEY = {
    image: "EditTab.CustomPage.Image",
    canvas: "EditTab.CustomPage.Canvas",
    video: "EditTab.CustomPage.Video",
    table: "EditTab.CustomPage.Table",
    text: "EditTab.CustomPage.Text",
    widget: "EditTab.CustomPage.HtmlWidget",
} as const;

/** A type a section can be made into. */
export type SectionType = keyof typeof TYPE_KEY;

/** Which edge to split a section from, as origami's own "+" buttons are named. */
export type SplitEdge = "left" | "right" | "top" | "bottom";

/** True when the page being edited is in Change Layout mode. */
export async function isChangeLayoutMode(page: Page): Promise<boolean> {
    return (
        (await editablePageFrame(page)
            .locator(".marginBox.origami-layout-mode")
            .count()) > 0
    );
}

/**
 * Turn Change Layout mode on or off, by clicking the same label a person clicks, and wait until the
 * page has rebuilt itself accordingly. Does nothing when it is already the way asked.
 *
 * Leaving the mode is what makes origami write its changes into the page, so a test that changed
 * the layout has to come back out before asking what the page holds.
 */
export async function setChangeLayoutMode(
    page: Page,
    on: boolean,
): Promise<void> {
    if ((await isChangeLayoutMode(page)) === on) return;
    // The switch is drawn above the page but inside the page's own iframe: AbovePageControls
    // renders it into that document, not into the Edit tab's shell.
    const frame = editablePageFrame(page);
    const label = frame.locator('label[for="changeLayoutToggle"]');
    const box = frame.locator("#changeLayoutToggle");
    if ((await label.count()) === 0 && (await box.count()) === 0)
        throw new Error(
            "The Edit tab is not offering a Change Layout switch, so this page does not allow a " +
                "custom layout. Origami is only offered for a page whose template is a customPage.",
        );
    await label.click();
    await expect
        .poll(async () => isChangeLayoutMode(page), {
            timeout: 30000,
            message:
                `Clicking the Change Layout switch did not turn the mode ` +
                `${on ? "on" : "off"}.`,
        })
        .toBe(on);
    if (!on) {
        // Leaving the mode is not finished when the mode is off. Origami saves the page and asks
        // Bloom to rebuild it, half a second later, and until that rebuild lands the page is the
        // one layout mode left behind: its text boxes have had contentEditable taken off them (see
        // setupLayoutMode in origami.ts), so anything typed goes nowhere and anything measured is
        // about to be replaced. An editable text box is therefore the signal that the real page is
        // back.
        await editablePageFrame(page)
            .locator('.bloom-editable[contenteditable="true"]')
            .first()
            .waitFor({ state: "attached", timeout: 60000 });
        await waitForEditablePage(page);
    }
}

/**
 * Every section of the page being edited, in document order. Origami's own name for one of these is
 * a split-pane component.
 */
export function sections(page: Page): Locator {
    return editablePageFrame(page).locator(".split-pane-component-inner");
}

/**
 * The types the section at `sectionIndex` is offering. Empty for a section that already holds
 * something, because origami takes the list away as soon as it does.
 */
export async function getSectionTypesOffered(
    page: Page,
    sectionIndex = 0,
): Promise<SectionType[]> {
    const keys = await sections(page)
        .nth(sectionIndex)
        .locator(".selector-links a[data-i18n]")
        .evaluateAll((links) =>
            links.map((link) => link.getAttribute("data-i18n") ?? ""),
        );
    return (Object.keys(TYPE_KEY) as SectionType[]).filter((type) =>
        keys.includes(TYPE_KEY[type]),
    );
}

/**
 * What the Table entry of a section's type chooser looks like. The entry has three possible
 * states, and they mean different things (see createTableSelector in origami.ts): missing
 * altogether when the "Tables" experiment is off, dimmed and badged when the experiment is on but
 * the collection's subscription tier is below the one tables need, and an ordinary link when
 * tables can be made.
 */
export interface ITableSectionTypeOffer {
    /** True when the word "Table" is in the section's list of types at all. */
    offered: boolean;
    /** True when the entry is dimmed and carries the subscription badge. */
    needsSubscription: boolean;
    /** The entry's opacity, which is what the dimming amounts to. 1 when it is not dimmed. */
    opacity: number;
}

/** The Table entry, and its badge, inside one section's list of types. */
function tableTypeEntry(page: Page, sectionIndex: number): Locator {
    return sections(page)
        .nth(sectionIndex)
        .locator(`.selector-links a[data-i18n="${TYPE_KEY.table}"]`);
}

/**
 * Read the state of the Table entry in the section at `sectionIndex`. Requires Change Layout mode
 * and a section with nothing in it yet, which is the only state in which origami offers the list.
 */
export async function getTableSectionTypeOffer(
    page: Page,
    sectionIndex = 0,
): Promise<ITableSectionTypeOffer> {
    const link = tableTypeEntry(page, sectionIndex);
    if ((await link.count()) === 0)
        return { offered: false, needsSubscription: false, opacity: 1 };
    const state = await link.first().evaluate((element) => ({
        // The class origami puts on the entry when the tier is too low, and the badge beside it.
        dimmed: element.classList.contains("origami-featureNeedsSubscription"),
        opacity: Number.parseFloat(getComputedStyle(element).opacity),
        badges:
            element.parentElement?.querySelectorAll(".subscription-badge")
                .length ?? 0,
    }));
    return {
        offered: true,
        needsSubscription: state.dimmed && state.badges > 0,
        opacity: state.opacity,
    };
}

/**
 * Click the Table entry in the section at `sectionIndex` without expecting the section to become a
 * table. That is what a person gets below the tier tables need: the entry is dimmed and clicking
 * it only explains itself. Use chooseSectionType when the click should actually make a table.
 */
export async function clickTableSectionType(
    page: Page,
    sectionIndex = 0,
): Promise<void> {
    const link = tableTypeEntry(page, sectionIndex);
    if ((await link.count()) === 0)
        throw new Error(
            `Section ${sectionIndex} does not offer the "table" type at all, so there is ` +
                `nothing to click. It offers: ` +
                `${(await getSectionTypesOffered(page, sectionIndex)).join(", ") || "(nothing)"}.`,
        );
    await link.first().click();
}

/**
 * Make the section at `sectionIndex` hold `type`, by clicking that word in its list of types, and
 * wait until the list has gone.
 *
 * Requires Change Layout mode, and a section with nothing in it yet: that is the only state in
 * which origami offers the list.
 */
export async function chooseSectionType(
    page: Page,
    type: SectionType,
    sectionIndex = 0,
): Promise<void> {
    const section = sections(page).nth(sectionIndex);
    const link = section.locator(
        `.selector-links a[data-i18n="${TYPE_KEY[type]}"]`,
    );
    if ((await link.count()) === 0) {
        const offered = await getSectionTypesOffered(page, sectionIndex);
        throw new Error(
            `Section ${sectionIndex} does not offer the "${type}" type. It offers: ` +
                `${offered.join(", ") || "(nothing, so it already holds something)"}.`,
        );
    }
    await link.click();
    await expect(
        section.locator(".selector-links"),
        `Choosing the "${type}" type left the list of types in place, so origami did not take it.`,
    ).toHaveCount(0, { timeout: 30000 });
}

/**
 * Split the section at `sectionIndex` in two, by pressing the "+" on one of its edges, and wait
 * until the page has more sections than it had.
 *
 * Requires Change Layout mode. Which edge decides where the new, empty section goes.
 */
export async function splitSection(
    page: Page,
    edge: SplitEdge,
    sectionIndex = 0,
): Promise<void> {
    const before = await sections(page).count();
    const section = sections(page).nth(sectionIndex);
    // The "+" buttons are drawn only while the pointer is over the section they belong to, so
    // bring it there first, which is also what a person does.
    await section.hover({ timeout: 30000 });
    const button = section
        .locator(`.origami-controls a.button.add-${edge}`)
        .first();
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.click();
    await expect
        .poll(async () => sections(page).count(), {
            timeout: 30000,
            message:
                `Pressing the "+" on the ${edge} edge did not split section ` +
                `${sectionIndex}.`,
        })
        .toBeGreaterThan(before);
}
