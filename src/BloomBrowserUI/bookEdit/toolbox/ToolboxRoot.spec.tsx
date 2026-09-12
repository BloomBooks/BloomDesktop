import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";

// The real ./toolbox module drags in the whole legacy toolbox (jQuery, every tool, the
// edit-page frames...). All ToolboxRoot wants from it is the list of tools that exist,
// which it uses to build a section for each tool the store says it is offering.
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
        // The sections' lifecycle hook wants these (see useToolLifecycle.ts). No tool is
        // running in these tests — the store's currentToolId is never set — so they are
        // never actually called; they are here so that this mock stays a complete stand-in.
        getSavedToolboxSettings: () => ({}),
        runTasksForClosingTool: () => undefined,
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
// records it in the store. Fail loudly if that ever regresses into a fetch from here.
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
const {
    offerTool,
    resetToolboxUiStateForTests,
    setActiveTool,
    subscribeToActiveToolChanges,
    withdrawTool,
} = await import("./toolboxState");

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
        resetToolboxUiStateForTests();
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
            offerTool("talkingBook");
            offerTool("settings");
        });
    };

    // BL-16602: visiting a game page auto-activates the Game tool; leaving the page removes
    // it again. Which tool runs follows from the current tool, and toolbox.ts learns about
    // activation changes only through the store's active-tool listeners, so if withdrawing
    // the active tool doesn't report the replacement, the tool the user can see is never
    // activated. That left the Talking Book tool doing no highlighting at all.
    it("shows the replacement tool when the active tool is withdrawn", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }
        await renderWithBaseTools(container);

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);

        // Arriving on a game page: toolbox.ts offers the required Game tool and makes it
        // active.
        await act(async () => {
            offerTool("game");
            setActiveTool("game");
        });

        // Sanity checks, so that a failure below can't just mean the setup never worked.
        expect(getHeaderToolIds(container)).toEqual([
            "game",
            "talkingBook",
            "settings",
        ]);
        expect(getExpandedToolId(container)).toBe("game");

        // Leaving the game page: toolbox.ts withdraws the Game tool it required.
        await act(async () => {
            withdrawTool("game");
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);
        expect(getExpandedToolId(container)).toBe("talkingBook");
    });

    it("leaves the active tool alone when some other tool is withdrawn", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }
        await renderWithBaseTools(container);

        await act(async () => {
            offerTool("game");
            setActiveTool("talkingBook");
        });

        expect(getExpandedToolId(container)).toBe("talkingBook");

        await act(async () => {
            withdrawTool("game");
        });

        expect(getHeaderToolIds(container)).toEqual([
            "talkingBook",
            "settings",
        ]);
        // Withdrawing a tool that wasn't active must not disturb the active tool.
        expect(getExpandedToolId(container)).toBe("talkingBook");
    });

    // The tool panels are ordinary children of the sections, so they hold their own state.
    // Rebuilding the sections on every store change would throw that away.
    it("does not remount a tool's panel when another tool is offered", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }
        await renderWithBaseTools(container);

        const talkingBookPanelBefore = container.querySelector(
            '[data-toolid="talkingBookTool"] > *',
        );
        expect(talkingBookPanelBefore).toBeTruthy();

        await act(async () => {
            offerTool("game");
        });

        expect(
            container.querySelector('[data-toolid="talkingBookTool"] > *'),
        ).toBe(talkingBookPanelBefore);
    });

    // Clicking a header is the other way a tool becomes active, and toolbox.ts has to hear
    // about it (again, BL-16602: it is what makes the tool the current one, which is what
    // gets it shown).
    it("reports the tool whose header the user clicks", async () => {
        if (!container) {
            throw new Error("render container not initialized");
        }
        await renderWithBaseTools(container);

        const reportedToolIds: string[] = [];
        const unsubscribe = subscribeToActiveToolChanges((toolId) =>
            reportedToolIds.push(toolId),
        );

        const settingsHeader = Array.from(
            container.querySelectorAll(".MuiAccordionSummary-root"),
        ).find(
            (header) =>
                header
                    .querySelector("[data-toolid]")
                    ?.getAttribute("data-toolid") === "settings",
        ) as HTMLElement;

        await act(async () => {
            settingsHeader.click();
        });

        expect(reportedToolIds).toEqual(["settings"]);
        expect(getExpandedToolId(container)).toBe("settings");

        unsubscribe();
    });
});
