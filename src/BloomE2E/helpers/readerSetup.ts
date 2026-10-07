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

import * as fs from "node:fs";
import * as Path from "node:path";
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

/**
 * The Leveled Reader's setup dialog, which is still the legacy jQuery one: its content is an
 * iframe of that id, wrapped by jQuery UI. Its presence is how a test tells the two dialogs
 * apart.
 */
const LEGACY_SETUP_DIALOG = "#settings_frame";

/** The "Set Up Levels" button in the Leveled Reader's panel. */
const SET_UP_LEVELS_BUTTON = '[data-testid="set-up-levels-button"]';

/** The Leveled Reader's "This book is leveled" switch. */
const LEVELED_SWITCH = '[data-testid="leveled-reader-switch"] input';

/** One row of the stage list down the left of the Decodable Stages tab. */
const PHASE_ROW = '[data-testid="reader-setup-phase-row"]';

/** One file in the Sample Words tab's listing of the collection's Sample Texts folder. */
const SAMPLE_TEXT_FILE_ROW = '[data-testid="reader-setup-sample-text-file"]';

/** The dialog's three tabs, in the order it shows them. */
export type ReaderSetupTab = "letters" | "sampleWords" | "stages";

const TAB_TEST_ID: Record<ReaderSetupTab, string> = {
    letters: "reader-setup-tab-letters",
    sampleWords: "reader-setup-tab-sample-words",
    stages: "reader-setup-tab-stages",
};

/** One stage as the collection's reader settings file stores it. */
export interface IReaderStage {
    /** The stage's displayed number, as a string; the dialog renumbers these on save. */
    name?: string;
    letters: string;
    sightWords: string;
    allowedWordsFile?: string;
}

