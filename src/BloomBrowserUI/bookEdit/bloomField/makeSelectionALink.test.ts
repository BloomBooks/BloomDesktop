// BloomField.makeSelectionALink, which Set Up Hyperlink's dialog calls on OK, as an undo step of
// the page history. The dialog is in the root window, so the tests attach the input transactions
// with this window, where the OK button is, as one of the other windows, and an empty frame as the
// page's. As in pageHistoryInputSpec.ts, the user's events are the ones in userEvents, and a test
// says where the browser's task ends by calling finishTask().
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import { attachInputTransactions } from "../pageHistory/pageHistoryInput";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T => fn(),
}));

// The requests to Bloom that have not been answered yet.
const api = vi.hoisted(() => ({
    pending: [] as {
        url: string;
        success: (response: { data: unknown }) => void;
        error?: (error: unknown) => void;
    }[],
}));
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    get: (
        url: string,
        success: (response: { data: unknown }) => void,
        error?: (error: unknown) => void,
    ) => api.pending.push({ url, success, error }),
}));

const messageBox = vi.hoisted(() => ({ show: vi.fn() }));
vi.mock("../../utils/bloomMessageBoxSupport", () => ({
    default: { CreateAndShowSimpleMessageBox: messageBox.show },
}));

import BloomField from "./BloomField";
import { whenNoRequestPageContentDelays } from "../js/bloomEditing";

const kLinkKey = "EditTab.SetupHyperlink";
const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kStartingHtml = "<p>Hello <b>big</b> world</p>";

let history: PageHistory;
let page: HTMLElement;
let editable: HTMLElement;
let okButton: HTMLButtonElement;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
let okUrl: string;
let onUnmarkedChange: ReturnType<typeof vi.fn>;

beforeEach(async () => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = `<div class="bloom-editable" contenteditable="true">${kStartingHtml}</div><div id="other"></div>`;
    document.body.appendChild(page);
    editable = page.querySelector(".bloom-editable")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({ root: page, onUnmarkedChange });
    access.history = history;
    // A step before the link, which must survive it.
    await history.doTransaction(kEarlierKey, () =>
        page.querySelector("#other")!.setAttribute("data-earlier", "true"),
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
    // What LinkTargetChooserDialog's OK does: call onSetUrl in the click's handler.
    okUrl = "https://example.com";
    okButton = document.createElement("button");
    okButton.addEventListener("click", () =>
        BloomField.makeSelectionALink(okUrl),
    );
    document.body.appendChild(okButton);
});

