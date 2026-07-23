import {
    describe,
    it,
    expect,
    beforeAll,
    beforeEach,
    afterEach,
    vi,
} from "vitest";
import AudioRecording, {
    AudioTextFragment,
    initializeTalkingBookToolAsync,
    AudioMode,
    getAllAudioModes,
    theOneAudioRecorder,
} from "./audioRecording";
import { kAnyRecordingApiUrl } from "./audioUtils";
import { RecordingMode } from "./recordingMode";
import axios from "axios";
import $ from "jquery";
import { mockReplies } from "../../../utils/bloomApi";
import { Status } from "./TalkingBookUiState";
import {
    currentHighlightName,
    splitHighlightNames,
} from "./audioTextHighlightManager";

// A controllable stand-in for the page-frame CanvasElementManager. By default it is DISABLED,
// so getCanvasElementManager() returns undefined -- exactly how the real one behaves in tests
// (there is no editable-page bundle) -- and every existing test is unaffected. A test that
// needs to exercise the audio code's handling of an active canvas element calls __enable() and
// __setActiveForTest(...). When enabled it is a Proxy so that any method the production code
// happens to call (e.g. resumeComicEditing during image-description setup) is a safe no-op,
// while getActiveElement returns the (possibly stale/detached) element the test supplied.
const mockCanvasElement = vi.hoisted(() => {
    let activeElement: HTMLElement | undefined = undefined;
    let enabled = false;
    const explicit: Record<string, unknown> = {
        getActiveElement: () => activeElement,
        setActiveElement: (element: HTMLElement | undefined) => {
            activeElement = element;
        },
    };
    const manager = new Proxy(explicit, {
        get(target, prop: string) {
            if (prop in target) return target[prop];
            // Any other method the production code calls is a harmless no-op in tests.
            return () => undefined;
        },
    });
    return {
        getManager: () => (enabled ? manager : undefined),
        __enable: () => {
            enabled = true;
        },
        __setActiveForTest: (element: HTMLElement | undefined) => {
            activeElement = element;
        },
        __reset: () => {
            activeElement = undefined;
            enabled = false;
        },
    };
});

vi.mock("../canvas/canvasElementPageBridge", async (importActual) => {
    const actual =
        await importActual<
            typeof import("../canvas/canvasElementPageBridge")
        >();
    return {
        ...actual,
        getCanvasElementManager: () => mockCanvasElement.getManager(),
    };
});

class FakeHighlight {
    public ranges: Range[];

    public constructor(...ranges: Range[]) {
        this.ranges = ranges;
    }
}

type FakeHighlightRegistry = Map<string, FakeHighlight>;
type TestCssWithHighlights = {
    highlights?: FakeHighlightRegistry;
};

const installPseudoHighlightPolyfill = (targetWindow: Window) => {
    const targetWindowWithCss = targetWindow as Window & {
        CSS?: TestCssWithHighlights;
    };
    if (!targetWindowWithCss.CSS) {
        targetWindowWithCss.CSS = {};
    }

    const cssWithHighlights = targetWindowWithCss.CSS;
    cssWithHighlights.highlights = new Map<string, FakeHighlight>();
    (
        targetWindow as Window & {
            Highlight?: typeof FakeHighlight;
        }
    ).Highlight = FakeHighlight;
};

const getPageWindow = (): Window | undefined => {
    const iframe = parent.window.document.getElementById(
        "page",
    ) as HTMLIFrameElement | null;
    return iframe?.contentWindow ?? undefined;
};

const getPseudoHighlightsRegistry = (): FakeHighlightRegistry => {
    const targetWindow = getPageWindow() ?? globalThis.window;
    const cssWithHighlights = (
        targetWindow as Window & {
            CSS?: TestCssWithHighlights;
        }
    ).CSS;
    if (!cssWithHighlights?.highlights) {
        throw new Error(
            "Expected CSS.highlights test polyfill to be installed",
        );
    }

    return cssWithHighlights.highlights;
};

const getSplitHighlightTexts = (): string[][] => {
    const registry = getPseudoHighlightsRegistry();
    return splitHighlightNames.map((name) => {
        const highlight = registry.get(name);
        return highlight
            ? highlight.ranges.map((range) => range.toString())
            : [];
    });
};

const getHighlightTexts = (highlightName: string): string[] => {
    const highlight = getPseudoHighlightsRegistry().get(highlightName);
    return highlight ? highlight.ranges.map((range) => range.toString()) : [];
};

/**
 * In BL-15300, the "current" audio element is tracked by AudioRecording.highlightedElement.
 * Tests mark the intended pre-selected element with data-test-preselect="true" in their
 * HTML fixture (never via the obsolete ui-audioCurrent class). This helper reads that
 * attribute and transfers the element to the recording's highlightedElement field before
 * calling methods under test.
 */
const setHighlightedElementFromDom = (recording: AudioRecording) => {
    const pageFrame = parent.window.document.getElementById(
        "page",
    ) as HTMLIFrameElement | null;
    const doc = pageFrame?.contentDocument ?? document;
    const elem = doc.querySelector(
        "[data-test-preselect]",
    ) as HTMLElement | null;
    (
        recording as unknown as { highlightedElement: HTMLElement | null }
    ).highlightedElement = elem;
};

// Notes:
// For any async tests:
//   I recommend using async/await syntax, without the done() callback.
//     The done callback() is trickier to get right if an exception is thrown. You need to add try/catch/finally
//     If you don't catch it, the error message will only say a Timeout error occurred, not the actual error.
//     If you do catch it and then call fail(error), that's better but the stack trace won't be quite right.
//     It'll show the line where fail() was called.
//     Easier to just let vitest handle it (you get that by not having done() callback).
//   There's also a promise based syntax available,
//     but I think async/await with try/catch/finally is more readable than promises. (Less nesting and more consistent levels of nesting).
//   Summary: Use Async/Await with NO done callback
//   * Just add async() in front of the anonymous function passed to it().
//   * The anonymous function should have no input parameters
//   * Await whatever async stuff needs to be awaited.
//   * Let vitest handle the rest

