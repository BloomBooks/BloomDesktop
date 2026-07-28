// Tests of useToolLifecycle: the tool lifecycle that used to be a sequence of calls in
// toolbox.ts and is now a consequence of rendering. What matters here is not that each
// method gets called but that they happen in the order the tools have always relied on.
import * as React from "react";
import { act } from "react";
import { createRoot, Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// toolbox.ts drags in every tool in the toolbox (and jQuery, CKEditor typings, the page
// iframe...). All this hook needs from it is the saved settings and the closing-tool tasks.
vi.mock("./toolbox", () => ({
    getSavedToolboxSettings: () => ({ fakeSetting: "x" }),
    runTasksForClosingTool: () => calls.push("runClosingTasks"),
}));
vi.mock("../../utils/bloomApi", () => ({
    postString: (api: string, data: string) =>
        calls.push(`post ${api} ${data}`),
}));

import { ITool } from "./toolbox";
import { useToolLifecycle } from "./useToolLifecycle";

// Everything the tools and the hook did, in order. The whole point of these tests.
let calls: string[] = [];

// A tool that records what it is asked to do. Only the lifecycle methods matter here, so
// the rest are the do-nothing stubs ToolboxToolReactAdaptor would have supplied.
function makeFakeTool(
    id: string,
    options: { showToolTakesTwoTicks?: boolean } = {},
): ITool {
    const record = (what: string) => calls.push(`${id}.${what}`);
    return {
        id: () => id,
        beginRestoreSettings: async () => {
            record("beginRestoreSettings");
        },
        showTool: async () => {
            record("showTool start");
            if (options.showToolTakesTwoTicks) {
                await Promise.resolve();
                await Promise.resolve();
            }
            record("showTool end");
        },
        newPageReady: () => record("newPageReady"),
        detachFromPage: () => record("detachFromPage"),
        hideTool: () => record("hideTool"),
        configureElements: () => undefined,
        updateMarkup: () => undefined,
        updateMarkupAsync: async () => () => undefined,
        isUpdateMarkupAsync: () => false,
        isAlwaysEnabled: () => false,
        requiresToolId: () => false,
        renderPanel: () => null,
        imageUpdated: () => undefined,
        iconPath: () => undefined,
    };
}

// Stands in for ToolboxRoot: a section per tool, each running its tool's lifecycle, with at
// most one of them the running tool. Like the real toolbox, the sections of the tools that
// are not running stay mounted.
const Section: React.FunctionComponent<{
    tool: ITool;
    isRunning: boolean;
    pageGeneration: number;
}> = (props) => {
    useToolLifecycle(props.tool, props.isRunning, props.pageGeneration);
    return <div>{props.tool.id()}</div>;
};

const Toolbox: React.FunctionComponent<{
    tools: ITool[];
    runningToolId?: string;
    pageGeneration: number;
}> = (props) => (
    <div>
        {props.tools.map((tool) => (
            <Section
                key={tool.id()}
                tool={tool}
                isRunning={tool.id() === props.runningToolId}
                pageGeneration={props.pageGeneration}
            />
        ))}
    </div>
);

let host: HTMLDivElement;
let root: Root;

// Renders and lets the lifecycle's promises settle, since the sequence is async.
const render = async (element: React.ReactElement): Promise<void> => {
    await act(async () => {
        root.render(element);
    });
};

beforeEach(() => {
    calls = [];
    host = document.createElement("div");
    document.body.appendChild(host);
    root = createRoot(host);
});

afterEach(async () => {
    await act(async () => {
        root.unmount();
    });
    host.remove();
    vi.useRealTimers();
});

describe("useToolLifecycle", () => {
    it("restores, shows and reports the page ready, in that order", async () => {
        const talkingBook = makeFakeTool("talkingBook");

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );

        expect(calls).toEqual([
            "talkingBook.beginRestoreSettings",
            "post logger/writeEvent Toolbox activated: talkingBook",
            "talkingBook.showTool start",
            "talkingBook.showTool end",
            "talkingBook.newPageReady",
        ]);
    });

    it("does not report the page ready until an async showTool has finished", async () => {
        const talkingBook = makeFakeTool("talkingBook", {
            showToolTakesTwoTicks: true,
        });

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );

        expect(calls).toEqual([
            "talkingBook.beginRestoreSettings",
            "post logger/writeEvent Toolbox activated: talkingBook",
            "talkingBook.showTool start",
            "talkingBook.showTool end",
            "talkingBook.newPageReady",
        ]);
    });

    it("runs nothing for a tool that is offered but is not the running one", async () => {
        const talkingBook = makeFakeTool("talkingBook");
        const motion = makeFakeTool("motion");

        await render(
            <Toolbox
                tools={[talkingBook, motion]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );

        expect(calls.filter((call) => call.startsWith("motion."))).toEqual([]);
    });

    it("detaches and hides the outgoing tool before restoring and showing the incoming one", async () => {
        const talkingBook = makeFakeTool("talkingBook");
        const motion = makeFakeTool("motion");
        const tools = [talkingBook, motion];

        await render(
            <Toolbox
                tools={tools}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );
        calls = [];

        await render(
            <Toolbox tools={tools} runningToolId="motion" pageGeneration={1} />,
        );

        expect(calls).toEqual([
            // This is the ordering the whole conversion turns on: everything the outgoing
            // tool does happens before anything the incoming one does.
            "runClosingTasks",
            "talkingBook.detachFromPage",
            "talkingBook.hideTool",
            "motion.beginRestoreSettings",
            "post logger/writeEvent Toolbox activated: motion",
            "motion.showTool start",
            "motion.showTool end",
            "motion.newPageReady",
        ]);
    });

    it("detaches and hides a tool the toolbox stops offering, before showing its replacement", async () => {
        // What leaving a game page does: the Game tool's section goes away entirely and
        // another tool becomes the running one. Getting this wrong was BL-16602.
        const game = makeFakeTool("game");
        const talkingBook = makeFakeTool("talkingBook");

        await render(
            <Toolbox
                tools={[game, talkingBook]}
                runningToolId="game"
                pageGeneration={1}
            />,
        );
        calls = [];

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={2}
            />,
        );

        expect(calls).toEqual([
            "runClosingTasks",
            "game.detachFromPage",
            "game.hideTool",
            "talkingBook.beginRestoreSettings",
            "post logger/writeEvent Toolbox activated: talkingBook",
            "talkingBook.showTool start",
            "talkingBook.showTool end",
            "talkingBook.newPageReady",
        ]);
    });

    it("detaches and hides the tool when the toolbox is hidden, and restores it when shown again", async () => {
        const talkingBook = makeFakeTool("talkingBook");

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );
        calls = [];

        // Hiding the toolbox: still the current tool, but no longer running.
        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId={undefined}
                pageGeneration={1}
            />,
        );
        expect(calls).toEqual([
            "runClosingTasks",
            "talkingBook.detachFromPage",
            "talkingBook.hideTool",
        ]);
        calls = [];

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );
        expect(calls).toEqual([
            "talkingBook.beginRestoreSettings",
            "post logger/writeEvent Toolbox activated: talkingBook",
            "talkingBook.showTool start",
            "talkingBook.showTool end",
            "talkingBook.newPageReady",
        ]);
    });

    it("re-runs the whole sequence for a new page, without hiding the tool", async () => {
        const talkingBook = makeFakeTool("talkingBook");

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );
        calls = [];

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={2}
            />,
        );

        // Restoring the settings again is how a tool's state follows a switch of book, and
        // some tools do their page-dependent setup in showTool(). But the tool was never
        // hidden, and it is not detached here: the detach for a page that is going away
        // happens before the page is replaced (removeToolboxMarkup), not after.
        expect(calls).toEqual([
            "talkingBook.beginRestoreSettings",
            "talkingBook.showTool start",
            "talkingBook.showTool end",
            "talkingBook.newPageReady",
        ]);
    });

    it("tells the tool the page is ready a second time, 600ms later", async () => {
        vi.useFakeTimers();
        const talkingBook = makeFakeTool("talkingBook");

        await render(
            <Toolbox
                tools={[talkingBook]}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );
        expect(
            calls.filter((call) => call === "talkingBook.newPageReady").length,
        ).toBe(1);

        await act(async () => {
            await vi.advanceTimersByTimeAsync(600);
        });

        expect(
            calls.filter((call) => call === "talkingBook.newPageReady").length,
        ).toBe(2);
    });

    it("does not make the delayed page-ready call if the tool stopped running first", async () => {
        vi.useFakeTimers();
        const talkingBook = makeFakeTool("talkingBook");
        const motion = makeFakeTool("motion");
        const tools = [talkingBook, motion];

        await render(
            <Toolbox
                tools={tools}
                runningToolId="talkingBook"
                pageGeneration={1}
            />,
        );
        await render(
            <Toolbox tools={tools} runningToolId="motion" pageGeneration={1} />,
        );
        calls = [];

        await act(async () => {
            await vi.advanceTimersByTimeAsync(600);
        });

        expect(calls).toEqual(["motion.newPageReady"]);
    });
});