afterEach(() => {
    detach();
    history.dispose();
    access.history = undefined;
    api.pending = [];
    messageBox.show.mockReset();
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

/** A click on OK by the user: the press, then the release as one task, and the end of that task. */
function clickOk(): void {
    mouse(okButton, "pointerdown", 1);
    mouse(okButton, "mousedown", 1);
    mouse(okButton, "pointerup", 0);
    mouse(okButton, "mouseup", 0);
    mouse(okButton, "click", 0);
    finishTask();
}

/** Select from offset start of the start node to offset end of the end node. */
function select(
    startNode: Node,
    start: number,
    endNode: Node,
    end: number,
): void {
    const range = document.createRange();
    range.setStart(startNode, start);
    range.setEnd(endNode, end);
    const selection = document.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
}

/**
 * Returns a function that says whether a save would have stopped waiting for page content delays
 * (whenNoRequestPageContentDelays) once the promises then settled have run.
 */
function watchForSaveToGoAhead(): () => Promise<boolean> {
    let goesAhead = false;
    void whenNoRequestPageContentDelays().then(() => (goesAhead = true));
    return async () => {
        for (let i = 0; i < 5; i++) await Promise.resolve();
        return goesAhead;
    };
}

/** Bloom's answer to the request for the book's information. */
function answerBookInfo(data: unknown): void {
    expect(api.pending.length).toBe(1);
    const request = api.pending.shift()!;
    expect(request.url).toBe("app/selectedBookInfo");
    request.success({ data });
}

describe("BloomField.makeSelectionALink", () => {
    it("makes the link one step, which undo and redo reverse, after the earlier step", async () => {
        // "world"
        const lastText = editable.querySelector("p")!.lastChild!;
        select(lastText, 1, lastText, 6);

        clickOk();
        // The click's task is over, but the link waits for the book's id. No transaction is open
        // meanwhile, so the click made no step and ended nothing.
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(editable.querySelector("a")).toBeNull();

        answerBookInfo({ id: "book1" });

        expect(editable.querySelector("a")?.getAttribute("href")).toBe(
            "https://example.com",
        );
        expect(editable.querySelector("a")?.textContent).toBe("world");
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kLinkKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(editable.innerHTML).toBe(kStartingHtml);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(history.redoDescriptionL10nKey).toBe(kLinkKey);

        await history.redo();
        expect(editable.querySelector("a")?.textContent).toBe("world");
        expect(history.undoDescriptionL10nKey).toBe(kLinkKey);

        await history.undo();
        await history.undo();
        expect(page.querySelector("#other")!.hasAttribute("data-earlier")).toBe(
            false,
        );
    });

    it("leaves no step and keeps the earlier one when the selection cannot hold a link", () => {
        // From inside "Hello " to inside "big": the range takes part of the <b>, which
        // surroundContents refuses.
        const p = editable.querySelector("p")!;
        select(p.firstChild!, 2, p.querySelector("b")!.firstChild!, 1);

        clickOk();
        answerBookInfo({ id: "book1" });

        expect(messageBox.show).toHaveBeenCalledTimes(1);
        expect(editable.innerHTML).toBe(kStartingHtml);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });

    it("leaves no step and keeps the earlier one when Bloom has no book", () => {
        const lastText = editable.querySelector("p")!.lastChild!;
        select(lastText, 1, lastText, 6);

        clickOk();
        answerBookInfo(undefined);

        expect(editable.innerHTML).toBe(kStartingHtml);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });

    it("stops holding saves when the request for the book fails", async () => {
        const lastText = editable.querySelector("p")!.lastChild!;
        select(lastText, 1, lastText, 6);
        clickOk();
        const saveGoesAhead = watchForSaveToGoAhead();
        expect(await saveGoesAhead()).toBe(false);
        const request = api.pending.shift()!;
        const failure = new Error("no answer");

        expect(() => request.error!(failure)).toThrow(failure);

        expect(await saveGoesAhead()).toBe(true);
        expect(editable.innerHTML).toBe(kStartingHtml);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });

    it("links the text selected at OK when the user selects other text before Bloom answers", () => {
        const p = editable.querySelector("p")!;
        // "world"
        select(p.lastChild!, 1, p.lastChild!, 6);
        clickOk();

        // "Hello"
        select(p.firstChild!, 0, p.firstChild!, 5);
        answerBookInfo({ id: "book1" });

        expect(editable.querySelector("a")?.textContent).toBe("world");
        expect(history.undoDescriptionL10nKey).toBe(kLinkKey);
    });

    it("keeps an edit made before Bloom answers out of the link's step", async () => {
        const p = editable.querySelector("p")!;
        const world = p.lastChild as Text;
        select(world, 1, world, 6);
        clickOk();

        const kEditKey = "Test.Edit";
        await history.doTransaction(kEditKey, () =>
            p.firstChild!.replaceWith("Goodbye "),
        );
        expect(history.undoDescriptionL10nKey).toBe(kEditKey);
        answerBookInfo({ id: "book1" });

        expect(editable.innerHTML).toBe(
            '<p>Goodbye <b>big</b> <a href="https://example.com">world</a></p>',
        );
        expect(history.undoDescriptionL10nKey).toBe(kLinkKey);
        await history.undo();
        expect(editable.innerHTML).toBe("<p>Goodbye <b>big</b> world</p>");
        expect(history.undoDescriptionL10nKey).toBe(kEditKey);
        await history.undo();
        expect(editable.innerHTML).toBe(kStartingHtml);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("makes no link when the selected text is deleted before Bloom answers", async () => {
        const p = editable.querySelector("p")!;
        const world = p.lastChild as Text;
        select(world, 0, world, 6);
        clickOk();

        await history.doTransaction("Test.Delete", () => world.remove());
        answerBookInfo({ id: "book1" });

        expect(editable.querySelector("a")).toBeNull();
        expect(history.undoDescriptionL10nKey).toBe("Test.Delete");
    });

    it("holds a save until the link is on the page", async () => {
        const lastText = editable.querySelector("p")!.lastChild!;
        select(lastText, 1, lastText, 6);
        clickOk();
        const saveGoesAhead = watchForSaveToGoAhead();
        expect(await saveGoesAhead()).toBe(false);

        answerBookInfo({ id: "book1" });

        expect(await saveGoesAhead()).toBe(true);
        expect(editable.querySelector("a")?.textContent).toBe("world");
    });

    it("asks Bloom nothing for OK with no url", () => {
        okUrl = "";
        clickOk();

        expect(api.pending.length).toBe(0);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });
});