describe("audio recording tests", () => {
    beforeAll(async () => {
        installPseudoHighlightPolyfill(globalThis.window);

        await setupForAudioRecordingTests();

        const pageWindow = getPageWindow();
        if (pageWindow) {
            installPseudoHighlightPolyfill(pageWindow);
        }
    });

    afterEach(() => {
        // Clean up any pending timers to prevent "parent is not defined" errors
        // when tests finish before timers fire
        theOneAudioRecorder?.clearTimeouts();
        getPseudoHighlightsRegistry().clear();
        mockCanvasElement.__reset();
    });

    // In an earlier version of our API, checkForAnyRecording was designed to fail (404) if there was no recording.
    // That was inconvenient for the real code, but convenient for tests, since without the Bloom server running,
    // we effortlessly got a 404 error wherever it was used. Once it was changed, a lot of tests had to be fixed
    // so that this API call would succeed and return {data:false} for this call. This function sets that up.
    // Unfortunately, we're not allowed to call it directly from describe(); it has to be called from an it() or beforeEach/All()
    // It would be nice to call it more in beforeAll(), but some of the tests need to set up other things
    // to return while spying on axios.get, and Jasmine doesn't allow more than one spy on the same function.
    // (maybe vitest would allow that?)
    function setupDefaultApiResponses() {
        vi.spyOn(axios, "get").mockImplementation((url: string) => {
            if (url.includes(kAnyRecordingApiUrl)) {
                return Promise.resolve({ data: false });
            } else {
                return Promise.reject("Fake 404 error 7.");
            }
        });
    }

    // Returns the HTML for a single text box for a variety of recording modes
    function getTextBoxHtmlSimple1(scenario: AudioMode) {
        if (scenario === AudioMode.PureSentence) {
            return `<div class="bloom-translationGroup"><div class="bloom-editable" id="div1" data-audioRecordingMode="Sentence"><p><span id="1.1" class="audio-sentence" data-test-preselect="true">Sentence 1.1.</span> <span id="1.2" class="audio-sentence">Sentence 1.2</span></p></div></div>`;
        } else if (scenario === AudioMode.PreTextBox) {
            return `<div class="bloom-translationGroup"><div class="bloom-editable" data-test-preselect="true" id="div1" data-audioRecordingMode="TextBox"><p><span id="1.1" class="audio-sentence">Sentence 1.1.</span> <span id="1.2" class="audio-sentence">Sentence 1.2</span></p></div></div>`;
        } else if (scenario === AudioMode.PureTextBox) {
            return `<div class="bloom-translationGroup"><div class="bloom-editable audio-sentence" data-test-preselect="true" id="div1" data-audioRecordingMode="TextBox"><p>Sentence 1.1. Sentence 1.2</p></div></div>`;
        } else if (scenario === AudioMode.HardSplitTextBox) {
            // FYI: Yes, it is confirmed that in hardSplit, the pre-selected element is the div, not the span.
            return `<div class="bloom-translationGroup"><div class="bloom-editable bloom-postAudioSplit" data-test-preselect="true" id="div1" data-audioRecordingMode="TextBox"><p><span id="1.1" class="audio-sentence">Sentence 1.1.</span> <span id="1.2" class="audio-sentence">Sentence 1.2</span></p></div></div>`;
        } else if (scenario === AudioMode.SoftSplitTextBox) {
            return `<div class="bloom-translationGroup"><div class="bloom-editable audio-sentence bloom-postAudioSplit" data-test-preselect="true" id="div1" data-audioRecordingMode="TextBox" data-audiorecordingendtimes="1.0 2.0"><p><span id="1.1" class="bloom-highlightSegment">Sentence 1.1.</span> <span id="1.2" class="bloom-highlightSegment">Sentence 1.2</span></p></div></div>`;
        } else {
            throw new Error("Unknown scenario: " + AudioMode[scenario]);
        }
    }

    describe("- Next()", () => {
        it("Record=Sentence, last sentence returns disabled for Next button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable' data-audiorecordingmode='Sentence'><p><span id='id1' class='audio-sentence' data-test-preselect='true'>Sentence 1.</span></p></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            const observed = recording.getNextAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=Sentence, last sentence returns disabled for Next button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'><p><span id='id1' class='audio-sentence'>Sentence 1.</span></p></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=TextBox, last sentence returns disabled for Next button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed).toBeFalsy();
        });

        it("SS -> SS, returns next box's first sentence", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable bloom-visibility-code-on' data-audiorecordingmode='Sentence'><p><span id='sentence1' class='audio-sentence' data-test-preselect='true'>Sentence 1.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable bloom-visibility-code-on' data-audiorecordingmode='Sentence'><p><span id='sentence2' class='audio-sentence'>Sentence 2.</span><span id='sentence3' class='audio-sentence'>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(
                `<div id='page1'><div class='bloom-translationGroup'>${box1Html}${box2Html}</div></div>`,
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBe("sentence2");
        });

        it("TS -> TT, returns next box", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable bloom-visibility-code-on audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'><p><span id='sentence1' class='audio-sentence'>Sentence 1.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable bloom-visibility-code-on audio-sentence' data-audiorecordingmode='TextBox'><p><span id='sentence2' class='audio-sentence'>Sentence 2.</span><span id='sentence3' class=''>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(
                `<div id='page1'><div class='bloom-translationGroup'>${box1Html}${box2Html}</div></div>`,
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBe("box2");
        });

        it("TT -> TT, returns next box", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-translationGroup'><div id='box1' class='bloom-editable bloom-visibility-code-on audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable bloom-visibility-code-on audio-sentence' data-audiorecordingmode='TextBox'>p>Sentence 2.</p></div></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBe("box2");
        });

        it("Next() skips over empty box, TT -> TT -> TT", () => {
            const boxTemplate = (
                index: number,
                extraClasses: string,
                preselect = false,
            ) => {
                return `<div id="box${index}" class="bloom-editable bloom-visibility-code-on audio-sentence${extraClasses}"${preselect ? ' data-test-preselect="true"' : ""} data-audiorecordingmode="TextBox">p>Sentence ${index}.</p></div>`;
            };
            const box1Html = boxTemplate(1, "", true);
            const box2Html =
                '<div id="box2" class="bloom-editable"><p></p></div>';
            const box3Html = boxTemplate(3, "");

            SetupIFrameFromHtml(
                `<div id="page1"><div class='bloom-translationGroup'>${box1Html}${box2Html}${box3Html}</div></div>`,
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBeTruthy(); // Null is definitely the wrong answer here
            expect(observed!.id).toBe("box3");
        });
    });

    describe("- Prev()", () => {
        it("Record=Sentence, first sentence returns disabled for Back button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable' data-audiorecordingmode='Sentence'><p><span id='id1' class='audio-sentence' data-test-preselect='true'>Sentence 1.</span></p></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            const observed = recording.getPreviousAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=Sentence, first sentence returns disabled for Back button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'><p><span id='id1' class='audio-sentence'>Sentence 1.</span></p></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=TextBox, first sentence returns disabled for Back button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed).toBeFalsy();
        });

        it("SS <- SS, returns previous box's last sentence", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable bloom-visibility-code-on' data-audiorecordingmode='Sentence'><p><span id='sentence1' class='audio-sentence'>Sentence 1.</span><span id='sentence2' class='audio-sentence'>Sentence 2.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable bloom-visibility-code-on' data-audiorecordingmode='Sentence'><p><span id='sentence3' class='audio-sentence' data-test-preselect='true'>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(
                `<div id='page1'><div class='bloom-translationGroup'>${box1Html}${box2Html}</div></div>`,
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBe("sentence2");
        });

        it("TS <- TT, returns previous box", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable bloom-visibility-code-on audio-sentence' data-audiorecordingmode='TextBox'><p><span id='sentence1' class='audio-sentence'>Sentence 1.</span><span id='sentence2' class='audio-sentence'>Sentence 2.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable bloom-visibility-code-on audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'><p><span id='sentence3' class=''>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(
                `<div id='page1'><div class='bloom-translationGroup'>${box1Html}${box2Html}</div></div>`,
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBe("box1");
        });

        it("TT <- TT, returns previous box", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-translationGroup'><div id='box1' class='bloom-editable bloom-visibility-code-on audio-sentence' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable bloom-visibility-code-on audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'>p>Sentence 2.</p></div></div></div>",
            );
            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBe("box1");
        });

        it("Prev() skips over empty box, TT <- TT <- TT", () => {
            const boxTemplate = (
                index: number,
                extraClasses: string,
                preselect = false,
            ) => {
                return `<div id="box${index}" class="bloom-editable bloom-visibility-code-on audio-sentence${extraClasses}"${preselect ? ' data-test-preselect="true"' : ""} data-audiorecordingmode="TextBox">p>Sentence ${index}.</p></div>`;
            };
            const box1Html = boxTemplate(1, "");
            const box2Html =
                '<div id="box2" class="bloom-editable"><p></p></div>';
            const box3Html = boxTemplate(3, "", true);

            SetupIFrameFromHtml(
                `<div id="page1"><div class='bloom-translationGroup'>${box1Html}${box2Html}${box3Html}</div></div>`,
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBeTruthy(); // Null is definitely the wrong answer here
            expect(observed!.id).toBe("box1");
        });
    });

    describe("- PlayingMultipleAudio()", () => {
        it("returns true while in listen to whole page with multiple text boxes", async () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-translationGroup'><div id='box1' class='bloom-editable bloom-visibility-code-on audio-sentence' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'>p>Sentence 2.</p></div></div></div>",
            );
            const recording = new AudioRecording();
            await recording.listenAsync();
            expect(recording.playingAudio()).toBe(true);
        });

        it("returns true while in listen to whole page with only one box", async () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-translationGroup'><div id='box1' class='bloom-editable bloom-visibility-code-on audio-sentence' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div></div></div>",
            );
            const recording = new AudioRecording();
            await recording.listenAsync();
            expect(recording.playingAudio()).toBe(true);
        });

        it("returns false while preloading", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div id='box1' class='bloom-editable audio-sentence' data-audiorecordingmode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable audio-sentence' data-test-preselect='true' data-audiorecordingmode='TextBox'>p>Sentence 2.</p></div></div>",
            );

            const recording = new AudioRecording();
            expect(recording.playingAudio()).toBe(false);
            recording.setupForListen();
            const player: Element = document.getElementById("player")!;
            player.setAttribute("preload", "auto");
            const nonExistentFilePath = "`[];'/.,<>?;.mp3";
            player.setAttribute("src", nonExistentFilePath);

            expect(recording.playingAudio()).toBe(false);
        });
    });

    describe("- MakeAudioSentenceElements()", () => {
        it("inserts sentence spans with ids and class when none exist", () => {
            const div = $("<div>This is a sentence. This is another</div>");
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(2);
            expect(spans[0].innerHTML).toBe("This is a sentence.");
            expect(spans[1].innerHTML).toBe("This is another");
            expect(div.text()).toBe("This is a sentence. This is another");
            expect(spans.first().attr("id")).not.toBe(
                spans.first().next().attr("id"),
            );
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.last().attr("class")).toBe("audio-sentence");
        });
        // BL-15300 (flicker): newPageReady is deliberately re-fired ~600ms after the first call
        // (scheduleDelayedNewPageReady) as a settle-timing safety net, so this markup runs a
        // second time with identical input. It must be idempotent: the second, identical pass
        // must NOT rewrite the DOM -- doing so re-creates the sentence spans, repainting the text
        // and dropping/re-adding the audio ::highlight (the "renders twice, once without the
        // highlight and then with it" flicker). Verify the span nodes are preserved (same objects).
        it("does not re-create audio-sentence spans on a repeated identical markup pass", () => {
            const div = $("<div>One. Two.</div>");
            const recording = new AudioRecording();

            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const firstSpans = div.find("span.audio-sentence").toArray();
            expect(firstSpans.length).toBe(2);
            const htmlAfterFirst = div.html();

            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const secondSpans = div.find("span.audio-sentence").toArray();

            expect(secondSpans.length).toBe(2);
            // Same DOM node objects, not rebuilt copies:
            expect(secondSpans[0]).toBe(firstSpans[0]);
            expect(secondSpans[1]).toBe(firstSpans[1]);
            expect(div.html()).toBe(htmlAfterFirst);
        });
        it("retains matching sentence spans with same ids.keeps md5s and adds missing ones", () => {
            const div = $(
                '<div><p><span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span> This is another</p></div>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(2);
            expect(spans[0].innerHTML).toBe("This is a sentence.");
            expect(spans[1].innerHTML).toBe("This is another");
            expect(div.text()).toBe("This is a sentence. This is another");
            expect(spans.first().attr("id")).toBe("abc");
            expect(spans.first().attr("recordingmd5")).toBe(
                "d15ba5f31fa7c797c093931328581664",
            );
            expect(spans.first().attr("id")).not.toBe(
                spans.first().next().attr("id"),
            );
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.last().attr("class")).toBe("audio-sentence");
        });
        it("retains markup within sentences", () => {
            const div = $(
                '<div><p><span id="abc" class="audio-sentence">This <b>is</b> a sentence.</span> This <i>is</i> another</p></div>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(2);
            expect(spans[0].innerHTML).toBe("This <b>is</b> a sentence.");
            expect(spans[1].innerHTML).toBe("This <i>is</i> another");
        });
        [
            "Phrase 1| Phrase 2.",
            "phrase 1| phrase 2.",
            "1 | 2",
            "1 ||| 2", // collapse multiple
        ].forEach((testInput) => {
            it(`treats vertical bar as a phrase delimiter (Input=${testInput})`, () => {
                const div = $(`<div><p>${testInput}</p></div>`);
                const recording = new AudioRecording();
                recording.makeAudioSentenceElementsTest(
                    div,
                    RecordingMode.Sentence,
                );
                const spans = div.find("span");
                expect(
                    spans.length,
                    `Input "${testInput}" should be split into 2 phrase.`,
                ).toBe(2);
                expect(
                    spans[0].innerText.endsWith("|"),
                    `${spans[0].innerText} should end with "|"`,
                ).toBe(true);
                expect(div[0].innerText, "InnerText no longer matches").toBe(
                    testInput,
                );
            });
        });
        it("keeps id with unchanged recorded sentence when new inserted before", () => {
            const div = $(
                '<div><p>This is a new sentence. <span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span></p></div>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(2);
            expect(spans[0].innerHTML).toBe("This is a new sentence.");
            expect(spans[1].innerHTML).toBe("This is a sentence.");
            expect(div.text()).toBe(
                "This is a new sentence. This is a sentence.",
            );
            expect(spans.first().next().attr("id")).toBe("abc"); // with matching md5 id should stay with sentence
            expect(spans.first().next().attr("recordingmd5")).toBe(
                "d15ba5f31fa7c797c093931328581664",
            );
            expect(spans.first().attr("id")).not.toBe(
                spans.first().next().attr("id"),
            );
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.last().attr("class")).toBe("audio-sentence");
        });
        it("keeps ids and md5s when inserted between", () => {
            const div = $(
                '<div><p><span id="abcd" recordingmd5="qed" class="audio-sentence">This is the first sentence.</span> This is inserted. <span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span> Inserted after.</p></div>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(4);
            expect(spans[0].innerHTML).toBe("This is the first sentence.");
            expect(spans[1].innerHTML).toBe("This is inserted.");
            expect(spans[2].innerHTML).toBe("This is a sentence.");
            expect(spans[3].innerHTML).toBe("Inserted after.");
            expect(div.text()).toBe(
                "This is the first sentence. This is inserted. This is a sentence. Inserted after.",
            );
            expect(spans.first().attr("id")).toBe("abcd"); // with matching md5 id should stay with sentence
            expect(spans.first().next().next().attr("id")).toBe("abc"); // with matching md5 id should stay with sentence
            expect(spans.first().next().next().attr("recordingmd5")).toBe(
                "d15ba5f31fa7c797c093931328581664",
            );
            // The first span is reused just by position, since its md5 doesn't match, but it should still keep it.
            expect(spans.first().attr("recordingmd5")).toBe("qed");
            expect(spans.first().attr("id")).not.toBe(
                spans.first().next().attr("id"),
            );
            expect(spans.first().next().attr("id")).not.toBe(
                spans.first().next().next().attr("id"),
            );
            expect(spans.first().next().next().attr("id")).not.toBe(
                spans.first().next().next().next().attr("id"),
            );
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.last().attr("class")).toBe("audio-sentence");
            expect(spans.first().next().attr("class")).toBe("audio-sentence");
        });

        // We can get something like this when we paste from Word
        it("ignores empty span", () => {
            const div = $(
                '<div><p>This is the first sentence.<span data-cke-bookmark="1" style="display: none;" id="cke_bm_35C"> </span></p></div>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(2);
            expect(spans[0].innerHTML).toBe("This is the first sentence.");
            expect(spans[1].innerHTML).toBe(" ");
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.last().attr("class") ?? "").not.toContain(
                "audio-sentence",
            );
        });

        // We can get something like this when we paste from Word
        it("ignores empty span and <br>", () => {
            const p = $(
                '<p><span data-cke-bookmark="1" style="display: none;" id="cke_bm_35C">&nbsp;</span><br></p>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(p, RecordingMode.Sentence);
            const spans = p.find("span");
            expect(spans.length).toBe(1);
            expect(spans[0].innerHTML).toBe("&nbsp;");
            expect(spans.first().attr("class") ?? "").not.toContain(
                "audio-sentence",
            );
        });

        it("flattens nested audio spans", () => {
            const p = $(
                '<p><span id="efgh" recordingmd5="xyz" class="audio-sentence"><span id="abcd" recordingmd5="qed" class="audio-sentence">This is the first.</span> <span id="abde" recordingmd5="qef" class="audio-sentence">This is the second.</span> This is the third.</span></p>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(p, RecordingMode.Sentence);
            const spans = p.find("span");
            // Should have removed the outer span and left the two inner ones and added a third one.
            expect(spans.length).toBe(3);
            expect(spans.first().attr("id")).toBe("abcd");
            expect(spans.first().next().attr("id")).toBe("abde");
            expect(spans[0].innerHTML).toBe("This is the first.");
            expect(spans[1].innerHTML).toBe("This is the second.");
            expect(spans[2].innerHTML).toBe("This is the third.");
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.first().next().attr("class")).toBe("audio-sentence");
            expect(spans.first().next().next().attr("class")).toBe(
                "audio-sentence",
            );
        });

        it("does not create nested spans", () => {
            // This scenario could happen when trying to perform soft-split again on a text box that has already been soft-split previously.
            const p = $(
                '<div class="bloom-editable" data-audiorecordingmode="TextBox" class="audio-sentence"><p><span id="a" class="bloom-highlightSegment">One.</span> <span id="b" class="bloom-highlightSegment">Two.</span> <span id="c" class="bloom-highlightSegment">Three.</span></p></div>',
            );
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.Sentence;
            recording.makeAudioSentenceElementsTest(p, RecordingMode.Sentence);
            const spans = p.find("span");
            // Should have removed the outer span and left the two inner ones and added a third one.
            expect(spans.length).toBe(3); // If regresses, it would probably show twice as many (i.e. 6) instead of 3.
        });

        it("ensures full span coverage of paragraph", () => {
            // based on BL-6038 user data
            const p = $(
                '<p>Random text <strong><span data-duration="9.400227" id="abcd" class="audio-sentence" recordingmd5="undefined"><u>underlined</u></span></strong> finish the sentence. Another sentence <u><strong>boldunderlined</strong></u> finish the second.</p>',
            );
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(p, RecordingMode.Sentence);
            const spans = p.find("span");
            // Should have expanded the first span and created one for the second sentence.
            expect(spans.length).toBe(2);
            expect(spans.first().attr("id")).toBe("abcd");
            // expect(spans.first().next().attr("id")).toBe("abde");
            expect(spans[0].innerHTML).toBe(
                "Random text <strong><u>underlined</u></strong> finish the sentence.",
            );
            expect(spans[1].innerHTML).toBe(
                "Another sentence <u><strong>boldunderlined</strong></u> finish the second.",
            );
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.first().next().attr("class")).toBe("audio-sentence");
        });

        it("handles hyperlinks", () => {
            const sentence1 = 'This is a <a href="www.google.com">link</a>.';
            const sentence2 = 'This is <a href="www.bing.com">another</a>.';
            const sentence3 = "Click them.";
            const div = $(`<div>${sentence1} ${sentence2} ${sentence3}</div>`);
            const recording = new AudioRecording();
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            const spans = div.find("span");
            expect(spans.length).toBe(3);
            expect(spans[0].innerHTML).toBe(sentence1); // Make sure the anchor is not lost in the HTML
            expect(spans[1].innerHTML).toBe(sentence2); // Make sure the anchor is not lost in the HTML
            expect(spans[2].innerHTML).toBe(sentence3);
            expect(div.text()).toBe(
                "This is a link. This is another. Click them.",
            );
            expect(spans.first().attr("id")).not.toBe(
                spans.first().next().attr("id"),
            );
            expect(spans.first().attr("class")).toBe("audio-sentence");
            expect(spans.last().attr("class")).toBe("audio-sentence");
        });

        it("converts from unmarked to text-box (bloom-editable includes format button)", () => {
            // This tests real input from Bloom that has not been marked up. (e.g. if the Talking Book dialog is opened up for the first time on an existing page while the Collection default is by-sentence)

            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const textBoxInnerHtml =
                "<p>Paragraph 1 Sentence 1. Paragraph 1, Sentence 2.</p> <p>Paragraph 2, Sentence 1. Paragraph 2, Sentence 2.</p>";
            const originalHtml =
                '<div class="bloom-editable">' +
                textBoxInnerHtml +
                formatButtonHtml +
                "</div>";
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            expect(div.text(), "div text").toBe(
                "Paragraph 1 Sentence 1. Paragraph 1, Sentence 2. Paragraph 2, Sentence 1. Paragraph 2, Sentence 2.",
            );

            const spans = div.find("span");
            expect(
                spans.length,
                "number of spans does not match expected count",
            ).toBe(0);

            const parent = $("<div>").append(div).clone();

            const divs = parent.find("div");
            expect(
                divs.length,
                "number of divs does not match expected count",
            ).toBe(2);

            expect($(divs[0]).is(".audio-sentence"), "textbox's class").toBe(
                true,
            );
            expect($(divs[0]).attr("id"), "textbox's id").not.toBe(undefined);
            expect(
                $(divs[0]).attr("id").length,
                "textbox's id",
            ).toBeGreaterThan(31); // GUID without hyphens is 32 chars longs
            expect($(divs[0]).attr("id").length, "textbox's id").toBeLessThan(
                38,
            ); // GUID with hyphens adds 4 chars. And we sometimes insert a 1-char prefix, adding up to 37.

            expect(
                $(divs[1]).is(".audio-sentence"),
                "formatButton's class",
            ).toBe(false);
            expect($(divs[1]).attr("id"), "formatButton's id").toBe(
                "formatButton",
            );
            expect(divs[1].outerHTML, "formatButton's outerHTML").toBe(
                formatButtonHtml,
            );

            const paragraphs = div.find("p");
            expect(paragraphs.length, "number of paragraphs").toBe(2);
            paragraphs.each((index, paragraph) => {
                expect(
                    $(paragraph).is(".audio-sentence"),
                    "paragraph " + index + " class",
                ).toBe(false);
                expect(paragraph.id, "paragraph " + index + " id").toBe(""); // If id attribute is not set, this actually returns empty string, which is kinda surprising.
            });

            expect(
                parent.html().indexOf('id=""'),
                "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)",
            ).toBe(-1);

            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent HTML",
            ).toBe(
                '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox"><p>Paragraph 1 Sentence 1. Paragraph 1, Sentence 2.</p> <p>Paragraph 2, Sentence 1. Paragraph 2, Sentence 2.</p>' +
                    formatButtonHtml +
                    "</div>",
            );

            recording.recordingMode = RecordingMode.Sentence;
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            expect(div.text, "Swap back test").toBe($(originalHtml).text);
            // Note: It is not expected that going to by-sentence to here will lead back the original HTML structure. (Because we started with unmarked text, not by-sentence)
        });

        it("converts from single unmarked paragraph to text-box", () => {
            // This tests real input from Bloom that has not been marked up. (e.g. if the Talking Book dialog is opened up for the first time on an existing page while the Collection default is by-sentence)
            // Note: a single paragraph does not have any newlines (<br>) tags in it so it could exercise a different path through the recursion.
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const textBoxInnerHtml = "<p>Hello world</p>";
            const originalHtml =
                '<div class="bloom-editable">' +
                textBoxInnerHtml +
                formatButtonHtml +
                "</div>";
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            expect(div.text(), "div text").toBe("Hello world");

            const spans = div.find("span");
            expect(spans.length, "number of spans").toBe(0);

            const parent = $("<div>").append(div).clone();
            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent HTML",
            ).toBe(
                '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox"><p>Hello world</p>' +
                    formatButtonHtml +
                    "</div>",
            );

            const divs = parent.find("div");
            expect(divs.length, "number of divs").toBe(2);

            expect($(divs[0]).is(".audio-sentence"), "textbox's class").toBe(
                true,
            );
            expect(divs[0].id.length, "textbox's id length").toBeGreaterThan(
                31,
            );
            expect(divs[0].id.length, "textbox's id length").toBeLessThan(38);

            expect(
                $(divs[1]).is(".audio-sentence"),
                "formatButton's class",
            ).toBe(false);
            expect(divs[1].id, "formatButton's id").toBe("formatButton");
            expect(divs[1].outerHTML, "formatButton's outerHTML").toBe(
                formatButtonHtml,
            );

            const paragraphs = div.find("p");
            expect(paragraphs.length, "number of paragraphs").toBe(1);
            paragraphs.each((index, paragraph) => {
                expect(
                    $(paragraph).is(".audio-sentence"),
                    "paragraph " + index + " class",
                ).toBe(false);
                expect(paragraph.id, "paragraph " + index + " id").toBe("");
            });

            expect(
                parent.html().indexOf('id=""'),
                "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)",
            ).toBe(-1);

            recording.recordingMode = RecordingMode.Sentence;
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            expect(div.text, "Swap back test").toBe($(originalHtml).text);
            // Note: It is not expected that going to by-sentence to here will lead back the original HTML structure. (Because we started with unmarked text, not by-sentence)
        });

        it("converts from single marked by-sentence paragraph to text-box", () => {
            // Note: a single paragraph does not have any newlines (<br>) tags in it so it could exercise a different path through the recursion.
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const textBoxInnerHtml =
                '<p><span id="ef142986-373a-4353-808f-a05d9478c0ed" class="audio-sentence">Hello world</span></p>';
            const originalHtml =
                '<div class="bloom-editable" role="textbox">' +
                textBoxInnerHtml +
                formatButtonHtml +
                "</div>";
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            expect(div.text(), "div text").toBe("Hello world");

            const parent = $("<div>").append(div).clone();
            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent HTML",
            ).toBe(
                '<div class="bloom-editable audio-sentence" role="textbox" data-audiorecordingmode="TextBox"><p>Hello world</p>' +
                    formatButtonHtml +
                    "</div>",
            );

            const divs = parent.find("div");
            expect(divs.length, "number of divs").toBe(2);

            expect($(divs[0]).is(".audio-sentence"), "textbox's class").toBe(
                true,
            );
            expect($(divs[0]).attr("id"), "textbox's id").not.toBe(undefined);
            expect(divs[0].id.length, "textbox's id length").toBeGreaterThan(
                31,
            );
            // Enhance: It would be great if it preserve the original one
            //expect(divs[0].id).toBe("ef142986-373a-4353-808f-a05d9478c0ed", "textbox's id");

            expect(
                $(divs[1]).is(".audio-sentence"),
                "formatButton's class",
            ).toBe(false);
            expect(divs[1].id, "formatButton's id").toBe("formatButton");
            expect(divs[1].outerHTML, "formatButton's outerHTML").toBe(
                formatButtonHtml,
            );

            const spans = div.find("span");
            expect(spans.length, "number of spans").toBe(0);

            expect(
                parent.html().indexOf('id=""'),
                "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)",
            ).toBe(-1);

            recording.recordingMode = RecordingMode.Sentence;
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            expect(div.text, "Swap back test").toBe($(originalHtml).text);
            // Note: It is not expected that going to by-sentence to here will lead back the original HTML structure. (Because we started with unmarked text, not by-sentence)
        });

        it("converts from single line text-box to by-sentence", () => {
            const originalHtml =
                '<div id="ba497822-afe7-4e16-90e8-91a795242720" class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on cke_editable cke_editable_inline cke_contents_ltr normal-style audio-sentence" data-languagetipcontent="English" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" data-audiorecordingmode="TextBox" lang="en" contenteditable="true"><p>hi<br></p><div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div></div>';
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.Sentence; // Should be the new mode
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );

            const parent = $("<div>").append(div).clone();

            const spans = parent.find("span");
            expect(spans.length, "number of spans").toBe(1);

            const paragraphs = parent.find("p");
            expect(paragraphs.length, "number of spans").toBe(1);

            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent html",
            ).toBe(
                '<div class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on cke_editable cke_editable_inline cke_contents_ltr normal-style" data-languagetipcontent="English" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" data-audiorecordingmode="Sentence" lang="en" contenteditable="true"><p><span class="audio-sentence">hi<br></span></p><div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div></div>',
            );
        });

        it("converts from by-sentence to text-box (bloom-editable includes format button)", () => {
            // This tests real input from Bloom that has already been marked up in by-sentence mode. (i.e., this is executed upon un-clicking the checkbox from by-sentence to not-by-sentence)
            const textBoxDivHtml =
                '<div class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style cke_editable cke_editable_inline cke_contents_ltr" data-languagetipcontent="English" data-audiorecordingmode="Sentence" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" lang="en" contenteditable="true">';
            const paragraphsMarkedBySentenceHtml =
                '<p><span id="i663e4f39-2d34-4624-829f-e927a58e2101" class="audio-sentence">Sentence 1.</span> <span id="d5df952d-dd60-4790-bb9d-e24fb9b5d4da" class="audio-sentence">Sentence 2.</span> <span id="i66e6edf8-49bf-4fb0-b48f-ab8235e3b902" class="audio-sentence">Sentence 3.</span><br></p><p><span id="i828de727-4ef9-45ef-afd6-4841bbe0b3d3" class="audio-sentence">Paragraph 2.</span><br></p>';
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml =
                textBoxDivHtml +
                paragraphsMarkedBySentenceHtml +
                formatButtonHtml +
                "</div>";
            const div = $(originalHtml);

            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            let parent = $("<div>").append(div).clone();

            const spans = parent.find("span");
            expect(spans.length, "number of spans").toBe(0);

            const divs = parent.find("div");
            expect(divs.length, "number of divs").toBe(2);
            expect($(divs[0]).is(".audio-sentence"), "textbox's class").toBe(
                true,
            );
            // GUID without hyphens is 32 chars longs
            expect(
                $(divs[0]).attr("id").length,
                "textbox's id",
            ).toBeGreaterThan(31);
            // GUID with hyphens adds 4 chars. And we sometimes insert a 1-char prefix, adding up to 37.
            expect($(divs[0]).attr("id").length, "textbox's id").toBeLessThan(
                38,
            );
            expect($(divs[1]).attr("id"), "formatButton's id").toBe(
                "formatButton",
            );

            const paragraphs = parent.find("p");
            expect(paragraphs.length, "number of paragraphs").toBe(2);
            paragraphs.each((index, paragraph) => {
                expect(
                    $(paragraph).attr("id"),
                    "paragraph " + index + " id",
                ).toBe(undefined); // If id attribute is not set, this actually returns empty string, which is kinda surprising.
                expect(
                    $(paragraph).hasClass("audio-sentence"),
                    "paragraph " + index + " class",
                ).toBe(false);
            });

            expect(
                parent.html().indexOf('id=""'),
                "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)",
            ).toBe(-1);

            const expectedTextBoxDiv = $(textBoxDivHtml)
                .attr("data-audiorecordingmode", "TextBox")
                .addClass("audio-sentence");
            const expectedTextBoxDivHtml = $("<div>")
                .append(expectedTextBoxDiv)
                .html()
                .replace(/<\/div>/, "");
            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent HTML",
            ).toBe(
                expectedTextBoxDivHtml +
                    "<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>" +
                    StripAllGuidIds(formatButtonHtml) +
                    "</div>",
            );

            recording.recordingMode = RecordingMode.Sentence;
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );
            parent = $("<div>").append(div).clone();
            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Swap back to original",
            ).toBe(StripAllGuidIds(StripEmptyClasses(originalHtml)));
        });

        it("converts by-text-box into by-sentence (bloom-editable includes format button)", () => {
            // This tests real input from Bloom that has been marked up in by-text-box mode (e.g., clicking the checkbox from not-by-sentence into by-sentence)
            const textBoxDivHtml =
                '<div id="ee41e518-7855-472a-b8ce-a0c6caa68341" aria-label="false" role="textbox" spellcheck="true" tabindex="0" style="min-height: 24px;" class="bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style audio-sentence" data-languagetipcontent="English" data-audiorecordingmode="TextBox" lang="en" contenteditable="true">';
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml = `<div id="numberedPage">${textBoxDivHtml}<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>${formatButtonHtml}</div></div>`;
            SetupIFrameFromHtml(originalHtml);

            const pageFrame = parent.window.document.getElementById("page");
            const div = $(
                (<HTMLIFrameElement>pageFrame).contentDocument!.getElementById(
                    "numberedPage",
                )!,
            );

            let recording = new AudioRecording();
            recording.recordingMode = RecordingMode.Sentence;
            recording.makeAudioSentenceElementsTest(
                div,
                RecordingMode.Sentence,
            );

            expect(div.text(), "div text").toBe(
                "Sentence 1. Sentence 2. Sentence 3.Paragraph 2.",
            );

            const spans = div.find("span");
            expect(spans.length, "number of spans").toBe(4);
            spans.each((index, span) => {
                expect(span.id.length, "span " + index + " id").toBeGreaterThan(
                    31,
                );
                expect(
                    $(span).hasClass("audio-sentence"),
                    "span " + index + " class",
                ).toBe(true);
            });

            expect($(spans[0]).text()).toBe("Sentence 1.");
            expect($(spans[3]).text()).toBe("Paragraph 2.");

            const paragraphs = div.find("p");
            expect(paragraphs.length, "number of paragraphs").toBe(2);
            paragraphs.each((index, paragraph) => {
                expect(paragraph.id, "paragraph " + index + " id").toBe(""); // If id attribute is not set, this actually returns empty string, which is kinda surprising.
                expect(
                    $(paragraph).hasClass("audio-sentence"),
                    "paragraph " + index + " class",
                ).toBe(false);
            });

            let parentDiv = $("<div>").append(div).clone();
            const expectedTextBoxDiv = $(textBoxDivHtml)
                .attr("data-audiorecordingmode", "Sentence")
                .removeClass("audio-sentence")
                .removeAttr("id");
            const expectedTextBoxDivHtml = $("<div>")
                .append(expectedTextBoxDiv)
                .html()
                .replace(/<\/div>/, "");
            const expectedTextBoxInnerHtml =
                '<p><span class="audio-sentence">Sentence 1.</span> <span class="audio-sentence">Sentence 2.</span> <span class="audio-sentence">Sentence 3.</span><br></p><p><span class="audio-sentence">Paragraph 2.</span><br></p>';

            console.log(
                "we got this: " +
                    StripAllGuidIds(StripEmptyClasses(parentDiv.html())),
            );
            console.log(
                "we expected: " +
                    '<div id="numberedPage">' +
                    expectedTextBoxDivHtml +
                    expectedTextBoxInnerHtml +
                    formatButtonHtml +
                    "</div></div>",
            );
            expect(
                StripAllGuidIds(StripEmptyClasses(parentDiv.html())),
                "parent.html",
            ).toBe(
                '<div id="numberedPage">' +
                    expectedTextBoxDivHtml +
                    expectedTextBoxInnerHtml +
                    formatButtonHtml +
                    "</div></div>",
            );

            expect(
                parentDiv.html().indexOf('id=""'),
                "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)",
            ).toBe(-1);

            // Test that you can switch back and recover more-or-less the original
            recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);
            parentDiv = $("<div>").append(div).clone();
            expect(
                StripAllGuidIds(StripEmptyClasses(parentDiv.html())),
                "Swap back to original",
            ).toBe(StripAllGuidIds(StripEmptyClasses(originalHtml)));
        });

        it("loads by-text-box without changing anything", () => {
            // This tests real input from Bloom that has been marked up in by-text-box mode (e.g., clicking the checkbox from not-by-sentence into by-sentence)
            const textBoxDivHtml =
                '<div id="ee41e518-7855-472a-b8ce-a0c6caa68341" aria-label="false" role="textbox" spellcheck="true" tabindex="0" style="min-height: 24px;" class="bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style audio-sentence" data-test-preselect="true" data-languagetipcontent="English" data-audiorecordingmode="TextBox" lang="en" contenteditable="true">';
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml =
                '<div id="page">' +
                textBoxDivHtml +
                "<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>" +
                formatButtonHtml +
                "</div></div>";
            const div = $(originalHtml);

            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            const parent = $("<div>").append(div).clone();
            expect(parent.html(), "re-load identical content test").toBe(
                originalHtml,
            );
        });

        it("converts from unmarked to text-box (no format button)", () => {
            // The input is hypothetical to exercise corner cases in the code, but these inputs are not actually expected to show up in normal usage.
            const textBoxInnerHtml =
                "<p>Paragraph 1A. Paragraph 1B.<br></p><p>Paragraph 2A. Paragraph 2B.<br></p>";
            const originalHtml =
                '<div class="bloom-editable">' + textBoxInnerHtml + "</div>";
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            expect(div.text(), "div text").toBe(
                "Paragraph 1A. Paragraph 1B.Paragraph 2A. Paragraph 2B.",
            );
            const parent = $("<div>").append(div).clone();
            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent HTML",
            ).toBe(
                '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox">' +
                    textBoxInnerHtml +
                    "</div>",
            );
        });

        it("hypothetically converts from by-sentence to text-box (multiple audio-sentence runs, ends with an audio-sentence)", () => {
            // The input is hypothetical to exercise corner cases in the code, but these inputs are not actually expected to show up in normal usage.
            // Note: this test is a lot more boring now that we're no longer trying to come up with the minimal groupings
            const nonAudioRun1Html =
                '<div id="nonAudio1"></div><div id="nonAudio2"></div>';
            const audioRun1Html =
                '<p><span id="audio1" class="audio-sentence">Paragraph 1A.</span> <span id="audio2" class="audio-sentence">Paragraph 1B.</span><br></p><p><span id="audio3" class="audio-sentence">Paragraph 2A.</span> <span id="audio4" class="audio-sentence">Paragraph 2B.</span><br></p>';
            const nonAudioRun2Html =
                '<div id="nonAudio3"></div><div id="nonAudio4"></div>';
            const audioRun2Html =
                '<p><span id="audio5" class="audio-sentence">Paragraph 3A.</span> <span id="audio6" class="audio-sentence">Paragraph 3B.</span><br></p><p><span id="audio7" class="audio-sentence">Paragraph 4A.</span> <span id="audio8" class="audio-sentence">Paragraph 4B.</span><br></p>';
            const originalHtml =
                '<div class="bloom-editable" data-audiorecordingmode="Sentence">' +
                nonAudioRun1Html +
                audioRun1Html +
                nonAudioRun2Html +
                audioRun2Html +
                "</div>";
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.makeAudioSentenceElementsTest(div, RecordingMode.TextBox);

            const parent = $("<div>").append(div).clone();
            const expectedAudioRun1Html =
                "<p>Paragraph 1A. Paragraph 1B.<br></p><p>Paragraph 2A. Paragraph 2B.<br></p>";
            const expectedAudioRun2Html =
                "<p>Paragraph 3A. Paragraph 3B.<br></p><p>Paragraph 4A. Paragraph 4B.<br></p>";
            const expectedHtml =
                '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox">' +
                nonAudioRun1Html +
                expectedAudioRun1Html +
                nonAudioRun2Html +
                expectedAudioRun2Html +
                "</div>";
            expect(
                StripAllGuidIds(StripEmptyClasses(parent.html())),
                "Parent HTML",
            ).toBe(expectedHtml);
        });
    });

    /* TODO: I don't know why this isn't running anything and thus giving an error
    describe("endRecordCurrentAsync", () => {
        function setupMockRecording() {
            // Make all the startRecord and endRecord post calls "succeed".
            vi.spyOn(axios, "post").mockReturnValue(Promise.resolve());
        }

        function setupTest(checksumSetting: string, scenario: AudioMode) {
            const divHtml = getTextBoxHtmlSimple1(scenario);
            SetupIFrameFromHtml(`<div id="page1">${divHtml}</div>`);

            if (checksumSetting === "missing") {
                // No need to do anything
                //
                // But let's double-check that the test is setup correctly.
                const spans = [
                    getFrameElementById("page", "1.1"),
                    getFrameElementById("page", "1.2")
                ];

                spans.forEach(span => {
                    // Span may be null in PureTextBox scenario
                    if (span) {
                        const md5 = span.getAttribute("recordingmd5");
                        expect(!md5 || md5 === "undefined").toBe(
                            true,
                            "Test setup failure: recordingmd5 is not missing."
                        );
                    }
                });
            } else if (checksumSetting === "outOfDate") {
                const div = getFrameElementById("page", "div1")!;
                div.setAttribute("recordingmd5", "wrongMD5");
            } else {
                throw new Error(
                    "Unrecognized checksumSetting: " + checksumSetting
                );
            }
        }

        async function runEndRecordSetsMd5TestsAsync(
            md5Setting: string,
            scenario: AudioMode
        ) {
            // Setup
            setupTest(md5Setting, scenario);
            setupMockRecording();

            // StartRecording doesn't do anything unless Record button is enabled.
            // So set it up into enabled state.
            const recording = new AudioRecording();
            if (scenario === AudioMode.PureSentence) {
                recording.recordingMode = RecordingMode.Sentence;
            } else {
                recording.recordingMode = RecordingMode.TextBox;
            }

            recording.setEnabledOrExpecting("record", "record");

            // Need to start recording because endRecord() doesn't do anything if a recording hasn't been started.
            await recording.startRecordCurrentAsync();

            // System under test
            await recording.endRecordCurrentAsync();

            // Verification
            if (scenario === AudioMode.PureSentence) {
                // Only PureSentence should set a sentence-level MD5.
                // All the other ones shoudl set a text-box level MD5.
                const span1 = getFrameElementById("page", "1.1") as HTMLElement;
                // This md5 is for "Sentence 1.1."
                expect(span1).toHaveAttr(
                    "recordingmd5",
                    "7b07751111f5c613158db809b20a1aad"
                );

                // Only the current one should've been updated. span2's should stay its original value
                const span2 = getFrameElementById("page", "1.2") as HTMLElement;
                expect(span2).not.toHaveAttr("recordingmd5");
            } else {
                const div = getFrameElementById("page", "div1") as HTMLElement;

                // This md5 is for "Sentence 1.1. Sentence 1.2"
                expect(div).toHaveAttr(
                    "recordingmd5",
                    "134edfa2be336e3ff596f013b0cbe16b"
                );
            }
        }

        const md5Settings = ["missing", "outOfDate"];
        md5Settings.forEach(md5Setting => {
            getAllAudioModes().forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`recording updates the md5 (md5=${md5Setting}, scenario=${scenarioName})`, async () => {
                    await runEndRecordSetsMd5TestsAsync(md5Setting, scenario);
                }, 15000);
            });
        });
    });
    */

    describe("startRecordCurrentAsync", () => {
        const setupRecordButtonHtml = () => {
            SetupIFrameFromHtml(
                `<div id="page1"><div class="bloom-editable" data-test-preselect="true" id="div1" data-audiorecordingmode="TextBox"><p><span id="1.1" class="audio-sentence">Sentence 1.1.</span></p></div></div>`,
            );
        };

        const createMockedRecordingForStartRecord = () => {
            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            recording.setEnabledOrExpecting("record", "record");

            vi.spyOn(recording as any, "resetAudioIfPaused").mockImplementation(
                () => undefined,
            );
            vi.spyOn(recording as any, "setHighlightToAsync").mockResolvedValue(
                undefined,
            );
            vi.spyOn(recording as any, "clearAudioSplit").mockImplementation(
                () => undefined,
            );
            vi.spyOn(recording as any, "getCurrentAudioId").mockReturnValue(
                "div1",
            );
            vi.spyOn(
                recording as any,
                "updateInputDeviceDisplay",
            ).mockImplementation(() => undefined);
            vi.spyOn(
                recording as any,
                "finishNewRecordingOrImportAsync",
            ).mockResolvedValue(undefined);
            vi.spyOn(recording as any, "updateDisplay").mockImplementation(
                () => undefined,
            );

            return recording;
        };

        beforeEach(() => {
            vi.useFakeTimers();
        });

        afterEach(() => {
            vi.useRealTimers();
        });

        it("cancels the delayed active state when mouseup ends recording early", async () => {
            setupRecordButtonHtml();

            vi.spyOn(axios, "post").mockResolvedValue({ data: {} } as never);

            const recording = createMockedRecordingForStartRecord();

            await recording.startRecordCurrentAsync();
            await recording.endRecordCurrentAsync();
            await vi.advanceTimersByTimeAsync(300);

            expect(recording.uiState.buttons.record).not.toBe(Status.Active);

            recording.clearTimeouts();
        });

        it("cancels delayed active state when mouseup happens before startRecord resolves", async () => {
            setupRecordButtonHtml();

            let resolveStartRecord: () => void;
            const startRecordPromise = new Promise<void>((resolve) => {
                resolveStartRecord = () => resolve();
            });
            vi.spyOn(axios, "post").mockImplementation((url: string) => {
                if (url.startsWith("/bloom/api/audio/startRecord")) {
                    return startRecordPromise as never;
                }

                return Promise.resolve({ data: {} }) as never;
            });

            const recording = createMockedRecordingForStartRecord();

            const startRecordCall = recording.startRecordCurrentAsync();
            await Promise.resolve();
            await recording.endRecordCurrentAsync();
            resolveStartRecord!();
            await startRecordCall;
            await vi.advanceTimersByTimeAsync(300);

            expect(recording.uiState.buttons.record).not.toBe(Status.Active);

            recording.clearTimeouts();
        });
    });

    describe("clearRecording", () => {
        function setupClearRecordingTest(
            scenario: AudioMode = AudioMode.PureSentence,
        ) {
            setupDefaultApiResponses();
            const divHtml = getTextBoxHtmlSimple1(scenario);
            SetupIFrameFromHtml(`<div id="page1">${divHtml}</div>`);

            addRecordingChecksums(scenario);
        }

        function addRecordingChecksums(scenario: AudioMode) {
            const audioSentenceIds =
                scenario === AudioMode.PureTextBox ||
                scenario === AudioMode.SoftSplitTextBox
                    ? ["div1"]
                    : ["1.1", "1.2"];

            audioSentenceIds.forEach((id) => {
                const elem = getFrameElementById("page", id)!;
                elem.setAttribute("recordingmd5", "fakeMd5");
            });
        }

        const runClearRecordingAsync = async (recording, scenario) => {
            setHighlightedElementFromDom(recording);
            if (scenario === AudioMode.PureSentence) {
                recording.recordingMode = RecordingMode.Sentence;
            } else {
                recording.recordingMode = RecordingMode.TextBox;
            }
            recording.uiState.buttons.clear = Status.Enabled;
            recording.setSoundFrom(getFrameElementById("page", "div1")!);
            await recording.clearRecordingAsync();
        };

        getAllAudioModes().forEach((scenario) => {
            const scenarioName = AudioMode[scenario];
            it(`clearRecording() removes recordingmd5 (scenario=${scenarioName}`, async () => {
                await runClearRecordingMd5TestAsync(scenario);
            });
        });

        async function runClearRecordingMd5TestAsync(scenario: AudioMode) {
            const recording = new AudioRecording();
            setupClearRecordingTest(scenario);
            await runClearRecordingAsync(recording, scenario);

            // Verification
            if (scenario === AudioMode.PureSentence) {
                // Deleted
                const span1 = getFrameElementById("page", "1.1");
                expect(span1).not.toHaveAttribute("recordingmd5");

                // The other spans aren't deleted though.
                const span2 = getFrameElementById("page", "1.2");
                expect(span2).toHaveAttribute("recordingmd5", "fakeMd5");
            } else if (
                scenario === AudioMode.PreTextBox ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                // All recordings within the text box should be cleared out
                const spanIds = ["1.1", "1.2"];
                spanIds.forEach((id) => {
                    const span = getFrameElementById("page", id);
                    expect(span).not.toHaveAttribute("recordingmd5");
                });
            } else {
                const div = getFrameElementById("page", "div1");
                expect(div).not.toHaveAttribute("recordingmd5");
            }
        }

        it("clearRecording() disables button", async () => {
            const recording = new AudioRecording();
            setupClearRecordingTest();

            await runClearRecordingAsync(recording, AudioMode.PureSentence);

            expect(recording.uiState.buttons.clear).not.toBe(Status.Enabled);
            expect(recording.uiState.buttons.clear).toBe(Status.Disabled);
        });

        getAllAudioModes().forEach((scenario) => {
            const scenarioName = AudioMode[scenario];
            it(`clearRecording() deletes recordings (${scenarioName})`, async () => {
                await runClearRecordingDeleteTest(scenario);
            });
        });

        async function runClearRecordingDeleteTest(scenario: AudioMode) {
            const recording = new AudioRecording();
            vi.restoreAllMocks();
            vi.spyOn(axios, "post").mockImplementation((url: string) => {
                if (url.includes("/bloom/api/audio/deleteSegment?id")) {
                    // Helps it test a more realistic scenario, instead of always testing the 404s
                    return Promise.resolve();
                } else {
                    return Promise.reject("Fake 404 error 1.");
                }
            });

            const setup = () => {
                setupClearRecordingTest(scenario);
            };

            const run = async () => {
                return runClearRecordingAsync(recording, scenario);
            };

            const verify = () => {
                let ids: string[] = [];
                switch (scenario) {
                    case AudioMode.PureSentence: {
                        ids = ["1.1"]; // , "1.2"];
                        break;
                    }
                    case AudioMode.PreTextBox:
                    case AudioMode.HardSplitTextBox: {
                        ids = ["1.1", "1.2"];
                        break;
                    }
                    case AudioMode.PureTextBox:
                    case AudioMode.SoftSplitTextBox: {
                        ids = ["div1"];
                        break;
                    }
                    default:
                        throw new Error(
                            "Unrecognized scenario: " + AudioMode[scenario],
                        );
                }

                ids.forEach((id) => {
                    const path = "/bloom/api/audio/deleteSegment?id=" + id;
                    expect(axios.post).toHaveBeenCalledWith(path);
                });

                expect(axios.post).toHaveBeenCalledTimes(ids.length);
            };

            setup();
            await run();
            verify();
        }
    });

    describe("- newPageReady()", () => {
        it("sets current to correct 1st element upon newPageReady", async () => {
            setupDefaultApiResponses();
            // Regression test to make sure we don't set it to the qTip element or a different language's text box.
            const editable1 =
                '<div id="div1" class="bloom-editable audio-sentence bloom-visibility-code-on" lang="es" data-audiorecordingmode="TextBox"><p>Uno. Dos.</p></div>';
            const editable2 =
                '<div id="div2" class="bloom-editable audio-sentence" lang="en" data-audiorecordingmode="TextBox"><p>One. Two.</p></div>';
            const translationGroup = `<div class="bloom-translationGroup">${editable1}${editable2}</div>`;

            // Note: Theoretically this should have ${editable2}${editable1} inside, but...
            // For the test case, let's just use editable2 only so less chance of false negatives.
            const qtip = `<div id="qtip-0" class="qtip qtip-default">${editable2}</div>`;
            SetupIFrameFromHtml(translationGroup + qtip);

            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;
            // Simulate the tool being visible so doesCurrentToolPlayAudio() returns true.
            (recording as unknown as { isShowing: boolean }).isShowing = true;

            // System under test
            await recording.handleNewPageReady();

            // Verification
            const firstDiv = getFrameElementById("page", "div1")!;
            expect(recording.getAudioCurrentElement()).toBe(firstDiv);
        });

        // BL-15300 regression (Problem 1): in a picture book (e.g. Moon and Cap) the recordable
        // text lives inside a canvas element (text over picture). Highlighting text makes that
        // canvas element the CanvasElementManager's "active" element, and that reference is not
        // cleared when you navigate to another page. When the next page loads,
        // setCurrentAudioElementToDefaultAsync used to see the (now detached) active canvas
        // element and return without highlighting anything -- so after a couple of page turns no
        // text was highlighted at all. The new page's own text must still get highlighted even
        // though a stale active canvas element from the previous page is still hanging around.
        it("still highlights text on a new page when a stale canvas element from the previous page is still active", async () => {
            setupDefaultApiResponses();
            mockCanvasElement.__enable();

            const canvasTextPage = (n: number) =>
                `<div id="page${n}"><div class="bloom-canvas"><div class="bloom-canvas-element"><div class="bloom-translationGroup"><div id="box${n}" class="bloom-editable bloom-visibility-code-on" data-audiorecordingmode="Sentence"><p><span id="s${n}" class="audio-sentence">Page ${n} text.</span></p></div></div></div></div></div>`;

            // Page 1: opens fine and highlights its text.
            SetupIFrameFromHtml(canvasTextPage(1));
            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            recording.recordingMode = RecordingMode.Sentence;
            await recording.handleNewPageReady();
            expect(getHighlightTexts(currentHighlightName)).toEqual([
                "Page 1 text.",
            ]);

            // Simulate the CanvasElementManager keeping page 1's canvas element active.
            const page1CanvasElement = getPageWindow()!.document.querySelector(
                ".bloom-canvas-element",
            ) as HTMLElement;
            mockCanvasElement.__setActiveForTest(page1CanvasElement);

            // Navigate to page 2: replacing the body detaches page 1's nodes, so the still-active
            // canvas element reference is now stale (not part of the current page).
            SetupIFrameFromHtml(canvasTextPage(2));
            getPseudoHighlightsRegistry().clear();
            // Real navigation nulls the current highlight during teardown.
            (
                recording as unknown as {
                    highlightedElement: HTMLElement | null;
                }
            ).highlightedElement = null;

            await recording.handleNewPageReady();

            // The regression: page 2's text should still be highlighted.
            expect(getHighlightTexts(currentHighlightName)).toEqual([
                "Page 2 text.",
            ]);
        });
    });

    describe("- initializeAudioRecordingMode()", () => {
        it("initializeAudioRecordingMode gets mode from current div if available (synchronous) (Text Box)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-translationGroup'><div class='bloom-editable' lang='en' data-audiorecordingmode='Sentence'>Sentence 1. Sentence 2.</div></div><div class='bloom-translationGroup'><div class='bloom-editable' data-test-preselect='true' lang='es' data-audiorecordingmode='TextBox'>Paragraph 2.</div></div>",
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            // Just to make sure that the code under test can read the current div at all.
            const currentTextBox = recording.getCurrentTextBox();
            expect(
                currentTextBox,
                "Could not find currentDiv. Possible test setup problem?",
            ).toBeTruthy();

            recording.initializeAudioRecordingMode();

            expect(recording.recordingMode).toBe(RecordingMode.TextBox);
        });

        it("initializeAudioRecordingMode gets mode from current div if available (synchronous) (Sentence)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-translationGroup'><div class='bloom-editable' lang='en' data-audiorecordingmode='TextBox'>Paragraph 1.</div></div><div class='bloom-translationGroup'><div class='bloom-editable' data-test-preselect='true' lang='es' data-audiorecordingmode='Sentence'>Paragraph 2.</div></div>",
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(
                currentDiv,
                "Could not find currentDiv. Possible test setup problem?",
            ).toBeTruthy();

            recording.initializeAudioRecordingMode();

            expect(recording.recordingMode).toBe(RecordingMode.Sentence);
        });

        it("initializeAudioRecordingMode gets mode from other divs on page as fallback (synchronous) (TextBox)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-translationGroup'><div class='audio-sentence bloom-editable' lang='en' data-audiorecordingmode='TextBox'>Paragraph 1</div></div><div class='bloom-translationGroup'><div class='bloom-editable' lang='es'><span id='id2' class='audio-sentence' data-test-preselect='true'>Paragraph 2.</span></div></div>",
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(
                currentDiv,
                "Could not find currentDiv. Possible test setup problem?",
            ).toBeTruthy();

            recording.initializeAudioRecordingMode();

            expect(recording.recordingMode).toBe(RecordingMode.TextBox);
        });

        it("initializeAudioRecordingMode gets mode from other divs on page as fallback (synchronous) (Sentence)", () => {
            // The 2nd div doesn't really look well-formed because we're trying to get the test to exercise some fallback cases
            // The first div doesn't look well-formed either but I want the test to exercise that it is getting it from the data-audiorecordingmode attribute not from any of the div's innerHTML markup.
            SetupIFrameFromHtml(
                "<div class='bloom-translationGroup'><div class='bloom-editable' lang='en' data-audiorecordingmode='Sentence'>Paragraph 1</div></div><div class='bloom-translationGroup'><div class='bloom-editable audio-sentence' data-test-preselect='true' lang='es'>Paragraph 2.</div></div>",
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(
                currentDiv,
                "Could not find currentDiv. Possible test setup problem?",
            ).toBeTruthy();

            recording.initializeAudioRecordingMode();

            expect(recording.recordingMode).toBe(RecordingMode.Sentence);
        });

        it("initializeAudioRecordingMode identifies 4.3 audio-sentences (synchronous)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-translationGroup'><div class='bloom-editable' lang='en'><span id='id1' class='audio-sentence'>Sentence 1.</span> <span id='id2' class='audio-sentence'>Sentence 2.</span></div></div><div class='bloom-translationGroup'><div class='bloom-editable' data-test-preselect='true' lang='es'>Paragraph 2.</div></div>",
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox;

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(
                currentDiv,
                "Could not find currentDiv. Possible test setup problem?",
            ).toBeTruthy();

            recording.initializeAudioRecordingMode();

            expect(recording.recordingMode).toBe(RecordingMode.Sentence);
        });
    });

    describe("- setupAndUpdateMarkupAsync()", () => {
        // BL-8425 The Jonah SuperBible comic book was found with data-audioRecordingMode, but no audio-sentences.
        // Not sure how that happened, but now the Talking Book Tool will repair this case.
        it("setupAndUpdateMarkupAsync() repairs faulty setup, TextBox div has no audio-sentence class", async () => {
            setupDefaultApiResponses();
            const textBox1 =
                "<div id='testId1' data-audioRecordingMode='TextBox' class='bloom-editable bloom-visibility-code-on' lang='en' tabindex='-1'><p>Sentence 1.</p></div>";
            const textBox2 =
                "<div id='testId2' data-audioRecordingMode='TextBox' class='bloom-editable bloom-visibility-code-on' lang='en' tabindex='-1'><p>Sentence 2.</p></div>";
            SetupIFrameFromHtml(
                `<div class='bloom-translationGroup'>${textBox1}${textBox2}</div>`,
            );

            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.TextBox;

            const currentDiv = recording
                .getPageDocBody()!
                .ownerDocument!.getElementById("testId1")!;

            // Make div1 active
            currentDiv.focus();

            // System under test
            await recording.setupAndUpdateMarkupAsync();

            // Verification
            expect(recording.recordingMode).toBe(RecordingMode.TextBox);

            // Verify that both the active and inactive divs are updated.
            const idsToCheck = ["testId1", "testId2"];

            idsToCheck.forEach((id: string) => {
                const div = recording
                    .getPageDocBody()!
                    .ownerDocument!.getElementById(id)!;

                expect(div).toHaveClass("audio-sentence");
            });
        });

        it("setupAndUpdateMarkupAsync() repairs faulty setup, Sentence div has no spans", async () => {
            setupDefaultApiResponses();
            SetupIFrameFromHtml(
                "<div class='bloom-translationGroup'><div id='testId' data-audioRecordingMode='Sentence' class='bloom-editable bloom-visibility-code-on' lang='en'><p>Sentence 1.</p></div></div>",
            );

            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.Sentence;

            const currentDiv = recording
                .getPageDocBody()!
                .ownerDocument!.getElementById("testId");

            await recording.setupAndUpdateMarkupAsync();

            expect(recording.recordingMode).toBe(RecordingMode.Sentence);
            expect(
                currentDiv!.querySelectorAll("span.audio-sentence").length,
            ).toBe(1);
        });
    });

    it("isRecordableDiv works", () => {
        setupDefaultApiResponses();
        const recording = new AudioRecording();

        const translationGroup = document.createElement("div");
        translationGroup.classList.add("bloom-translationGroup");
        document.body.appendChild(translationGroup);

        let elem = document.createElement("div");
        translationGroup.appendChild(elem);
        elem.classList.add("bloom-editable");
        expect(recording.isRecordableDiv(elem), "Case 1A: no text").toBe(false);

        elem.appendChild(document.createTextNode("Hello world"));
        expect(recording.isRecordableDiv(elem), "Case 1B: text").toBe(true);

        const parent = document.createElement("div");
        document.body.appendChild(parent);
        parent.classList.add("bloom-noAudio");
        parent.appendChild(elem);
        expect(
            recording.isRecordableDiv(elem),
            "Case 2: parent is no-audio",
        ).toBe(false);

        elem = document.createElement("div");
        document.body.appendChild(elem);
        elem.appendChild(document.createTextNode("Layout: Basic Image"));
        expect(
            recording.isRecordableDiv(elem),
            "Case 3: not recordable (no bloom-editable class)",
        ).toBe(false);

        const translationGroup2 = document.createElement("div");
        translationGroup2.classList.add("bloom-translationGroup");
        document.body.appendChild(translationGroup2);

        elem = document.createElement("div");
        translationGroup2.appendChild(elem);
        elem.style.display = "none";
        elem.classList.add("bloom-editable");
        elem.appendChild(document.createTextNode("Hello world"));
        expect(
            recording.isRecordableDiv(elem),
            "Case 4: Element not visible",
        ).toBe(false);
    });

    it("getCurrentText works", () => {
        setupDefaultApiResponses();
        SetupIFrameFromHtml(
            "<div data-test-preselect='true'>Hello world</div>",
        );

        const recording = new AudioRecording();
        setHighlightedElementFromDom(recording);
        expect(recording.getCurrentHighlight()).toBeTruthy();
        const returnedText = recording.getCurrentText();

        expect(returnedText).toBe("Hello world");
    });

    it("getAutoSegmentLanguageCode works", () => {
        setupDefaultApiResponses();
        SetupIFrameFromHtml(
            "<div data-test-preselect='true' lang='es'>Hello world</div>",
        );

        const recording = new AudioRecording();
        setHighlightedElementFromDom(recording);
        const returnedText = recording.getAutoSegmentLanguageCode();

        expect(returnedText).toBe("es");
    });

    it("extractFragmentsForAudioSegmentation works", () => {
        setupDefaultApiResponses();
        SetupIFrameFromHtml(
            "<div data-test-preselect='true' lang='es'>Sentence 1. Sentence 2.</div>",
        );

        const recording = new AudioRecording();
        setHighlightedElementFromDom(recording);
        const returnedFragmentIds: AudioTextFragment[] =
            recording.extractFragmentsAndSetSpanIdsForAudioSegmentation();

        expect(returnedFragmentIds.length).toBe(2);
        for (let i = 0; i < returnedFragmentIds.length; ++i) {
            expect(returnedFragmentIds[i].fragmentText).toBe(
                `Sentence ${i + 1}.`,
            );
            expect(returnedFragmentIds[i].id).toBeTruthy();
        }
    });

    it("extractFragmentsForAudioSegmentation handles duplicate sentences separately", () => {
        setupDefaultApiResponses();
        SetupIFrameFromHtml(
            "<div data-test-preselect='true' lang='en'>What color is the sky? Blue. What color is the ocean? Blue. Hello. Hello.</p></div>",
        );

        const recording = new AudioRecording();
        setHighlightedElementFromDom(recording);
        recording.recordingMode = RecordingMode.TextBox;
        const returnedFragmentIds: AudioTextFragment[] =
            recording.extractFragmentsAndSetSpanIdsForAudioSegmentation();

        expect(
            Object.keys(recording.__testonly__sentenceToIdListMap).length,
        ).toBe(4); // the number of distinct sentences

        // Check that the stored map actually maps back to the correct ID (even if there are duplicate sentences)
        expect(returnedFragmentIds.length).toBe(6); // the number of sentences
        for (let i = 0; i < returnedFragmentIds.length; ++i) {
            const fragmentText = returnedFragmentIds[i].fragmentText;
            const expectedId = returnedFragmentIds[i].id;
            const idList =
                recording.__testonly__sentenceToIdListMap[fragmentText];
            expect(idList[0], `Fragment ${i} (${fragmentText})`).toBe(
                expectedId,
            );

            idList.shift(); // The existing one is all done, move on to next one.
        }
    });

    it("normalizeText() works", () => {
        setupDefaultApiResponses();
        function testAllFormsMatch(
            rawForm: string, // Directly after typing text, immediately upon clicking AutoSegment, before anything is modified
            processingForm: string, // After clicking AutoSegment and the response is being proc, during MakeAudioSentenceElementsLeaf
            savedForm, // After saving the page.
        ) {
            expect(AudioRecording.normalizeText(rawForm)).toBe(
                AudioRecording.normalizeText(processingForm),
            );
            expect(AudioRecording.normalizeText(rawForm)).toBe(
                AudioRecording.normalizeText(savedForm),
            );
            // 3rd check is unnecessary because transitive property
        }
        testAllFormsMatch(
            "John 3:16 (NIV)\n\n\n",
            "John 3:16 (NIV)<br />",
            "John 3:16 (NIV)",
        );
        testAllFormsMatch(
            "\u00a0\u00a0\u00a0 In the beginning...",
            "\u00a0\u00a0\u00a0 In the beginning...",
            "&nbsp;&nbsp;&nbsp; In the beginning...",
        );
        // Test what happens when inserting <em> (italics) in the HTML version.  (This input corresponds to the text() version).
        testAllFormsMatch(
            "Title: The Cat in the Hat .",
            "Title:  The Cat in the Hat  .",
            "Title:  The Cat in the Hat  .",
        );
    });

    describe("- importRecordingAsync()", () => {
        function simulateBloomApiResponses(
            audioToCopyFilePath: string,
            bookPath: string,
        ) {
            vi.spyOn(axios, "get").mockImplementation((url: string) => {
                if (url.endsWith("fileIO/chooseFile")) {
                    return Promise.resolve({ data: audioToCopyFilePath });
                } else if (url.includes(kAnyRecordingApiUrl)) {
                    return Promise.resolve({ data: false });
                } else {
                    return Promise.reject("Fake 404 Error 2");
                }
            });

            vi.spyOn(axios, "post").mockImplementation((url: string) => {
                if (url.endsWith("fileIO/getSpecialLocation")) {
                    return Promise.resolve({ data: `${bookPath}/audio` });
                } else if (url.endsWith("fileIO/copyFile")) {
                    return Promise.resolve({});
                } else {
                    return Promise.reject("Fake 404 Error 3");
                }
            });
        }

        function encodeFilenameForHttpRequest(
            filename: string,
            baseName: string,
            encodedBaseName: string,
        ) {
            return replaceAll(filename, baseName, encodedBaseName)
                .replace(/ /g, "%20")
                .replace(/:/g, "%3A")
                .replace(/\//g, "%2F");
        }

        // Also Refer to BloomExe tests "CopyFile_InputWithSpecialChars_CompletesSuccessfully"
        it("importRecording() encodes special characters", async () => {
            // Setup
            const div1 =
                '<div class="bloom-editable audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox" id="div1"><p>One. Two. Three.</p></div>';
            SetupIFrameFromHtml(div1);

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.TextBox; // Should be the old state, toggleRecordingMode() will flip the state

            const baseName = "A`B~C!D@E#F$G%H^I&J(K)L-M_N=O+P[Q{R]S}T;U'V,W.X";
            const audioFilename = `${baseName} audio.mp3`;
            const audioToCopyFilePath = `${baseName} Folder/${audioFilename}`;
            const bookPath = `C:/${baseName} Collection/${baseName} Book`;
            simulateBloomApiResponses(audioToCopyFilePath, bookPath);

            // System under test
            await recording.importRecordingAsync();

            // Verification
            const encodedBaseName =
                "A%60B~C!D%40E%23F%24G%25H%5EI%26J(K)L-M_N%3DO%2BP%5BQ%7BR%5DS%7DT%3BU'V%2CW.X";
            // const encodedAudioToCopyFilePath =
            encodeFilenameForHttpRequest(
                audioToCopyFilePath,
                baseName,
                encodedBaseName,
            );
            const destPath = `${bookPath}/audio/div1.mp3`;
            // const encodedDestPath =
            encodeFilenameForHttpRequest(destPath, baseName, encodedBaseName);

            // this got more complicated... doesn't seem worth having a test track
            // the exact details of the request
            // expect(axios.post).toHaveBeenCalledWith(
            //     "/bloom/api/fileIO/copyFile",
            //     {
            //         from: encodedAudioToCopyFilePath,
            //         to: encodedDestPath
            //     }
            // );
        });
    });

    it("makeAudioSentenceElementsLeaf creates new ids for duplicate text", () => {
        const recording = new AudioRecording();

        const sent1 =
            '<span id="i00c41f76-0d90-41be-988d-084517eea47d" class="audio-sentence" recordingmd5="702edca0b2181c15d457eacac39de39b">This is a test!</span>';
        // The outer <p>...</p> is needed to get the right jquery object passed to the method.
        const elt1 = $($.parseHTML(`<p>${sent1}</p>`));
        expect(elt1.html()).toBe(sent1); // verify original state
        recording.makeAudioSentenceElementsLeafTest(elt1);
        expect(elt1.html()).toBe(sent1);
        const oldId = elt1.find(".audio-sentence").attr("id");
        expect(oldId).toBe("i00c41f76-0d90-41be-988d-084517eea47d");

        const sent2 = "This is a test!";
        const elt2 = $($.parseHTML(`<p>${sent2}</p>`));
        expect(elt2.html()).toBe(sent2); // verify original state
        recording.makeAudioSentenceElementsLeafTest(elt2);
        expect(elt2.html()).not.toBe(sent2);
        const newId = elt2.find(".audio-sentence").attr("id");
        expect(newId).not.toBe(oldId);
        expect(newId.length).toBeGreaterThan(35);
        expect(newId.length).toBeLessThan(38);
        expect(elt2.html().startsWith('<span id="')).toBe(true);
        expect(
            elt2
                .html()
                .endsWith('" class="audio-sentence">This is a test!</span>'),
        ).toBe(true);
    });

    it("makeAudioSentenceElementsLeaf preserves color markup", () => {
        const recording = new AudioRecording();

        const sent1 = '<span style="color:#ff1616;">One. Two. Three</span>.';
        // The outer <p>...</p> is needed to get the right jquery object passed to the method.
        const elt1$ = $($.parseHTML(`<p>${sent1}</p>`));
        expect(elt1$.html()).toBe(sent1); // verify original state
        recording.makeAudioSentenceElementsLeafTest(elt1$);
        expect(elt1$.text()).toBe("One. Two. Three.");
        const elt1 = elt1$.get(0);
        const sentences = Array.from(
            elt1.getElementsByClassName("audio-sentence"),
        );
        expect(sentences.length).toBe(3);
        const ids: Array<any> = sentences.map((s: HTMLElement) =>
            s.getAttribute("id"),
        );
        ids.forEach((id: any) => expect(id?.length).toBeGreaterThan(35));
        expect(ids[0]).not.toBe(ids[1]);
        expect(ids[0]).not.toBe(ids[2]);
        expect(ids[1]).not.toBe(ids[2]);

        sentences.forEach((s: any) => expect(s.parentElement).toBe(elt1));
        const colorSpans = Array.from(elt1.getElementsByTagName("span")).filter(
            (s: any) => s.getAttribute("style") === "color:#ff1616;",
        ) as HTMLElement[];
        expect(colorSpans.length).toBe(5);
        for (let i = 0; i < 3; i++) {
            expect(colorSpans[2 * i].parentElement).toBe(sentences[i]);
        }
        expect(colorSpans[0].innerText).toBe("One.");
        expect(colorSpans[2].innerText).toBe("Two.");
        expect(colorSpans[4].innerText).toBe("Three");
    });

    describe("- fixHighlighting()", () => {
        const scenarios: ("Check" | "Listen to whole page")[] = [
            "Check",
            "Listen to whole page",
        ];

        scenarios.forEach((scenario) => {
            it(`[${scenario}] doesn't change anything for two or fewer whitespace in split text box w/single highlight segment`, () => {
                const originalHtml =
                    '<div id="page1"><div id="box1" class="bloom-editable audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox"><p><span id="span1" class="bloom-highlightSegment">One Two&nbsp; Three</span></p></div></div>';
                SetupIFrameFromHtml(originalHtml);
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.parentElement!.outerHTML).toBe(originalHtml);
            });

            it(`[${scenario}] disables highlight on 3 or more whitespace in split text box w/single highlight segment`, () => {
                SetupIFrameFromHtml(
                    '<div id="page1"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox"><p><span id="span1" class="bloom-highlightSegment">One Two&nbsp; Three&nbsp;&nbsp; Four&nbsp;&nbsp;&nbsp; End</span></p></div></div></div>',
                );
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                const childSpan = box1.querySelector("span")!;
                expect(
                    childSpan.classList.contains("ui-disableHighlight"),
                    "missing ui-disableHighlight",
                ).toBe(true);

                expect(childSpan.innerHTML).toBe(
                    '<span class="ui-enableHighlight">One Two&nbsp; Three</span>&nbsp;&nbsp; <span class="ui-enableHighlight">Four</span>&nbsp;&nbsp;&nbsp; <span class="ui-enableHighlight">End</span>',
                );
            });

            it(`[${scenario}] disables highlight on 3 or more whitespace in split text box w/multiple highlight segments.`, () => {
                const html =
                    '<div id="page1"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox"><p><span id="span1" class="bloom-highlightSegment">One Two&nbsp; End1.</span><span id="span2" class="bloom-highlightSegment">Three&nbsp;&nbsp; End2.</span><span id="span3" class="bloom-highlightSegment">Four&nbsp;&nbsp;&nbsp; Five&nbsp;&nbsp;&nbsp;&nbsp; End3.</span></p></div></div></div>';
                SetupIFrameFromHtml(html);
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.innerHTML).toBe(
                    "<p>" +
                        '<span id="span1" class="bloom-highlightSegment">One Two&nbsp; End1.</span>' +
                        '<span id="span2" class="bloom-highlightSegment ui-disableHighlight"><span class="ui-enableHighlight">Three</span>&nbsp;&nbsp; <span class="ui-enableHighlight">End2.</span></span>' +
                        '<span id="span3" class="bloom-highlightSegment ui-disableHighlight"><span class="ui-enableHighlight">Four</span>&nbsp;&nbsp;&nbsp; <span class="ui-enableHighlight">Five</span>&nbsp;&nbsp;&nbsp;&nbsp; <span class="ui-enableHighlight">End3.</span></span>' +
                        "</p>",
                );
            });

            it(`[${scenario}] doesn't do anything on unsplit text box.`, () => {
                const originalHtml =
                    '<div id="box1" class="bloom-editable audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox"><p>One Two&nbsp; End1. Three&nbsp;&nbsp; End2. Four&nbsp;&nbsp;&nbsp; Five&nbsp;&nbsp;&nbsp;&nbsp; End3.</p></div>';
                SetupIFrameFromHtml(`<div id="page1">${originalHtml}</div>`);
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.outerHTML).toBe(originalHtml);
            });

            it(`[${scenario}] disables highlight on 3 or more whitespace in record-by-sentence box`, () => {
                const originalHtml =
                    '<div id="box1" class="bloom-editable bloom-visibility-code-on data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence" data-test-preselect="true">One Two&nbsp; End1.</span><span id="span2" class="audio-sentence">Three&nbsp;&nbsp; End2.</span><span id="span3" class="audio-sentence">Four&nbsp;&nbsp;&nbsp; Five&nbsp;&nbsp;&nbsp;&nbsp; End3.</span></p></div>';
                SetupIFrameFromHtml(
                    `<div id="page1"><div class='bloom-translationGroup'>${originalHtml}</div></div>`,
                );
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.innerHTML).toBe(
                    "<p>" +
                        '<span id="span1" class="audio-sentence" data-test-preselect="true">One Two&nbsp; End1.</span>' +
                        '<span id="span2" class="audio-sentence ui-disableHighlight"><span class="ui-enableHighlight">Three</span>&nbsp;&nbsp; <span class="ui-enableHighlight">End2.</span></span>' +
                        '<span id="span3" class="audio-sentence ui-disableHighlight"><span class="ui-enableHighlight">Four</span>&nbsp;&nbsp;&nbsp; <span class="ui-enableHighlight">Five</span>&nbsp;&nbsp;&nbsp;&nbsp; <span class="ui-enableHighlight">End3.</span></span>' +
                        "</p>",
                );
            });

            it(`[${scenario}] disables highlight on emphasized text`, () => {
                const originalHtml =
                    '<div class="bloom-translationGroup"><div id="box1" class="bloom-editable bloom-visibility-code-on data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence" data-test-preselect="true">T<em>hree&nbsp;&nbsp; End2.</em></span></p></div></div>';
                SetupIFrameFromHtml(`<div id="page1">${originalHtml}</div>`);
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.innerHTML).toBe(
                    "<p>" +
                        '<span id="span1" class="audio-sentence ui-disableHighlight" data-test-preselect="true"><span class="ui-enableHighlight">T</span><em><span class="ui-enableHighlight">hree</span>&nbsp;&nbsp; <span class="ui-enableHighlight">End2.</span></em></span>' +
                        "</p>",
                );
            });

            it(`[${scenario}] disables highlight for &ZeroWidthSpace; (\u200B)`, () => {
                const originalHtml =
                    '<div id="box1" class="bloom-editable bloom-visibility-code-on data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence" data-test-preselect="true">T<em>hree\u200B \u200BEnd2.</em></span></p></div>';
                SetupIFrameFromHtml(
                    `<div id="page1"><div class='bloom-translationGroup'>${originalHtml}</div></div>`,
                );
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.innerHTML).toBe(
                    "<p>" +
                        '<span id="span1" class="audio-sentence ui-disableHighlight" data-test-preselect="true"><span class="ui-enableHighlight">T</span><em><span class="ui-enableHighlight">hree</span>\u200B \u200B<span class="ui-enableHighlight">End2.</span></em></span>' +
                        "</p>",
                );
            });

            it(`[${scenario}] disables highlight for complex html from real user`, () => {
                SetupIFrameFromHtml(
                    `<div id="page1"><div class='bloom-translationGroup'>${getComplexHtmlFromUser()}</div></div>`,
                );
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );

                // Verification
                expect(box1.innerHTML).toBe(
                    getExpectedResultForComplexHtmlFromUser(),
                );
            });

            it(`[${scenario}] reverts fixHighlighting() in split text box.`, () => {
                const originalHtml =
                    '<div id="box1" class="bloom-editable audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox"><p><span id="span1" class="bloom-highlightSegment">One Two&nbsp; End1.</span><span id="span2" class="bloom-highlightSegment">Three&nbsp;&nbsp; End2.</span><span id="span3" class="bloom-highlightSegment">Four&nbsp;&nbsp;&nbsp; Five&nbsp;&nbsp;&nbsp;&nbsp; End3.</span></p></div>';
                SetupIFrameFromHtml(`<div id="page1">${originalHtml}</div>`);
                const box1 = getFrameElementById("page", "box1")!;

                const recording = new AudioRecording();
                recording.fixHighlighting(
                    scenario === "Check" ? box1 : undefined,
                );
                recording.revertFixHighlighting();

                // Verification
                expect(box1.outerHTML).toBe(originalHtml);
            });
        });
    });

    describe("- pseudo-element split highlights", () => {
        it("registers the current sentence highlight with pseudo-element highlights", async () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-editable" data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence" data-test-preselect="true">One.</span></p></div></div>',
            );

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            const setHighlightToAsync = (
                recording as unknown as {
                    setHighlightToAsync(args: {
                        newElement: Element;
                        shouldScrollToElement: boolean;
                        forceRedisplay?: boolean;
                    }): Promise<void>;
                }
            ).setHighlightToAsync.bind(recording);

            await setHighlightToAsync({
                newElement: getFrameElementById("page", "span1")!,
                shouldScrollToElement: false,
                forceRedisplay: true,
            });

            expect(getHighlightTexts(currentHighlightName)).toEqual(["One."]);
        });

        // BL-15300 regression: on page change the highlight was often missing. Cause: after the
        // page frame reloads, highlightedElement can be left pointing at a same-id node from the
        // previous (now detached) document. refreshHighlights reads the highlight registry from
        // that node's window (null for a detached document) and silently does nothing, so nothing
        // paints -- and isVisible()/isConnected don't catch it because the stale node is still
        // "connected" to its old document. ensureHighlight's reestablishCurrentHighlightIfNeeded
        // must detect the staleness, re-point at the live node, and re-register the highlight.
        it("re-registers the current highlight against the live page when highlightedElement is stale", () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-editable" data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence">One.</span></p></div></div>',
            );

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            recording.recordingMode = RecordingMode.Sentence;

            // Simulate the stale reference: a same-id element that is NOT in the live page
            // document (as happens after the page frame reloads to a new document).
            const pageDoc = (
                parent.window.document.getElementById(
                    "page",
                ) as HTMLIFrameElement
            ).contentDocument!;
            const stale = pageDoc.createElement("span");
            stale.id = "span1";
            stale.className = "audio-sentence";
            stale.textContent = "One.";
            (
                recording as unknown as { highlightedElement: HTMLElement }
            ).highlightedElement = stale;

            // Nothing registered yet, and the stale node is a different object than the live one.
            expect(getHighlightTexts(currentHighlightName)).toEqual([]);
            const liveSpan = getFrameElementById("page", "span1");
            expect(stale).not.toBe(liveSpan);

            (
                recording as unknown as {
                    reestablishCurrentHighlightIfNeeded(): void;
                }
            ).reestablishCurrentHighlightIfNeeded();

            // Healed: highlightedElement now points at the LIVE span, and the highlight paints.
            expect(recording.getAudioCurrentElement()).toBe(liveSpan);
            expect(getHighlightTexts(currentHighlightName)).toEqual(["One."]);
        });

        // BL-15300: the ensureHighlight loop (which reestablishCurrentHighlightIfNeeded drives)
        // keeps running for a few seconds after a page loads. If the toolbox is closed during that
        // window, handleToolHiding clears the highlight -- but the loop must NOT put it back. So
        // reestablish must be a no-op while the tool is not showing (isShowing false). Without this
        // guard the highlight stays stuck on the page after the toolbox is closed.
        it("does not re-establish the current highlight while the tool is not showing", () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-editable" data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence">One.</span></p></div></div>',
            );

            const recording = new AudioRecording();
            recording.recordingMode = RecordingMode.Sentence;
            (
                recording as unknown as {
                    highlightedElement: HTMLElement | null;
                }
            ).highlightedElement = getFrameElementById("page", "span1");

            const reestablish = (
                recording as unknown as {
                    reestablishCurrentHighlightIfNeeded(): void;
                }
            ).reestablishCurrentHighlightIfNeeded.bind(recording);

            // Tool hidden (e.g., toolbox just closed): reestablish must not re-add the highlight.
            (recording as unknown as { isShowing: boolean }).isShowing = false;
            expect(getHighlightTexts(currentHighlightName)).toEqual([]);
            reestablish();
            expect(getHighlightTexts(currentHighlightName)).toEqual([]);

            // Sanity/positive control: when the tool IS showing, it does establish the highlight
            // (so the assertion above is meaningful, not a no-op for some unrelated reason).
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            reestablish();
            expect(getHighlightTexts(currentHighlightName)).toEqual(["One."]);
        });

        it("clears pseudo-element highlights when entering show playback order mode", async () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div class="bloom-editable" data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence" data-test-preselect="true">One.</span></p></div></div></div>',
            );

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            const setHighlightToAsync = (
                recording as unknown as {
                    setHighlightToAsync(args: {
                        newElement: Element;
                        shouldScrollToElement: boolean;
                        forceRedisplay?: boolean;
                    }): Promise<void>;
                }
            ).setHighlightToAsync.bind(recording);

            await setHighlightToAsync({
                newElement: getFrameElementById("page", "span1")!,
                shouldScrollToElement: false,
                forceRedisplay: true,
            });

            expect(getHighlightTexts(currentHighlightName)).toEqual(["One."]);

            await recording.setShowPlaybackOrderMode(true);

            expect(getHighlightTexts(currentHighlightName)).toEqual([]);
            expect(getSplitHighlightTexts()).toEqual([[], [], []]);
        });

        it("uses only ui-enableHighlight descendants for current highlight when its own background is disabled", async () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-editable" data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence ui-disableHighlight"><span class="ui-enableHighlight">One</span>&nbsp;&nbsp; <span class="ui-enableHighlight">Two</span></span></p></div></div>',
            );

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            const setHighlightToAsync = (
                recording as unknown as {
                    setHighlightToAsync(args: {
                        newElement: Element;
                        shouldScrollToElement: boolean;
                        forceRedisplay?: boolean;
                    }): Promise<void>;
                }
            ).setHighlightToAsync.bind(recording);

            await setHighlightToAsync({
                newElement: getFrameElementById("page", "span1")!,
                shouldScrollToElement: false,
                forceRedisplay: true,
            });

            expect(getHighlightTexts(currentHighlightName)).toEqual([
                "One",
                "Two",
            ]);
        });

        it("sets default highlight colors when no user styles are configured", async () => {
            // Tests that setHighlightToAsync writes the CSS custom properties to the document
            // element. When no userModifiedStyles sheet is present the defaults (#febf00, black)
            // are used. Reading custom colors from the userModifiedStyles stylesheet requires
            // real CSS rule parsing that jsdom's iframe documents don't support.
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-editable" data-audiorecordingmode="Sentence"><p><span id="span1" class="audio-sentence" data-test-preselect="true">One.</span></p></div></div>',
            );

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            const setHighlightToAsync = (
                recording as unknown as {
                    setHighlightToAsync(args: {
                        newElement: Element;
                        shouldScrollToElement: boolean;
                        forceRedisplay?: boolean;
                    }): Promise<void>;
                }
            ).setHighlightToAsync.bind(recording);

            await setHighlightToAsync({
                newElement: getFrameElementById("page", "span1")!,
                shouldScrollToElement: false,
                forceRedisplay: true,
            });

            const pageDocument = (
                parent.window.document.getElementById(
                    "page",
                ) as HTMLIFrameElement
            ).contentDocument!;
            const documentStyle = pageDocument.documentElement.style;
            expect(
                documentStyle.getPropertyValue(
                    "--bloom-audio-current-highlight-background",
                ),
            ).toBe("#febf00");
            expect(
                documentStyle.getPropertyValue(
                    "--bloom-audio-current-highlight-color",
                ),
            ).toBe("black");
        });

        it("registers the split highlight color groups for the current text box", () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable audio-sentence bloom-postAudioSplit" data-test-preselect="true" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0 3.0"><p><span id="span1" class="bloom-highlightSegment">One.</span><span id="span2" class="bloom-highlightSegment">Two.</span><span id="span3" class="bloom-highlightSegment">Three.</span><span id="span4" class="bloom-highlightSegment">Four.</span></p></div></div></div>',
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.markAudioSplit();

            expect(getSplitHighlightTexts()).toEqual([
                ["One.", "Four."],
                ["Two."],
                ["Three."],
            ]);
        });

        it("clears split highlights while playback moves to an individual segment", async () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable audio-sentence bloom-postAudioSplit" data-test-preselect="true" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0"><p><span id="span1" class="bloom-highlightSegment">One.</span><span id="span2" class="bloom-highlightSegment">Two.</span></p></div></div></div>',
            );

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            recording.markAudioSplit();

            const setHighlightToAsync = (
                recording as unknown as {
                    setHighlightToAsync(args: {
                        newElement: Element;
                        shouldScrollToElement: boolean;
                    }): Promise<void>;
                }
            ).setHighlightToAsync.bind(recording);

            await setHighlightToAsync({
                newElement: getFrameElementById("page", "span1")!,
                shouldScrollToElement: false,
            });

            expect(getSplitHighlightTexts()).toEqual([[], [], []]);
        });

        it("keeps the current highlight when Speak clears a previous split state", async () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable audio-sentence bloom-postAudioSplit" data-test-preselect="true" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0"><p><span id="span1" class="bloom-highlightSegment">One.</span><span id="span2" class="bloom-highlightSegment">Two.</span></p></div></div></div>',
            );

            vi.spyOn(axios, "post").mockResolvedValue({});

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            recording.recordingMode = RecordingMode.TextBox;
            document.getElementById("audio-record")?.classList.add("expected");

            await recording.startRecordCurrentAsync();

            expect(getHighlightTexts(currentHighlightName).join("")).toContain(
                "One.",
            );
            expect(getSplitHighlightTexts()).toEqual([[], [], []]);
        });

        it("uses only ui-enableHighlight descendants when a segment disables its own background", () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div id="box1" class="bloom-editable audio-sentence bloom-postAudioSplit" data-test-preselect="true" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0"><p><span id="span1" class="bloom-highlightSegment">One.</span><span id="span2" class="bloom-highlightSegment ui-disableHighlight"><span class="ui-enableHighlight">Two</span>&nbsp;&nbsp; <span class="ui-enableHighlight">Three</span></span></p></div></div></div>',
            );

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            recording.markAudioSplit();

            expect(getSplitHighlightTexts()).toEqual([
                ["One."],
                ["Two", "Three"],
                [],
            ]);
        });

        // BL-15300 regression (Problem 2): after recording a whole text box and splitting it,
        // clearing the recording should return the text box to the yellow current highlight.
        // The split (blue) highlights must be removed AND the yellow current highlight
        // re-registered. In the old DOM-class model the yellow reappeared automatically once
        // bloom-postAudioSplit was removed from the .ui-audioCurrent box; with pseudo-element
        // highlights nothing re-registers it unless clearRecordingAsync asks for a refresh.
        it("restores the yellow current highlight when a split text-box recording is cleared", async () => {
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div id="div1" class="bloom-editable audio-sentence bloom-postAudioSplit" data-test-preselect="true" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0"><p><span id="span1" class="bloom-highlightSegment">One.</span> <span id="span2" class="bloom-highlightSegment">Two.</span></p></div></div></div>',
            );

            setupDefaultApiResponses();
            vi.spyOn(axios, "post").mockResolvedValue({});

            const recording = new AudioRecording();
            setHighlightedElementFromDom(recording);
            (recording as unknown as { isShowing: boolean }).isShowing = true;
            recording.recordingMode = RecordingMode.TextBox;

            // Simulate the post-split state: blue segment highlights are showing, no yellow.
            recording.markAudioSplit();
            expect(getSplitHighlightTexts()).toEqual([["One."], ["Two."], []]);
            expect(getHighlightTexts(currentHighlightName)).toEqual([]);

            recording.uiState.buttons.clear = Status.Enabled;
            await recording.clearRecordingAsync();

            // The blue split highlights should be gone...
            expect(getSplitHighlightTexts()).toEqual([[], [], []]);
            // ...and the whole text box should be highlighted yellow again.
            expect(getHighlightTexts(currentHighlightName).join("")).toContain(
                "One.",
            );
            expect(getHighlightTexts(currentHighlightName).join("")).toContain(
                "Two.",
            );
        });

        // BL-15300 regression (Problem 3): the whole-box-vs-first-sentence decision when
        // switching to Record by Sentence must be based on the current box's actual mode, not a
        // mode remembered from an earlier switch elsewhere in the (session-long) recorder. Here
        // the user first works with a by-sentence box, then navigates to a box recorded By Whole
        // Text Box, clears it, and switches to by-sentence. Only the FIRST sentence should be
        // highlighted -- previously the whole box was, because the box was never split into spans.
        it("highlights only the first sentence when switching a whole-text-box box to by-sentence after an earlier by-sentence switch", async () => {
            setupDefaultApiResponses();
            vi.spyOn(axios, "post").mockResolvedValue({});

            const recording = new AudioRecording();
            (recording as unknown as { isShowing: boolean }).isShowing = true;

            // 1) Earlier in the session: a by-sentence text box; end on Sentence mode.
            SetupIFrameFromHtml(
                '<div id="page1"><div class="bloom-translationGroup"><div id="boxA" class="bloom-editable" data-test-preselect="true" data-audiorecordingmode="Sentence"><p><span id="a1" class="audio-sentence">Alpha one.</span></p></div></div></div>',
            );
            setHighlightedElementFromDom(recording);
            recording.recordingMode = RecordingMode.Sentence;
            await recording.setRecordingModeAsync(RecordingMode.Sentence);

            // 2) Navigate to a page whose box was recorded By Whole Text Box, and let the tool
            //    (re)derive the mode for the current box the way handleNewPageReady does.
            SetupIFrameFromHtml(
                '<div id="page2"><div class="bloom-translationGroup"><div id="boxB" class="bloom-editable audio-sentence" data-test-preselect="true" data-audiorecordingmode="TextBox"><p>First sentence. Second sentence.</p></div></div></div>',
            );
            getFrameElementById("page", "boxB")!.setAttribute(
                "recordingmd5",
                "fakeMd5",
            );
            setHighlightedElementFromDom(recording);
            recording.initializeAudioRecordingMode();
            // Sanity: the current box's mode is correctly read as TextBox.
            expect(recording.recordingMode).toBe(RecordingMode.TextBox);

            // 3) Clear the recording, then switch to Record by Sentence.
            recording.uiState.buttons.clear = Status.Enabled;
            await recording.clearRecordingAsync();
            await recording.setRecordingModeAsync(RecordingMode.Sentence);

            expect(getHighlightTexts(currentHighlightName)).toEqual([
                "First sentence.",
            ]);
        });
    });
});

