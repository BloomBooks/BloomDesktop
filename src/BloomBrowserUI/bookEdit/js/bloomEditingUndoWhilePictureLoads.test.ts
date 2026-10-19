// Undo and redo asked for while a picture change is still open. doTransactionUntilPageSettles keeps
// the step open until the picture has loaded and its canvas element has been fitted, which holds a
// page content delay; an undo asked for meanwhile waits and runs as soon as the step closes
// (PageHistory.undo). Each test goes through one of the routes that the user has: Ctrl+Z, which
// pageHistoryKeys.ts takes, and the top bar's Undo button, which calls the page frame's handleUndo
// directly. Both reach handleUndo in pageHistorySetup.ts. Also
// what happens to a second change made while the first still waits for its fit: it joins that
// step.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        access.history ? access.history.doOutsideHistory(fn) : fn(),
}));

import {
    addRequestPageContentDelay,
    changeImageInfo,
    doTransaction,
    doTransactionUntilPageSettles,
    kChangePictureUndoL10nKey,
    removeRequestPageContentDelay,
    whenNoRequestPageContentDelays,
} from "./bloomEditing";
import {
    canUndo,
    handleRedo,
    handleUndo,
    startPageHistoryForThisPage,
} from "../pageHistory/pageHistorySetup";

let page: HTMLElement;
let canvasElement: HTMLElement;
let img: HTMLImageElement;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas">' +
        '<div class="bloom-canvas-element" style="left: 0px; top: 0px; width: 100px; height: 100px;">' +
        '<div class="bloom-imageContainer"><img src="old.png"></div>' +
        "</div></div>";
    document.body.appendChild(page);
    canvasElement = page.querySelector<HTMLElement>(".bloom-canvas-element")!;
    img = page.querySelector("img")!;
    startPageHistoryForThisPage(
        {
            noPageContentDelays: () => Promise.resolve(),
            toolboxPassesHaveRun: () => Promise.resolve(),
        },
        [],
        (history) => (access.history = history),
    );
});

afterEach(() => {
    // What the page unload does: pageHistorySetup ends the history.
    window.dispatchEvent(new Event("pagehide"));
    access.history = undefined;
    page.remove();
});

/**
 * Start a picture change as C# does, whose fit is still waiting for the picture to load. Returns
 * a function that does the fit, a new size for the canvas element, and waits for the step to
 * close.
 */
function startPictureChange(
    src: string,
    fittedWidth: string,
): () => Promise<void> {
    doTransactionUntilPageSettles(kChangePictureUndoL10nKey, () => {
        changeImageInfo(img, {
            imageId: "",
            src,
            copyright: "",
            creator: "",
            license: "",
            undoable: "true",
        });
        addRequestPageContentDelay("fit");
    });
    return async () => {
        canvasElement.style.width = fittedWidth;
        removeRequestPageContentDelay("fit");
        await whenNoRequestPageContentDelays();
        // doTransactionUntilPageSettles ends the step in a then() of that wait.
        await Promise.resolve();
    };
}

function pressCtrlZ(): void {
    document.body.dispatchEvent(
        new KeyboardEvent("keydown", {
            key: "z",
            ctrlKey: true,
            bubbles: true,
            cancelable: true,
        }),
    );
}

