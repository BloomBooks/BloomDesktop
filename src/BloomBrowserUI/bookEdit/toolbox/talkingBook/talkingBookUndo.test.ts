// Talking Book and the page history. The audio-sentence split that Talking Book makes when it
// opens on a page joins the latest undo step, so undoing the typing that it follows undoes the
// split too. Switching the recording mode is an undo step of its own. What it writes when it
// leaves is not recorded. After an undo or redo of a playback-order arrow, the arrows follow the
// order that the undo put back. Playing audio changes the page only until playing ends, and that
// is not recorded either.
import {
    afterEach,
    beforeAll,
    beforeEach,
    describe,
    expect,
    it,
    vi,
} from "vitest";
import axios from "axios";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";
import { mockReplies } from "../../../utils/bloomApi";
import { installHighlightPolyfill } from "../../test/highlightTestSupport";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
// The history that the code under test finds, with doOutsideHistoryIfAny doing what the real
// one does when there is a history.
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistoryIfAny: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    addToLatestTransactionIfAny: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.addToLatestTransaction(fn) : fn(),
    clearUndoAndRedoIfAny: () => mocks.history?.clearUndoAndRedo(),
}));

import AudioRecording, {
    initializeTalkingBookToolAsync,
    theOneAudioRecorder,
} from "./audioRecording";
import TalkingBookTool from "./talkingBookTool";
import { RecordingMode } from "./recordingMode";
import { kAnyRecordingApiUrl } from "./audioUtils";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kArrowKey = "EditTab.Toolbox.TalkingBookTool.PlaybackOrder";
const kTypingKey = "EditTab.Undo.Typing";
const kModeKey = "EditTab.Toolbox.TalkingBookTool.RecordingMode";

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn>;
let recording: AudioRecording;

function pageFrame(): HTMLIFrameElement {
    return document.getElementById("page") as HTMLIFrameElement;
}

function pageDocument(): Document {
    return pageFrame().contentDocument!;
}

function byId(id: string): HTMLElement {
    const element = pageDocument().getElementById(id);
    if (!element) throw new Error(`The page has no element with id ${id}.`);
    return element;
}

/** Put this html in the page frame's body, with the polyfills that audioRecordingSpec uses. */
function setPage(bodyHtml: string): void {
    const doc = pageDocument();
    doc.body.innerHTML = bodyHtml;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const win = pageFrame().contentWindow as any;
    for (const proto of [win.HTMLElement.prototype, win.Node.prototype]) {
        if (!Object.getOwnPropertyDescriptor(proto, "innerText")) {
            Object.defineProperty(proto, "innerText", {
                get() {
                    return this.textContent ?? "";
                },
                set(value) {
                    this.textContent = value;
                },
                configurable: true,
            });
        }
    }
    if (!win.HTMLElement.prototype.scrollIntoView)
        win.HTMLElement.prototype.scrollIntoView = vi.fn();
}

/** A history of the page's .bloom-page, with one step that the user made before the tool opened. */
async function startHistoryWithAnEarlierStep(): Promise<void> {
    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: pageDocument().querySelector(".bloom-page")!,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    mocks.history = history;
    await history.doTransaction(kEarlierKey, () => {
        byId("other").textContent = "typed";
    });
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
}

/**
 * Listen to the history before and after each undo and redo as the Edit tab does with Talking Book
 * open: TalkingBookTool.newPageReady and listenToPageHistory in bloomEditing.ts, for the
 * AudioRecording that these tests make.
 */
function listenAsTheEditTabDoes(): void {
    history.onBeforeUndoOrRedo(() => recording.prepareForUndoOrRedo());
    history.onAfterUndoOrRedo(() => recording.updateAfterUndoOrRedo());
}

beforeAll(async () => {
    installHighlightPolyfill(globalThis.window);
    const iframe = document.createElement("iframe");
    iframe.id = "page";
    document.body.appendChild(iframe);
    const doc = iframe.contentDocument!;
    if (!doc.body) {
        doc.write("<html><body></body></html>");
        doc.close();
    }
    installHighlightPolyfill(iframe.contentWindow!);
    mockReplies[
        "features/status?featureName=WholeTextBoxAudio&forPublishing=false"
    ] = { data: { enabled: true } };
    await initializeTalkingBookToolAsync();
});

beforeEach(() => {
    // No sentence has a recording.
    vi.spyOn(axios, "get").mockImplementation((url: string) =>
        url.includes(kAnyRecordingApiUrl)
            ? Promise.resolve({ data: false })
            : Promise.reject("Not found"),
    );
    vi.spyOn(axios, "post").mockResolvedValue({});
    recording = new AudioRecording();
    (recording as unknown as { isShowing: boolean }).isShowing = true;
});