function StripEmptyClasses(html) {
    // Because after running removeAttr, it leaves class="" in the HTML
    return html.replace(/ class=""/g, "");
}

// eslint-disable-next-line @typescript-eslint/no-unused-vars
function StripAllIds(html) {
    // Note: add the "g" (global) flag to the end of the search setting if you want to replace all instead.
    return html.replace(/ id="[^"]*"/g, "").replace(/ id=""/g, "");
}

export function StripAllGuidIds(html) {
    // Note: add the "g" (global) flag to the end of the search setting if you want to replace all instead.
    return html
        .replace(
            / id="i?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"/g,
            "",
        )
        .replace(/ id=""/g, "");
}

export function StripRecordingMd5(html: string): string {
    return html.replace(/ recordingmd5="[0-9A-Za-z]*"/g, "");
}

// Adds the iframe into the parent window.
// Returns a Promise which resolves when the iframe is finished loading and its contentDocument is safe to use.
async function SetupIFrameAsync(id = "page"): Promise<HTMLIFrameElement> {
    let iframe: HTMLIFrameElement;
    const element = parent.window.document.getElementById(id);
    if (element) {
        if (element.tagName.toLowerCase() !== "iframe") {
            throw new Error(
                `An element with the id ${id} already exists, but it is a ${element.tagName} not an iframe.`,
            );
        } else {
            iframe = element as HTMLIFrameElement;
            if (iframe.contentDocument!.readyState === "complete") {
                return iframe;
            } else {
                throw new Error(
                    "Not implemented exception: An iframe with that id already exists, but is loading.",
                );
                // Enhance: I guess you could do setTimeouts and wait until the readyState is complete.
                // If so, then you resolve the promise.
                // If not, then you recycle the setTimeout
            }
        }
    }

    iframe = parent.window.document.createElement("iframe");
    parent.window.document.body.appendChild(iframe);
    iframe.id = id;
    iframe.name = id;
    // Use about:blank but we'll override src later for tests that need specific URLs
    iframe.src = "about:blank";

    // It needs to be asynchronous because an iframe may not be valid to use until its onload is called.
    // Notably, it's contentDocument will be re-written during the loading process... changes made before onload() finishes (asynchronously)
    // can get wiped out.
    return new Promise<HTMLIFrameElement>((resolve) => {
        iframe.onload = () => {
            resolve(iframe);
        };
    });
}

