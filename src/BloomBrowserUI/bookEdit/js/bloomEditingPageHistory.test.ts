// The page history steps that bloomEditing.ts makes for changes with no click or key press behind
// them (docs/undo/MINIMUM.md item 5): doTransactionUntilPageSettles, which keeps a picture change
// open until the asynchronous refit that it started has finished, and doTransaction. They run
// against a real page history on a small page.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T => fn(),
}));

import {
    addRequestPageContentDelay,
    changeImageInfo,
    doTransaction,
    doTransactionUntilPageSettles,
    removeRequestPageContentDelay,
    whenNoRequestPageContentDelays,
} from "./bloomEditing";
import { createEditTimeMarkupFilter } from "../pageHistory/editTimeMarkup";

const kKey = "EditTab.Image.ChangeImage";
let page: HTMLElement;
let img: HTMLImageElement;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = '<img id="img1" src="old.png" style="width: 100px">';
    document.body.appendChild(page);
    img = page.querySelector("img")!;
    access.history = createPageHistory({ root: page });
});

afterEach(() => {
    access.history?.dispose();
    access.history = undefined;
    page.remove();
    vi.useRealTimers();
});

describe("doTransactionUntilPageSettles", () => {
    it("keeps the step open while a page content delay is pending, so the refit joins it", async () => {
        const history = access.history!;
        doTransactionUntilPageSettles(kKey, () => {
            img.setAttribute("src", "new.png");
            // What the refit does: hold a delay until the picture loads.
            addRequestPageContentDelay("refit");
        });
        expect(history.hasOpenTransaction).toBe(true);

        img.style.width = "200px"; // the refit, once the picture has loaded
        removeRequestPageContentDelay("refit");
        await whenNoRequestPageContentDelays();
        await Promise.resolve();

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kKey);
        await history.undo();
        expect(img.getAttribute("src")).toBe("old.png");
        expect(img.style.width).toBe("100px");
        // One step held both the change and the refit.
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("closes the step at once when no page content delay is pending", () => {
        const history = access.history!;
        const result = doTransactionUntilPageSettles(kKey, () => {
            img.setAttribute("src", "new.png");
            return 7;
        });
        expect(result).toBe(7);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kKey);
    });

    it("closes the step after the time a save waits, if a delay never clears", async () => {
        vi.useFakeTimers();
        const history = access.history!;
        doTransactionUntilPageSettles(kKey, () => {
            img.setAttribute("src", "new.png");
            addRequestPageContentDelay("neverEnds");
        });
        await vi.advanceTimersByTimeAsync(3999);
        expect(history.hasOpenTransaction).toBe(true);
        await vi.advanceTimersByTimeAsync(1);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kKey);
        removeRequestPageContentDelay("neverEnds");
    });

    it("makes one step of a call made inside another's change", async () => {
        const history = access.history!;
        doTransactionUntilPageSettles(kKey, () => {
            for (const src of ["a.png", "b.png"]) {
                doTransactionUntilPageSettles(kKey, () => {
                    img.setAttribute("src", src);
                    addRequestPageContentDelay("refit");
                });
            }
        });
        removeRequestPageContentDelay("refit");
        removeRequestPageContentDelay("refit");
        await whenNoRequestPageContentDelays();
        await Promise.resolve();

        expect(history.hasOpenTransaction).toBe(false);
        await history.undo();
        expect(img.getAttribute("src")).toBe("old.png");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("puts back what the change did when it throws", () => {
        const history = access.history!;
        expect(() =>
            doTransactionUntilPageSettles(kKey, () => {
                img.setAttribute("src", "new.png");
                throw new Error("no such image");
            }),
        ).toThrow("no such image");
        expect(img.getAttribute("src")).toBe("old.png");
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("just runs the change when there is no history", () => {
        access.history!.dispose();
        access.history = undefined;
        doTransactionUntilPageSettles(kKey, () =>
            img.setAttribute("src", "new.png"),
        );
        expect(img.getAttribute("src")).toBe("new.png");
    });
});

describe("changeImageInfo and bloom-imageLoadError", () => {
    // C# gives a cover picture an onerror attribute that adds bloom-imageLoadError and never takes
    // it off. The page history leaves the class alone, so after an undo puts back a src that
    // loads, only a load listener can take it off; HandleImageError adds one.
    it("takes the class off a cover picture when undo puts back a src that loads", async () => {
        img.setAttribute(
            "onerror",
            "this.classList.add('bloom-imageLoadError')",
        );
        access.history!.dispose();
        const history = createPageHistory({
            root: page,
            filter: createEditTimeMarkupFilter(),
        });
        access.history = history;

        await history.doTransaction(kKey, () =>
            changeImageInfo(img, {
                imageId: "",
                src: "missing.png",
                copyright: "",
                creator: "",
                license: "",
                undoable: "true",
            }),
        );
        // jsdom loads no pictures; this is what the browser does when missing.png fails.
        img.dispatchEvent(new Event("error"));
        expect(img.classList.contains("bloom-imageLoadError")).toBe(true);

        await history.undo();
        expect(img.getAttribute("src")).toBe("old.png");
        img.dispatchEvent(new Event("load"));

        expect(img.classList.contains("bloom-imageLoadError")).toBe(false);
        // The class came and went outside the history, and redo is still there.
        expect(history.redoDescriptionL10nKey).toBe(kKey);
    });
});

describe("doTransaction", () => {
    it("makes one step of an async change", async () => {
        const history = access.history!;
        await doTransaction("EditTab.PasteButton", async () => {
            img.setAttribute("src", "a.png");
            await Promise.resolve();
            img.setAttribute("src", "b.png");
        });
        expect(history.undoDescriptionL10nKey).toBe("EditTab.PasteButton");
        await history.undo();
        expect(img.getAttribute("src")).toBe("old.png");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("just runs fn when there is no history", async () => {
        access.history!.dispose();
        access.history = undefined;
        expect(await doTransaction("EditTab.CutButton", () => 3)).toBe(3);
    });
});
