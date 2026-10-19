// OK in Talking Book's Adjust Timings dialog, which is in the root window, and the page history:
// the new end times are an undo step of their own, though a change to the page from another window
// that nothing describes ends the history (pageHistory/pageHistoryInput.ts).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    markAudioSplit: vi.fn(),
}));

vi.mock("../../pageHistory/pageHistoryAccess", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    currentPageHistory: () => mocks.history,
}));
vi.mock("./audioRecording", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getAudioRecorder: () => ({ markAudioSplit: mocks.markAudioSplit }),
}));

import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import {
    applyAdjustedTimings,
    kAdjustTimingsUndoL10nKey,
} from "./AdjustTimingsDialog";

describe("OK in the Adjust Timings dialog", () => {
    let page: HTMLElement;
    let box: HTMLElement;
    let history: PageHistory;
    let detach: () => void;
    let otherFrame: HTMLIFrameElement;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];

    beforeEach(() => {
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML =
            '<div class="bloom-editable" data-audiorecordingendtimes="1 2"><p>One. Two.</p></div>';
        document.body.appendChild(page);
        box = page.querySelector(".bloom-editable")!;
        history = createPageHistory({ root: page, onUnmarkedChange: () => {} });
        mocks.history = history;
        // The root window, where the dialog is, as one of the windows other than the page's.
        otherFrame = document.createElement("iframe");
        document.body.appendChild(otherFrame);
        otherFrame.contentDocument!.body.innerHTML =
            '<button id="ok">OK</button>';
        userEvents = new WeakSet();
        endOfTask = [];
        detach = attachInputTransactions(
            history,
            window,
            [otherFrame.contentWindow!],
            {
                isFromUser: (event) => userEvents.has(event),
                afterCurrentTask: (fn) => endOfTask.push(fn),
            },
        );
    });

    afterEach(() => {
        detach();
        history.dispose();
        mocks.history = undefined;
        page.remove();
        otherFrame.remove();
    });

    /** A click by the user on target: press, release and click, then the end of the task. */
    function click(target: Element): void {
        for (const type of [
            "pointerdown",
            "mousedown",
            "pointerup",
            "mouseup",
            "click",
        ]) {
            const event = new MouseEvent(type, {
                bubbles: true,
                cancelable: true,
            });
            userEvents.add(event);
            target.dispatchEvent(event);
        }
        while (endOfTask.length > 0) endOfTask.shift()!();
    }

    it("is an undo step of its own, which Undo takes back", async () => {
        const ok = otherFrame.contentDocument!.getElementById("ok")!;
        ok.addEventListener("click", () =>
            applyAdjustedTimings(box, [1.5, 2.5]),
        );
        expect(box.getAttribute("data-audiorecordingendtimes")).toBe("1 2");

        click(ok);

        expect(box.getAttribute("data-audiorecordingendtimes")).toBe("1.5 2.5");
        expect(mocks.markAudioSplit).toHaveBeenCalled();
        expect(
            history.undoDescriptionL10nKey,
            "OK should make a step, not end the history",
        ).toBe(kAdjustTimingsUndoL10nKey);

        await history.undo();

        expect(box.getAttribute("data-audiorecordingendtimes")).toBe("1 2");
    });
});