// bodyContentHtml should not contain HTML or Body tags. It should be the innerHtml of the body
// It might look something like this: <div data-test-preselect="true">Hello world</div>
export function SetupIFrameFromHtml(bodyContentHtml: string, id = "page") {
    const iframe = <HTMLIFrameElement>parent.window.document.getElementById(id);

    // In jsdom, readyState might not always be "complete" but the document is still usable
    // Only check that contentDocument exists
    if (!iframe.contentDocument) {
        throw new Error(
            "Possible setup error: IFrame's contentDocument is null!",
        );
    }

    // Ensure the iframe has a body element
    if (!iframe.contentDocument.body) {
        iframe.contentDocument.write("<html><body></body></html>");
        iframe.contentDocument.close();
    }

    iframe.contentDocument.body.innerHTML = bodyContentHtml;

    // Apply polyfills to the iframe's content document (jsdom creates new prototypes for each document)
    applyPolyfillsToIframe(iframe);
}

// Apply necessary polyfills to iframe content documents
function applyPolyfillsToIframe(iframe: HTMLIFrameElement) {
    const iframeWindow = iframe.contentWindow;
    if (!iframeWindow) return;

    // Polyfill innerText for jsdom in iframe context
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const win = iframeWindow as any;
    [
        win.HTMLElement.prototype,
        win.Element.prototype,
        win.Node.prototype,
    ].forEach((proto) => {
        if (!Object.getOwnPropertyDescriptor(proto, "innerText")) {
            Object.defineProperty(proto, "innerText", {
                get() {
                    const text = this.textContent;
                    return text !== null && text !== undefined ? text : "";
                },
                set(value) {
                    this.textContent = value;
                },
                configurable: true,
            });
        }
    });

    // Mock scrollIntoView in iframe context
    if (!win.HTMLElement.prototype.scrollIntoView) {
        win.HTMLElement.prototype.scrollIntoView = vi.fn();
    }
}

