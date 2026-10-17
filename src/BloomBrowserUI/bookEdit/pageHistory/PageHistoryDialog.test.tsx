// The Show Page History dialog (PageHistoryDialog.tsx): it shows what the recorder already holds,
// labels undo and redo apart from steps, copies the whole list, and turns detailed logging off
// when it closes.
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
import { PageHistoryDialog } from "./PageHistoryDialog";
import { pageHistoryRecorder } from "./pageHistoryRecorder";
import { kIgnoredByPageHistoryInputSelector } from "./pageHistoryInput";

describe("PageHistoryDialog", () => {
    let container: HTMLElement;
    let onClose: Mock<() => void>;
    let writeText: Mock<(text: string) => Promise<void>>;

    beforeEach(() => {
        pageHistoryRecorder.clear();
        pageHistoryRecorder.setDetailed(false);
        pageHistoryRecorder.notePageLoad("page-1");
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
    });

    afterEach(() => {
        act(() => {
            unmountRoot(container);
        });
        container.remove();
        pageHistoryRecorder.clear();
        pageHistoryRecorder.setDetailed(false);
    });

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

    it("shows the events recorded before it opened, and those that come while it is open", () => {
        const kinds = () =>
            Array.from(dialog().querySelectorAll(".page-history-row")).map(
                (row) => row.className.match(/page-history-row-(\w+)/)![1],
            );
        expect(
            kinds(),
            "the page load is not shown while logging is not detailed",
        ).toEqual(["step", "undo", "redo"]);

        act(() =>
            pageHistoryRecorder.addLogLine("Page history: step B", "step"),
        );

        expect(kinds()).toEqual(["step", "undo", "redo", "step"]);
    });

    it("labels undo and redo rows with words, not only colour", () => {
        const texts = Array.from(
            dialog().querySelectorAll(".page-history-row"),
        ).map((row) => row.textContent);
        expect(texts[0]).toContain("STEP");
        expect(texts[1]).toContain("UNDO");
        expect(texts[2]).toContain("REDO");
    });

    it("is marked so that the page history's input handling leaves it alone", () => {
        expect(dialog().matches(kIgnoredByPageHistoryInputSelector)).toBe(true);
    });

    it("copies the whole list as text, one event per line", () => {
        act(() => button("Copy").click());
        expect(writeText).toHaveBeenCalledWith(pageHistoryRecorder.asText());
        expect(
            writeText.mock.calls[0][0]
                .split("\n")
                .map((line) => line.split("\t")[1]),
            "the copy should leave out the page load, as the list does",
        ).toEqual(["STEP", "UNDO", "REDO"]);
    });

    it("shows and copies page loads once detailed logging is on", () => {
        const box = dialog().querySelector<HTMLInputElement>(
            'input[type="checkbox"]',
        )!;
        act(() => box.click());
        act(() => pageHistoryRecorder.notePageLoad("page-2"));
        act(() => button("Copy").click());

        expect(
            dialog().querySelectorAll(".page-history-row-pageLoad"),
        ).toHaveLength(1);
        expect(writeText.mock.calls[0][0]).toContain("Page loaded: page-2");
    });

    it("clears the list", () => {
        act(() => button("Clear").click());
        expect(dialog().querySelectorAll(".page-history-row")).toHaveLength(0);
    });

    it("turns detailed logging on with its box, and off when it closes", () => {
        const box = dialog().querySelector<HTMLInputElement>(
            'input[type="checkbox"]',
        )!;
        act(() => box.click());
        expect(pageHistoryRecorder.isDetailed).toBe(true);

        act(() => button("Close").click());

        expect(pageHistoryRecorder.isDetailed).toBe(false);
        expect(onClose).toHaveBeenCalled();
    });
});
