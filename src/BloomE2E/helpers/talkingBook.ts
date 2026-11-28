// The Edit tab's toolbox, and the Talking Book tool inside it: opening them, reading the sentences
// the tool marks, reading the colors the tool highlights the current segment with, and getting
// narration onto a page.
//
// While the tool is open it highlights the segment that the next recording would go into. In the
// Edit tab that highlight is a CSS ::highlight() pseudo-element that the tool registers in the page
// frame (audioHighlightManager.ts), and its colors come from two CSS variables the tool sets on
// the page's root element from the style's Highlighting settings in the Format dialog. Reading those
// two things is how a test checks "the current segment is highlighted in the colors I chose".
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
import { apiPost, getUiString } from "./api";
import { clickInGroup, editablePageFrame } from "./bookMaking";
import { cssColorToHex } from "./cssValues";

/** The mp3 the suite uses when a test just needs SOME narration. Relative to src/BloomE2E. */
export const sampleNarrationFile = "fixtures/audio/sample.mp3";

/** The name the tool registers its current-segment highlight under (audioHighlightManager.ts). */
const CURRENT_HIGHLIGHT = "bloom-audio-current";
const BACKGROUND_VAR = "--bloom-audio-current-highlight-background";
const TEXT_VAR = "--bloom-audio-current-highlight-color";

/** The colors the Talking Book tool is highlighting the current segment with, as "#rrggbb". */
export interface IAudioHighlightColors {
    background: string;
    text: string;
}

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
 * Wait until the Talking Book tool has marked each of `sentences`, in order, as a sentence to
 * record in the `languageTag` box of the first translation group `groupSelector` picks (such as
 * ".bloom-imageDescription" or ".bookTitle"), and return their ids, which name their recordings.
 * The tool must be open on the page being edited.
 */