export async function setupForAudioRecordingTests() {
    const iframe = await SetupIFrameAsync();

    // Keep iframe src as about:blank to avoid jsdom trying to load a file
    iframe.src = "about:blank";

    // initializeTalkingBookToolAsync will call this endpoint; if we don't set up a
    // mock reply, it will throw an error and the whole test suite will fail.
    mockReplies[
        "features/status?featureName=WholeTextBoxAudio&forPublishing=false"
    ] = {
        data: {
            enabled: true,
        },
    };

    await initializeTalkingBookToolAsync();

    // Mock urlPrefix to return the correct base URL for tests
    // The real implementation constructs from iframe.src, but in tests iframe.src is "about:blank"
    // Real format: "/bloom/api/audio/wavFile?id=" + bookFolderUrl + "audio/"
    // In tests, we simulate the full path as if bookFolderUrl was "http://localhost:63315/bloom/C%23Injection/"
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    vi.spyOn(theOneAudioRecorder as any, "urlPrefix").mockReturnValue(
        "http://localhost:63315/bloom/api/audio/wavFile?id=audio/",
    );
}

export function StripPlayerSrcNoCacheSuffix(url: string): string {
    const index = url.lastIndexOf("&");
    if (index < 0) {
        return url;
    }

    return url.substring(0, index);
}

