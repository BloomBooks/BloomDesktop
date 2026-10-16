// The image description tool's "This image should not be described" checkbox is not undoable, and
// ticking it keeps the page history. The tool lives in the toolbox frame, so the tests attach the
// input transactions with this window, where the tool renders, as one of the other windows, and an
// empty frame as the page's. As in pageHistoryInputSpec.ts, the user's events are the ones in
// userEvents, and a test says where the browser's task ends by calling finishTask().
import $ from "jquery";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    page: undefined as HTMLElement | undefined,
    // The callbacks of the posts not yet answered, which a test answers when it chooses.
    pendingPosts: [] as Array<(result: { data: string }) => void>,
}));
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../../utils/bloomApi")>()),
    post: (_url: string, callback: (result: { data: string }) => void) =>
        mocks.pendingPosts.push(callback),
}));
// The page frame's exports that appendTranslationGroup uses.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../js/workspaceFrames")>()),
    getEditablePageBundleExports: () => ({
        makeElement: (html: string) => $(html),
        SetupElements: () => undefined,
        attachToCkEditor: () => undefined,
    }),
}));
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistoryIfAny: <T,>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    clearUndoAndRedoIfAny: () => mocks.history?.clearUndoAndRedo(),
}));
// The page that newPageReady finds; it is in this document, which the tests treat as the page
// frame's.
vi.mock("../../../utils/shared", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../../utils/shared")>()),
    getPageIframeBody: () => mocks.page,
    isXmatterPage: () => false,
}));
vi.mock("../canvas/canvasElementPageBridge", () => ({
    getCanvasElementManager: () => ({
        getAllBloomCanvasesOnPage: () =>
            Array.from(mocks.page!.querySelectorAll(".bloom-canvas")),
    }),
}));
vi.mock("./imageDescriptionUtils", () => ({
    showImageDescriptions: () => undefined,
    hideImageDescriptions: () => undefined,
}));

import { ImageDescriptionAdapter } from "./imageDescription";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";

let history: PageHistory;
let bloomCanvas: HTMLElement;
let other: HTMLElement;
let adapter: ImageDescriptionAdapter;
let toolRoot: HTMLElement;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
let onUnmarkedChange: ReturnType<typeof vi.fn>;

beforeEach(async () => {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas"><img src="a.png"><div class="bloom-translationGroup bloom-imageDescription"><div class="bloom-editable" lang="en"></div></div></div><div id="other"></div>';
    document.body.appendChild(page);
    mocks.page = page;
    mocks.pendingPosts = [];
    bloomCanvas = page.querySelector(".bloom-canvas")!;
    other = page.querySelector("#other")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({ root: page, onUnmarkedChange });
    mocks.history = history;
    // A step before the checkbox, which must survive it.
    await history.doTransaction(kEarlierKey, () =>
        other.setAttribute("data-earlier", "true"),
    );
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);

    const pageFrame = document.createElement("iframe");
    document.body.appendChild(pageFrame);
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

    adapter = new ImageDescriptionAdapter();
    act(() => {
        toolRoot = adapter.makeRootElement();
    });
    document.body.appendChild(toolRoot);
    act(() => adapter.newPageReady());
});

afterEach(() => {
    detach();
    history.dispose();
    mocks.history = undefined;
    mocks.page = undefined;
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

function getCheckbox(): HTMLInputElement {
    const checkbox = toolRoot.querySelector<HTMLInputElement>(
        '.imageDescriptionCheck input[type="checkbox"]',
    );
    if (!checkbox) fail("The tool did not render its checkbox.");
    return checkbox;
}

function expectHistoryKept(): void {
    expect(history.hasOpenTransaction).toBe(false);
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    expect(history.redoDescriptionL10nKey).toBeUndefined();
    expect(onUnmarkedChange).not.toHaveBeenCalled();
}

describe("The image description tool's checkbox", () => {
    it("marks the picture without making a step or ending the history", async () => {
        expect(getCheckbox().checked).toBe(false);

        click(getCheckbox());

        expect(bloomCanvas.getAttribute("aria-hidden")).toBe("true");
        expect(getCheckbox().checked).toBe(true);
        expectHistoryKept();

        // Undo reverses the earlier step and leaves the mark.
        await act(async () => history.undo());
        expect(other.hasAttribute("data-earlier")).toBe(false);
        expect(bloomCanvas.getAttribute("aria-hidden")).toBe("true");
        expect(getCheckbox().checked).toBe(true);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("unmarks the picture without making a step either", () => {
        click(getCheckbox());
        click(getCheckbox());

        expect(bloomCanvas.hasAttribute("aria-hidden")).toBe(false);
        expect(getCheckbox().checked).toBe(false);
        expectHistoryKept();
    });

    it("adds a missing image description group without making a step or joining one", async () => {
        const secondCanvas = document.createElement("div");
        secondCanvas.className = "bloom-canvas";
        history.doOutsideHistory(() => mocks.page!.appendChild(secondCanvas));

        act(() => adapter.newPageReady());
        expect(mocks.pendingPosts.length).toBe(1);
        // The reply arrives later, outside any transaction.
        act(() =>
            mocks.pendingPosts[0]({
                data: '<div class="bloom-editable" lang="en"></div>',
            }),
        );
        // Let the history's MutationObserver see the change.
        await Promise.resolve();

        expect(
            secondCanvas.querySelector(
                ".bloom-imageDescription .bloom-editable",
            ),
        ).not.toBeNull();
        expectHistoryKept();

        // Undo reverses the earlier step and leaves the group.
        await act(async () => history.undo());
        expect(other.hasAttribute("data-earlier")).toBe(false);
        expect(
            secondCanvas.querySelector(".bloom-imageDescription"),
        ).not.toBeNull();
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("keeps redo", async () => {
        await act(async () => history.undo());
        expect(history.redoDescriptionL10nKey).toBe(kEarlierKey);

        click(getCheckbox());

        expect(bloomCanvas.getAttribute("aria-hidden")).toBe("true");
        expect(history.redoDescriptionL10nKey).toBe(kEarlierKey);
        await act(async () => history.redo());
        expect(other.getAttribute("data-earlier")).toBe("true");
        expect(bloomCanvas.getAttribute("aria-hidden")).toBe("true");
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });
});
