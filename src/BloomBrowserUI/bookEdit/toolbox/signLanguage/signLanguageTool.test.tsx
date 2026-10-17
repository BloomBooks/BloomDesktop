import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../../utils/reactRender";
import {
    createEditTimeMarkupFilter,
    withoutVideoTimestamp,
} from "../../pageHistory/editTimeMarkup";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import { PageHistory } from "../../pageHistory/pageHistoryTypes";
import { SignLanguageTool, SignLanguageToolControls } from "./signLanguageTool";

type ResponseCallback = (response: { data: string }) => void;

// What the tests put in the page frame and the root window, and what the tool asked Bloom for:
// the callback of the request that sends a recording, the listeners of the web socket, and the
// function that the progress dialog calls with an imported video.
const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    recordedVideoReply: undefined as ResponseCallback | undefined,
    posts: [] as string[],
    socketListeners: [] as ((event: object) => void)[],
    applyImport: undefined as ((importedPath?: string) => void) | undefined,
}));
// The tool reaches the page history through the page frame's exports (pageHistoryAccess.ts),
// and the import's progress dialog through the root window's.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history
            ? {
                  pageHistory: () => frames.history,
                  getTheOneCanvasElementManager: () => undefined,
              }
            : null,
    getWorkspaceBundleExports: () => ({
        processVideoAndShowProgressDialog: (
            _sourcePath: string,
            onImported: (importedPath?: string) => void,
        ) => (frames.applyImport = onImported),
    }),
}));
// Bloom answers no request for a video's statistics or whether its file exists; the tests answer
// the recording's request themselves.
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    get: () => undefined,
    getWithConfig: () => undefined,
    post: (url: string) => {
        frames.posts.push(url);
    },
    postDataWithConfig: (
        url: string,
        _data: unknown,
        _config: unknown,
        reply: ResponseCallback,
    ) => {
        frames.posts.push(url);
        if (url === "signLanguage/recordedVideo")
            frames.recordedVideoReply = reply;
    },
}));
vi.mock("../../../utils/WebSocketManager", () => ({
    default: {
        addListener: (_clientContext: string, listener: () => void) =>
            frames.socketListeners.push(listener),
        removeListener: (_clientContext: string, listener: () => void) =>
            (frames.socketListeners = frames.socketListeners.filter(
                (l) => l !== listener,
            )),
        notifyReady: (_clientContext: string, ready: () => void) => ready(),
    },
}));

/** A MediaRecorder that records nothing, and whose stop() leaves onstop for the test to call. */
class FakeMediaRecorder {
    public static latest: FakeMediaRecorder | undefined;
    public ondataavailable: (e: { data: Blob }) => void;
    public onstop: () => void;
    public stopped = false;
    public constructor() {
        FakeMediaRecorder.latest = this;
    }
    public started = false;
    public start(): void {
        this.started = true;
    }
    public stop(): void {
        this.stopped = true;
    }
}

const kVideoUrl = "video/abc.mp4";