export async function waitForSentencesMarked(
    page: Page,
    groupSelector: string,
    languageTag: string,
    sentences: string[],
): Promise<string[]> {
    const marked = editablePageFrame(page)
        .locator(groupSelector)
        .first()
        .locator(`.bloom-editable[lang="${languageTag}"] .audio-sentence[id]`);
    await expect
        .poll(
            async () =>
                (await marked.allTextContents()).map((text) => text.trim()),
            {
                timeout: 30000,
                message: `The Talking Book tool did not mark each sentence of "${groupSelector}" for recording.`,
            },
        )
        .toEqual(sentences);
    return marked.evaluateAll((elements) => elements.map((e) => e.id));
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
 * Wait until the Talking Book tool is highlighting a current segment on the page being edited, and
 * return the colors it is using. Throws, naming the tool, if no highlight appears: the tool has to
 * be open (see helpers/toolbox.ts) and the page has to have some text.
 */
export async function getCurrentAudioHighlightColors(
    page: Page,
): Promise<IAudioHighlightColors> {
    const read = () =>
        editablePageFrame(page).evaluate(
            ({ name, backgroundVar, textVar }) => {
                const highlights = (
                    CSS as unknown as { highlights?: Map<string, unknown> }
                ).highlights;
                const style = document.documentElement.style;
                return {
                    registered: !!highlights?.has(name),
                    background: style.getPropertyValue(backgroundVar),
                    text: style.getPropertyValue(textVar),
                };
            },
            {
                name: CURRENT_HIGHLIGHT,
                backgroundVar: BACKGROUND_VAR,
                textVar: TEXT_VAR,
            },
        );
    await expect
        .poll(async () => (await read()).registered, {
            timeout: 30000,
            message:
                "The Talking Book tool never highlighted a current segment on the page. " +
                "It has to be open, and the page has to have text.",
        })
        .toBe(true);
    const { background, text } = await read();
    return {
        background: cssColorToHex(background),
        text: cssColorToHex(text),
    };
}

/**
 * The text of the segment the Talking Book tool is highlighting as current on the page being
 * edited: the text its "bloom-audio-current" highlight covers, which is where the next recording
 * would go. Waits until the tool highlights something; the tool has to be open, and the page has
 * to have text.
 */
export async function getCurrentAudioSegmentText(page: Page): Promise<string> {
    const read = () =>
        editablePageFrame(page).evaluate((name) => {
            const highlight = (
                CSS as unknown as {
                    highlights?: Map<string, Iterable<AbstractRange>>;
                }
            ).highlights?.get(name);
            if (!highlight) return undefined;
            return [...highlight]
                .map((range) => {
                    const live = document.createRange();
                    live.setStart(range.startContainer, range.startOffset);
                    live.setEnd(range.endContainer, range.endOffset);
                    return live.toString();
                })
                .join("")
                .trim();
        }, CURRENT_HIGHLIGHT);
    let text: string | undefined;
    await expect
        .poll(
            async () => {
                text = await read();
                return !!text;
            },
            {
                timeout: 30000,
                message:
                    "The Talking Book tool never highlighted a current segment with text in it. " +
                    "It has to be open, and the page has to have text.",
            },
        )
        .toBe(true);
    return text ?? "";
}

/**
 * The Talking Book tool's recording mode that is chosen, as the English label of its radio (see
 * setRecordingMode). Opens the tool's Advanced section, where the radios are, and waits until one
 * of them is checked: the tool shows the radios with neither checked (RecordingMode.Unknown) until
 * it has settled the page's mode in handleNewPageReady.
 */
export async function getRecordingMode(
    page: Page,
): Promise<RecordingModeLabel> {
    await openAdvancedSection(page);
    const values = Object.values(RECORDING_MODE_VALUE)
        .map((value) => `[value="${value}"]`)
        .join(",");
    const value = await toolboxFrame(page)
        .locator(`input[type="radio"]:is(${values}):checked`)
        .getAttribute("value", { timeout: 30000 });
    const mode = (
        Object.keys(RECORDING_MODE_VALUE) as RecordingModeLabel[]
    ).find((label) => RECORDING_MODE_VALUE[label] === value);
    if (!mode)
        throw new Error(
            `The Talking Book tool has the recording mode "${value}" checked, which is none of ` +
                `${Object.values(RECORDING_MODE_VALUE).join(", ")}.`,
        );
    return mode;
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
 * it through Bloom's e2e hook before clicking (see E2eTestingApi's nextFileToChoose).
 *
 * `bookFolder` is needed to wait for the import to finish -- see the comment on the wait below.
 * `groupIndex` picks the box when `groupSelector` matches more than one group on the page.
 */
export async function importNarration(
    page: Page,
    bookFolder: string,
    groupSelector: string,
    languageTag: string,
    mp3Path: string = sampleNarrationFile,
    groupIndex = 0,
): Promise<void> {
    const audioFolder = Path.join(bookFolder, "audio");
    const before = narrationFilesIn(audioFolder).length;

    await clickImportRecording(
        page,
        groupSelector,
        languageTag,
        mp3Path,
        groupIndex,
    );

    // The import is several round trips -- choose the file, find the book's audio folder, copy,
    // then write the markup -- so wait for its RESULT: one more narration file on disk than
    // before. Waiting on the page instead does not work: the tool has already marked the text
    // box as recordable, so every DOM signal a caller might poll is true before the import
    // begins, and the wait would return immediately while the copy was still in flight.
    await expect
        .poll(() => narrationFilesIn(audioFolder).length, {
            timeout: 60000,
            message: `Importing "${mp3Path}" never added a narration file to ${audioFolder}.`,
        })
        .toBeGreaterThan(before);
}

/**
 * Replace the whole-text-box recording of one language's box on the page being shown with
 * `mp3Path`, through the tool's real Import Recording button, answering "Replace" when Bloom asks
 * whether to replace the audio. This is how a test "records again" over narration the box already
 * has, since recording itself needs a microphone. Returns the id of the recording it replaced,
 * which is also the base name of its file in `audio/`.
 *
 * The box must already hold a whole-text-box recording (see importNarration), and the same
 * subscription rule applies. Waits until the box's narration file holds the bytes of `mp3Path`, so
 * `mp3Path` has to differ from what is there.
 */
export async function replaceNarrationByImport(
    page: Page,
    bookFolder: string,
    groupSelector: string,
    languageTag: string,
    mp3Path: string,
): Promise<string> {
    const box = await clickInGroup(page, groupSelector, languageTag);
    const id = await box.evaluate((element) =>
        element.classList.contains("audio-sentence") ? element.id : "",
    );
    if (!id)
        throw new Error(
            `The "${languageTag}" box of "${groupSelector}" holds no whole-text-box recording to replace.`,
        );
    const narrationFile = narrationFilePath(bookFolder, id);
    const isReplaced = () => narrationIs(bookFolder, id, mp3Path) === true;
    await expect
        .poll(() => narrationIs(bookFolder, id, mp3Path), {
            timeout: 30000,
            message: `${narrationFile} is missing, or already holds the audio of ${mp3Path}, so replacing it would show nothing.`,
        })
        .toBe(false);

    await clickImportRecording(page, groupSelector, languageTag, mp3Path);

    // Bloom asks before replacing a recording, but only when the tool's cached Play-button state
    // says the selection has one (see handleImportRecordingClick in audioRecording.ts), so wait
    // for whichever comes first: the question, or the import having gone ahead without it.
    const replaceButton = page.getByRole("button", {
        name: "Replace",
        exact: true,
    });
    await expect
        .poll(async () => isReplaced() || (await replaceButton.isVisible()), {
            timeout: 30000,
            message:
                "Import Recording neither asked whether to replace the audio nor replaced it.",
        })
        .toBe(true);
    if (!isReplaced()) await replaceButton.click();
    await expect
        .poll(isReplaced, {
            timeout: 60000,
            message: `Importing "${mp3Path}" never replaced ${narrationFile}.`,
        })
        .toBe(true);
    return id;
}

/**
 * Click in one language's box, put the tool in whole-text-box mode, and press its Import Recording
 * button with the native file chooser pre-answered by `mp3Path`. Returns without waiting for the
 * import; each caller waits for the result it expects.
 */
async function clickImportRecording(
    page: Page,
    groupSelector: string,
    languageTag: string,
    mp3Path: string,
    groupIndex = 0,
): Promise<void> {
    await clickInGroup(page, groupSelector, languageTag, groupIndex);
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
}

/**
 * The file Bloom plays for the sentence or text box with this id: Bloom names a narration file
 * after the id of the text it belongs to. The file exists only once that text has narration.
 */
export function narrationFilePath(bookFolder: string, id: string): string {
    return Path.join(bookFolder, "audio", `${id}.mp3`);
}

/**
 * Whether the narration Bloom plays for the text with this id is exactly the audio in `mp3Path`.
 * Undefined when there is no saying: the text has no narration file, or Bloom has the file open
 * for itself at this moment, as it does briefly while it copies or loads one.
 */
function narrationIs(
    bookFolder: string,
    id: string,
    mp3Path: string,
): boolean | undefined {
    const narrationFile = narrationFilePath(bookFolder, id);
    if (!fs.existsSync(narrationFile)) return undefined;
    let narration: Buffer;
    try {
        narration = fs.readFileSync(narrationFile);
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === "EBUSY") return undefined;
        throw error;
    }
    return narration.equals(fs.readFileSync(Path.resolve(mp3Path)));
}

