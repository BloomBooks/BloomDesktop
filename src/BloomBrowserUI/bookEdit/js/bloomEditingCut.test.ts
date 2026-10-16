// The top bar's Cut (cutSelection in bloomEditing.ts) deletes a range with CKEditor's range code,
// which raises no input event and no beforeinput. So it fires the editor's "change" itself, for
// BL-13779's data-user-deleted, and it refuses to delete an element that has the
// bloom-preventRemoval class, as Ctrl+X does. No real CKEditor runs here: a stand-in has the parts
// that cutSelection and BloomField.MarkWhenUserDeletesEverything use.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistoryIfAny: <T>(fn: () => T): T => fn(),
}));

import { cutSelection } from "./bloomEditing";
import BloomField from "../bloomField/BloomField";

let page: HTMLElement;
let box: HTMLElement;
let copied: string[];
let ckeditorWasDefined: boolean;

/** A stand-in for the CKEditor instance of box, as CKEDITOR.currentInstance. */
class StandInEditor {
    public element = { $: box };
    private listeners = new Map<string, (() => void)[]>();
    public on(eventName: string, listener: () => void): void {
        this.listeners.set(eventName, [
            ...(this.listeners.get(eventName) ?? []),
            listener,
        ]);
    }
    public fire(eventName: string): void {
        this.listeners.get(eventName)?.forEach((listener) => listener());
    }
    // CKEditor's selection of box, which is the document's.
    public getSelection() {
        const range = document.getSelection()!.getRangeAt(0);
        return {
            getRanges: () => [
                {
                    deleteContents: () => range.deleteContents(),
                    select: () => {},
                },
            ],
        };
    }
}

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div id="box" class="bloom-editable bloom-copyFromOtherLanguageIfNecessary" contenteditable="true">' +
        '<p id="p1">One</p><p id="p2">Two</p></div>';
    document.body.appendChild(page);
    box = page.querySelector("#box")!;
    access.history = createPageHistory({ root: page });
    copied = [];
    Object.defineProperty(navigator, "clipboard", {
        value: {
            writeText: (text: string) => {
                copied.push(text);
                return Promise.resolve();
            },
        },
        configurable: true,
    });
    ckeditorWasDefined = "CKEDITOR" in globalThis;
    (globalThis as unknown as { CKEDITOR: unknown }).CKEDITOR = {
        currentInstance: new StandInEditor(),
    };
});

afterEach(() => {
    access.history?.dispose();
    access.history = undefined;
    page.remove();
    if (!ckeditorWasDefined)
        delete (globalThis as unknown as { CKEDITOR?: unknown }).CKEDITOR;
});

function currentEditor(): StandInEditor {
    return (
        globalThis as unknown as {
            CKEDITOR: { currentInstance: StandInEditor };
        }
    ).CKEDITOR.currentInstance;
}

/** Select from offset start in the text of startId to offset end in the text of endId. */
function selectText(
    startId: string,
    start: number,
    endId: string,
    end: number,
) {
    const range = document.createRange();
    range.setStart(page.querySelector(`#${startId}`)!.firstChild!, start);
    range.setEnd(page.querySelector(`#${endId}`)!.firstChild!, end);
    const selection = document.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
}

describe("cutSelection", () => {
    it("marks a box that it empties as deleted by the user, in the cut's undo step (BL-13779)", async () => {
        BloomField.MarkWhenUserDeletesEverything(
            box,
            currentEditor() as unknown as CKEDITOR.editor,
        );
        selectText("p1", 0, "p2", 3);

        await cutSelection();

        expect(copied.length, "the cut copied the selection").toBe(1);
        expect(box.textContent).toBe("");
        expect(box.getAttribute("data-user-deleted")).toBe("true");
        expect(access.history!.undoDescriptionL10nKey).toBe(
            "EditTab.CutButton",
        );
        await access.history!.undo();
        expect(box.textContent).toBe("OneTwo");
        expect(box.getAttribute("data-user-deleted")).toBeNull();
    });

    it("copies but deletes nothing when the cut would remove a protected element", async () => {
        box.insertAdjacentHTML(
            "afterbegin",
            '<div id="picture" class="bloom-preventRemoval"><p id="caption">Caption</p></div>',
        );
        selectText("caption", 2, "p2", 1);

        await cutSelection();

        expect(copied.length, "the cut copied the selection").toBe(1);
        expect(box.textContent).toBe("CaptionOneTwo");
        expect(page.querySelector("#picture")).not.toBeNull();
    });

    it("deletes a selection beside a protected element", async () => {
        box.insertAdjacentHTML(
            "afterbegin",
            '<div id="picture" class="bloom-preventRemoval"><p id="caption">Caption</p></div>',
        );
        selectText("p1", 0, "p2", 1);

        await cutSelection();

        expect(box.textContent).toBe("Captionwo");
    });
});