// The tool lives in the toolbox frame, and reaches the page through the iframe with the id
// "page" (getPageIframeBody in utils/shared.ts), so the tests make that iframe here and attach
// the input transactions with this window, where the tool renders, as one of the other windows.
// As in CanvasToolControls.test.tsx, the user's events are the ones in userEvents, and a test
// says where the browser's task ends by calling finishTask().
describe("SignLanguageToolControls and the page history", () => {
    let history: PageHistory;
    let page: HTMLElement;
    let videoContainer: HTMLElement;
    let source: HTMLSourceElement;
    let container: HTMLDivElement;
    let controls: SignLanguageToolControls;
    let detach: () => void;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];
    let onUnmarkedChange: ReturnType<typeof vi.fn>;

    beforeEach(() => {
        frames.posts = [];
        frames.socketListeners = [];
        frames.recordedVideoReply = undefined;
        frames.applyImport = undefined;
        const pageFrame = document.createElement("iframe");
        pageFrame.id = "page";
        document.body.appendChild(pageFrame);
        const pageDocument = pageFrame.contentDocument!;
        // As vitest.setup.ts does for this window: jsdom does not implement loading a video.
        (
            pageFrame.contentWindow as unknown as typeof window
        ).HTMLMediaElement.prototype.load = () => undefined;
        pageDocument.body.innerHTML = `<div class="bloom-page">
            <div class="bloom-videoContainer bloom-selected">
                <video><source src="${kVideoUrl}"></video>
            </div>
        </div>`;
        page = pageDocument.querySelector(".bloom-page")!;
        videoContainer = page.querySelector(".bloom-videoContainer")!;
        source = page.querySelector("source")!;
        onUnmarkedChange = vi.fn();
        // The filter that the Edit tab's history has.
        history = createPageHistory({
            root: page,
            onUnmarkedChange,
            filter: createEditTimeMarkupFilter(),
        });
        frames.history = history;
        userEvents = new WeakSet();
        endOfTask = [];
        detach = attachInputTransactions(
            history,
            pageFrame.contentWindow!,
            [window],
            {
                isFromUser: (event) => userEvents.has(event),
                afterCurrentTask: (fn) => endOfTask.push(fn),
            },
        );
        container = document.createElement("div");
        document.body.appendChild(container);
        act(() => {
            renderRoot(
                <SignLanguageToolControls ref={(c) => (controls = c!)} />,
                container,
            );
        });
        // What getVideoStatsFromFile sets when Bloom has read the selected video's file.
        act(() =>
            controls.setState({
                haveRecording: true,
                videoInfoLoaded: true,
                videoStatistics: {
                    duration: "10.0",
                    fileSize: "",
                    frameSize: "",
                    framesPerSecond: "",
                    megabitsPerSecond: "",
                    hasAudio: "false",
                    fileFormat: "",
                    startSeconds: "0.0",
                    endSeconds: "0.0",
                    aspectRatio: "",
                },
            }),
        );
    });

    afterEach(() => {
        act(() => unmountRoot(container));
        detach();
        history.dispose();
        frames.history = undefined;
        vi.unstubAllGlobals();
        document.body.innerHTML = "";
    });

    function finishTask(): void {
        while (endOfTask.length > 0) endOfTask.shift()!();
    }

    function mouse(target: Element, type: string, buttons: number): void {
        const event = new MouseEvent(type, {
            bubbles: true,
            cancelable: true,
            buttons,
        });
        userEvents.add(event);
        target.dispatchEvent(event);
    }

    /** A click by the user: the press, then the release as one task, and the end of that task. */
    function click(target: Element): void {
        act(() => {
            mouse(target, "pointerdown", 1);
            mouse(target, "mousedown", 1);
        });
        act(() => {
            mouse(target, "pointerup", 0);
            mouse(target, "mouseup", 0);
            mouse(target, "click", 0);
            finishTask();
        });
    }

    /** A key press by the user on target, and the end of its task. */
    function press(target: Element, key: string, keyCode: number): void {
        act(() => {
            const event = new KeyboardEvent("keydown", {
                key,
                bubbles: true,
                cancelable: true,
            });
            // rc-slider reads only keyCode, which jsdom's KeyboardEvent leaves at 0.
            Object.defineProperty(event, "keyCode", { value: keyCode });
            userEvents.add(event);
            target.dispatchEvent(event);
            finishTask();
        });
    }

    function getButton(id: string): Element {
        const button = container.querySelector(`#${id}`);
        if (!button) fail(`The tool did not render #${id}.`);
        return button;
    }

    /**
     * A handle of the trim slider. With no handle focused, rc-slider's Range moves the one that
     * moved last, which at first is the handle for the end of the video.
     */
    function getHandle(): Element {
        const handle = container.querySelector(
            ".videoTrimSlider [role='slider']",
        );
        if (!handle) fail("The tool did not render the trim slider.");
        return handle;
    }

    /** The file that the page's video names, without the parameter that only reloads it. */
    function videoOnPage(): string | undefined {
        const src = page.querySelector("video source")?.getAttribute("src");
        return src ? withoutVideoTimestamp(src) : undefined;
    }

    async function makeEarlierStep(): Promise<void> {
        await history.doTransaction("Test.EarlierStep", () =>
            page.setAttribute("data-earlier", "true"),
        );
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
    }

    async function undo(): Promise<void> {
        await act(async () => {
            await history.undo();
        });
    }

    async function redo(): Promise<void> {
        await act(async () => {
            await history.redo();
        });
    }

    /**
     * The change from kVideoUrl to newVideo is one step with descriptionL10nKey, above the earlier
     * step: undo puts back kVideoUrl, redo newVideo, and the earlier step still undoes after it.
     */
    async function expectOneStepAboveEarlierStep(
        descriptionL10nKey: string,
        newVideo: string | undefined,
    ): Promise<void> {
        expect(videoOnPage()).toBe(newVideo);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(descriptionL10nKey);
        expect(onUnmarkedChange.mock.calls).toEqual([]);

        await undo();
        expect(videoOnPage()).toBe(kVideoUrl);
        expect(videoContainer.classList).not.toContain("bloom-noVideoSelected");
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
        expect(history.redoDescriptionL10nKey).toBe(descriptionL10nKey);

        await redo();
        expect(videoOnPage()).toBe(newVideo);

        await undo();
        await undo();
        expect(page.hasAttribute("data-earlier")).toBe(false);
        expect(videoOnPage()).toBe(kVideoUrl);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    }

    it("ends the history for a change from the toolbox that is recorded", async () => {
        // The check that the other tests' "the earlier step survives" can fail.
        await makeEarlierStep();
        const handle = getHandle();
        handle.addEventListener("keydown", () =>
            page.setAttribute("data-unwrapped", "true"),
        );

        press(handle, "ArrowLeft", 37);

        expect(page.getAttribute("data-unwrapped")).toBe("true");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes no step for trimming, and keeps the earlier step", async () => {
        await makeEarlierStep();

        press(getHandle(), "ArrowLeft", 37);

        const trimmedSrc = `${kVideoUrl}#t=0.0,9.9`;
        expect(source.getAttribute("src")).toBe(trimmedSrc);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
        expect(onUnmarkedChange.mock.calls).toEqual([]);

        await undo();
        expect(page.hasAttribute("data-earlier")).toBe(false);
        expect(source.getAttribute("src")).toBe(trimmedSrc);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes Delete one step, which takes the video off the page without asking Bloom", async () => {
        await makeEarlierStep();

        click(getButton("videoDelete"));

        expect(page.querySelector("video")).toBeNull();
        expect(videoContainer.classList).toContain("bloom-noVideoSelected");
        expect(frames.posts).toEqual([]);
        await expectOneStepAboveEarlierStep(
            "EditTab.Toolbox.SignLanguage.DeleteVideo",
            undefined,
        );
    });

    /** Start a recording as the record button's countdown does, and stop it with a click. */
    function recordAndStop(): FakeMediaRecorder {
        vi.stubGlobal("MediaRecorder", FakeMediaRecorder);
        // The camera's stream, as turnOnVideo gets it, and the camera that onstop turns on again.
        const stream = { getVideoTracks: () => [], getAudioTracks: () => [] };
        vi.stubGlobal("navigator", {
            mediaDevices: { getUserMedia: () => new Promise(() => {}) },
        });
        const privateControls = controls as unknown as {
            videoStream: object;
            startRecording: () => void;
        };
        privateControls.videoStream = stream;
        act(() => {
            controls.setState({ stateClass: "recording", recording: true });
            privateControls.startRecording();
        });

        const recorder = FakeMediaRecorder.latest!;
        expect(recorder.started).toBe(true);

        click(getButton("videoToggleRecording"));

        expect(recorder.stopped).toBe(true);
        // The recorder's stop event comes in a later task.
        act(() => recorder.onstop());
        expect(frames.posts).toEqual(["signLanguage/recordedVideo"]);
        return recorder;
    }

    it("makes a recording one step when the reply puts it on the page", async () => {
        await makeEarlierStep();

        recordAndStop();
        // While C# processes the recording, the user's changes are steps of their own.
        expect(history.hasOpenTransaction).toBe(false);
        expect(videoOnPage()).toBe(kVideoUrl);

        act(() => frames.recordedVideoReply!({ data: "video/new.mp4" }));

        await expectOneStepAboveEarlierStep(
            "EditTab.Toolbox.SignLanguage.Recording",
            "video/new.mp4",
        );
    });

    it("keeps what the user does while a recording is processed, when its request fails", async () => {
        await makeEarlierStep();
        recordAndStop();

        await history.doTransaction("Test.Typing", () =>
            page.setAttribute("data-typed", "true"),
        );
        // The request fails: no reply ever comes.

        expect(page.getAttribute("data-typed")).toBe("true");
        expect(videoOnPage()).toBe(kVideoUrl);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe("Test.Typing");
        await undo();
        expect(page.hasAttribute("data-typed")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
    });

    it("writes nothing for a recording whose container left the page before the reply", async () => {
        recordAndStop();
        await history.doTransaction("Test.RemoveContainer", () =>
            videoContainer.remove(),
        );

        act(() => frames.recordedVideoReply!({ data: "video/new.mp4" }));

        expect(source.getAttribute("src")).toBe(kVideoUrl);
        expect(history.undoDescriptionL10nKey).toBe("Test.RemoveContainer");
        expect(onUnmarkedChange.mock.calls).toEqual([]);
        // The tool has left "processing" all the same.
        expect(controls.state.stateClass).toBe("idle");
    });

    it("makes applying an imported video one step", async () => {
        await makeEarlierStep();

        click(getButton("videoImport"));
        expect(frames.posts).toEqual(["signLanguage/chooseVideo"]);
        // C# sends the chosen file, then the progress dialog processes it; both answers come in
        // later tasks.
        act(() =>
            frames.socketListeners.forEach((listener) =>
                listener({
                    id: "chooseVideo-results",
                    success: true,
                    path: "C:/videos/mine.mp4",
                }),
            ),
        );
        if (!frames.applyImport) fail("The import showed no progress dialog.");
        expect(videoOnPage()).toBe(kVideoUrl);
        act(() => frames.applyImport!("video/imported.mp4"));

        await expectOneStepAboveEarlierStep(
            "EditTab.Toolbox.SignLanguage.ImportVideo",
            "video/imported.mp4",
        );
    });

    it("no longer shows the page's video again after an undo once the tool has left the page", async () => {
        const tool = new SignLanguageTool();
        (
            tool as unknown as { reactControls: SignLanguageToolControls }
        ).reactControls = controls;
        // The camera that newPageReady turns on for the selected video container.
        vi.stubGlobal("navigator", {
            mediaDevices: { getUserMedia: () => new Promise(() => {}) },
        });
        act(() => tool.newPageReady());
        const refresh = vi
            .spyOn(controls, "refreshAfterVideoChange")
            .mockImplementation(() => {});
        await makeEarlierStep();
        await undo();
        // The check that the tool listens while it is on the page.
        expect(refresh).toHaveBeenCalledTimes(1);

        act(() => tool.detachFromPage());
        await redo();

        expect(page.getAttribute("data-earlier")).toBe("true");
        expect(refresh).toHaveBeenCalledTimes(1);
    });

    it("makes no step for an import that is cancelled", async () => {
        await makeEarlierStep();
        click(getButton("videoImport"));
        act(() =>
            frames.socketListeners.forEach((listener) =>
                listener({
                    id: "chooseVideo-results",
                    success: true,
                    path: "C:/videos/mine.mp4",
                }),
            ),
        );

        act(() => frames.applyImport!());

        expect(videoOnPage()).toBe(kVideoUrl);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
    });
});
