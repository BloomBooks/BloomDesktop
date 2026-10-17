// Choosing the books of a link grid is not undoable, and neither it nor the thumbnails that arrive
// afterwards end the page history. The Book Grid Setup dialog's OK button is in the root window, so
// the tests attach the input transactions with this window, where they press it, as one of the
// other windows, and an empty frame as the page's. The user's events are the ones in userEvents,
// and a test says where the browser's task ends by calling finishTask(), as in
// imageDescriptionUndo.test.tsx.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import { attachInputTransactions } from "../pageHistory/pageHistoryInput";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";
import type { Link } from "../../react_components/BookGridSetup/BookLinkTypes";
import type { IBloomWebSocketEvent } from "../../utils/WebSocketManager";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    onOk: undefined as ((links: Link[]) => void) | undefined,
    thumbnailListeners: new Map<string, (e: IBloomWebSocketEvent) => void>(),
    postJson: vi.fn(),
}));

vi.mock("./workspaceFrames", () => ({
    // The page frame's exports, through which pageHistoryAccess.ts finds the history.
    getEditablePageBundleExports: () =>
        mocks.history ? { pageHistory: () => mocks.history } : null,
    getWorkspaceBundleExports: () => ({
        showBookGridSetupDialog: (
            _links: Link[],
            onOk: (links: Link[]) => void,
        ) => {
            mocks.onOk = onOk;
        },
    }),
}));

vi.mock("../../utils/WebSocketManager", () => ({
    default: {
        closeSocketsWithPrefix: () => {},
        notifyReady: (_context: string, fn: () => void) => fn(),
        once: (context: string, fn: (e: IBloomWebSocketEvent) => void) =>
            mocks.thumbnailListeners.set(context, fn),
    },
}));

vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../utils/bloomApi")>()),
    postJson: mocks.postJson,
}));

import { editLinkGrid } from "./linkGrid";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";

let history: PageHistory;
let linkGrid: HTMLElement;
let other: HTMLElement;
let okButton: HTMLButtonElement;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
let onUnmarkedChange: ReturnType<typeof vi.fn>;

beforeEach(async () => {
    (globalThis as unknown as { GetSettings: () => object }).GetSettings =
        () => ({ languageForNewTextBoxes: "en" });
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-link-grid"><div class="bloom-bookButton-skeleton"></div></div><div id="other"></div>';
    document.body.appendChild(page);
    linkGrid = page.querySelector(".bloom-link-grid")!;
    other = page.querySelector("#other")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({ root: page, onUnmarkedChange });
    mocks.history = history;
    mocks.onOk = undefined;
    mocks.thumbnailListeners.clear();
    mocks.postJson.mockReset();
    // A step before the dialog, which must survive it.
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

    // Stands in for the dialog's OK button, in the root window.
    okButton = document.createElement("button");
    document.body.appendChild(okButton);
});

afterEach(() => {
    detach();
    history.dispose();
    mocks.history = undefined;
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

/** The user presses OK, choosing links: the press, then the release as one task, and its end. */
function pressOk(links: Link[]): void {
    okButton.onclick = () => mocks.onOk!(links);
    mouse(okButton, "pointerdown", 1);
    mouse(okButton, "mousedown", 1);
    mouse(okButton, "pointerup", 0);
    mouse(okButton, "mouseup", 0);
    mouse(okButton, "click", 0);
    finishTask();
}

function expectHistoryKept(): void {
    expect(history.hasOpenTransaction).toBe(false);
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    expect(onUnmarkedChange).not.toHaveBeenCalled();
}

const kLink: Link = {
    book: { id: "abc", title: "Linked Book", thumbnail: "/cover.png" },
};

describe("Choosing the books of a link grid", () => {
    it("fills the grid without making a step or ending the history", async () => {
        editLinkGrid(linkGrid);
        pressOk([kLink]);

        const buttons = linkGrid.querySelectorAll(".bloom-bookButton");
        expect(buttons.length).toBe(1);
        expect(buttons[0].getAttribute("data-bloom-book-id")).toBe("abc");
        expect(linkGrid.querySelector(".bloom-bookButton-skeleton")).toBeNull();
        expect(mocks.postJson).toHaveBeenCalledWith(
            "editView/addImageFromUrl",
            {
                desiredFileNameWithoutExtension: "bookButton-abc",
                url: "/cover.png",
            },
        );
        expectHistoryKept();

        // The thumbnail arrives later, outside any transaction.
        const setThumbnail = mocks.thumbnailListeners.get(
            "makeThumbnailFile-bookButton-abc",
        );
        if (!setThumbnail) fail("The grid did not listen for its thumbnail.");
        setThumbnail({
            id: "success",
            clientContext: "makeThumbnailFile-bookButton-abc",
            message: "bookButton-abc.png",
        });
        expect(linkGrid.querySelector("img")!.getAttribute("src")).toBe(
            "bookButton-abc.png",
        );
        // Let the history's MutationObserver see the change.
        await Promise.resolve();
        expectHistoryKept();

        // Undo reverses the earlier step and leaves the grid alone.
        await history.undo();
        expect(other.hasAttribute("data-earlier")).toBe(false);
        expect(linkGrid.querySelectorAll(".bloom-bookButton").length).toBe(1);
        expect(linkGrid.querySelector("img")!.getAttribute("src")).toBe(
            "bookButton-abc.png",
        );
    });

    it("puts back the skeleton for no books without making a step", () => {
        history.doOutsideHistory(() => {
            linkGrid.innerHTML =
                '<div class="bloom-bookButton" data-bloom-book-id="abc"><p>Linked Book</p></div>';
        });

        editLinkGrid(linkGrid);
        pressOk([]);

        expect(linkGrid.querySelector(".bloom-bookButton")).toBeNull();
        expect(
            linkGrid.querySelectorAll(".bloom-bookButton-skeleton").length,
        ).toBe(6);
        expectHistoryKept();
    });
});
