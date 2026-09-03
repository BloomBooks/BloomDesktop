import { beforeEach, describe, expect, it } from "vitest";
import {
    clearActiveTool,
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
    setCurrentToolId,
    setEnabledTools,
    setToolEnabled,
    setToolboxUiMounted,
    setToolboxVisible,
    subscribeToActiveToolChanges,
    subscribeToToolboxUiState,
    withdrawTool,
} from "./toolboxState";

// The toolbox's state, tested without React. The behaviour that matters most here is
// what happens when the active tool is withdrawn (BL-16602); see that describe block.

describe("toolboxState", () => {
    beforeEach(() => {
        resetToolboxUiStateForTests();
    });

    // Records every tool id reported to the active-tool listeners, which is how toolbox.ts
    // learns which tool it must record as the current (running) one.
    const recordActiveToolReports = (): string[] => {
        const reportedToolIds: string[] = [];
        subscribeToActiveToolChanges((toolId) => reportedToolIds.push(toolId));
        return reportedToolIds;
    };

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

    describe("which tool is active", () => {
        it("reports every tool made active, so toolbox.ts can make it current", () => {
            const reportedToolIds = recordActiveToolReports();
            offerTool("motion");

            setActiveTool("motion");

            expect(getToolboxUiState().activeToolId).toBe("motion");
            expect(reportedToolIds).toEqual(["motion"]);
        });

        it("clears the active tool without reporting it", () => {
            const reportedToolIds = recordActiveToolReports();
            offerTool("motion");
            setActiveTool("motion");

            clearActiveTool();

            expect(getToolboxUiState().activeToolId).toBeUndefined();
            // Nothing is reported: these listeners are about a tool *becoming* current.
            expect(reportedToolIds).toEqual(["motion"]);
        });

        it("leaves the current tool running when its section is collapsed", () => {
            offerTool("motion");
            setActiveTool("motion");
            setCurrentToolId("motion");

            clearActiveTool();

            // Collapsing a section is only how the toolbox looks. The tool goes on
            // running, as it always has.
            expect(getCurrentToolId()).toBe("motion");
        });
    });

    // What decides whether a tool actually runs; see useToolLifecycle.ts.
    describe("what the current tool should be doing", () => {
        it("starts with no current tool, hidden, on page generation 0", () => {
            expect(getCurrentToolId()).toBeUndefined();
            expect(isToolboxVisible()).toBe(false);
            expect(getPageGeneration()).toBe(0);
        });

        it("records the current tool, and that there is none", () => {
            setCurrentToolId("motion");
            expect(getCurrentToolId()).toBe("motion");

            setCurrentToolId(undefined);
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

    // BL-16602: visiting a game page offers the Game tool and makes it active; leaving the
    // page withdraws it again. Which tool runs follows from the current tool, and toolbox.ts
    // learns about activation changes only from the active-tool listeners, so if withdrawing
    // the active tool doesn't report the replacement, the toolbox goes on believing the
    // withdrawn tool is current and the tool that replaced it is never shown. That killed
    // Talking Book's highlighting and audio on leaving a game page.
    describe("withdrawing a tool", () => {
        it("reports the replacement when the withdrawn tool was the active one", () => {
            offerTool("talkingBook");
            offerTool("settings");
            offerTool("game");
            setActiveTool("game");
            const reportedToolIds = recordActiveToolReports();

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "talkingBook",
                "settings",
            ]);
            expect(getToolboxUiState().activeToolId).toBe("talkingBook");
            expect(reportedToolIds).toEqual(["talkingBook"]);
        });

        it("leaves the active tool alone when some other tool is withdrawn", () => {
            offerTool("talkingBook");
            offerTool("settings");
            offerTool("game");
            setActiveTool("talkingBook");
            const reportedToolIds = recordActiveToolReports();

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([
                "talkingBook",
                "settings",
            ]);
            expect(getToolboxUiState().activeToolId).toBe("talkingBook");
            expect(reportedToolIds).toEqual([]);
        });

        it("clears the active tool, without reporting, when nothing is left", () => {
            offerTool("game");
            setActiveTool("game");
            const reportedToolIds = recordActiveToolReports();

            withdrawTool("game");

            expect(getToolboxUiState().offeredToolIds).toEqual([]);
            expect(getToolboxUiState().activeToolId).toBeUndefined();
            // Deliberately silent: these listeners are about a tool *becoming* current,
            // and expanding a section later will tell them then.
            expect(reportedToolIds).toEqual([]);
        });

        it("does nothing at all when the tool isn't being offered", () => {
            offerTool("talkingBook");
            setActiveTool("talkingBook");
            const reportedToolIds = recordActiveToolReports();
            const before = getToolboxUiState();

            withdrawTool("game");

            expect(getToolboxUiState()).toBe(before);
            expect(reportedToolIds).toEqual([]);
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
