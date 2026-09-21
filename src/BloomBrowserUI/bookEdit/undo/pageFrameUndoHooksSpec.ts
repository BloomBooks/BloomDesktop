// Tests for the hooks that tell the one undo stack about the page frame's lifetime (BL-6681).

import { describe, it, expect, beforeEach, vi } from "vitest";
import { UndoStack } from "./UndoStack";
import { IUndoEntry } from "./undoTypes";

// The hooks find the page through utils/shared, which reaches into the page iframe. There is no
// iframe in jsdom, so stand in for that one lookup.
let pageElement: HTMLElement | null = null;
vi.mock("../../utils/shared", () => ({
    getBloomPageElement: () => pageElement,
}));

import {
    getCurrentPageIdFromPageFrame,
    pageFrameLoaded,
    pageFrameNavigating,
} from "./pageFrameUndoHooks";

function entry(label: string, pageId: string | undefined): IUndoEntry {
    return { label, pageId, kind: "custom", undo: () => {} };
}

function makePage(id: string): HTMLElement {
    const div = document.createElement("div");
    div.className = "bloom-page";
    div.id = id;
    return div;
}

describe("pageFrameUndoHooks", () => {
    let stack: UndoStack;

    beforeEach(() => {
        stack = new UndoStack();
        pageElement = null;
    });

    describe("getCurrentPageIdFromPageFrame", () => {
        it("reads the .bloom-page element's id", () => {
            pageElement = makePage("page-abc");
            expect(getCurrentPageIdFromPageFrame()).toBe("page-abc");
        });

        it("is undefined when there is no page, or the page has no id", () => {
            expect(getCurrentPageIdFromPageFrame()).toBeUndefined();
            pageElement = makePage("");
            expect(getCurrentPageIdFromPageFrame()).toBeUndefined();
        });
    });

    describe("pageFrameNavigating", () => {
        it("drops page-scoped entries and keeps the ones that survive a page change", () => {
            stack.setCurrentPageId("page-1");
            stack.push(entry("delete page", undefined));
            stack.push(entry("typing", "page-1"));
            expect(stack.getEntryCount()).toBe(2); // sanity

            pageFrameNavigating(stack);

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("delete page");
        });

        it("clears even when the page id is not going to change", () => {
            // Leaving Change Layout mode rebuilds the same page under its own id; the rebuilt
            // elements are new, so the old entries are just as stale as after a real page change.
            stack.setCurrentPageId("page-1");
            stack.push(entry("typing", "page-1"));
            pageElement = makePage("page-1");

            pageFrameNavigating(stack);
            pageFrameLoaded(stack);

            expect(stack.getEntryCount()).toBe(0);
            expect(stack.getCurrentPageId()).toBe("page-1");
        });
    });

    describe("pageFrameLoaded", () => {
        it("records the loaded page's id on the stack", () => {
            pageElement = makePage("page-2");
            pageFrameLoaded(stack);
            expect(stack.getCurrentPageId()).toBe("page-2");
        });

        it("records undefined when no page is loaded", () => {
            stack.setCurrentPageId("page-2");
            pageFrameLoaded(stack);
            expect(stack.getCurrentPageId()).toBeUndefined();
        });
    });
});