/**
 * Assert that the narration Bloom plays for the text with this id is exactly the audio in
 * `mp3Path`: the file named after the id holds those bytes. `message` says what it means when it
 * does not.
 */
export async function expectNarrationToBe(
    bookFolder: string,
    id: string,
    mp3Path: string,
    message: string,
): Promise<void> {
    await expect
        .poll(() => narrationIs(bookFolder, id, mp3Path), {
            timeout: 15000,
            message: `${message} (${narrationFilePath(bookFolder, id)} against ${mp3Path}; undefined means the file is missing or stayed locked.)`,
        })
        .toBe(true);
}

/**
 * Write a second sample narration to `filePath`: the suite's sample played twice, so it is a
 * playable mp3 that differs from sampleNarrationFile. For a test that replaces one recording with
 * another and has to tell them apart.
 */
export function writeSecondSampleNarration(filePath: string): void {
    const sample = fs.readFileSync(Path.resolve(sampleNarrationFile));
    fs.writeFileSync(filePath, Buffer.concat([sample, sample]));
}

/**
 * About `seconds` of silence as mp3 frames in the format of sampleNarrationFile (MPEG-1 Layer III,
 * 64 kbps, 44.1 kHz, mono), so that it can be joined to that sample's bytes to make a recording
 * whose sound and silence fall at known times. Each frame is a header and nothing else: it holds
 * no sound, and lasts 1152 samples.
 */
export function silentMp3Frames(seconds: number): Buffer {
    const frameCount = Math.round((seconds * 44100) / 1152);
    // 144 * bitrate / sample rate, with no padding byte.
    const frame = Buffer.alloc(Math.floor((144 * 64000) / 44100));
    Buffer.from([0xff, 0xfb, 0x50, 0xc0]).copy(frame);
    return Buffer.concat(Array(frameCount).fill(frame));
}

/** The narration files in a book's audio folder; empty when the folder does not exist yet. */
function narrationFilesIn(audioFolder: string): string[] {
    if (!fs.existsSync(audioFolder)) return [];
    return fs
        .readdirSync(audioFolder)
        .filter((name) => name.toLowerCase().endsWith(".mp3"));
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
    // Bloom unescapes a text/plain body as it would a URL (RequestInfo.UnescapeString), which
    // turns "+" into a space and "%41" into "A". So the path goes escaped, and arrives as it is.
    await apiPost(
        page,
        "e2e/nextFileToChoose",
        encodeURIComponent(filePath),
        "text/plain",
    );
}

