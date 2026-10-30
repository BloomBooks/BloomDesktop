// Talking Book and the page history. The audio-sentence split that Talking Book makes when it
// opens on a page joins the latest undo step, so undoing the typing that it follows undoes the
// split too. What it writes when it leaves is not recorded. After an undo or redo of a
// playback-order arrow, the arrows follow the order that the undo put back. Playing audio changes
// the page only until playing ends, and that is not recorded either.
import {
    afterEach,
    beforeAll,
    beforeEach,
    describe,
    expect,
    it,
    vi,
} from "vitest";

vi.mock("../../../utils/bloomApi", async (importOriginal) =>
    (await import("./talkingBookTestBloomApi")).withTalkingBookReplies(
        await importOriginal(),
    ),
);
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
// The history that the code under test finds, with doOutsideHistory doing what the real
// one does when there is a history.
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    addToLatestTransaction: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.addToLatestTransaction(fn) : fn(),
    clearUndoAndRedo: (reason: string) =>
        mocks.history?.clearUndoAndRedo(reason),
}));

import AudioRecording, {
    initializeTalkingBookToolAsync,
    theOneAudioRecorder,
} from "./audioRecording";
import TalkingBookTool from "./talkingBookTool";
import { RecordingMode } from "./recordingMode";
import { kAnyRecordingApiUrl } from "./audioUtils";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kTypingKey = "EditTab.Undo.Typing";

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

/**
 * Give the AudioRecording that a test made the collection's default recording mode, which the one
 * that Bloom makes reads from the collection when it initializes. A box with no recording mode of
 * its own, and no audio-sentence markup, gets it.
 */