/** The collection's reader settings, as Bloom reads them back from disk. */
export interface IReaderSettings {
    letters: string;
    moreWords: string;
    /** 1 when stages are defined by allowed-word lists rather than by letters. */
    useAllowedWords?: number;
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
    // Mark the book decodable here too, rather than by clicking the panel's switch later.
    // The switch's position is React state seeded from the page's body classes, so setting
    // the book BEFORE the reload below means the panel comes up with the switch already on,
    // and nothing has to click it. Clicking was the unreliable step: a re-render arriving
    // just after the click puts the switch back, and retrying in a loop only toggles it to
    // and fro, posting to Bloom and re-running the page markup each time.
    await apiPost(page, "toolbox/decodable", "true", "application/json");
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
    // Saving the reader settings makes Bloom rebuild the edit view, so a caller that starts
    // straight after a save -- the next test in a file, most often -- can arrive while the Edit
    // tab is briefly not showing at all. Waiting for the editing state machine first turns that
    // into a pause instead of "there is no toolbox toggle in this document". Cheap when the tab
    // is already up.
    await waitForEditablePage(page);
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
        // enableDecodableReaderTool normally leaves this already on, so this click is the
        // exception rather than the rule. Click once and verify -- never retry in a loop,
        // because clicking toggles, so a loop oscillates instead of converging (and each
        // toggle posts to Bloom and re-runs the page markup).
        await box.click();
    }
    await expect(
        box,
        `The Decodable Reader switch is not ${isDecodable ? "on" : "off"}.`,
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

/** One file as the Sample Words tab lists it: its name, and whether its type is one Bloom reads. */
export interface ISampleTextFile {
    name: string;
    validType: boolean;
    /** The explanation shown beside a file of the wrong type, or "" otherwise. */
    explanation: string;
}

/**
 * The Sample Texts folder as the dialog is showing it. An unreadable file is expected to be
 * listed with an explanation rather than left out; an empty list means the dialog is showing its
 * "no sample texts yet" message instead. The caller must be on the Sample Words tab.
 */
export async function getSampleTextFiles(
    page: Page,
): Promise<ISampleTextFile[]> {
    return page.locator(SAMPLE_TEXT_FILE_ROW).evaluateAll((rows) =>
        rows.map((row) => {
            const explanation = row.querySelector("span:last-of-type");
            const validType = row.getAttribute("data-valid-type") === "true";
            return {
                name: (row.textContent ?? "").trim(),
                validType,
                explanation: validType
                    ? ""
                    : (explanation?.textContent ?? "").trim(),
            };
        }),
    );
}

/** True when the Sample Words tab is showing its "no sample texts yet" message. */
export async function isShowingNoSampleTextsMessage(
    page: Page,
): Promise<boolean> {
    return (await page.locator("#readerSetupNoSampleTexts").count()) > 0;
}

/**
 * Type the words the collection supplies directly, rather than through files, into the Sample
 * Words tab's own box. The caller must be on that tab.
 */
export async function setTypedSampleWords(
    page: Page,
    words: string,
): Promise<void> {
    await fillReaderSetupBox(page, "reader-setup-sample-words-box", words);
}

/** What the Sample Words tab's typed-words box is showing. */
export async function getTypedSampleWords(page: Page): Promise<string> {
    return page
        .locator('[data-testid="reader-setup-sample-words-box"]')
        .inputValue();
}

/**
 * The words the Decodable Stages tab is previewing as decodable at the selected stage.
 *
 * This is the one panel fed from the toolbox frame rather than from the dialog's own copy of the
 * settings, so it is how a test checks that the cross-frame lookups still work.
 */
export async function getMatchingWords(page: Page): Promise<string[]> {
    return page
        .locator('[data-testid="reader-setup-matching-word"]')
        .evaluateAll((chips) =>
            chips.map((chip) => (chip.textContent ?? "").trim()),
        );
}

/** Remove the selected stage, and wait until the list is one shorter. */
export async function removeSelectedStage(page: Page): Promise<number> {
    const before = await getStageCount(page);
    await page.locator('[data-testid="reader-setup-remove-phase"]').click();
    await expect
        .poll(async () => getStageCount(page), {
            timeout: 30000,
            message: `Removing the stage did not shorten the list; there are still ${before}.`,
        })
        .toBe(before - 1);
    return before - 1;
}

/** Select a stage by its position in the list (0-based), and wait for it to be the shown one. */
export async function selectStage(page: Page, index: number): Promise<void> {
    await page.locator(PHASE_ROW).nth(index).click();
    // The sight-words box belongs to the selected stage, so its presence is the settled signal.
    await page
        .locator('[data-testid="reader-setup-sight-words-box"]')
        .waitFor({ state: "visible", timeout: 30000 });
}

/** True while the React Decodable Reader setup dialog is showing. */
export async function isDecodableSetupDialogShowing(
    page: Page,
): Promise<boolean> {
    return (await page.locator(SETUP_DIALOG).count()) > 0;
}

/** True while the legacy Leveled Reader setup dialog is showing. */
export async function isLeveledSetupDialogShowing(
    page: Page,
): Promise<boolean> {
    return (await page.locator(LEGACY_SETUP_DIALOG).count()) > 0;
}

/**
 * Turn the Leveled Reader tool on for the book being edited, the counterpart of
 * enableDecodableReaderTool, and wait until the toolbox offers it.
 */
export async function enableLeveledReaderTool(page: Page): Promise<void> {
    await apiPost(
        page,
        "editView/saveToolboxSetting",
        "active	leveledReaderCheck	1",
        "text/plain",
    );
    // Marked leveled before the reload, for the reason given in enableDecodableReaderTool.
    await apiPost(page, "toolbox/leveled", "true", "application/json");
    await switchTab(page, "collection");
    await switchTab(page, "edit");
    await waitForEditablePage(page);
    await expect
        .poll(
            async () =>
                toolboxFrame(page)
                    .locator('[data-toolid="leveledReader"]')
                    .count(),
            {
                timeout: 30000,
                message:
                    "The toolbox never offered the Leveled Reader tool after it was enabled.",
            },
        )
        .toBeGreaterThan(0);
}

/**
 * Open the Leveled Reader's "Set Up Levels" dialog -- still the legacy jQuery one -- the way a
 * person does, and wait until it is showing. Turns the tool's "This book is leveled" switch on
 * first, because the panel shows the button only while it is.
 */
export async function openLeveledReaderSetup(page: Page): Promise<void> {
    await waitForEditablePage(page); // see openDecodableReaderTool
    await openTool(page, "leveledReader", LEVELED_SWITCH);
    const frame = toolboxFrame(page);
    const box = frame.locator(LEVELED_SWITCH);
    if (!(await box.isChecked())) {
        await box.click(); // once only; see setBookIsDecodable
    }
    await expect(box, "The Leveled Reader switch is not on.").toBeChecked({
        timeout: 30000,
    });
    const button = frame.locator(SET_UP_LEVELS_BUTTON);
    await button.waitFor({ state: "visible", timeout: 30000 });
    await button.scrollIntoViewIfNeeded({ timeout: 30000 });
    await button.click();
    await page
        .locator(LEGACY_SETUP_DIALOG)
        .waitFor({ state: "visible", timeout: 30000 });
}

/**
 * Shut the legacy Leveled Reader setup dialog with its title-bar close button, and wait until it
 * is gone. The title bar is used rather than the Cancel button because the buttons are localized.
 */
export async function closeLeveledReaderSetup(page: Page): Promise<void> {
    await page.locator(".ui-dialog-titlebar-close").first().click();
    await page
        .locator(LEGACY_SETUP_DIALOG)
        .waitFor({ state: "detached", timeout: 30000 });
}

/** Show one of the setup dialog's three tabs, and wait until it is the selected one. */
export async function openReaderSetupTab(
    page: Page,
    tab: ReaderSetupTab,
): Promise<void> {
    const testId = TAB_TEST_ID[tab];
    // The Sample Words tab asks Bloom for the Sample Texts folder listing as it mounts, and shows
    // its "no sample texts yet" message until the answer arrives. Wait for that answer, and for
    // the list to show every file in it, so a caller never mistakes the placeholder for an empty
    // folder. Start listening before the click, or a quick answer is missed.
    const listing =
        tab === "sampleWords"
            ? page.waitForResponse((response) =>
                  response.url().includes("readers/ui/sampleTextsList"),
              )
            : undefined;
    await page.locator(`[data-testid="${testId}"]`).click();
    await expect(
        page.locator(`[data-testid="${testId}"]`),
        `Clicking the "${tab}" tab did not select it.`,
    ).toHaveAttribute("aria-selected", "true", { timeout: 30000 });
    if (listing) {
        const files = (await (await listing).text())
            .split("\r")
            .filter((path) => path);
        await expect
            .poll(async () => page.locator(SAMPLE_TEXT_FILE_ROW).count(), {
                timeout: 30000,
                message: `Bloom reported ${files.length} file(s) in the Sample Texts folder, but the Sample Words tab did not list them all.`,
            })
            .toBe(files.length);
    }
}

/**
 * Make the collection's Sample Texts folder hold exactly these files, removing anything else in
 * it. Every test in a spec file shares one Bloom and one collection (the fixture is
 * worker-scoped), so a test states the whole folder rather than adding to what the last one
 * left. An empty map leaves the folder existing but empty.
 */
export function setSampleTexts(
    collectionDir: string,
    files: { [name: string]: string },
): void {
    const folder = Path.join(collectionDir, "Sample Texts");
    fs.mkdirSync(folder, { recursive: true });
    for (const existing of fs.readdirSync(folder)) {
        fs.rmSync(Path.join(folder, existing), { force: true });
    }
    for (const [name, contents] of Object.entries(files)) {
        fs.writeFileSync(Path.join(folder, name), contents);
    }
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

/**
 * What each row of the stage list says, in the order shown, with the stage NUMBER stripped off.
 *
 * A row runs its number, its letters and its sight words together. The number is dropped because
 * it is the row's position rather than its identity: removing or reordering a stage renumbers
 * every row after it, so leaving the numbers in would make every such change look like every row
 * changed. What is left -- the letters and sight words -- is what tells the stages apart.
 */
export async function getStageRowTexts(page: Page): Promise<string[]> {
    return page.locator(PHASE_ROW).evaluateAll((rows) =>
        rows.map((row) =>
            (row.textContent ?? "")
                .replace(/\s+/g, " ")
                .trim()
                .replace(/^\d+\s*/, ""),
        ),
    );
}

/**
 * Drag the stage at `fromIndex` onto the row at `toIndex` and wait until the list order has
 * actually changed.
 *
 * The list is a dnd-kit sortable whose only sensor is a PointerSensor with an 8px activation
 * distance, so this has to be a real pointer gesture: press, move far enough to start the drag,
 * move in steps so dnd-kit sees the movement, then release. A single jump to the target does not
 * start a drag at all.
 */
export async function dragStage(
    page: Page,
    fromIndex: number,
    toIndex: number,
): Promise<void> {
    const before = await getStageRowTexts(page);
    const rows = page.locator(PHASE_ROW);
    const from = await rows.nth(fromIndex).boundingBox();
    const to = await rows.nth(toIndex).boundingBox();
    if (!from || !to) {
        throw new Error(
            `Cannot drag stage ${fromIndex} to ${toIndex}: the list is showing ${before.length} rows.`,
        );
    }
    const startX = from.x + from.width / 2;
    const startY = from.y + from.height / 2;
    const endY = to.y + to.height / 2;
    await page.mouse.move(startX, startY);
    await page.mouse.down();
    // Past the 8px activation distance first, then on to the target in steps.
    await page.mouse.move(startX, startY + 12, { steps: 4 });
    await page.mouse.move(startX, endY, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(async () => (await getStageRowTexts(page)).join("|"), {
            timeout: 30000,
            message: `Dragging stage ${fromIndex} onto ${toIndex} did not change the order.`,
        })
        .not.toBe(before.join("|"));
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
 * Change the collection's saved reader settings directly. This is SETUP, not a path under test:
 * it reads the whole settings file, lets the caller edit it, and posts it back, so fields this
 * suite does not model survive. The toolbox picks the change up, re-reading the sample texts with
 * the new alphabet, the next time the setup dialog opens.
 */
export async function changeSavedReaderSettings(
    page: Page,
    change: (settings: IReaderSettings) => void,
): Promise<void> {
    const settings = await getSavedReaderSettings(page);
    change(settings);
    await apiPost(
        page,
        "readers/io/readerToolSettings",
        JSON.stringify(settings),
        "application/json",
    );
}

/**
 * Give the collection a known alphabet and four decodable stages. This is SETUP, not a path under
 * test, and it must run after the book exists, because the settings endpoint needs a current book.
 *
 * A test may not rely on the stages a new collection happens to arrive with: Bloom copies them from
 * the machine's app-data folder when it has some for the collection's language (see "Choosing or
 * creating test inputs" in the add-e2e-test skill), so they differ between machines.
 */
export async function useKnownReaderStages(page: Page): Promise<void> {
    await changeSavedReaderSettings(page, (settings) => {
        settings.letters =
            "a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z";
        settings.moreWords = "";
        settings.useAllowedWords = 0; // letters, not allowed-word lists
        settings.stages = [
            { name: "1", letters: "a e r", sightWords: "the of and to" },
            { name: "2", letters: "i o", sightWords: "is you that he" },
            { name: "3", letters: "n t", sightWords: "was for as with" },
            { name: "4", letters: "l s", sightWords: "his they I" },
        ];
    });
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
