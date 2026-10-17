// Tests of writeToBloomLog: the page history's lines reach Bloom's log, without the repeats that
// typing makes; and of makeStackSender, which keeps the Show Page History dialog's Stack tab up to
// date.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
    postString: vi.fn<(urlSuffix: string, value: string) => void>(),
}));
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../utils/bloomApi")>()),
    postString: mocks.postString,
}));

import { makeStackSender, writeToBloomLog } from "./pageHistorySetup";
import { type PageHistory } from "./pageHistoryTypes";
import { createPageHistory } from "./pageHistory";
import { PageHistoryRecorder } from "./pageHistoryRecorder";

const kGenericStep = "Page history: did Edit";

function loggedLines(): string[] {
    return mocks.postString.mock.calls.map((call) => {
        expect(call[0]).toBe("common/logger/writeEvent");
        return call[1];
    });
}

describe("writeToBloomLog", () => {
    beforeEach(() => {
        // Start each test after a line that none of them writes.
        writeToBloomLog("Page history: test start");
        mocks.postString.mockClear();
    });

    it("leaves out a step with the general description that repeats the line before it", () => {
        writeToBloomLog(kGenericStep);
        writeToBloomLog(kGenericStep);
        writeToBloomLog(kGenericStep);
        writeToBloomLog("Page history: undid EditTab.CutButton");
        writeToBloomLog(kGenericStep);
        expect(loggedLines()).toEqual([
            kGenericStep,
            "Page history: undid EditTab.CutButton",
            kGenericStep,
        ]);
    });

    it("keeps repeated lines that name other steps", () => {
        writeToBloomLog("Page history: did EditTab.CutButton");
        writeToBloomLog("Page history: did EditTab.CutButton");
        writeToBloomLog("Page history: undid Edit");
        writeToBloomLog("Page history: undid Edit");
        expect(loggedLines()).toEqual([
            "Page history: did EditTab.CutButton",
            "Page history: did EditTab.CutButton",
            "Page history: undid Edit",
            "Page history: undid Edit",
        ]);
    });
});

describe("makeStackSender", () => {
    let page: HTMLElement | undefined;
    let history: PageHistory | undefined;

    afterEach(() => {
        if (history && !history.isDisposed) history.dispose();
        page?.remove();
        history = undefined;
        page = undefined;
    });

    // Lets the microtasks queued so far run, the sender's among them.
    async function letMicrotasksRun(): Promise<void> {
        await Promise.resolve();
        await Promise.resolve();
    }

    it("sends the steps once the history's work is done, once however often it is asked, and nothing once the history is disposed", async () => {
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML = '<p id="p1">Hello</p>';
        document.body.appendChild(page);
        const p = page.querySelector("#p1")!;
        const recorder = new PageHistoryRecorder(() => 0);
        const load = recorder.notePageLoad("page-1");
        let sendStackSoon = () => {};
        const thisHistory = createPageHistory({
            root: page,
            onUnmarkedChange: () => {},
            log: () => sendStackSoon(),
        });
        history = thisHistory;
        sendStackSoon = makeStackSender(thisHistory, recorder, load);
        const listener = vi.fn();
        recorder.subscribe(listener);

        await thisHistory.doTransaction("A", () => p.setAttribute("a", "1"));
        await thisHistory.doTransaction("B", () => p.setAttribute("b", "1"));
        await thisHistory.undo();
        await letMicrotasksRun();
        expect(recorder.getStack()).toEqual({
            undo: ["A"],
            redo: ["B"],
            hasOpenTransaction: false,
            isInChangeLayoutMode: false,
        });

        // The history logs a clear before it drops its steps.
        thisHistory.clearUndoAndRedo("a test");
        expect(
            recorder.getStack()?.undo,
            "nothing should be sent until the clear has dropped the steps",
        ).toEqual(["A"]);
        await letMicrotasksRun();
        expect(recorder.getStack()?.undo).toEqual([]);
        expect(recorder.getStack()?.redo).toEqual([]);

        const callsBefore = listener.mock.calls.length;
        thisHistory.beginTransaction("Drag");
        sendStackSoon();
        sendStackSoon();
        await letMicrotasksRun();
        expect(recorder.getStack()?.hasOpenTransaction).toBe(true);
        expect(
            listener.mock.calls.length - callsBefore,
            "two requests in one task should send once",
        ).toBe(1);

        thisHistory.dispose();
        sendStackSoon();
        await letMicrotasksRun();
        expect(recorder.getStack()?.hasOpenTransaction).toBe(true);
    });
});
