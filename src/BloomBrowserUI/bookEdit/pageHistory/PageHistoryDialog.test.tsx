// The Show Page History dialog (PageHistoryDialog.tsx): its Stack tab shows the current page's
// undo and redo steps around a marker and copies them; its Log tab shows what the recorder already
// holds, labels undo and redo apart from steps, and copies the whole list; and detailed logging is
// on while it is open, off once it closes.
import { act } from "react";
import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
    vi,
    type Mock,
} from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { kMaxLogRowsShown, PageHistoryDialog } from "./PageHistoryDialog";
import {
    pageHistoryRecorder,
    type PageHistoryStack,
} from "./pageHistoryRecorder";
import { kIgnoredByPageHistoryInputSelector } from "./pageHistoryInput";

const kSteps: PageHistoryStack = {
    undo: ["EditTab.Undo.A", "EditTab.Undo.B"],
    redo: ["EditTab.Undo.C", "EditTab.Undo.D"],
    hasOpenTransaction: false,
    isInChangeLayoutMode: false,
};

describe("PageHistoryDialog", () => {
    let container: HTMLElement;
    let onClose: Mock<() => void>;
    let writeText: Mock<(text: string) => Promise<void>>;
    let pageLoad: number;
    let isMounted: boolean;

    beforeEach(() => {
        pageHistoryRecorder.clear();
        pageHistoryRecorder.setDetailed(false);
        pageLoad = pageHistoryRecorder.notePageLoad("page-1");
        pageHistoryRecorder.setStack(pageLoad, kSteps);
        pageHistoryRecorder.addLogLine("Page history: step A", "step");
        pageHistoryRecorder.addLogLine("Page history: undo A", "undo");
        pageHistoryRecorder.addLogLine("Page history: redo A", "redo");
        writeText = vi.fn<(text: string) => Promise<void>>(() =>
            Promise.resolve(),
        );
        Object.defineProperty(navigator, "clipboard", {
            value: { writeText },
            configurable: true,
        });
        container = document.createElement("div");
        document.body.appendChild(container);
        onClose = vi.fn<() => void>();
        act(() =>
            renderRoot(<PageHistoryDialog onClose={onClose} />, container),
        );
        isMounted = true;
    });

    afterEach(() => {
        if (isMounted) unmount();
        container.remove();
        pageHistoryRecorder.clear();
        pageHistoryRecorder.setDetailed(false);
        pageHistoryRecorder.notePageGone(pageLoad);
    });

    function unmount(): void {
        act(() => {
            unmountRoot(container);
        });
        isMounted = false;
    }

    function dialog(): HTMLElement {
        const element = document.querySelector<HTMLElement>(
            ".page-history-dialog",
        );
        expect(element, "the dialog should be in the document").not.toBeNull();
        return element!;
    }

    function button(label: string): HTMLButtonElement {
        const found = Array.from(dialog().querySelectorAll("button")).find(
            (b) =>
                b.textContent === label ||
                b.getAttribute("aria-label") === label,
        );
        expect(found, `the dialog should have a ${label} button`).toBeDefined();
        return found!;
    }

    // Each row of the Stack tab, as its kind and its text.
    function stackRows(): string[] {
        const stack = dialog().querySelector(".page-history-dialog-stack");
        expect(stack, "the Stack tab should be showing").not.toBeNull();
        return Array.from(stack!.children).map((row) => {
            const kind = row.className
                .split(" ")
                .find(
                    (name) =>
                        name.startsWith("page-history-stack-") &&
                        name !== "page-history-stack-row",
                )!
                .substring("page-history-stack-".length);
            return `${kind}: ${row.textContent}`;
        });
    }

    function showLog(): void {
        act(() => button("Log").click());
        expect(
            dialog().querySelector(".page-history-dialog-list"),
            "the Log tab should be showing",
        ).not.toBeNull();
    }

    describe("the Stack tab", () => {
        it("opens first, with the undo steps oldest first above the marker, and the redo steps below it, the next redo first", () => {
            expect(stackRows()).toEqual([
                "undo: EditTab.Undo.A",
                "undo: EditTab.Undo.B",
                "marker: you are here",
                "redo: EditTab.Undo.C",
                "redo: EditTab.Undo.D",
            ]);
        });

        it("shows a step in progress just above the marker", () => {
            act(() =>
                pageHistoryRecorder.setStack(pageLoad, {
                    ...kSteps,
                    hasOpenTransaction: true,
                }),
            );
            expect(stackRows()).toEqual([
                "undo: EditTab.Undo.A",
                "undo: EditTab.Undo.B",
                "in-progress: (a step is in progress)",
                "marker: you are here",
                "redo: EditTab.Undo.C",
                "redo: EditTab.Undo.D",
            ]);
        });

        it("follows the stack as the page frame sends it, and says when there is no page", () => {
            act(() =>
                pageHistoryRecorder.setStack(pageLoad, {
                    ...kSteps,
                    undo: ["EditTab.Undo.A"],
                    redo: [],
                }),
            );
            expect(stackRows()).toEqual([
                "undo: EditTab.Undo.A",
                "marker: you are here",
            ]);

            act(() => pageHistoryRecorder.notePageGone(pageLoad));
            expect(stackRows()).toEqual(["none: (no page history)"]);
        });

        it("says what last cleared the history, at the top, until the next page load", () => {
            act(() => {
                pageHistoryRecorder.addLogLine(
                    "Page history: cleared by request editview/settopic",
                    "cleared",
                );
                pageHistoryRecorder.setStack(pageLoad, {
                    ...kSteps,
                    undo: [],
                    redo: [],
                });
            });
            const rows = stackRows();
            expect(rows.map((row) => row.split(":")[0])).toEqual([
                "clear",
                "marker",
            ]);
            expect(rows[0]).toMatch(
                /^clear: History cleared at \d+\.\d{3} s by request editview\/settopic$/,
            );
            act(() => button("Copy").click());
            expect(writeText.mock.calls[0][0].split("\n")[0]).toMatch(
                /^History cleared at .* by request editview\/settopic$/,
            );

            act(() => {
                pageLoad = pageHistoryRecorder.notePageLoad("page-2");
                pageHistoryRecorder.setStack(pageLoad, kSteps);
            });
            expect(
                stackRows().filter((row) => row.startsWith("clear")),
            ).toEqual([]);
        });

        it("notes Change Layout mode, whose undo it does not show", () => {
            act(() =>
                pageHistoryRecorder.setStack(pageLoad, {
                    undo: [],
                    redo: [],
                    hasOpenTransaction: false,
                    isInChangeLayoutMode: true,
                }),
            );
            const rows = stackRows();
            expect(rows[0]).toMatch(/^note: In Change Layout mode/);
        });

        it("copies the stack as text, one step per line, with the marker", () => {
            act(() => button("Copy").click());
            expect(writeText).toHaveBeenCalledTimes(1);
            expect(writeText.mock.calls[0][0].split("\n")).toEqual([
                "UNDO\tEditTab.Undo.A",
                "UNDO\tEditTab.Undo.B",
                "-------- you are here --------",
                "REDO\tEditTab.Undo.C",
                "REDO\tEditTab.Undo.D",
            ]);
        });
    });

    describe("the Log tab", () => {
        it("shows the events recorded before it opened, and those that come while it is open, page loads included", () => {
            showLog();
            const kinds = () =>
                Array.from(dialog().querySelectorAll(".page-history-row")).map(
                    (row) => row.className.match(/page-history-row-(\w+)/)![1],
                );
            expect(
                kinds(),
                "the page load came before the dialog opened, when it was not recorded",
            ).toEqual(["step", "undo", "redo"]);

            act(() => {
                pageHistoryRecorder.addLogLine("Page history: step B", "step");
                pageHistoryRecorder.notePageLoad("page-2");
            });

            expect(kinds()).toEqual([
                "step",
                "undo",
                "redo",
                "step",
                "pageLoad",
            ]);
        });

        it("labels undo and redo rows with words, not only colour", () => {
            showLog();
            const texts = Array.from(
                dialog().querySelectorAll(".page-history-row"),
            ).map((row) => row.textContent);
            expect(texts[0]).toContain("STEP");
            expect(texts[1]).toContain("UNDO");
            expect(texts[2]).toContain("REDO");
        });

        it("copies the whole list as text, one event per line", () => {
            showLog();
            act(() => button("Copy").click());
            expect(writeText).toHaveBeenCalledWith(
                pageHistoryRecorder.asText(),
            );
            expect(
                writeText.mock.calls[0][0]
                    .split("\n")
                    .map((line) => line.split("\t")[1]),
            ).toEqual(["STEP", "UNDO", "REDO"]);
        });

        it("clears the list", () => {
            showLog();
            act(() => button("Clear").click());
            expect(dialog().querySelectorAll(".page-history-row")).toHaveLength(
                0,
            );
        });
    });

    it("shows only the newest rows in the Log tab, says how many are left out, and copies them all", () => {
        act(() => {
            for (let i = 0; i < kMaxLogRowsShown + 5; i++)
                pageHistoryRecorder.add("change", `change ${i}`);
        });
        const total = pageHistoryRecorder.getEvents().length;
        expect(total, "test setup: 3 events, then the changes").toBe(
            kMaxLogRowsShown + 8,
        );
        showLog();

        const rows = dialog().querySelectorAll(".page-history-row");
        expect(rows).toHaveLength(kMaxLogRowsShown);
        expect(rows[0].textContent).toContain("change 5");
        expect(
            dialog().querySelector(".page-history-log-omitted")?.textContent,
        ).toBe("Older rows are not shown here; Copy includes them all.");

        act(() => button("Copy").click());
        expect(writeText.mock.calls[0][0].split("\n")).toHaveLength(total);
    });

    it("shows no counts of steps or events", () => {
        expect(dialog().textContent).not.toMatch(/\d+ (undo|redo|events)/i);
        showLog();
        act(() => button("Copy").click());
        expect(dialog().textContent).not.toMatch(/\d+ (undo|redo|events)/i);
    });

    // A mousedown at (x, y) on target, in a dialog given the size it starts at (jsdom does no
    // layout); returns the event, to check whether its default was prevented.
    function press(target: Element, x: number, y: number): MouseEvent {
        vi.spyOn(dialog(), "getBoundingClientRect").mockReturnValue(
            new DOMRect(0, 0, 760, 440),
        );
        const event = new MouseEvent("mousedown", {
            bubbles: true,
            cancelable: true,
            clientX: x,
            clientY: y,
        });
        act(() => {
            target.dispatchEvent(event);
        });
        return event;
    }

    it("keeps the focus in the page when its tabs and buttons are pressed", () => {
        const input = document.createElement("input");
        document.body.appendChild(input);
        input.focus();
        expect(document.activeElement, "test setup").toBe(input);

        ["Log", "Copy", "Stack", "Copy"].forEach((label) => {
            const target = button(label);
            expect(
                press(target, 20, 50).defaultPrevented,
                `pressing ${label} should not move the focus`,
            ).toBe(true);
            act(() => target.click());
        });
        expect(
            press(
                dialog().querySelector(".page-history-dialog-stack")!,
                300,
                200,
            ).defaultPrevented,
            "pressing the list should not move the focus",
        ).toBe(true);

        expect(document.activeElement).toBe(input);
        input.remove();
    });

    it("leaves a press on the resize grip alone, so that the dialog can be resized", () => {
        expect(press(dialog(), 755, 435).defaultPrevented).toBe(false);
    });

    it("is marked so that the page history's input handling leaves it alone", () => {
        expect(dialog().matches(kIgnoredByPageHistoryInputSelector)).toBe(true);
    });

    it("turns detailed logging on while it is open, and off once it closes, with no box to turn it off", () => {
        expect(pageHistoryRecorder.isDetailed).toBe(true);
        expect(
            dialog().querySelector('input[type="checkbox"]'),
            "there should be no Detailed logging box",
        ).toBeNull();

        act(() => button("Close").click());
        expect(onClose).toHaveBeenCalled();
        unmount();

        expect(pageHistoryRecorder.isDetailed).toBe(false);
    });
});
