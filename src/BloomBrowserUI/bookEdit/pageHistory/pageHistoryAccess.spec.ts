import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "./pageHistoryTypes";

const frames = vi.hoisted(() => ({
    pageFrameExports: undefined as
        | { pageHistory: () => PageHistory }
        | undefined,
}));

vi.mock("../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => frames.pageFrameExports ?? null,
}));

import {
    clearUndoAndRedo,
    currentPageHistory,
    describeOpenStep,
    doOutsideHistory,
} from "./pageHistoryAccess";
import { createPageHistory } from "./pageHistory";
import { kGenericUndoDescriptionL10nKey } from "./pageHistoryTypes";

// Just the members that pageHistoryAccess uses; the rest throw if anything calls them.
function makeFakeHistory(isDisposed = false) {
    const fake = {
        isDisposed,
        clearUndoAndRedo: vi.fn(),
        doOutsideHistory: vi.fn(<T>(fn: () => T): T => fn()),
    };
    return fake;
}

function setPageFrame(history: object | "none") {
    frames.pageFrameExports = {
        pageHistory: () => {
            if (history === "none")
                throw new Error(
                    "Page history: there is none; startPageHistory has not been called.",
                );
            return history as PageHistory;
        },
    };
}

afterEach(() => {
    frames.pageFrameExports = undefined;
});

describe("pageHistoryAccess", () => {
    it("finds no history when there is no page frame", () => {
        expect(currentPageHistory()).toBeUndefined();
    });

    it("finds no history when the page frame has not started one", () => {
        setPageFrame("none");
        expect(currentPageHistory()).toBeUndefined();
    });

    it("finds no history when the current one has been disposed", () => {
        setPageFrame(makeFakeHistory(true));
        expect(currentPageHistory()).toBeUndefined();
    });

    it("finds the page frame's history", () => {
        const fake = makeFakeHistory();
        setPageFrame(fake);
        expect(currentPageHistory()).toBe(fake);
    });

    it("runs fn directly and returns its value when there is no history", () => {
        setPageFrame("none");
        const fn = vi.fn(() => 42);
        expect(doOutsideHistory(fn)).toBe(42);
        expect(fn).toHaveBeenCalledTimes(1);
    });

    it("runs fn inside the history's doOutsideHistory when there is one", () => {
        const fake = makeFakeHistory();
        setPageFrame(fake);
        const fn = vi.fn(() => "result");
        expect(doOutsideHistory(fn)).toBe("result");
        expect(fake.doOutsideHistory).toHaveBeenCalledTimes(1);
        expect(fake.doOutsideHistory).toHaveBeenCalledWith(fn);
        expect(fn).toHaveBeenCalledTimes(1);
    });

    it("does nothing on clear when there is no history", () => {
        expect(() => clearUndoAndRedo("a test")).not.toThrow();
        setPageFrame("none");
        expect(() => clearUndoAndRedo("a test")).not.toThrow();
        const disposed = makeFakeHistory(true);
        setPageFrame(disposed);
        clearUndoAndRedo("a test");
        expect(disposed.clearUndoAndRedo).not.toHaveBeenCalled();
    });

    it("clears the history when there is one", () => {
        const fake = makeFakeHistory();
        setPageFrame(fake);
        clearUndoAndRedo("a test");
        expect(fake.clearUndoAndRedo).toHaveBeenCalledTimes(1);
    });
});

describe("describeOpenStep", () => {
    let page: HTMLElement;
    let history: PageHistory;

    beforeEach(() => {
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML = '<p id="p1">Hello</p>';
        document.body.appendChild(page);
        history = createPageHistory({ root: page });
        setPageFrame(history);
    });

    afterEach(() => {
        history.dispose();
        page.remove();
    });

    // A change that the stand-in which a click opens records, made by an action with no
    // transaction of its own.
    function changeInStandIn(action: () => void): void {
        const standIn = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        action();
        page.setAttribute("data-x", "1");
        standIn.end();
    }

    it("gives the step of the open stand-in its description", () => {
        changeInStandIn(() => describeOpenStep("Test.Action"));
        expect(history.undoDescriptionL10nKey).toBe("Test.Action");
    });

    it("keeps the description that a transaction nested earlier gave the step", () => {
        changeInStandIn(() => {
            void history.doTransaction("Test.First", () => {});
            describeOpenStep("Test.Second");
        });
        expect(history.undoDescriptionL10nKey).toBe("Test.First");
    });

    it("opens nothing and makes no step when no transaction is open", () => {
        describeOpenStep("Test.Action");
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });
});
