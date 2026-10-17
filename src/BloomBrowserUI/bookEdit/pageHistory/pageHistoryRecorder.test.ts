// Tests of the record that the Show Page History dialog shows (pageHistoryRecorder.ts), with the
// stack of the page open now, and of the rows that the page frame makes for it from the history's
// details (pageHistoryDetails.ts).
import { afterEach, describe, expect, it, vi } from "vitest";
import {
    formatPageHistoryClear,
    formatPageHistoryEvent,
    formatPageHistoryStack,
    kMaxPageHistoryEvents,
    kPageHistoryChangeLayoutModeNote,
    PageHistoryRecorder,
} from "./pageHistoryRecorder";
import {
    describeInputOpening,
    describePageHistoryDetail,
    kMaxRecordsShownPerBatch,
    quote,
} from "./pageHistoryDetails";
import { createPageHistory } from "./pageHistory";
import type { PageHistory } from "./pageHistoryTypes";

// A recorder whose clock the test sets, in milliseconds.
function makeRecorder(): {
    recorder: PageHistoryRecorder;
    clock: { ms: number };
} {
    const clock = { ms: 1000 };
    return { recorder: new PageHistoryRecorder(() => clock.ms), clock };
}

describe("PageHistoryRecorder", () => {
    it("keeps only the newest kMaxPageHistoryEvents events", () => {
        const { recorder } = makeRecorder();
        for (let i = 0; i < kMaxPageHistoryEvents + 5; i++)
            recorder.add("step", `step ${i}`);

        const events = recorder.getEvents();
        expect(events.length).toBe(kMaxPageHistoryEvents);
        expect(events[0].text, "the five oldest should have gone").toBe(
            "step 5",
        );
        expect(events[events.length - 1].text).toBe(
            `step ${kMaxPageHistoryEvents + 4}`,
        );
    });

    it("times events from the latest page load, and adds rows for page load and ready while detailed", () => {
        const { recorder, clock } = makeRecorder();
        recorder.setDetailed(true);
        clock.ms = 5000;
        recorder.notePageLoad("page-1");
        clock.ms = 6234.5;
        recorder.addLogLine("Page history: undo EditTab.Undo.Edit", "undo");
        clock.ms = 9000;
        recorder.notePageLoad("page-2");
        clock.ms = 9001;
        recorder.notePageReady();

        expect(recorder.asText().split("\n")).toEqual([
            "0.000\tPAGE\tPage loaded: page-1",
            "1.234\tUNDO\tundo EditTab.Undo.Edit",
            "0.000\tPAGE\tPage loaded: page-2",
            "0.001\tREADY\tPage ready (markPageReady)",
        ]);
    });

    it("keeps only steps, undos, redos, refusals and clears while not detailed, still timing from the page load", () => {
        const { recorder, clock } = makeRecorder();
        expect(
            recorder.isDetailed,
            "test setup: detailed logging starts off",
        ).toBe(false);
        clock.ms = 5000;
        recorder.notePageLoad("page-1");
        recorder.notePageReady();
        clock.ms = 5500;
        recorder.addLogLine("Page history: step A", "step");
        recorder.addLogLine("Page history: undo A", "undo");
        recorder.addLogLine("Page history: redo A", "redo");
        recorder.addLogLine(
            "Page history: undo of A found the page changed since, and dropped the undo steps",
            "refused",
        );
        recorder.addLogLine(
            "Page history: rollback of B reversed 2 records",
            "rollback",
        );
        recorder.addLogLine("Page history: cleared by a test", "cleared");

        expect(recorder.asText().split("\n")).toEqual([
            "0.500\tSTEP\tstep A",
            "0.500\tUNDO\tundo A",
            "0.500\tREDO\tredo A",
            "0.500\tREFUSED\tundo of A found the page changed since, and dropped the undo steps",
            "0.500\tCLEARED\tcleared by a test",
        ]);
    });

    it("labels undo, redo and rollback differently from a step", () => {
        const { recorder } = makeRecorder();
        recorder.setDetailed(true);
        recorder.addLogLine("Page history: step A", "step");
        recorder.addLogLine("Page history: undo A", "undo");
        recorder.addLogLine("Page history: redo A", "redo");
        recorder.addLogLine(
            "Page history: rollback of B reversed 2 records",
            "rollback",
        );
        expect(
            recorder
                .getEvents()
                .map((event) => formatPageHistoryEvent(event).split("\t")[1]),
        ).toEqual(["STEP", "UNDO", "REDO", "ROLLBACK"]);
    });

    it("tells each history listening whether detailed logging is on, at once and on each change", () => {
        const { recorder } = makeRecorder();
        const heard = vi.fn<(detailed: boolean) => void>();
        const stop = recorder.onDetailedChange(heard);
        expect(heard.mock.calls).toEqual([[false]]);

        recorder.setDetailed(true);
        recorder.setDetailed(true);
        recorder.setDetailed(false);
        stop();
        recorder.setDetailed(true);

        expect(
            heard.mock.calls,
            "a repeat of the same value and a change after stop() should not be heard",
        ).toEqual([[false], [true], [false]]);
        expect(recorder.isDetailed).toBe(true);
    });

    it("keeps a copy of the stack that the page frame sends, under the id of the page loaded", () => {
        const { recorder } = makeRecorder();
        expect(recorder.getStack(), "no page has loaded yet").toBeUndefined();
        const load = recorder.notePageLoad("page-1");
        expect(
            recorder.getStack(),
            "the page frame has sent no stack yet",
        ).toBeUndefined();
        const undo = ["A", "B"];
        const redo = ["C"];
        recorder.setStack(load, {
            undo,
            redo,
            hasOpenTransaction: true,
            isInChangeLayoutMode: false,
        });
        undo.push("changed after sending");
        redo.length = 0;

        expect(recorder.getStack()).toEqual({
            undo: ["A", "B"],
            redo: ["C"],
            hasOpenTransaction: true,
            isInChangeLayoutMode: false,
        });
        expect(
            recorder.getStack()!.undo,
            "the recorder should hold its own array, not the page frame's",
        ).not.toBe(undo);
    });

    it("drops the stack when its page goes or another loads, and ignores a page that has gone", () => {
        const { recorder } = makeRecorder();
        const steps = {
            undo: ["A"],
            redo: [],
            hasOpenTransaction: false,
            isInChangeLayoutMode: false,
        };
        const first = recorder.notePageLoad("page-1");
        recorder.setStack(first, steps);
        expect(recorder.getStack()?.undo, "test setup").toEqual(["A"]);

        const second = recorder.notePageLoad("page-2");
        expect(recorder.getStack()).toBeUndefined();
        recorder.setStack(first, steps);
        recorder.notePageGone(first);
        recorder.setStack(second, { ...steps, undo: ["B"] });
        recorder.notePageGone(first);
        expect(
            recorder.getStack(),
            "the first page, gone, should neither set nor remove the second's stack",
        ).toEqual({ ...steps, undo: ["B"] });

        recorder.notePageGone(second);
        expect(recorder.getStack()).toBeUndefined();
        recorder.setStack(second, steps);
        expect(
            recorder.getStack(),
            "a page that has gone should not set a stack",
        ).toBeUndefined();
    });

    it("keeps the latest clear, with what cleared the history and when, until the next page load", () => {
        const { recorder, clock } = makeRecorder();
        recorder.notePageLoad("page-1");
        expect(recorder.getLatestClear()).toBeUndefined();
        clock.ms = 2000;
        recorder.addLogLine("Page history: cleared by a test", "cleared");
        clock.ms = 13345;
        recorder.addLogLine(
            "Page history: cleared by request editview/settopic",
            "cleared",
        );
        recorder.addLogLine("Page history: step A", "step");
        recorder.clear();

        const clear = recorder.getLatestClear();
        expect(
            clear,
            "the log's Clear button should leave the latest clear",
        ).toBeDefined();
        expect(formatPageHistoryClear(clear!)).toBe(
            "History cleared at 12.345 s by request editview/settopic",
        );

        recorder.notePageLoad("page-2");
        expect(recorder.getLatestClear()).toBeUndefined();
    });

    it("tells subscribers when the stack changes, but not when the same stack comes again", () => {
        const { recorder } = makeRecorder();
        const load = recorder.notePageLoad("page-1");
        const listener = vi.fn();
        recorder.subscribe(listener);
        const steps = {
            undo: ["A"],
            redo: ["B"],
            hasOpenTransaction: false,
            isInChangeLayoutMode: false,
        };
        recorder.setStack(load, steps);
        recorder.setStack(load, { ...steps, undo: ["A"] });
        expect(listener).toHaveBeenCalledTimes(1);
        recorder.setStack(load, { ...steps, hasOpenTransaction: true });
        recorder.setStack(load, { ...steps, redo: [] });
        expect(listener).toHaveBeenCalledTimes(3);
    });

    it("writes a stack as text: the undo steps oldest first, the step in progress, the marker, then the redo steps", () => {
        expect(
            formatPageHistoryStack({
                undo: ["A", "B"],
                redo: ["C", "D"],
                hasOpenTransaction: true,
                isInChangeLayoutMode: false,
            }).split("\n"),
        ).toEqual([
            "UNDO\tA",
            "UNDO\tB",
            "\t(a step is in progress)",
            "-------- you are here --------",
            "REDO\tC",
            "REDO\tD",
        ]);
        expect(
            formatPageHistoryStack({
                undo: [],
                redo: [],
                hasOpenTransaction: false,
                isInChangeLayoutMode: true,
            }).split("\n"),
        ).toEqual([
            kPageHistoryChangeLayoutModeNote,
            "-------- you are here --------",
        ]);
        expect(formatPageHistoryStack(undefined)).toBe("(no page history)");

        const { recorder, clock } = makeRecorder();
        recorder.notePageLoad("page-1");
        clock.ms = 1500;
        recorder.addLogLine("Page history: cleared by a test", "cleared");
        expect(
            formatPageHistoryStack(
                {
                    undo: ["A"],
                    redo: [],
                    hasOpenTransaction: false,
                    isInChangeLayoutMode: false,
                },
                recorder.getLatestClear(),
            ).split("\n"),
        ).toEqual([
            "History cleared at 0.500 s by a test",
            "UNDO\tA",
            "-------- you are here --------",
        ]);
    });

    it("changes its change count, and tells subscribers, on each change; clear empties it", () => {
        const { recorder } = makeRecorder();
        const listener = vi.fn();
        recorder.subscribe(listener);
        const before = recorder.getChangeCount();
        recorder.add("step", "A");
        recorder.setDetailed(true);
        recorder.clear();
        expect(listener).toHaveBeenCalledTimes(3);
        expect(recorder.getChangeCount()).toBe(before + 3);
        expect(recorder.getEvents()).toEqual([]);
    });
});

