// The Edit tab's toolbox, and the Talking Book tool inside it: opening them, reading the sentences
// the tool marks, and getting narration onto a page.
//
// RECORDING cannot be automated: the tool records from a real microphone (see AUTOMATION-DEBT.md,
// "Native OS dialogs hang automation"). There are two ways around that, and they are for different
// jobs:
//
//  - `addNarration` is SETUP. It puts an mp3 where a recording would have gone, which is all it
//    takes, because the mp3's NAME is what ties it to the text: opening the Talking Book tool
//    marks each sentence with an id, and Bloom looks for `audio/<that id>.mp3`. A test that just
//    needs a book with narration uses this.
//  - `importNarration` drives the tool's real Import Recording button, for the one test whose
//    subject IS that button. It is not a general way to seed audio: Bloom offers Import Recording
//    only in whole-text-box recording mode and only to a Pro subscription.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Locator, type Page } from "@playwright/test";
import { apiPost } from "./api";
import { clickInGroup, editablePageFrame } from "./bookMaking";

/** The mp3 the suite uses when a test just needs SOME narration. Relative to src/BloomE2E. */
export const sampleNarrationFile = "fixtures/audio/sample.mp3";

/** One sentence the Talking Book tool has marked as recordable. */
export interface INarrationSentence {
    /**
     * The sentence's id, which is also the base name of the file in `audio/` that its narration
     * goes into. Bloom generates these, so a test reads them rather than choosing them.
     */
    id: string;
    /** The language of the text box the sentence is in. */
    languageTag: string;
    /** The sentence's text, for failure messages. */
    text: string;
}

/**
 * Open the Edit tab's toolbox, the way a person clicks the hamburger at its top right, and wait
 * until the Talking Book tool has marked the page's sentences.
 *
 * The toolbox starts closed for a new book, and its first tool is Talking Book, already expanded;
 * so opening the panel is all it takes to put that tool to work. Marking the sentences is a side
 * effect of the tool becoming active, and it is the state every caller here is really waiting for.
 * Does nothing if the toolbox is already open.
 */
export async function openToolboxWithTalkingBook(page: Page): Promise<void> {
    if (!(await toolboxToggle(page).isChecked())) {
        await page
            .locator('label.pure-toggle-label[for="pure-toggle-right"]')
            .click();
        await expect(
            toolboxToggle(page),
            "Clicking the toolbox hamburger did not open the toolbox.",
        ).toBeChecked({ timeout: 30000 });
    }
    await expect
        .poll(async () => (await getNarrationSentences(page)).length, {
            timeout: 60000,
            message:
                "Opening the toolbox never got the Talking Book tool to mark any sentence on this page as recordable. A page with no text has none to mark.",
        })
        .toBeGreaterThan(0);
}

/**
 * Every sentence the Talking Book tool has marked on the page being shown, in document order.
 * Empty until the toolbox has been opened on this page -- the marking is the tool's work, not the
 * page's.
 */
export async function getNarrationSentences(
    page: Page,
): Promise<INarrationSentence[]> {
    return editablePageFrame(page)
        .locator(".audio-sentence[id]")
        .evaluateAll((elements) =>
            elements.map((element) => ({
                id: element.id,
                languageTag:
                    element.closest("[lang]")?.getAttribute("lang") ?? "",
                text: element.textContent ?? "",
            })),
        );
}

/**
 * Give one language's text on the page being shown the narration in `mp3Path` -- the state a
 * person reaches by recording it -- by copying that mp3 to the file Bloom looks for, once per
 * sentence in that language.
 *
 * This is SETUP, not a UI path: recording needs a microphone. What makes it faithful rather than a
 * fabrication is that Bloom's own tool decided which sentences exist and what their ids are (see
 * openToolboxWithTalkingBook, which must have run on this page); all this adds is the audio file
 * that a recording would have written, under the name Bloom derives from the id.
 *
 * `bookFolder` is the book's folder on disk; `mp3Path` is absolute or relative to src/BloomE2E.
 * Returns the ids that now have narration. Nothing in the page changes, so a caller that wants
 * Bloom to notice has to make Bloom re-read the book -- entering the Publish tab does.
 */
export async function addNarration(
    page: Page,
    bookFolder: string,
    languageTag: string,
    mp3Path: string = sampleNarrationFile,
): Promise<string[]> {
    const sentences = await getNarrationSentences(page);
    const wanted = sentences.filter((s) => s.languageTag === languageTag);
    if (wanted.length === 0)
        throw new Error(
            `The page has no "${languageTag}" sentence to narrate. It has: ` +
                `${JSON.stringify(sentences.map((s) => `${s.languageTag}: ${s.text}`))}. ` +
                "A language whose box is empty, or which the book is not showing, gets no sentence.",
        );

    const audioFolder = Path.join(bookFolder, "audio");
    fs.mkdirSync(audioFolder, { recursive: true });
    for (const sentence of wanted)
        fs.copyFileSync(
            Path.resolve(mp3Path),
            Path.join(audioFolder, `${sentence.id}.mp3`),
        );
    return wanted.map((s) => s.id);
}

/** The languages that have a narration file in the book on disk, whatever Bloom thinks. */
export function getNarratedLanguages(
    sentences: INarrationSentence[],
    bookFolder: string,
): string[] {
    const audioFolder = Path.join(bookFolder, "audio");
    const narrated = sentences.filter((sentence) =>
        fs.existsSync(Path.join(audioFolder, `${sentence.id}.mp3`)),
    );
    return [...new Set(narrated.map((s) => s.languageTag))].sort();
}

