// What CKEditor writes into a box as it attaches (joinLatestTransactionWhenCkEditorAttaches in
// bloomEditing.ts) joins the page history's latest transaction, with no warning, whether CKEditor
// finishes attaching inside CKEDITOR.inline() or in its "loaded" event in a later task. No real
// CKEditor runs here: a stand-in fires "loaded" through fireOnce, as CKEditor's own event code
// does, and its listener rewrites the box's content as CKEditor's setData does.
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

import { joinLatestTransactionWhenCkEditorAttaches } from "./bloomEditing";

let page: HTMLElement;
let box: HTMLElement;
let warnings: string[];

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-editable" id="box"><p>Hello</p></div><img id="img" src="a.png">';
    document.body.appendChild(page);
    box = page.querySelector("#box")!;
    warnings = [];
    access.history = createPageHistory({
        root: page,
        onUnmarkedChange: (whatChanged) => warnings.push(whatChanged),
    });
});

afterEach(() => {
    access.history?.dispose();
    access.history = undefined;
    page.remove();
});

/** A stand-in for a CKEditor instance: fire and fireOnce as CKEDITOR.event has them. */
class StandInEditor {
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
    public fireOnce(eventName: string): void {
        this.fire(eventName);
        this.listeners.delete(eventName);
    }
}

/** What CKEditor's "loaded" listener does to the box: its content again, as new nodes. */
function rewriteBox(): void {
    const content = box.innerHTML;
    box.innerHTML = content;
}

describe("joinLatestTransactionWhenCkEditorAttaches", () => {
    it("joins the rewrite in a later task to the latest step, with no warning", async () => {
        const history = access.history!;
        const oldParagraph = box.firstChild;
        await history.doTransaction("Duplicate", () =>
            page.querySelector("#img")!.setAttribute("src", "b.png"),
        );
        const editor = joinLatestTransactionWhenCkEditorAttaches(() => {
            const made = new StandInEditor();
            made.on("loaded", rewriteBox);
            return made as unknown as CKEDITOR.editor;
        });
        expect(box.firstChild, "test setup: not attached yet").toBe(
            oldParagraph,
        );

        (editor as unknown as StandInEditor).fireOnce("loaded");
        await Promise.resolve();

        expect(warnings).toEqual([]);
        expect(box.firstChild).not.toBe(oldParagraph);
        await history.undo();
        expect(
            box.firstChild,
            "undoing the step puts back the nodes that the rewrite replaced",
        ).toBe(oldParagraph);
        expect(page.querySelector("#img")!.getAttribute("src")).toBe("a.png");
    });

    it("joins a rewrite made inside CKEDITOR.inline() too", async () => {
        const history = access.history!;
        await history.doTransaction("Add box", () =>
            page.querySelector("#img")!.setAttribute("src", "b.png"),
        );
        joinLatestTransactionWhenCkEditorAttaches(() => {
            rewriteBox();
            return new StandInEditor() as unknown as CKEDITOR.editor;
        });
        await Promise.resolve();
        expect(warnings).toEqual([]);
        expect(history.undoDescriptionL10nKey).toBe("Add box");
    });

    it("leaves other events alone", async () => {
        const history = access.history!;
        await history.doTransaction("A", () =>
            page.querySelector("#img")!.setAttribute("src", "b.png"),
        );
        const made = new StandInEditor();
        made.on("change", rewriteBox);
        const editor = joinLatestTransactionWhenCkEditorAttaches(
            () => made as unknown as CKEDITOR.editor,
        );
        (editor as unknown as StandInEditor).fire("change");
        await Promise.resolve();
        expect(warnings.length).toBeGreaterThan(0);
    });

    it("just makes the editor when there is no page history", () => {
        access.history!.dispose();
        access.history = undefined;
        const made = new StandInEditor();
        made.on("loaded", rewriteBox);
        const editor = joinLatestTransactionWhenCkEditorAttaches(
            () => made as unknown as CKEDITOR.editor,
        );
        (editor as unknown as StandInEditor).fireOnce("loaded");
        expect(box.textContent).toBe("Hello");
    });
});