/**
 * Choose one of the Talking Book tool's recording modes. `mode` is the English label of its
 * radio; the radio is found by its value, since the label is localized.
 *
 * Both radios can legitimately be unavailable, and for different reasons: whole-text-box mode
 * needs a Pro subscription, and By Sentence is refused once the box holds a whole-text-box
 * recording. Either way this fails rather than silently leaving the mode alone, because a caller
 * that thinks it changed the mode would go on to test the wrong thing.
 */
export async function setRecordingMode(
    page: Page,
    mode: RecordingModeLabel,
): Promise<void> {
    await openAdvancedSection(page);
    const radio = recordingModeRadio(page, mode);
    await radio.waitFor({ state: "visible", timeout: 30000 });
    await expect(
        radio,
        `The "${mode}" recording mode is disabled. Whole-text-box mode needs a Pro subscription; By Sentence is refused once the text box holds a whole-text-box recording; and either needs a page with a recordable text box.`,
    ).toBeEnabled({ timeout: 30000 });
    await radio.click();
    await expect(radio).toBeChecked({ timeout: 30000 });
}

/**
 * Whether the Talking Book tool lets a person choose one of its recording modes now, i.e. whether
 * that radio in the Advanced section is enabled. Opens the Advanced section. `mode` is the English
 * label of the radio; it is found by its value, since the label is localized.
 */
export async function isRecordingModeEnabled(
    page: Page,
    mode: RecordingModeLabel,
): Promise<boolean> {
    await openAdvancedSection(page);
    const radio = recordingModeRadio(page, mode);
    await radio.waitFor({ state: "visible", timeout: 30000 });
    return radio.isEnabled();
}

/**
 * Assert that resting the pointer on one of the Talking Book tool's recording-mode radios shows a
 * tooltip reading `expected`. Moves the pointer onto the radio's label and waits for the tooltip
 * to appear with that text (Bloom's tooltips open after a short hover delay, and fetch their
 * localized text after they appear), then moves the pointer away again so the tooltip does not
 * cover later clicks. Opens the Advanced section.
 */
export async function expectRecordingModeTooltip(
    page: Page,
    mode: RecordingModeLabel,
    expected: string,
    message: string,
): Promise<void> {
    await openAdvancedSection(page);
    const frame = toolboxFrame(page);
    // The radio sits in a label; the tooltip is wrapped around that label (BloomTooltip adds a
    // span so it shows even over a disabled control), so hovering the label shows it.
    const label = frame.locator("label").filter({
        has: recordingModeRadio(page, mode),
    });
    await label.hover();
    const tooltip = frame.getByRole("tooltip");
    await expect(
        tooltip,
        `Resting the pointer on the "${mode}" radio showed no tooltip.`,
    ).toBeVisible({ timeout: 30000 });
    await expect(tooltip, message).toHaveText(expected, { timeout: 30000 });
    // Move the pointer off the toolbox and let the tooltip go.
    await page.mouse.move(0, 0);
    await expect(tooltip).toBeHidden({ timeout: 30000 });
}

/**
 * The recording mode Bloom has stored on one language's text box of the first translation group
 * that `groupSelector` picks: its data-audiorecordingmode attribute ("Sentence" or "TextBox"), which
 * the tool sets when a person chooses a mode with the cursor in that box. Undefined when the box
 * has none.
 */
