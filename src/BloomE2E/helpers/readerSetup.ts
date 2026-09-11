// The Decodable Reader's "Set Up Stages" dialog: getting to it, and driving its three tabs.
//
// Three quirks about this surface are absorbed here, so tests do not have to know them.
//
// 1. The toolbox offers a tool only when the book has that tool enabled, and the Decodable
//    Reader is off on a new book. `enableDecodableReaderTool` turns it on through the same
//    endpoint the toolbox's own "More..." check box posts to, then makes the Edit tab rebuild
//    its toolbox, because the toolbox reads the enabled list once, when it initializes.
//
// 2. The dialog is NOT in the toolbox frame. It is React-rendered in the workspace root so its
//    modal backdrop covers the whole workspace rather than just the book pane, so its controls
//    are on `page` while the button that opens it is inside the toolbox frame. Every helper
//    below is on the side it belongs to.
//
// 3. The dialog's own labels are all localized (and its text boxes are named only by a nearby
//    heading, through aria-label), so nothing here matches on text. Each control carries a
//    `data-testid` added for this suite.

import { expect, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { waitForEditablePage } from "./bookMaking";
import { openTool, toolboxFrame } from "./toolbox";
import { switchTab } from "./workspace";

/** The "Set Up Stages" button along the bottom of the Decodable Reader tool's panel. */
const SET_UP_STAGES_BUTTON = '[data-testid="set-up-stages-button"]';

/**
 * The "This book is decodable" switch in the Decodable Reader's panel. The id is on MUI's
 * Switch root; the check box itself is the input inside it.
 */
const DECODABLE_SWITCH = '[data-testid="decodable-reader-switch"] input';

/** The setup dialog's root, which exists only while the dialog is showing. */
const SETUP_DIALOG = '[data-testid="decodable-reader-setup-dialog"]';

/** One row of the stage list down the left of the Decodable Stages tab. */
const PHASE_ROW = '[data-testid="reader-setup-phase-row"]';

/** The dialog's three tabs, in the order it shows them. */
export type ReaderSetupTab = "letters" | "sampleWords" | "stages";

const TAB_TEST_ID: Record<ReaderSetupTab, string> = {
    letters: "reader-setup-tab-letters",
    sampleWords: "reader-setup-tab-sample-words",
    stages: "reader-setup-tab-stages",
};

/** One stage as the collection's reader settings file stores it. */
export interface IReaderStage {
    letters: string;
    sightWords: string;
    allowedWordsFile?: string;
}

/** The collection's reader settings, as Bloom reads them back from disk. */
export interface IReaderSettings {
    letters: string;
    moreWords: string;
    stages: IReaderStage[];
}

/**
 * Turn the Decodable Reader tool on for the book being edited -- what a person does by ticking
 * it under the toolbox's "More..." -- and wait until the toolbox is actually offering it.
 *
 * This is SETUP, not the path under test. It posts the same setting the check box posts, then
 * leaves the Edit tab and comes back, because the toolbox asks which tools are enabled only
 * while it is initializing; without that round trip the tool stays absent until something else
 * happens to rebuild the toolbox.
 */
export async function enableDecodableReaderTool(page: Page): Promise<void> {
    await apiPost(
        page,
        "editView/saveToolboxSetting",
        // "active", then the check box's id (the tool id plus "Check"), then 1 for on.
        "active\tdecodableReaderCheck\t1",
        "text/plain",
    );
    await switchTab(page, "collection");
    await switchTab(page, "edit");
    // Wait for Bloom's editing state machine, not just for the toolbox to exist. While the Edit
    // tab is still loading, the tool panels re-render and the accordion is still animating, and
    // clicks aimed at them land on nothing, get swallowed by the expanding panel, or are undone
    // by the re-render that follows -- which is what made this test pass only three runs in five.
    await waitForEditablePage(page);
    await expect
        .poll(
            async () =>
                toolboxFrame(page)
                    .locator('[data-toolid="decodableReader"]')
                    .count(),
            {
                timeout: 30000,
                message:
                    "The toolbox never offered the Decodable Reader tool after it was enabled " +
                    "and the Edit tab was re-entered.",
            },
        )
        .toBeGreaterThan(0);
}

/**
 * Open the Decodable Reader tool in the toolbox, the way a person does, and wait until its
 * panel -- and so its "Set Up Stages" button -- is showing. Opens the toolbox drawer first if
 * it is shut.
 */
export async function openDecodableReaderTool(page: Page): Promise<void> {
    // Wait on the switch, not on the Set Up button: the panel renders the button only once the
    // book is marked decodable, so waiting on the button here would hang on a book that is not.
    await openTool(page, "decodableReader", DECODABLE_SWITCH);
}

/**
 * Turn the Decodable Reader's "This book is decodable" switch on or off, by clicking it the way
 * a person does, and wait until the panel has caught up.
 *
 * This matters for more than the switch itself: the panel shows "Set Up Stages" only while the
 * book is decodable, so this is on the click path to the setup dialog. The switch's own starting
 * position is read from the book's body classes, not from the toolbox's enabled-tools list, so
 * enabling the tool is not enough to make it on.
 */
export async function setBookIsDecodable(
    page: Page,
    isDecodable: boolean,
): Promise<void> {
    await openDecodableReaderTool(page);
    const frame = toolboxFrame(page);
    const box = frame.locator(DECODABLE_SWITCH);
    if ((await box.isChecked()) !== isDecodable) {
        await box.click();
    }
    await expect(
        box,
        `The Decodable Reader switch would not go ${isDecodable ? "on" : "off"}.`,
    ).toBeChecked({ checked: isDecodable, timeout: 30000 });
    // The Set Up Stages button appears and disappears with the switch, so its presence is how we
    // know the panel has re-rendered rather than just the switch having moved.
    await frame.locator(SET_UP_STAGES_BUTTON).waitFor({
        state: isDecodable ? "visible" : "detached",
        timeout: 30000,
    });
}

/**
 * Click "Set Up Stages", having first brought it into the toolbox's own scroll area.
 *
 * The button sits at the bottom of the tool's panel, and a run takes whatever window size the
 * machine gives it (see "Every run takes the developer's window size" in AUTOMATION-DEBT.md), so
 * on a shorter window it starts below the fold.
 */
async function clickSetUpStagesButton(page: Page): Promise<void> {
    const button = toolboxFrame(page).locator(SET_UP_STAGES_BUTTON);
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.scrollIntoViewIfNeeded({ timeout: 30000 });
    await button.click();
}

/**
 * Click "Set Up Stages" in the Decodable Reader's panel and wait until the setup dialog is
 * showing, with its stage list filled in. Opens the toolbox, the tool, and the decodable switch
 * first, so a test can call this straight after enabling the tool.
 */
export async function openDecodableStagesSetup(page: Page): Promise<void> {
    // "Set Up Stages" is shown only while the book is marked decodable.
    await setBookIsDecodable(page, true);
    await clickSetUpStagesButton(page);
    await page
        .locator(SETUP_DIALOG)
        .waitFor({ state: "visible", timeout: 30000 });
    // The stage list is filled from the settings the dialog was handed, so waiting for the
    // dialog alone would let a caller read an empty list.
    await expect
        .poll(async () => page.locator(PHASE_ROW).count(), {
            timeout: 30000,
            message:
                "The setup dialog opened but never showed a stage. It always shows at least " +
                "one, materializing an empty Stage 1 when the collection has none.",
        })
        .toBeGreaterThan(0);
}

/** Show one of the setup dialog's three tabs, and wait until it is the selected one. */
export async function openReaderSetupTab(
    page: Page,
    tab: ReaderSetupTab,
): Promise<void> {
    const testId = TAB_TEST_ID[tab];
    await page.locator(`[data-testid="${testId}"]`).click();
    await expect(
        page.locator(`[data-testid="${testId}"]`),
        `Clicking the "${tab}" tab did not select it.`,
    ).toHaveAttribute("aria-selected", "true", { timeout: 30000 });
}

/**
 * Type the collection's alphabet into the "Letters and Letter Combinations" box on the Letters
 * tab, replacing whatever is there. The caller must already be on that tab.
 */
export async function setAlphabetLetters(
    page: Page,
    letters: string,
): Promise<void> {
    await fillReaderSetupBox(page, "reader-setup-letters-box", letters);
}

/** What the Letters tab's alphabet box is showing. The caller must be on that tab. */
export async function getAlphabetLetters(page: Page): Promise<string> {
    return page
        .locator('[data-testid="reader-setup-letters-box"]')
        .inputValue();
}

/**
 * Type the selected stage's new sight words, replacing whatever is there. The caller must be on
 * the Decodable Stages tab.
 */
export async function setStageSightWords(
    page: Page,
    sightWords: string,
): Promise<void> {
    await fillReaderSetupBox(page, "reader-setup-sight-words-box", sightWords);
}

/** The selected stage's new sight words. The caller must be on the Decodable Stages tab. */
export async function getStageSightWords(page: Page): Promise<string> {
    return page
        .locator('[data-testid="reader-setup-sight-words-box"]')
        .inputValue();
}

/**
 * Add `letter` to the selected stage by clicking it in the letter grid, the way a person does.
 * The letters on offer are the collection's alphabet, so the Letters tab has to have been given
 * one first. Throws, naming the letters on offer, when this one is not among them.
 */
export async function selectStageLetter(
    page: Page,
    letter: string,
): Promise<void> {
    const button = page.locator(
        `[data-testid="reader-setup-letter-${letter}"]`,
    );
    if ((await button.count()) === 0) {
        const offered = await page
            .locator('[data-testid^="reader-setup-letter-"]')
            .evaluateAll((buttons) =>
                buttons.map((b) => b.textContent?.trim() ?? ""),
            );
        throw new Error(
            `The stage's letter grid does not offer "${letter}". It offers: ` +
                `${offered.join(", ") || "(nothing)"}. The grid shows the collection's ` +
                `alphabet, which is set on the Letters tab.`,
        );
    }
    await button.click();
}

/**
 * Click "Add Stage" and wait until the new stage is in the list. Returns the number of stages
 * afterwards.
 */
export async function addStage(page: Page): Promise<number> {
    const before = await getStageCount(page);
    await page.locator('[data-testid="reader-setup-add-phase"]').click();
    await expect
        .poll(async () => getStageCount(page), {
            timeout: 30000,
            message: `Clicking "Add Stage" did not add one; there are still ${before}.`,
        })
        .toBe(before + 1);
    return before + 1;
}

/** How many stages the dialog's stage list is showing. */
export async function getStageCount(page: Page): Promise<number> {
    return page.locator(PHASE_ROW).count();
}

/**
 * Press OK and wait until the dialog is gone. Bloom saves the settings and re-reads the sample
 * word files on the way out, so this can take a moment on a big collection.
 */
export async function acceptReaderSetup(page: Page): Promise<void> {
    await clickDialogButton(page, "dialog-ok");
}

/** Press Cancel and wait until the dialog is gone, abandoning the edits. */
export async function cancelReaderSetup(page: Page): Promise<void> {
    await clickDialogButton(page, "dialog-cancel");
}

/**
 * The collection's reader settings as Bloom reads them from disk -- what was actually saved,
 * rather than what the dialog is showing. For assertions after the dialog has closed.
 */
export async function getSavedReaderSettings(
    page: Page,
): Promise<IReaderSettings> {
    return apiGetJson<IReaderSettings>(page, "readers/io/readerToolSettings");
}

/**
 * Replace a setup-dialog text box's contents. The boxes are React-controlled and also commit on
 * blur, so this clears, types, and then blurs, rather than setting the value behind React's
 * back.
 */
async function fillReaderSetupBox(
    page: Page,
    testId: string,
    value: string,
): Promise<void> {
    const box = page.locator(`[data-testid="${testId}"]`);
    await box.fill(value);
    await box.blur();
    await expect(
        box,
        `The "${testId}" box did not take the text typed into it.`,
    ).toHaveValue(value, { timeout: 30000 });
}

/**
 * Press one of the dialog's bottom buttons and wait until the dialog has gone. Every Bloom
 * dialog's OK and Cancel carry these ids, because their labels are localized.
 */
async function clickDialogButton(
    page: Page,
    button: "dialog-ok" | "dialog-cancel",
): Promise<void> {
    const dialog = page.locator(SETUP_DIALOG);
    await dialog.locator(`[data-testid="${button}"]`).first().click();
    await dialog.waitFor({ state: "detached", timeout: 60000 });
}