describe("the rows of detailed logging, from a real history", () => {
    let page: HTMLElement | undefined;
    let history: PageHistory | undefined;

    afterEach(() => {
        history?.dispose();
        page?.remove();
        history = undefined;
        page = undefined;
    });

    // A history on a small page, whose details become rows in recorder while detailed logging is
    // on, wired as pageHistorySetup.ts wires them.
    function setUpWiredHistory(recorder: PageHistoryRecorder): HTMLElement {
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML =
            '<div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p id="p1">Hello</p></div></div>';
        document.body.appendChild(page);
        const thisHistory = createPageHistory({
            root: page,
            onUnmarkedChange: () => {},
            log: (line, kind) => recorder.addLogLine(line, kind),
        });
        history = thisHistory;
        recorder.onDetailedChange((detailed) =>
            thisHistory.setDetailListener(
                detailed
                    ? (detail) =>
                          describePageHistoryDetail(detail).forEach((row) =>
                              recorder.add(row.kind, row.text),
                          )
                    : undefined,
            ),
        );
        return page.querySelector("#p1")!;
    }

    it("records no detail while detailed logging is off, and every transaction and change while on", async () => {
        const { recorder } = makeRecorder();
        const p = setUpWiredHistory(recorder);
        const text = p.firstChild as Text;
        await history!.doTransaction("A", () => {
            text.data = "Hello!";
        });
        expect(
            recorder.getEvents().map((event) => event.kind),
            "with detailed logging off, only the step",
        ).toEqual(["step"]);

        recorder.setDetailed(true);
        await history!.doTransaction("B", () => {
            text.data = "Hello!!";
        });
        recorder.setDetailed(false);
        await history!.doTransaction("C", () => {
            text.data = "Hello!!!";
        });

        expect(recorder.asText().split("\n").map(withoutTime)).toEqual([
            "STEP\tstep A",
            "TXN\topened B (depth 1)",
            'CHANGE\tin B (depth 1): text of editable[lang=en] > p#p1[0] > #text[0]: was "Hello!", now "Hello!!"',
            "TXN\tended B (depth 1)",
            "STEP\tstep B",
            "STEP\tstep C",
        ]);
    });

    it("makes a cancelled transaction's rollback stand out, showing the typing it took away", () => {
        const { recorder } = makeRecorder();
        const p = setUpWiredHistory(recorder);
        recorder.setDetailed(true);
        const text = p.firstChild as Text;
        const t = history!.beginTransaction("Background");
        text.data = "Hello world";
        t.cancel();

        const rows = recorder.asText().split("\n").map(withoutTime);
        expect(rows.slice(2)).toEqual([
            "TXN\tcancelled Background (depth 1), reversing 1 records",
            "ROLLBACK\trollback of Background reversed 1 records",
            'ROLLBACK\trollback of Background put back: text of editable[lang=en] > p#p1[0] > #text[0]: was "Hello world", now "Hello"',
        ]);
    });

    it("gives the records of a large batch a row each up to a limit, then one row for the rest", async () => {
        const { recorder } = makeRecorder();
        const p = setUpWiredHistory(recorder);
        recorder.setDetailed(true);
        const count = kMaxRecordsShownPerBatch + 3;
        await history!.doTransaction("Many", () => {
            for (let i = 0; i < count; i++) p.setAttribute("data-n", `${i}`);
        });
        const changeRows = recorder
            .getEvents()
            .filter((event) => event.kind === "change");
        expect(changeRows.length).toBe(kMaxRecordsShownPerBatch + 1);
        expect(changeRows[changeRows.length - 1].text).toBe(
            "in Many (depth 1): and 3 more records in this batch",
        );
    });
});

describe("the other rows of detailed logging", () => {
    it("shows characters that would not show, and cuts long values", () => {
        expect(quote("a\u200bb\u00a0c\n")).toBe('"a\\u200bb\\u00a0c\\u000a"');
        expect(quote(undefined)).toBe("(none)");
        expect(quote("x".repeat(100))).toBe(`"${"x".repeat(80)}..."`);
    });

    it("says which key press in which window opened a transaction", () => {
        const page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML =
            '<div class="bloom-editable" lang="fr"><p>Bonjour</p></div>';
        document.body.appendChild(page);
        const p = page.querySelector("p")!;
        const event = new KeyboardEvent("keydown", { key: "a", bubbles: true });
        p.dispatchEvent(event);

        expect(describeInputOpening(event, "typing")).toEqual({
            kind: "input",
            text: 'keydown "a" in the root window on editable[lang=fr] > p[0] opened a typing transaction',
        });
        page.remove();
    });
});

// A row of asText() without its time.
function withoutTime(line: string): string {
    return line.substring(line.indexOf("\t") + 1);
}
