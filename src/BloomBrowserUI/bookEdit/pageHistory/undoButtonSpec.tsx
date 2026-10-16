// The top bar's real Undo button (UndoButton in topbar/editTopBarControls.tsx) and the input
// transactions of pageHistoryInput.ts: a press on the button must open no transaction, since
// undo() waits while one is open, and the click reaches the undo through C# at some time that
// nothing here controls.
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { UndoButton } from "../topbar/editTopBarControls";
import { createPageHistory } from "./pageHistory";
import { attachInputTransactions } from "./pageHistoryInput";
import { PageHistory } from "./pageHistoryTypes";

// The button posts editView/topBarButtonClick, which C# answers by running handleUndo in the
// page frame; the test runs the undo itself.
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    postJson: () => {},
}));

describe("the top bar's Undo button and the input transactions", () => {
    let pageFrame: HTMLIFrameElement;
    let page: HTMLElement;
    let history: PageHistory;
    let detach: () => void;
    let container: HTMLElement;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];

    beforeEach(() => {
        // The page in its own frame; the button renders in this window, as the top bar does in
        // the root window.
        pageFrame = document.createElement("iframe");
        document.body.appendChild(pageFrame);
        page = pageFrame.contentDocument!.createElement("div");
        page.className = "bloom-page";
        page.setAttribute("data-n", "0");
        pageFrame.contentDocument!.body.appendChild(page);
        history = createPageHistory({ root: page, onUnmarkedChange: () => {} });
        userEvents = new WeakSet();
        endOfTask = [];
        detach = attachInputTransactions(
            history,
            pageFrame.contentWindow!,
            [window],
            {
                isFromUser: (event) => userEvents.has(event),
                afterCurrentTask: (fn) => endOfTask.push(fn),
            },
        );
        container = document.createElement("div");
        document.body.appendChild(container);
        act(() => renderRoot(<UndoButton enabled={true} />, container));
    });

    afterEach(() => {
        act(() => unmountRoot(container));
        detach();
        history.dispose();
        document.body.innerHTML = "";
    });

    /** A user's press and click on target, as the browser sends them, all in one task. */
    function pressAndClick(target: Element): void {
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
                buttons: type.endsWith("down") ? 1 : 0,
            });
            userEvents.add(event);
            target.dispatchEvent(event);
        }
    }

    it("has the attribute that kUndoControlSelector finds, so its press opens nothing", async () => {
        await history.doTransaction("Step", () =>
            page.setAttribute("data-n", "1"),
        );
        expect(history.undoDescriptionL10nKey).toBe("Step");
        const button = container.querySelector("button")!;
        expect(button.hasAttribute("data-runs-undo")).toBe(true);
        // The click lands on the icon or the label inside the button.
        const icon = button.querySelector("img")!;
        expect(icon).not.toBeNull();
        let openDuringClick: boolean | undefined;
        button.addEventListener("click", () => {
            openDuringClick = history.hasOpenTransaction;
        });

        pressAndClick(icon);

        expect(openDuringClick).toBe(false);
        expect(history.hasOpenTransaction).toBe(false);
        // C#'s call to handleUndo may arrive before the end of the click's task.
        await history.undo();
        expect(page.getAttribute("data-n")).toBe("0");
        endOfTask.forEach((fn) => fn());
        expect(history.redoDescriptionL10nKey).toBe("Step");
    });

    it("would hold the undo until the end of the click's task if the button lacked the attribute", async () => {
        // A sanity check that the test above can fail: the same press on a button without the
        // attribute opens a transaction, and undo waits while it is open.
        await history.doTransaction("Step", () =>
            page.setAttribute("data-n", "1"),
        );
        const button = container.querySelector("button")!;
        button.removeAttribute("data-runs-undo");

        pressAndClick(button.querySelector("img")!);

        expect(history.hasOpenTransaction).toBe(true);
        const undone = history.undo();
        expect(page.getAttribute("data-n")).toBe("1");
        endOfTask.forEach((fn) => fn());
        await undone;
        expect(page.getAttribute("data-n")).toBe("0");
    });
});
