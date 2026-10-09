import { beforeEach, describe, expect, it } from "vitest";
import {
    getCurrentToolId,
    getFirstOfferedToolId,
    getPageGeneration,
    getToolboxUiState,
    isToolEnabled,
    isToolOffered,
    isToolboxUiMounted,
    isToolboxVisible,
    notePageReady,
    offerTool,
    resetToolboxUiStateForTests,
    setActiveTool,
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

            // "More..." is never the first *tool*: it can't be the current tool.
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

            setActiveTool("motion");

            expect(getToolboxUiState().activeToolId).toBe("motion");
            expect(getCurrentToolId()).toBe("motion");
        });

        it("runs no tool that the toolbox is not offering", () => {
            // A tool the toolbox isn't offering has nowhere to display itself, so it
            // cannot be the running one however it came to be the open one. Recording
            // that as "no current tool" is what lets returning from "More..." to the same
            // tool activate it again (BL-6720).
            setActiveTool("motion");

            expect(getToolboxUiState().activeToolId).toBe("motion");
            expect(getCurrentToolId()).toBeUndefined();
        });
    });

    // What decides whether a tool actually runs; see useToolLifecycle.ts.
    describe("what the current tool should be doing", () => {
        it("starts with no current tool, hidden, on page generation 0", () => {
            expect(getCurrentToolId()).toBeUndefined();
            expect(isToolboxVisible()).toBe(false);
            expect(getPageGeneration()).toBe(0);
        });

        it("has a current tool once one is open, and none again once it goes", () => {
            offerTool("motion");
            setActiveTool("motion");
            expect(getCurrentToolId()).toBe("motion");

            withdrawTool("motion");
            expect(getCurrentToolId()).toBeUndefined();
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
            expect(getPageGeneration()).toBe(2);
        });
    });

    // BL-16602: visiting a game page offers the Game tool and makes it open; leaving the
    // page withdraws it again. Which tool runs is derived from which one is open and which
    // are offered, so withdrawing the open tool moves the running tool on its own. When
    // that had to be reported to toolbox.ts by hand and wasn't, the toolbox went on
    // believing the withdrawn tool was current and the tool that replaced it was never
    // shown, which killed Talking Book's highlighting and audio on leaving a game page.
    describe("withdrawing a tool", () => {
        it("hands running to the replacement when the withdrawn tool was the open one", () => {
            offerTool("talkingBook");
            offerTool("settings");
            offerTool("game");
            setActiveTool("game");
            // sanity check: the Game tool really is the one running before we withdraw it
            expect(getCurrentToolId()).toBe("game");

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "talkingBook",
                "settings",
            ]);
            expect(getToolboxUiState().activeToolId).toBe("talkingBook");
            expect(getCurrentToolId()).toBe("talkingBook");
        });

        it("leaves the open tool alone when some other tool is withdrawn", () => {
            offerTool("talkingBook");
            offerTool("settings");
            offerTool("game");
            setActiveTool("talkingBook");

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "talkingBook",
                "settings",
            ]);
            expect(getToolboxUiState().activeToolId).toBe("talkingBook");
            expect(getCurrentToolId()).toBe("talkingBook");
        });

        it("leaves nothing open, and nothing running, when nothing is left", () => {
            offerTool("game");
            setActiveTool("game");
            expect(getCurrentToolId()).toBe("game");

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([]);
            expect(getToolboxUiState().activeToolId).toBeUndefined();
            expect(getCurrentToolId()).toBeUndefined();
        });

        it("does nothing at all when the tool isn't being offered", () => {
            offerTool("talkingBook");
            setActiveTool("talkingBook");
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