function useCollectionDefaultRecordingMode(mode: RecordingMode): void {
    (
        recording as unknown as {
            cachedCollectionDefaultRecordingMode: RecordingMode;
        }
    ).cachedCollectionDefaultRecordingMode = mode;
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
    (
        recording as unknown as { isTalkingBookToolRunning: boolean }
    ).isTalkingBookToolRunning = true;
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

    it("opening on a box with no recording mode writes the mode in the typing's step too", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en"><p>One. Two.</p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        useCollectionDefaultRecordingMode(RecordingMode.Sentence);
        const box = byId("box1");
        const paragraph = box.querySelector("p")!;
        const typedText = paragraph.firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            typedText.data = "One. Two. Three.";
        });
        expect(box.hasAttribute("data-audiorecordingmode")).toBe(false);

        await recording.handleNewPageReady();

        expect(box.getAttribute("data-audiorecordingmode")).toBe("Sentence");
        expect(box.getElementsByClassName("audio-sentence")).toHaveLength(3);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        // One undo takes back the typing, the split and the mode together.
        await history.undo();
        expect(Array.from(paragraph.childNodes)).toEqual([typedText]);
        expect(typedText.data).toBe("One. Two.");
        expect(box.hasAttribute("data-audiorecordingmode")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        await Promise.resolve();
        expect(history.redoDescriptionL10nKey).toBe(kTypingKey);

        await history.redo();
        expect(typedText.data).toBe("One. Two. Three.");
        expect(box.getAttribute("data-audiorecordingmode")).toBe("Sentence");
        expect(box.getElementsByClassName("audio-sentence")).toHaveLength(3);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("opening after a text block is added writes its markup in the adding step, which undo and redo take back and put back", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-canvas"><img src="a.png"></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        // Recording whole text boxes, so opening gives the box the audio-sentence class and an id.
        useCollectionDefaultRecordingMode(RecordingMode.TextBox);
        const bloomCanvas = pageDocument().querySelector(".bloom-canvas")!;
        const kAddKey = "EditTab.Toolbox.ComicTool.Options.AddTextBlock";
        await history.doTransaction(kAddKey, () => {
            const textBlock = pageDocument().createElement("div");
            textBlock.className = "bloom-canvas-element";
            textBlock.innerHTML =
                '<div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" lang="en"><p>Hello there.</p></div></div>';
            bloomCanvas.appendChild(textBlock);
        });
        const box = bloomCanvas.querySelector<HTMLElement>(".bloom-editable")!;
        const textBlock = box.closest(".bloom-canvas-element")!;
        const classesBefore = box.className;
        expect(box.hasAttribute("data-audiorecordingmode")).toBe(false);
        expect(box.id).toBe("");

        await recording.handleNewPageReady();

        expect(box.getAttribute("data-audiorecordingmode")).toBe("TextBox");
        expect(box.classList.contains("audio-sentence")).toBe(true);
        const idGiven = box.id;
        expect(idGiven).not.toBe("");
        expect(history.undoDescriptionL10nKey).toBe(kAddKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(textBlock.isConnected).toBe(false);
        expect(box.hasAttribute("data-audiorecordingmode")).toBe(false);
        expect(box.className).toBe(classesBefore);
        expect(box.id).toBe("");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        await Promise.resolve();
        expect(history.redoDescriptionL10nKey).toBe(kAddKey);

        await history.redo();
        expect(textBlock.parentElement).toBe(bloomCanvas);
        expect(box.getAttribute("data-audiorecordingmode")).toBe("TextBox");
        expect(box.classList.contains("audio-sentence")).toBe(true);
        expect(box.id).toBe(idGiven);
        expect(history.undoDescriptionL10nKey).toBe(kAddKey);

        await history.undo();
        await history.undo();
        expect(byId("other").textContent).toBe("before");
        expect(onUnmarkedChange).not.toHaveBeenCalled();
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
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        expect(byId("g1").getAttribute("tabindex")).toBe("2");
        expect(cachedOrder()).toEqual(["g2", "g1"]);

        await history.undo();
        expect(byId("g1").getAttribute("tabindex")).toBe("1");
        expect(cachedOrder()).toEqual(["g1", "g2"]);
        // Building the arrows again did not clear redo.
        expect(history.redoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
        expect(
            pageDocument().getElementsByClassName(
                "bloom-playbackOrderControlsContainer",
            ),
        ).toHaveLength(2);

        await history.redo();
        expect(byId("g1").getAttribute("tabindex")).toBe("2");
        expect(cachedOrder()).toEqual(["g2", "g1"]);
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );

        // A click of the other arrow is a step of its own too.
        const otherClick = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        recording.bumpDown(2);
        expect(otherClick.end()).toBe(true);
        expect(byId("g1").getAttribute("tabindex")).toBe("1");
        await history.undo();
        expect(byId("g1").getAttribute("tabindex")).toBe("2");
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );
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

    it("after an undo takes away the highlighted sentence, highlights a sentence of the same box", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>One. Two.</p></div></div><div class="bloom-translationGroup"><div id="box2" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>Four.</p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        await recording.handleNewPageReady();
        const box = byId("box1");
        // The user types a sentence, which Talking Book splits off in the typing's step, and
        // moves the highlight to it.
        const lastText = box.getElementsByClassName("audio-sentence")[1]
            .firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            lastText.data = lastText.data + " Three.";
        });
        await recording.handleNewPageReady();
        const sentences = box.getElementsByClassName("audio-sentence");
        expect(sentences).toHaveLength(3);
        const third = sentences[2] as HTMLElement;
        await recording.setSoundAndHighlightAsync({
            newElement: third,
            shouldScrollToElement: false,
        });
        expect(recording.getCurrentHighlight()).toBe(third);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);

        await history.undo();
        await Promise.resolve();

        expect(third.isConnected).toBe(false);
        expect(sentences).toHaveLength(2);
        expect(recording.getCurrentHighlight()).toBe(sentences[0]);
        expect(history.redoDescriptionL10nKey).toBe(kTypingKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    /**
     * A box that opening the tool split in the typing's step, with the highlight on its second
     * sentence, and another box with a saved sentence. Then an undo of the typing, which takes
     * away every sentence of the first box. Returns the first box, its live list of sentences,
     * and the sentence that had the highlight.
     */
    async function undoTheSplitOfTheHighlightedBox(): Promise<{
        box: HTMLElement;
        sentences: HTMLCollectionOf<Element>;
        highlightedSentence: HTMLElement;
    }> {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>One. Two.</p></div></div><div class="bloom-translationGroup"><div id="box2" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p><span id="four" class="audio-sentence">Four.</span></p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        const box = byId("box1");
        const typedText = box.querySelector("p")!.firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            typedText.data = "One. Two. Three.";
        });
        await recording.handleNewPageReady();
        const sentences = box.getElementsByClassName("audio-sentence");
        expect(sentences).toHaveLength(3);
        const highlightedSentence = sentences[1] as HTMLElement;
        await recording.setSoundAndHighlightAsync({
            newElement: highlightedSentence,
            shouldScrollToElement: false,
        });
        expect(recording.getCurrentHighlight()).toBe(highlightedSentence);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);

        await history.undo();
        await Promise.resolve();

        expect(sentences).toHaveLength(0);
        expect(byId("four").isConnected).toBe(true);
        expect(history.redoDescriptionL10nKey).toBe(kTypingKey);
        return { box, sentences, highlightedSentence };
    }

    it("after an undo takes away every sentence of the highlighted box, keeps the highlight in that box", async () => {
        const { box, sentences } = await undoTheSplitOfTheHighlightedBox();

        // The other box has a sentence, but the highlight stays in the box that had it, which the
        // tool cannot split again without losing redo.
        expect(recording.getCurrentHighlight()).toBe(box);

        // The next action that needs the sentences splits the box and highlights its first one.
        await recording.moveToNextAudioElement();
        expect(sentences).toHaveLength(2);
        expect(recording.getCurrentHighlight()).toBe(sentences[1]);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("a redo that puts back the sentences of the highlighted box highlights the sentence that had it", async () => {
        const { box, sentences, highlightedSentence } =
            await undoTheSplitOfTheHighlightedBox();
        expect(recording.getCurrentHighlight()).toBe(box);

        await history.redo();
        await Promise.resolve();

        expect(sentences).toHaveLength(3);
        expect(highlightedSentence.isConnected).toBe(true);
        expect(recording.getCurrentHighlight()).toBe(highlightedSentence);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("an import after an undo takes away every sentence of the highlighted box splits the box first and files the audio under a sentence", async () => {
        const { box, sentences } = await undoTheSplitOfTheHighlightedBox();
        vi.mocked(axios.post).mockImplementation((url: string) => {
            if (url.endsWith("fileIO/chooseFile"))
                return Promise.resolve({ data: "C:/chosen.mp3" });
            if (url.endsWith("fileIO/getSpecialLocation"))
                return Promise.resolve({ data: "C:/book/audio" });
            return Promise.resolve({});
        });
        vi.spyOn(
            recording as unknown as {
                finishNewRecordingOrImportAsync: () => Promise<void>;
            },
            "finishNewRecordingOrImportAsync",
        ).mockResolvedValue();

        await recording.importRecordingAsync();

        expect(sentences).toHaveLength(2);
        const copies = vi
            .mocked(axios.post)
            .mock.calls.filter(([url]) =>
                String(url).endsWith("fileIO/copyFile"),
            );
        expect(copies).toHaveLength(1);
        const copy = copies[0][1] as { to: string };
        expect(decodeURIComponent(copy.to)).toBe(
            `C:/book/audio/${sentences[0].id}.mp3`,
        );
        expect(box.id).not.toBe(sentences[0].id);
    });

    it("a release of Speak before the box has been split again starts no recording", async () => {
        setPage(
            '<div class="bloom-page"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on" lang="en" data-audiorecordingmode="Sentence"><p>One. Two.</p></div></div><div id="other">before</div></div>',
        );
        await startHistoryWithAnEarlierStep();
        listenAsTheEditTabDoes();
        const typedText = byId("box1").querySelector("p")!.firstChild as Text;
        await history.doTransaction(kTypingKey, () => {
            typedText.data = "One. Two. Three.";
        });
        await recording.handleNewPageReady();
        // Undoing the typing takes away the split, which Speak makes again before recording.
        await history.undo();
        expect(
            byId("box1").getElementsByClassName("audio-sentence"),
        ).toHaveLength(0);
        // The split waits for something that only a later task brings, as a request to Bloom
        // does, so the release comes first.
        const privateRecording = recording as unknown as {
            splitAgainIfNeededAsync: () => Promise<void>;
            recording: boolean;
        };
        const split = privateRecording.splitAgainIfNeededAsync.bind(recording);
        let letSplitFinish: () => void = () => {
            throw new Error("The press did not start the split.");
        };
        const splitCanFinish = new Promise<void>(
            (resolve) => (letSplitFinish = resolve),
        );
        vi.spyOn(
            privateRecording,
            "splitAgainIfNeededAsync",
        ).mockImplementation(async () => {
            await splitCanFinish;
            await split();
        });

        const press = recording.startRecordCurrentAsync();
        await recording.endRecordCurrentAsync();
        letSplitFinish();
        await press;

        expect(
            byId("box1").getElementsByClassName("audio-sentence"),
        ).toHaveLength(2);
        expect(privateRecording.recording).not.toBe(true);
        expect(
            vi
                .mocked(axios.post)
                .mock.calls.filter(([url]) =>
                    String(url).includes("audio/startRecord"),
                ),
        ).toEqual([]);
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
