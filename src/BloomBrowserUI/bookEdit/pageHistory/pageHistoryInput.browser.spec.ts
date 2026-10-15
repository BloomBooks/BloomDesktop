// The input transactions of pageHistoryInput.ts with their real defaults, in headless Edge (the
// "browser" vitest project): events that the browser marks trusted, from real mouse and keyboard
// input, and a transaction that ends with a timer after the event's task. These check what jsdom
// cannot: that the browser dispatches pointerup, mouseup and click in one task, before the timer,
// and the input events that real typing makes. pageHistoryInputSpec.ts covers the rest.
import { afterEach, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { createPageHistory } from "./pageHistory";
import { attachInputTransactions } from "./pageHistoryInput";
import { PageHistory } from "./pageHistoryTypes";

let page: HTMLElement | undefined;
let history: PageHistory | undefined;
let detach: (() => void) | undefined;
let removers: (() => void)[] = [];

const kPage =
    '<div class="bloom-editable" id="box1" contenteditable="true" lang="en" style="width:300px;min-height:40px"><p>Hello</p></div>' +
    '<div class="bloom-canvas" id="canvas" data-x="0" style="width:100px;height:100px;background:gray"></div>';

function setUp(): { page: HTMLElement; history: PageHistory } {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = kPage;
    document.body.appendChild(page);
    history = createPageHistory({ root: page, onUnmarkedChange: () => {} });
    detach = attachInputTransactions(history, window, []);
    // The undo and redo keys are taken by a handler outside pageHistoryInput.ts; this one only
    // keeps the browser's own undo from running.
    const preventUndoKeys = (e: KeyboardEvent) => {
        if (e.ctrlKey && ["z", "y"].includes(e.key.toLowerCase()))
            e.preventDefault();
    };
    document.addEventListener("keydown", preventUndoKeys);
    removers.push(() =>
        document.removeEventListener("keydown", preventUndoKeys),
    );
    return { page, history };
}

afterEach(() => {
    removers.forEach((remove) => remove());
    removers = [];
    detach?.();
    history?.dispose();
    page?.remove();
    detach = undefined;
    history = undefined;
    page = undefined;
});

function byId(root: Element, id: string): HTMLElement {
    const element = root.querySelector<HTMLElement>(`#${id}`);
    expect(element, `test setup: #${id} should be on the page`).not.toBeNull();
    return element as HTMLElement;
}

function onBloomEvent(
    target: EventTarget,
    type: string,
    listener: (event: Event) => void,
): void {
    target.addEventListener(type, listener);
    removers.push(() => target.removeEventListener(type, listener));
}

/** Undo every step and return how many there were. */
async function undoEverything(h: PageHistory): Promise<number> {
    let count = 0;
    while (h.undoDescriptionL10nKey && count < 100) {
        await h.undo();
        count++;
    }
    return count;
}

it("makes one step of a real click, with Bloom's handlers for the release and the click", async () => {
    const { page, history } = setUp();
    const canvas = byId(page, "canvas");
    // The press itself changes nothing, so a transaction that ended before Bloom's mouseup
    // handler ran would leave that handler's change in no step at all.
    onBloomEvent(document, "mouseup", () =>
        canvas.setAttribute("data-released", "yes"),
    );
    onBloomEvent(canvas, "click", () =>
        canvas.setAttribute("data-clicked", "yes"),
    );

    await userEvent.click(canvas);
    expect(canvas.getAttribute("data-clicked")).toBe("yes");
    await expect.poll(() => history.hasOpenTransaction).toBe(false);

    expect(await undoEverything(history)).toBe(1);
    expect(canvas.hasAttribute("data-released")).toBe(false);
    expect(canvas.hasAttribute("data-clicked")).toBe(false);
});

it("opens nothing for an event that a script dispatches", () => {
    const { page, history } = setUp();
    byId(page, "canvas").dispatchEvent(
        new PointerEvent("pointerdown", { bubbles: true, buttons: 1 }),
    );
    expect(history.hasOpenTransaction).toBe(false);
});

it("keeps real typing in one step until an undo key ends it", async () => {
    const { page, history } = setUp();
    const box = byId(page, "box1");
    await userEvent.click(box);
    await expect.poll(() => history.hasOpenTransaction).toBe(false);

    await userEvent.keyboard("abc");
    expect(box.textContent).toContain("abc");
    expect(history.hasOpenTransaction).toBe(true);
    await userEvent.keyboard("{Control>}z{/Control}");
    expect(history.hasOpenTransaction).toBe(false);

    expect(await undoEverything(history)).toBe(1);
    expect(box.textContent).toBe("Hello");
});

it("closes a step of real typing at the 25th input", async () => {
    const { page, history } = setUp();
    const box = byId(page, "box1");
    await userEvent.click(box);
    await expect.poll(() => history.hasOpenTransaction).toBe(false);

    await userEvent.keyboard("abcdefghijklmnopqrstuvwxyz1234");
    await userEvent.keyboard("{Control>}z{/Control}");

    expect(await undoEverything(history)).toBe(2);
    expect(box.textContent).toBe("Hello");
});