export function getFrameElementById(
    frameId: string,
    elementId: string,
): HTMLElement | null {
    const frame = parent.window.document.getElementById(frameId);
    if (!frame) {
        return null;
    }

    return (frame as HTMLIFrameElement).contentDocument!.getElementById(
        elementId,
    );
}

// Replaces all occurrences of pattern {within} {input} with {replacement}
function replaceAll(input: string, pattern: string, replacement: string) {
    // If pattern has punctuation, and you want to replace all,
    // then you need to create a regex version of the pattern,
    // except creating the regex version of the pattern isn't trivial if the pattern has punctuation...
    // How can it be so convoluted to do such a simple task?
    const escapedPattern = escapeRegExp(pattern);
    return input.replace(new RegExp(escapedPattern, "g"), replacement);
}

function escapeRegExp(regexPattern) {
    return regexPattern.replace(/[-\/\\^$*+?.()|[\]{}]/g, "\\$&");
}

// Actually, when we got it (BL-13428), it was even worse. It had leftover enableHighlight and disableHighlight classes
// which hadn't gotten cleaned up due to a bug.
function getComplexHtmlFromUser() {
    return `<div id="box1" class="bloom-editable normal-style bloom-content1 bloom-visibility-code-on" lang="es" style="min-height: 24px;" tabindex="0" spellcheck="false" role="textbox" aria-label="false" contenteditable="true" data-audiorecordingmode="Sentence" data-languagetipcontent="Spanish">
    <p><strong><span style="color:#FFFFFF;">&nbsp; &nbsp; &nbsp;</span></strong></p>
    <p></p>
    <p></p>
    <p></p>
    <p></p>
    <p></p>
    <p><span id="i9da9787c-53f9-4e22-831a-9a427cd8b928" class="audio-sentence" recordingmd5="64aaf12b2d884f144a6708b77b4d61cc" data-duration="4.388571">&nbsp; &nbsp; &nbsp; &nbsp; &nbsp;<span style="color:#FFFFFF;">&nbsp; &nbsp; <strong>&nbsp;Mientras navegaban,&nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp;</strong></span><strong><span style="color:#FFFFFF;">Jesús se quedó&nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp;</span></strong><strong><span style="color:#FFFFFF;">profundamente dormido.<span class="bloom-audio-split-marker"></span></span></strong></span></p>
    <p><span id="i2bff1986-70df-4539-8fbd-be90babc9057" class="audio-sentence" recordingmd5="f9f36fe0a9162025c3a64a8a8bf703e5" data-duration="3.291429"><strong>&nbsp; &nbsp; &nbsp; &nbsp; &nbsp;​ ​&nbsp; &nbsp;​ <span style="color:#FFFFFF;">De pronto, una gran&nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; </span></strong><strong><span style="color:#FFFFFF;">tormenta se desató.</span></strong></span><strong><span style="color:#FFFFFF;">&nbsp;</span></strong></p>
</div>`;
}
function getExpectedResultForComplexHtmlFromUser() {
    return `
    <p><strong><span style="color:#FFFFFF;">&nbsp; &nbsp; &nbsp;</span></strong></p>
    <p></p>
    <p></p>
    <p></p>
    <p></p>
    <p></p>
    <p><span id="i9da9787c-53f9-4e22-831a-9a427cd8b928" class="audio-sentence ui-disableHighlight" recordingmd5="64aaf12b2d884f144a6708b77b4d61cc" data-duration="4.388571">&nbsp; &nbsp; &nbsp; &nbsp; &nbsp;<span style="color:#FFFFFF;">&nbsp; &nbsp; <strong><span class="ui-enableHighlight">&nbsp;Mientras navegaban,</span>&nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp;</strong></span><strong><span style="color:#FFFFFF;"><span class="ui-enableHighlight">Jesús se quedó</span>&nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp;</span></strong><strong><span style="color:#FFFFFF;"><span class="ui-enableHighlight">profundamente dormido.</span><span class="bloom-audio-split-marker"></span></span></strong></span></p>
    <p><span id="i2bff1986-70df-4539-8fbd-be90babc9057" class="audio-sentence ui-disableHighlight" recordingmd5="f9f36fe0a9162025c3a64a8a8bf703e5" data-duration="3.291429"><strong>&nbsp; &nbsp; &nbsp; &nbsp; &nbsp;​ ​&nbsp; &nbsp;​ <span style="color:#FFFFFF;"><span class="ui-enableHighlight">De pronto, una gran</span>&nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; </span></strong><strong><span style="color:#FFFFFF;"><span class="ui-enableHighlight">tormenta se desató.</span></span></strong></span><strong><span style="color:#FFFFFF;">&nbsp;</span></strong></p>
`;
}