afterEach(() => {
    recording.handleToolHiding();
    theOneAudioRecorder?.clearTimeouts();
    history.dispose();
    mocks.history = undefined;
    vi.restoreAllMocks();
});

describe("Talking Book and the page history", () => {
    it("opening by sentence after typing splits the box in the typing's step, which undo and redo take back and put back", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>One. Two.</p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        const paragraph = byId("box1").querySelector("p")!;
        const typedText = paragraph.firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            typedText.data = "One. Two. Three.";
        });
        const sentences = byId("box1").getElementsByClassName("audio-sentence");
        expect(sentences).toHaveLength(0);

        await recording.handleNewPageReady();

        expect(sentences).toHaveLength(3);
        expect(typedText.isConnected).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();

        // The typing and the split went together.
        expect(Array.from(paragraph.childNodes)).toEqual([typedText]);
        expect(typedText.data).toBe("One. Two.");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        // Talking Book did not split the box again, which would have lost redo.
        await Promise.resolve();
        expect(sentences).toHaveLength(0);
        expect(history.redoDescriptionL10nKey).toBe(kTypingKey);

        await history.redo();
        expect(typedText.data).toBe("One. Two. Three.");
        expect(sentences).toHaveLength(3);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);

        // After another undo, the next Talking Book action that needs the sentences splits the
        // box again, joining the latest step, which is the earlier one now.
        await history.undo();
        expect(sentences).toHaveLength(0);
        await recording.moveToNextAudioElement();
        expect(sentences).toHaveLength(2);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        await history.undo();
        expect(byId("other").textContent).toBe("before");
        expect(Array.from(paragraph.childNodes)).toEqual([typedText]);
    });

    it("leaving the page makes no step", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>One.|Two.</p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        await recording.handleNewPageReady(
            TalkingBookTool.deshroudPhraseDelimiters,
        );
        expect(byId("box1").textContent).toContain("|");

        new TalkingBookTool().detachFromPage();

        expect(
            byId("box1").getElementsByClassName("bloom-audio-split-marker"),
        ).toHaveLength(1);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("switching the recording mode is one named step, which undo and redo take back and put back", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>One. Two.</p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        await recording.handleNewPageReady();
        const box = byId("box1");
        const modeOfBox = () => box.getAttribute("data-audiorecordingmode");
        const modeInTool = () =>
            recording.getTalkingBookUiState().recordingMode;
        expect(recording.recordingMode).toBe(RecordingMode.Sentence);
        const markupBySentence = box.innerHTML;
        expect(box.getElementsByClassName("audio-sentence")).toHaveLength(2);

        // The checkbox, to record the whole text box.
        await recording.setRecordingMode(RecordingMode.TextBox);
        expect(modeOfBox()).toBe("TextBox");
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kModeKey);

        // And back, which rewrites the box's audio-sentence markup.
        await recording.setRecordingMode(RecordingMode.Sentence);
        expect(modeOfBox()).toBe("Sentence");
        const markupAfterSwitchingBack = box.innerHTML;
        expect(history.undoDescriptionL10nKey).toBe(kModeKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(modeOfBox()).toBe("TextBox");
        expect(box.innerHTML).toBe(markupBySentence);
        expect(recording.recordingMode).toBe(RecordingMode.TextBox);
        expect(modeInTool()).toBe(RecordingMode.TextBox);
        expect(history.redoDescriptionL10nKey).toBe(kModeKey);

        await history.redo();
        expect(modeOfBox()).toBe("Sentence");
        expect(box.innerHTML).toBe(markupAfterSwitchingBack);
        expect(recording.recordingMode).toBe(RecordingMode.Sentence);
        expect(modeInTool()).toBe(RecordingMode.Sentence);

        await history.undo();
        await history.undo();
        expect(modeOfBox()).toBe("Sentence");
        expect(recording.recordingMode).toBe(RecordingMode.Sentence);
        expect(modeInTool()).toBe(RecordingMode.Sentence);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("puts the playback-order arrows back in the order that an undo or redo leaves", async () => {
        setPage(
            // Each group has a parent of its own, as on a real page, where showPlaybackOrderUi
            // looks for the arrows' container.
            '<div class="bloom-page"><div class="marginBox"><div><div id="g1" class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" lang="en"><p>One.</p></div></div></div><div><div id="g2" class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" lang="en"><p>Two.</p></div></div></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        history.onAfterUndoOrRedo(() => recording.updateAfterUndoOrRedo());
        const cachedOrder = (): string[] =>
            (
                recording as unknown as {
                    playbackOrderCache: {
                        sourceTranslationGroup: HTMLElement;
                    }[];
                }
            ).playbackOrderCache.map((info) => info.sourceTranslationGroup.id);

        await recording.setShowPlaybackOrderMode(true);
        expect(byId("g1").getAttribute("tabindex")).toBe("1");
        expect(byId("g2").getAttribute("tabindex")).toBe("2");
        expect(cachedOrder()).toEqual(["g1", "g2"]);
        // Showing the arrows numbered the groups without making a step or joining one.
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        // The user moves the first group later with its arrow. The click opens a transaction
        // with the general description (pageHistoryInput.ts), which closes after the click.
        const click = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        recording.bumpUp(1);
        expect(click.end()).toBe(true);
        expect(history.undoDescriptionL10nKey).toBe(kArrowKey);
        expect(byId("g1").getAttribute("tabindex")).toBe("2");
        expect(cachedOrder()).toEqual(["g2", "g1"]);

        await history.undo();
        expect(byId("g1").getAttribute("tabindex")).toBe("1");
        expect(cachedOrder()).toEqual(["g1", "g2"]);
        // Building the arrows again did not clear redo.
        expect(history.redoDescriptionL10nKey).toBe(kArrowKey);
        expect(
            pageDocument().getElementsByClassName(
                "bloom-playbackOrderControlsContainer",
            ),
        ).toHaveLength(2);

        await history.redo();
        expect(byId("g1").getAttribute("tabindex")).toBe("2");
        expect(cachedOrder()).toEqual(["g2", "g1"]);
        expect(history.undoDescriptionL10nKey).toBe(kArrowKey);

        // With no transaction open around it, the other arrow makes a step of its own.
        recording.bumpDown(2);
        expect(history.hasOpenTransaction).toBe(false);
        expect(byId("g1").getAttribute("tabindex")).toBe("1");
        await history.undo();
        expect(byId("g1").getAttribute("tabindex")).toBe("2");
        expect(history.undoDescriptionL10nKey).toBe(kArrowKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("stops playing before an undo, so that an undo of typing in text that playing split works", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on audio-sentence" lang="en" data-audiorecordingmode="TextBox"><p><span id="span1" class="bloom-highlightSegment">One    two.</span></p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        // Which descriptions to show depends on the toolbox, which these tests do not make.
        vi.spyOn(
            TalkingBookTool.prototype as unknown as {
                showImageDescriptionsIfAny(): void;
            },
            "showImageDescriptionsIfAny",
        ).mockImplementation(() => {});
        const tool = new TalkingBookTool();
        await tool.newPageReady();
        const span = byId("span1");
        const typedText = span.firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            typedText.data = "One    two three.";
        });

        // Playing, or a pause in it, has split the text around the run of spaces.
        theOneAudioRecorder.fixHighlighting(byId("box1"));
        expect(span.firstChild).not.toBe(typedText);

        await history.undo();

        expect(Array.from(span.childNodes)).toEqual([typedText]);
        expect(span.textContent).toBe("One    two.");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(history.redoDescriptionL10nKey).toBe(kTypingKey);

        // Once the tool has left the page, an undo no longer stops playing.
        tool.detachFromPage();
        const stopPlaying = vi.spyOn(
            theOneAudioRecorder,
            "prepareForUndoOrRedo",
        );
        await history.redo();
        expect(stopPlaying).not.toHaveBeenCalled();
    });

    it("playing after a typing step joins nothing, and the step can still be undone afterwards", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on audio-sentence" lang="en" data-audiorecordingmode="TextBox"><p><span id="span1" class="bloom-highlightSegment">One    two.</span></p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        const span = byId("span1");
        const typedText = span.firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            typedText.data = "One    two three.";
        });

        // Playing splits the text around the run of spaces, so that it is not highlighted.
        recording.fixHighlighting(byId("box1"));
        expect(span.firstChild).not.toBe(typedText);
        expect(span.getElementsByClassName("ui-enableHighlight").length).toBe(
            2,
        );
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);

        // When playing ends, the very text node that the typing changed is back.
        recording.revertFixHighlighting();
        expect(Array.from(span.childNodes)).toEqual([typedText]);
        expect(span.classList.contains("ui-disableHighlight")).toBe(false);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(span.textContent).toBe("One    two.");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });
});