export async function getTextBoxRecordingMode(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<string | undefined> {
    const mode = await editablePageFrame(page)
        .locator(groupSelector)
        .first()
        .locator(`.bloom-editable[lang="${languageTag}"]`)
        .getAttribute("data-audiorecordingmode");
    return mode ?? undefined;
}

/**
 * Whether the tool's Check button (play the current segment's recording) is enabled: the tool
 * enables it only when the segment it is on has a recording. This is how a test sees that the
 * tool itself knows about a recording, rather than only that the file is on disk.
 */
export async function isCheckEnabled(page: Page): Promise<boolean> {
    return toolboxFrame(page)
        .locator('button:has(img[src*="/play_"])')
        .isEnabled();
}

/**
 * Press the tool's Clear button, which deletes the recording of the segment the tool is on (or, in
 * whole-text-box mode, of the whole text box). The button has no id and a localized label, so it
 * is found by its picture; the button itself ignores the pointer, and its picture takes the click.
 * Returns without waiting for the deletion; the caller waits for the result it expects.
 */
export async function clickClearRecording(page: Page): Promise<void> {
    const button = toolboxFrame(page).locator(
        'button:has(img[src*="clear_enabled.svg"])',
    );
    await expect(
        button,
        "Clear is disabled; the tool enables it only when the current segment has a recording.",
    ).toBeEnabled({ timeout: 30000 });
    await button.locator("img").click();
}

/** A recording mode, as the English label of its radio. */
type RecordingModeLabel = "By Sentence" | "By Whole Text Box";

/**
 * Each recording-mode radio's value, a RecordingMode (recordingMode.ts), by the English label of
 * the radio. The labels are localized, so the helpers find the radios by value.
 */
const RECORDING_MODE_VALUE: Record<RecordingModeLabel, string> = {
    "By Sentence": "Sentence",
    "By Whole Text Box": "TextBox",
};

/** One of the recording-mode radios, found by its value (a RecordingMode, recordingMode.ts). */
function recordingModeRadio(page: Page, mode: RecordingModeLabel): Locator {
    return toolboxFrame(page).locator(
        `input[type="radio"][value="${RECORDING_MODE_VALUE[mode]}"]`,
    );
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
 * Align the whole-text-box recording of the text box the tool is on with that box's segments, as a
 * person does by pressing Adjust Timings... and then OK without moving a boundary. The dialog
 * splits the box into segments at its sentence ends and its segment markers ("|"), proposes an
 * end time for each, and OK saves those times on the box (data-audiorecordingendtimes) and shows
 * the box as split (bloom-postAudioSplit). Returns once the box shows as split.
 *
 * The box must hold a whole-text-box recording (see importNarration) and not be split yet. The
 * button is found by its picture, since it has no id and its label is localized.
 */
export async function acceptProposedTimings(page: Page): Promise<void> {
    const splitBoxes = editablePageFrame(page).locator(
        ".bloom-editable.bloom-postAudioSplit",
    );
    const splitBefore = await splitBoxes.count();
    const button = toolboxFrame(page).locator(
        'button:has(img[src*="adjustTimings.svg"])',
    );
    await expect(
        button,
        "Adjust Timings... is disabled; it needs a text box recorded as a whole.",
    ).toBeEnabled({ timeout: 30000 });
    // The button itself ignores the pointer (pointer-events: none); its picture takes the click.
    await button.locator("img").click();

    // The dialog is drawn in the shell page. It shows one region over the recording's waveform
    // for each segment once it has loaded the recording, and only then are the times it would
    // save its own rather than a first guess from the lengths of the segments' text.
    const dialog = page
        .getByRole("dialog")
        .filter({ has: page.locator("#waveform") });
    await expect(
        dialog.locator("#waveform .playButton").first(),
        "The Adjust Timings dialog never showed the recording's segments.",
    ).toBeVisible({ timeout: 60000 });
    await dialog.getByTestId("dialog-ok").click();
    await expect(dialog).toBeHidden({ timeout: 30000 });
    await expect(
        splitBoxes,
        "OK in Adjust Timings did not mark the text box as split into timed segments.",
    ).toHaveCount(splitBefore + 1, { timeout: 30000 });
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
    // (Common.Advanced, in the UI language) and nothing else to find it by -- no id, no test id.
    const advanced = await getUiString(page, "Common.Advanced", "Advanced");
    await toolboxFrame(page)
        .getByRole("button", { name: advanced, exact: true })
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

/**
 * How many of the Talking Book tool's sentence markers on the page being shown sit inside another
 * one. Always 0 in a healthy book. A nested marker is the damage BL-10291 did (fixed in 5.1): text
 * copied from a marked sentence and pasted back with the tool open brought its marker inside the
 * existing one, which threw script errors and made each later paste slower.
 */
export async function countNestedNarrationSentences(
    page: Page,
): Promise<number> {
    return editablePageFrame(page)
        .locator(".audio-sentence .audio-sentence")
        .count();
}

/**
 * Wait until the Talking Book tool has marked all of the text on the page being shown, i.e. the
 * text inside its sentence markers adds up to `text`. The tool re-marks a text box a moment after
 * it changes, so this is how a test waits out that pass before judging what it produced.
 */
export async function waitForNarrationSentencesToCover(
    page: Page,
    text: string,
): Promise<void> {
    await expect
        .poll(
            async () =>
                (await getNarrationSentences(page)).map((s) => s.text).join(""),
            {
                timeout: 30000,
                message: `The Talking Book tool never marked all of "${text}" as recordable sentences.`,
            },
        )
        .toBe(text);
}