/**
 * Import `mp3Path` as the narration of one language's box on the page being shown, through the
 * tool's real Import Recording button: click in the box, switch to whole-text-box recording mode,
 * open the Advanced section, and import.
 *
 * Only for the test whose subject is that button. Bloom enables it solely in whole-text-box mode
 * and solely for a Pro subscription, so a caller has to have granted one (see
 * helpers/collectionSettings.ts setBranding); and the narration it makes covers the whole box
 * rather than a sentence. Every other test wants addNarration.
 *
 * The button ends in a native file chooser, which Playwright cannot dismiss, so this pre-answers
 * it through Bloom's e2e hook before clicking (see E2eTestingApi's nextChosenFile).
 */
export async function importNarration(
    page: Page,
    groupSelector: string,
    languageTag: string,
    mp3Path: string = sampleNarrationFile,
): Promise<void> {
    await clickInGroup(page, groupSelector, languageTag);
    await setRecordingMode(page, "By Whole Text Box");
    await openAdvancedSection(page);

    const importButton = toolboxFrame(page).locator("#import-recording-button");
    await importButton.waitFor({ state: "visible", timeout: 30000 });
    await expect(
        importButton,
        "Import Recording is disabled. Bloom offers it only in whole-text-box recording mode, and only to a Pro subscription.",
    ).toBeEnabled({ timeout: 30000 });

    // Answer the chooser before opening it: once a native dialog is up, nothing here can close it.
    await armFileChooser(page, Path.resolve(mp3Path));
    await importButton.click();

    // The import is several round trips -- choose, find the audio folder, copy, write the markup --
    // so wait for its result rather than for the click.
    await expect
        .poll(async () => (await getNarrationSentences(page)).length, {
            timeout: 60000,
            message: `Importing "${mp3Path}" never left the page with a narrated sentence.`,
        })
        .toBeGreaterThan(0);
}

/**
 * Tell Bloom the path the NEXT native file chooser should return, instead of opening a dialog no
 * test can dismiss. One shot: it answers a single chooser and then goes back to opening the real
 * one. Available only because Bloom is running with --e2e.
 */
export async function armFileChooser(
    page: Page,
    filePath: string,
): Promise<void> {
    await apiPost(
        page,
        "e2e/nextChosenFile",
        JSON.stringify({ Path: filePath }),
        "application/json",
    );
}

/**
 * Choose one of the Talking Book tool's recording modes, by the label its radio shows.
 *
 * Both radios can legitimately be unavailable, and for different reasons: whole-text-box mode
 * needs a Pro subscription, and By Sentence is refused once the box holds a whole-text-box
 * recording. Either way this fails rather than silently leaving the mode alone, because a caller
 * that thinks it changed the mode would go on to test the wrong thing.
 */
export async function setRecordingMode(
    page: Page,
    mode: "By Sentence" | "By Whole Text Box",
): Promise<void> {
    await openAdvancedSection(page);
    const radio = toolboxFrame(page)
        .locator("label")
        .filter({ hasText: mode })
        .first()
        .locator('input[type="radio"]');
    await radio.waitFor({ state: "visible", timeout: 30000 });
    await expect(
        radio,
        `The "${mode}" recording mode is disabled. Whole-text-box mode needs a Pro subscription; By Sentence is refused once the text box holds a whole-text-box recording; and either needs a page with a recordable text box.`,
    ).toBeEnabled({ timeout: 30000 });
    await radio.click();
    await expect(radio).toBeChecked({ timeout: 30000 });
}

/**
 * Whether the tool is offering Import Recording. Bloom enables it only in whole-text-box
 * recording mode, only for a Pro subscription, and only on a page with a recordable text box, so
 * a caller checking for `false` should make sure it has ruled out the reasons it did not mean.
 */
export async function isImportRecordingEnabled(page: Page): Promise<boolean> {
    await openAdvancedSection(page);
    return toolboxFrame(page).locator("#import-recording-button").isEnabled();
}

/**
 * Expand the Talking Book tool's Advanced section, which holds the recording modes and Import
 * Recording. It starts collapsed, and its contents are in the DOM but not visible until then.
 * Does nothing if it is already open.
 */
export async function openAdvancedSection(page: Page): Promise<void> {
    const importButton = toolboxFrame(page).locator("#import-recording-button");
    if (await importButton.isVisible()) return;
    // The section is a TriangleCollapse, whose control is a button carrying the word "Advanced"
    // and nothing else to find it by -- no id, no test id. Matching on the English label is what
    // the suite already does for the top bar and the page menu; see AUTOMATION-DEBT.md.
    await toolboxFrame(page)
        .getByRole("button", { name: "Advanced" })
        .first()
        .click();
    await importButton.waitFor({ state: "visible", timeout: 30000 });
}

/** The toolbox's own frame. The Edit tab hosts it in an iframe named "toolbox". */
function toolboxFrame(page: Page) {
    const frame = page.frame({ name: "toolbox" });
    if (!frame)
        throw new Error(
            "There is no 'toolbox' frame, so the Edit tab is not showing. " +
                `Frames: ${page
                    .frames()
                    .map((f) => f.name() || "(main)")
                    .join(", ")}.`,
        );
    return frame;
}

/** The hidden check box behind the toolbox hamburger; it reports whether the toolbox is open. */
function toolboxToggle(page: Page): Locator {
    return page.locator("#pure-toggle-right");
}
