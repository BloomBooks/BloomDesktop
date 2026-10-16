// Tests that choosing a new video for a canvas element fits the element to the video's shape as
// part of the import's undo step, with a real page history, the real edit-time markup filter, and
// the real canvas element manager wired to the history as in the Edit tab. Selecting the element
// afterwards must write nothing.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Selecting an element
// only asks Comical to activate it, draw the bubbles again, find the element's family, and read
// its bubble style and tails; these canvas elements have none.
vi.mock("comicaljs", () => ({
    Bubble: class {
        public static getBubbleSpec() {
            return { style: "none", tails: [] };
        }
        public getBubbleSpec() {
            return { style: "none", tails: [] };
        }
    },
    Node: vi.fn(),
    Parser: vi.fn(),
    BubbleSpec: {},
    TailSpec: {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
        startEditing: () => {},
        findAncestors: () => [],
    },
}));

// What the test puts in the page frame and the root window, and what the import asked Bloom for:
// the requests, the listeners of the web socket, and the function that the progress dialog calls
// with an imported video.
const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    posts: [] as string[],
    socketListeners: [] as ((event: object) => void)[],
    applyImport: undefined as ((importedPath?: string) => void) | undefined,
}));
vi.mock("../workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../workspaceFrames")>()),
    getEditablePageBundleExports: () => ({
        getTheOneCanvasElementManager: () => theOneCanvasElementManager,
        // Like the real one, this throws when no history has been started.
        pageHistory: () => {
            if (!frames.history) throw new Error("No page history");
            return frames.history;
        },
    }),
    getWorkspaceBundleExports: () => ({
        processVideoAndShowProgressDialog: (
            _sourcePath: string,
            onImported: (importedPath?: string) => void,
        ) => (frames.applyImport = onImported),
    }),
}));
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    post: (url: string) => {
        frames.posts.push(url);
    },
    postThatMightNavigate: (url: string) => {
        frames.posts.push(url);
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

// These imports deliberately come after the vi.mock calls above, so that the module graph they
// pull in gets the stubbed modules.
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "./CanvasElementManager";
import {
    listenToPageHistory,
    whenNoRequestPageContentDelays,
} from "../bloomEditing";
import { doVideoCommand } from "../bloomVideo";
import { createPageHistory } from "../../pageHistory/pageHistory";
import {
    createEditTimeMarkupFilter,
    withoutVideoTimestamp,
} from "../../pageHistory/editTimeMarkup";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const kOldVideo = "video/old.mp4";
const kNewVideo = "video/new.mp4";
const kChooseVideo = "EditTab.Toolbox.ComicTool.Options.ChooseVideo";

let page: HTMLElement;
let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn>;
let videoElement: HTMLElement;
let video: HTMLVideoElement;
// Whether the video's metadata has loaded since its source last changed.
let metadataLoaded: boolean;
// The file that the video last loaded, whose size it reports. An undo that puts back the old
// source does not make the video load again.
let loadedVideo: string | undefined;

// A page with a text canvas element, and a video canvas element whose box fits its 4:3 video.
beforeEach(() => {
    frames.posts = [];
    frames.socketListeners = [];
    frames.applyImport = undefined;
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-canvas" id="canvas">
                <div class="bloom-canvas-element" id="ce1" style="left: 10px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>One</p></div>
                    </div>
                </div>
                <div class="bloom-canvas-element" id="videoElement" style="left: 200px; top: 100px; width: 80px; height: 60px;">
                    <div class="bloom-videoContainer"><video><source src="${kOldVideo}"></video></div>
                </div>
            </div>
        </div>`;
    page = document.getElementById("page")!;
    videoElement = document.getElementById("videoElement")!;
    video = videoElement.querySelector("video")!;
    // jsdom loads no video. Like a browser, this one knows no size from when load() is called
    // until its metadata arrives; then the old video is 4:3 and the new one 16:9.
    metadataLoaded = true;
    loadedVideo = kOldVideo;
    video.load = () => {
        metadataLoaded = false;
        loadedVideo = videoOnPage();
    };
    const size = (oldSize: number, newSize: number) => () => {
        if (!metadataLoaded) return 0;
        return loadedVideo === kNewVideo ? newSize : oldSize;
    };
    Object.defineProperty(video, "videoWidth", { get: size(80, 160) });
    Object.defineProperty(video, "videoHeight", { get: size(60, 90) });
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: page,
        onUnmarkedChange,
        filter: createEditTimeMarkupFilter(),
    });
    frames.history = history;
    listenToPageHistory(history);
    select(videoElement);
});

afterEach(() => {
    frames.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
});

// Select an element as a click does. A click is not an undo step, so it is made outside the
// history, as the stand-in transaction that Bloom opens on a click makes no step for it.
function select(element: HTMLElement | undefined): void {
    history.doOutsideHistory(() =>
        theOneCanvasElementManager.setActiveElement(element),
    );
}

/** The file that the page's video names, without the parameter that only reloads it. */
function videoOnPage(): string | undefined {
    const src = page.querySelector("video source")?.getAttribute("src");
    return src ? withoutVideoTimestamp(src) : undefined;
}

/** The box's size and position, as its style gives them. */
function box(): string {
    const style = videoElement.style;
    return `${style.left} ${style.top} ${style.width} ${style.height}`;
}

/** Choose Video, choose a file, and have the progress dialog report what it imported. */
function importVideo(importedPath: string): void {
    doVideoCommand(
        videoElement.querySelector(".bloom-videoContainer")!,
        "choose",
    );
    frames.socketListeners.forEach((listener) =>
        listener({
            id: "chooseVideo-results",
            success: true,
            path: "C:/videos/mine.mp4",
        }),
    );
    if (!frames.applyImport) fail("The import showed no progress dialog.");
    frames.applyImport(importedPath);
}

/** The browser has read the video's size, in a later task. */
function loadMetadata(): void {
    metadataLoaded = true;
    video.dispatchEvent(new Event("loadedmetadata"));
}

/**
 * Returns a function that says whether a save would have stopped waiting for page content delays
 * (whenNoRequestPageContentDelays) once the promises then settled have run.
 */
function watchForSaveToGoAhead(): () => Promise<boolean> {
    let goesAhead = false;
    void whenNoRequestPageContentDelays().then(() => (goesAhead = true));
    return async () => {
        await Promise.resolve();
        await Promise.resolve();
        return goesAhead;
    };
}

describe("choosing a new video for a canvas element", () => {
    it("fits the box to the new video within the import's undo step", async () => {
        await history.doTransaction("Test.EarlierStep", () =>
            page.setAttribute("data-earlier", "true"),
        );
        const oldBox = box();
        expect(oldBox).toBe("200px 100px 80px 60px");

        importVideo(kNewVideo);
        // Until the video's size is known, the box keeps its shape.
        expect(box()).toBe(oldBox);
        expect(history.undoDescriptionL10nKey).toBe(kChooseVideo);
        loadMetadata();

        expect(videoOnPage()).toBe(kNewVideo);
        expect(videoElement.style.width).toBe("80px");
        expect(videoElement.style.height).toBe("45px");
        const newBox = box();
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kChooseVideo);
        expect(onUnmarkedChange.mock.calls).toEqual([]);
        expect(frames.posts).toEqual(["signLanguage/chooseVideo"]);

        await history.undo();
        expect(videoOnPage()).toBe(kOldVideo);
        expect(box()).toBe(oldBox);
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");

        await history.redo();
        expect(videoOnPage()).toBe(kNewVideo);
        expect(box()).toBe(newBox);
        expect(history.undoDescriptionL10nKey).toBe(kChooseVideo);
    });

    it("holds saves until the new video's size is known and the box fits it", async () => {
        importVideo(kNewVideo);
        const saveGoesAhead = watchForSaveToGoAhead();
        expect(await saveGoesAhead()).toBe(false);

        loadMetadata();

        expect(videoElement.style.height).toBe("45px");
        expect(await saveGoesAhead()).toBe(true);
    });

    it("stops holding saves after a while when the video's size never comes", async () => {
        vi.useFakeTimers();
        try {
            importVideo(kNewVideo);
            const saveGoesAhead = watchForSaveToGoAhead();
            await vi.advanceTimersByTimeAsync(999);
            expect(await saveGoesAhead()).toBe(false);
            await vi.advanceTimersByTimeAsync(1);
            expect(await saveGoesAhead()).toBe(true);

            // If the size comes after all, the box is still fitted, within the import's step.
            loadMetadata();
            expect(videoElement.style.height).toBe("45px");
            expect(history.undoDescriptionL10nKey).toBe(kChooseVideo);
            expect(onUnmarkedChange.mock.calls).toEqual([]);
        } finally {
            vi.useRealTimers();
        }
    });

    it("drops the fit when an undo begins before the new video's size is known", async () => {
        await history.doTransaction("Test.EarlierStep", () =>
            page.setAttribute("data-earlier", "true"),
        );
        const oldBox = box();
        importVideo(kNewVideo);

        await history.undo();

        expect(videoOnPage()).toBe(kOldVideo);
        expect(box()).toBe(oldBox);
        // The fit no longer holds saves.
        expect(await watchForSaveToGoAhead()()).toBe(true);
        // The browser was loading the new video, whose size now arrives.
        loadMetadata();
        expect(box()).toBe(oldBox);
        expect(onUnmarkedChange.mock.calls).toEqual([]);
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
        expect(history.redoDescriptionL10nKey).toBe(kChooseVideo);

        await history.redo();
        expect(videoOnPage()).toBe(kNewVideo);
        expect(box()).toBe(oldBox);

        // A click on the box fits it.
        select(videoElement);
        expect(videoElement.style.width).toBe("80px");
        expect(videoElement.style.height).toBe("45px");
    });

    it("writes nothing when the box is selected after the import", async () => {
        importVideo(kNewVideo);
        loadMetadata();
        const newBox = box();
        expect(newBox).not.toBe("200px 100px 80px 60px");

        select(document.getElementById("ce1")!);
        await history.doTransaction("Test.LaterStep", () =>
            page.setAttribute("data-later", "true"),
        );
        // Selected without select()'s doOutsideHistory, so that a write would show: it would be
        // reported as an unmarked change and join the later step, whose undo would then put
        // the box back.
        theOneCanvasElementManager.setActiveElement(videoElement);

        expect(box()).toBe(newBox);
        expect(onUnmarkedChange.mock.calls).toEqual([]);
        await history.undo();
        expect(page.hasAttribute("data-later")).toBe(false);
        expect(box()).toBe(newBox);
        expect(history.undoDescriptionL10nKey).toBe(kChooseVideo);
    });
});
