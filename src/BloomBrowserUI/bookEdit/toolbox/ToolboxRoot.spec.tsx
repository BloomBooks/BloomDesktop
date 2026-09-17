import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";

// The real ./toolbox module drags in the whole legacy toolbox (jQuery, every tool, the
// edit-page frames...). All ToolboxRoot wants from it is the list of tools that exist,
// which it uses to decide which enabled tool ids are real.
vi.mock("./toolbox", () => {
    return {
        getMasterToolList: () => [
            { id: () => "talkingBook" },
            { id: () => "game" },
        ],
    };
});

// The real LocalizedString wants a live localization manager. The tool labels aren't what
// these tests are about, so just show the English.
vi.mock("../../react_components/l10nComponents", async (importOriginal) => {
    const actual =
        await importOriginal<
            typeof import("../../react_components/l10nComponents")
        >();
    const reactModule = await import("react");
    return {
        ...actual,
        LocalizedString: (props: { children?: React.ReactNode }) =>
            reactModule.createElement("span", undefined, props.children),
    };
});

vi.mock("axios", () => {
    return {
        default: {
            get: (url: string) => {
                if (url.includes("toolbox/enabledTools")) {
                    return Promise.resolve({ data: "talkingBook,settings" });
                }
                // Nothing else here should be making requests; be loud rather than
                // quietly returning something plausible if that ever changes.
                return Promise.reject(new Error(`unexpected GET of ${url}`));
            },
        },
    };
});

// Imported after the mocks above are registered.
const { ToolboxRoot } = await import("./ToolboxRoot");

// The adapter object is rebuilt on every render, so always read the current one rather
// than holding on to it.
const getAdapter = () => {
    const adapter = window.toolboxReactAdapter;
    if (!adapter) {
        throw new Error(
            "ToolboxRoot did not publish window.toolboxReactAdapter; the component probably failed to render.",
        );
    }
    return adapter;
};

// Each accordion header carries the tool's id on its icon span, so this is the order of
// the sections the user would see.
const getHeaderToolIds = (container: HTMLElement): string[] =>
    Array.from(
        container.querySelectorAll(".MuiAccordionSummary-root [data-toolid]"),
    ).map((element) => element.getAttribute("data-toolid") ?? "");

describe("ToolboxRoot", () => {
    let container: HTMLDivElement | null = null;

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
    });

    afterEach(() => {
        if (container) {
            unmountRoot(container);
            container.remove();
            container = null;
        }
        delete window.toolboxReactAdapter;
    });

    // BL-16602: visiting a game page auto-activates the Game tool; leaving the page removes
    // it again. The legacy toolbox code owns each tool's showTool()/hideTool() lifecycle and
    // learns about activation changes only through onActiveToolChanged, so if removing the
    // active tool doesn't report the replacement, the tool the user can see is never
    // activated. That left the Talking Book tool doing no highlighting at all.
    it("reports the replacement tool when the active tool is removed", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }

        await act(async () => {
            renderRoot(<ToolboxRoot />, container);
        });

        const reportedToolIds: string[] = [];
        getAdapter().onActiveToolChanged((toolId) => {
            reportedToolIds.push(toolId);
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);

        // Arriving on a game page: legacy code adds the required Game tool and makes it active.
        await act(async () => {
            window.dispatchEvent(
                new CustomEvent("toolbox-tool-added", {
                    detail: { toolId: "gameTool" },
                }),
            );
            getAdapter().setActiveToolByToolId("gameTool");
        });

        // Sanity checks, so that a failure below can't just mean the setup never worked.
        expect(getHeaderToolIds(container)).toEqual([
            "game",
            "talkingBook",
            "settings",
        ]);
        expect(getAdapter().getActiveToolId()).toBe("gameTool");
        expect(reportedToolIds).toEqual(["gameTool"]);

        // Leaving the game page: legacy code removes the Game tool it required.
        await act(async () => {
            window.dispatchEvent(
                new CustomEvent("toolbox-tool-removed", {
                    detail: { toolId: "gameTool" },
                }),
            );
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);
        expect(getAdapter().getActiveToolId()).toBe("talkingBookTool");
        expect(reportedToolIds).toEqual(["gameTool", "talkingBookTool"]);
    });

    it("leaves the active tool alone when some other tool is removed", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }

        await act(async () => {
            renderRoot(<ToolboxRoot />, container);
        });

        const reportedToolIds: string[] = [];
        getAdapter().onActiveToolChanged((toolId) => {
            reportedToolIds.push(toolId);
        });

        await act(async () => {
            window.dispatchEvent(
                new CustomEvent("toolbox-tool-added", {
                    detail: { toolId: "gameTool" },
                }),
            );
            getAdapter().setActiveToolByToolId("talkingBookTool");
        });

        expect(getAdapter().getActiveToolId()).toBe("talkingBookTool");
        expect(reportedToolIds).toEqual(["talkingBookTool"]);

        await act(async () => {
            window.dispatchEvent(
                new CustomEvent("toolbox-tool-removed", {
                    detail: { toolId: "gameTool" },
                }),
            );
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);
        // Removing a tool that wasn't active must not disturb the active tool.
        expect(getAdapter().getActiveToolId()).toBe("talkingBookTool");
        expect(reportedToolIds).toEqual(["talkingBookTool"]);
    });
});