describe("undo asked for while a picture change waits for its fit", () => {
    it("waits for the step to close, then undoes it, and redo brings back the picture and its fit (Ctrl+Z)", async () => {
        const history = access.history!;
        const finishFit = startPictureChange("new.png", "200px");
        const changedSrc = img.getAttribute("src");
        expect(changedSrc, "test setup: the picture changed").toContain(
            "new.png",
        );
        expect(history.hasOpenTransaction).toBe(true);
        expect(canUndo(), "the Undo button is enabled while open").toBe(true);

        pressCtrlZ();

        expect(img.getAttribute("src")).toBe(changedSrc);
        expect(history.hasOpenTransaction).toBe(true);

        await finishFit();

        expect(history.hasOpenTransaction).toBe(false);
        expect(img.getAttribute("src")).toBe("old.png");
        expect(canvasElement.style.width).toBe("100px");
        expect(history.redoDescriptionL10nKey).toBe(kChangePictureUndoL10nKey);

        handleRedo();

        expect(img.getAttribute("src")).toBe(changedSrc);
        expect(canvasElement.style.width).toBe("200px");
    });

    it("runs two quick undos after two picture changes, newest first (the Undo button)", async () => {
        const history = access.history!;
        await startPictureChange("first.png", "150px")();
        const firstSrc = img.getAttribute("src");
        expect(history.undoDescriptionL10nKey).toBe(kChangePictureUndoL10nKey);
        const finishSecondFit = startPictureChange("second.png", "250px");
        const secondSrc = img.getAttribute("src");
        expect(secondSrc, "test setup: the second change").toContain(
            "second.png",
        );

        handleUndo();
        handleUndo();

        expect(img.getAttribute("src")).toBe(secondSrc);
        expect(canUndo()).toBe(true);

        await finishSecondFit();

        expect(img.getAttribute("src")).toBe("old.png");
        expect(canvasElement.style.width).toBe("100px");
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        handleRedo();
        expect(img.getAttribute("src")).toBe(firstSrc);
        expect(canvasElement.style.width).toBe("150px");
        handleRedo();
        expect(img.getAttribute("src")).toBe(secondSrc);
        expect(canvasElement.style.width).toBe("250px");
    });

    it("makes one step of a second picture change made before the first has settled", async () => {
        const history = access.history!;
        startPictureChange("first.png", "150px");
        const finishSecondFit = startPictureChange("second.png", "250px");
        const secondSrc = img.getAttribute("src");
        expect(secondSrc, "test setup: the second change").toContain(
            "second.png",
        );

        // Each fit holds a page content delay; the step closes when the last is gone.
        canvasElement.style.width = "150px";
        removeRequestPageContentDelay("fit");
        await finishSecondFit();

        expect(history.hasOpenTransaction).toBe(false);
        expect(canvasElement.style.width).toBe("250px");
        expect(history.undoDescriptionL10nKey).toBe(kChangePictureUndoL10nKey);
        pressCtrlZ();
        expect(img.getAttribute("src")).toBe("old.png");
        expect(canvasElement.style.width).toBe("100px");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        handleRedo();
        expect(img.getAttribute("src")).toBe(secondSrc);
        expect(canvasElement.style.width).toBe("250px");
    });

    it("puts back only a second change that throws, and keeps the first one's step", async () => {
        const history = access.history!;
        const finishFirstFit = startPictureChange("first.png", "150px");
        const firstSrc = img.getAttribute("src");

        expect(() =>
            doTransactionUntilPageSettles(kChangePictureUndoL10nKey, () => {
                img.setAttribute("src", "second.png");
                throw new Error("no such picture");
            }),
        ).toThrow("no such picture");
        expect(img.getAttribute("src")).toBe(firstSrc);
        expect(history.hasOpenTransaction).toBe(true);

        await finishFirstFit();
        expect(history.hasOpenTransaction).toBe(false);
        pressCtrlZ();
        expect(img.getAttribute("src")).toBe("old.png");
        expect(canvasElement.style.width).toBe("100px");
    });

    it("waits for a canvas command begun while the picture's fit is pending", async () => {
        const history = access.history!;
        const finishFit = startPictureChange("new.png", "200px");
        let finishCommand: () => void = () => {
            throw new Error("test setup: the command has not started");
        };
        // What runControlCallback does: an async doTransaction that waits for the page too.
        const command = doTransaction("Command", async () => {
            canvasElement.setAttribute("data-command", "done");
            await new Promise<void>((resolve) => (finishCommand = resolve));
            await whenNoRequestPageContentDelays();
        });

        await finishFit();
        expect(
            history.hasOpenTransaction,
            "the picture's step waits for the command",
        ).toBe(true);
        finishCommand();
        await command;

        expect(history.hasOpenTransaction).toBe(false);
        pressCtrlZ();
        expect(img.getAttribute("src")).toBe("old.png");
        expect(canvasElement.hasAttribute("data-command")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("drops a waiting undo when the page goes", async () => {
        const history = access.history!;
        const finishFit = startPictureChange("new.png", "200px");
        const changedSrc = img.getAttribute("src");
        pressCtrlZ();

        window.dispatchEvent(new Event("pagehide"));
        expect(history.isDisposed).toBe(true);
        access.history = undefined;
        await finishFit();

        expect(img.getAttribute("src")).toBe(changedSrc);
        expect(canvasElement.style.width).toBe("200px");
    });
});
