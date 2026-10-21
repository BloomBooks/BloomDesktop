import { beforeEach, describe, expect, it, vi } from "vitest";
import {
    getOpenToolId,
    getFirstOfferedToolId,
    getToolboxUiState,
    isToolEnabled,
    isToolOffered,
    isToolboxUiMounted,
    notePageReady,
    offerTool,
    resetToolboxUiStateForTests,
    setOpenTool,
    setEnabledTools,
    setToolEnabled,
    setToolboxUiMounted,
    setToolboxVisible,
    subscribeToToolboxUiState,
    withdrawTool,
} from "./toolboxState";

// The toolbox's state, tested without React. The behaviour that matters most here is
// what happens when the open tool is withdrawn (BL-16602); see that describe block.

describe("toolboxState", () => {
    beforeEach(() => {
        resetToolboxUiStateForTests();
    });

    describe("the tools being offered", () => {
        it("keeps them alphabetical by label, with More... last", () => {
            offerTool("settings");
            offerTool("motion");
            offerTool("canvas");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "canvas",
                "motion",
                "settings",
            ]);
        });

        it("ignores a tool it is already offering", () => {
            offerTool("motion");
            offerTool("motion");

            expect(getToolboxUiState().offeredToolIds).toEqual(["motion"]);
        });

        it("answers isToolOffered and getFirstOfferedToolId", () => {
            expect(isToolOffered("motion")).toBe(false);
            expect(getFirstOfferedToolId()).toBeUndefined();

            // "More..." is never chosen as the *default* tool. (Opening it by hand does open
            // it; this asserts only that it is not what we fall back to.)
            offerTool("settings");
            expect(getFirstOfferedToolId()).toBeUndefined();

            offerTool("motion");
            expect(isToolOffered("motion")).toBe(true);
            expect(getFirstOfferedToolId()).toBe("motion");
        });

        it("replaces the snapshot rather than mutating it", () => {
            const before = getToolboxUiState();
            offerTool("motion");

            expect(getToolboxUiState()).not.toBe(before);
            expect(before.offeredToolIds).toEqual([]);
        });

        it("tells its subscribers, until they unsubscribe", () => {
            let notifications = 0;
            const unsubscribe = subscribeToToolboxUiState(
                () => notifications++,
            );

            offerTool("motion");
            expect(notifications).toBe(1);

            // Offering a tool we already offer changes nothing, so says nothing.
            offerTool("motion");
            expect(notifications).toBe(1);

            unsubscribe();
            offerTool("canvas");
            expect(notifications).toBe(1);
        });
    });

    describe("which tool is open", () => {
        it("makes the open tool the running one", () => {
            offerTool("motion");

            setOpenTool("motion");

            expect(getToolboxUiState().openToolId).toBe("motion");
            expect(getOpenToolId()).toBe("motion");
        });

        it("refuses to open a tool the toolbox is not offering", () => {
            // Such a tool has no accordion section to expand, so obeying would put the
            // store in a state the screen cannot show. The request is refused and logged
            // rather than obeyed, which is what keeps what the user sees and what is
            // running from drifting apart (BL-16602).
            const complaints = vi
                .spyOn(console, "error")
                .mockImplementation(() => undefined);
            try {
                // sanity check: nothing is open to begin with, so a "motion" below would
                // have to have come from this call.
                expect(getToolboxUiState().openToolId).toBeUndefined();

                setOpenTool("motion");

                expect(getToolboxUiState().openToolId).toBeUndefined();
                expect(getOpenToolId()).toBeUndefined();
                expect(complaints).toHaveBeenCalledTimes(1);
            } finally {
                complaints.mockRestore();
            }
        });
    });

    // What decides whether a tool actually runs; see useToolLifecycle.ts.
    describe("what the running tool should be doing", () => {
        it("starts with nothing open, hidden, on page generation 0", () => {
            expect(getOpenToolId()).toBeUndefined();
            expect(getToolboxUiState().toolboxVisible).toBe(false);
            expect(getToolboxUiState().pageGeneration).toBe(0);
        });

        it("has an open tool once one is opened, and none again once it goes", () => {
            offerTool("motion");
            setOpenTool("motion");
            expect(getOpenToolId()).toBe("motion");

            withdrawTool("motion");
            expect(getOpenToolId()).toBeUndefined();
        });

        it("says nothing changed when the toolbox is told the visibility it already has", () => {
            setToolboxVisible(true);
            const before = getToolboxUiState();

            setToolboxVisible(true);

            // Same snapshot, so React is not asked to re-render (and so no tool is
            // needlessly re-activated).
            expect(getToolboxUiState()).toBe(before);
        });

        it("bumps the page generation each time a page becomes ready", () => {
            notePageReady();
            notePageReady();
            expect(getToolboxUiState().pageGeneration).toBe(2);
        });
    });

    // BL-16602: visiting a game page offers the Game tool and opens it; leaving the page
    // withdraws it again. Withdrawing the open tool moves the open tool on to the first
    // one left, and the running tool follows, because running is just the open tool of a
    // showing toolbox. When that had to be reported to toolbox.ts by hand and wasn't, the
    // toolbox went on believing the withdrawn tool was still open and the tool that
    // replaced it was never shown, which killed Talking Book's highlighting and audio on
    // leaving a game page.
    describe("withdrawing a tool", () => {
        it("hands running to the replacement when the withdrawn tool was the open one", () => {
            offerTool("talkingBook");
            offerTool("settings");
            offerTool("game");
            setOpenTool("game");
            // sanity check: the Game tool really is the one running before we withdraw it
            expect(getOpenToolId()).toBe("game");

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "talkingBook",
                "settings",
            ]);
            expect(getToolboxUiState().openToolId).toBe("talkingBook");
            expect(getOpenToolId()).toBe("talkingBook");
        });

        it("leaves the open tool alone when some other tool is withdrawn", () => {
            offerTool("talkingBook");
            offerTool("settings");
            offerTool("game");
            setOpenTool("talkingBook");

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "talkingBook",
                "settings",
            ]);
            expect(getToolboxUiState().openToolId).toBe("talkingBook");
            expect(getOpenToolId()).toBe("talkingBook");
        });

        it("leaves nothing open, and nothing running, when nothing is left", () => {
            offerTool("game");
            setOpenTool("game");
            expect(getOpenToolId()).toBe("game");

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([]);
            expect(getToolboxUiState().openToolId).toBeUndefined();
            expect(getOpenToolId()).toBeUndefined();
        });

        it("does nothing at all when the tool isn't being offered", () => {
            offerTool("talkingBook");
            setOpenTool("talkingBook");
            const before = getToolboxUiState();

            withdrawTool("game");

            expect(getToolboxUiState()).toBe(before);
        });
    });

    describe("which tools the book has enabled", () => {
        it("takes the whole set at startup, then individual changes", () => {
            setEnabledTools(["motion", "canvas"]);
            expect(isToolEnabled("motion")).toBe(true);
            expect(isToolEnabled("music")).toBe(false);

            setToolEnabled("music", true);
            setToolEnabled("motion", false);

            expect(isToolEnabled("music")).toBe(true);
            expect(isToolEnabled("motion")).toBe(false);
        });

        it("replaces the set rather than mutating it", () => {
            setEnabledTools(["motion"]);
            const before = getToolboxUiState().enabledToolIds;

            setToolEnabled("canvas", true);

            expect(getToolboxUiState().enabledToolIds).not.toBe(before);
            expect(Array.from(before)).toEqual(["motion"]);
        });
    });

    describe("whether the toolbox UI exists", () => {
        // This is what keeps reader stage/level persistence from firing in unit tests and
        // before ToolboxRoot has mounted; see readerToolsModel saveState/restoreState.
        it("is false until ToolboxRoot says otherwise", () => {
            expect(isToolboxUiMounted()).toBe(false);

            setToolboxUiMounted(true);
            expect(isToolboxUiMounted()).toBe(true);

            setToolboxUiMounted(false);
            expect(isToolboxUiMounted()).toBe(false);
        });
    });
});
