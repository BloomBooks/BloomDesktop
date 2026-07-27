import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";

// The real ./toolbox module drags in the whole legacy toolbox (jQuery, every tool, the
// edit-page frames...). All ToolboxRoot wants from it is the list of tools that exist,
// which it uses to build a section for each tool it is told to offer.
const makeFakeTool = (id: string, featureName?: string) => ({
    id: () => id,
    iconPath: () => `/bloom/images/${id}.svg`,
    featureName,
    renderPanel: () => <div>{`${id} panel`}</div>,
});

vi.mock("./toolbox", () => {
    return {
        getMasterToolList: () => [
            makeFakeTool("talkingBook"),
            makeFakeTool("game"),
            makeFakeTool("settings"),
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

// ToolboxRoot no longer asks the server which tools are enabled; toolbox.ts owns that and
// tells us through addTool(). Fail loudly if that ever regresses into a fetch from here.
vi.mock("axios", () => {
    return {
        default: {
            get: (url: string) =>
                Promise.reject(new Error(`unexpected GET of ${url}`)),
        },
    };
});

// Imported after the mocks above are registered.
const { ToolboxRoot } = await import("./ToolboxRoot");
const { getToolboxReactAdapter } = await import("./toolboxReactAdapter");

const getAdapter = () => {
    const adapter = getToolboxReactAdapter();
    if (!adapter) {
        throw new Error(
            "ToolboxRoot did not register an adapter; the component probably failed to render.",
        );
    }
    return adapter;
};

// Each accordion header carries the tool's canonical id on its icon span, so this is the
// order of the sections the user would see.
const getHeaderToolIds = (container: HTMLElement): string[] =>
    Array.from(
        container.querySelectorAll(".MuiAccordionSummary-root [data-toolid]"),
    ).map((element) => element.getAttribute("data-toolid") ?? "");

// Which section the user actually has open.
const getExpandedToolId = (container: HTMLElement): string | undefined => {
    const expandedHeader = Array.from(
        container.querySelectorAll(".MuiAccordionSummary-root"),
    ).find((header) => header.getAttribute("aria-expanded") === "true");
    return (
        expandedHeader
            ?.querySelector("[data-toolid]")
            ?.getAttribute("data-toolid") ?? undefined
    );
};

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
    });

    // Offer the two tools every book has, the way toolbox.ts does at startup.
    const renderWithBaseTools = async (host: HTMLDivElement) => {
        await act(async () => {
            renderRoot(<ToolboxRoot />, host);
        });
        await act(async () => {
            getAdapter().addTool("talkingBook");
            getAdapter().addTool("settings");
        });
    };

    // BL-16602: visiting a game page auto-activates the Game tool; leaving the page removes
    // it again. toolbox.ts owns each tool's showTool()/hideTool() lifecycle and learns about
    // activation changes only through onActiveToolChanged, so if removing the active tool
    // doesn't report the replacement, the tool the user can see is never activated. That
    // left the Talking Book tool doing no highlighting at all.
    it("reports the replacement tool when the active tool is removed", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }
        await renderWithBaseTools(container);

        const reportedToolIds: string[] = [];
        getAdapter().onActiveToolChanged((toolId) => {
            reportedToolIds.push(toolId);
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);

        // Arriving on a game page: toolbox.ts adds the required Game tool and makes it active.
        await act(async () => {
            getAdapter().addTool("game");
            getAdapter().setActiveToolByToolId("game");
        });

        // Sanity checks, so that a failure below can't just mean the setup never worked.
        expect(getHeaderToolIds(container)).toEqual([
            "game",
            "talkingBook",
            "settings",
        ]);
        expect(getExpandedToolId(container)).toBe("game");
        expect(reportedToolIds).toEqual(["game"]);

        // Leaving the game page: toolbox.ts removes the Game tool it required.
        await act(async () => {
            getAdapter().removeTool("game");
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);
        expect(getExpandedToolId(container)).toBe("talkingBook");
        expect(reportedToolIds).toEqual(["game", "talkingBook"]);
    });

    it("leaves the active tool alone when some other tool is removed", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }
        await renderWithBaseTools(container);

        const reportedToolIds: string[] = [];
        getAdapter().onActiveToolChanged((toolId) => {
            reportedToolIds.push(toolId);
        });

        await act(async () => {
            getAdapter().addTool("game");
            getAdapter().setActiveToolByToolId("talkingBook");
        });

        expect(getExpandedToolId(container)).toBe("talkingBook");
        expect(reportedToolIds).toEqual(["talkingBook"]);

        await act(async () => {
            getAdapter().removeTool("game");
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);
        // Removing a tool that wasn't active must not disturb the active tool.
        expect(getExpandedToolId(container)).toBe("talkingBook");
        expect(reportedToolIds).toEqual(["talkingBook"]);
    });
});
