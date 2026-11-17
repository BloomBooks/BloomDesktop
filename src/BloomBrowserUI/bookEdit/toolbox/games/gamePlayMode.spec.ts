import { describe, it, expect, beforeEach } from "vitest";
import {
    endPlayModeAsPageUnloads,
    preparePageForPlay,
    removePlayModeMarkupFromClone,
    takePageOutOfPlayMode,
} from "./gamePlayMode";
import { removeToolboxMarkup, removeToolMarkupFromPageClone } from "../toolbox";

// The game tool's Play tab, as far as what gets saved is concerned: entering Play twice must not make
// the tester's drags look like the author's positions, and nothing play mode does to the page may
// reach the book.

const authoredLeft = "10px";
const authoredTop = "20px";

function makeActivityPage() {
    document.body.innerHTML = "";
    const page = document.createElement("div");
    page.classList.add("bloom-page");
    page.setAttribute("data-activity", "drag-letter-to-target");
    const draggable = document.createElement("div");
    draggable.setAttribute("data-draggable-id", "d1");
    draggable.style.left = authoredLeft;
    draggable.style.top = authoredTop;
    page.appendChild(draggable);
    document.body.appendChild(page);
    return { page, draggable };
}

function dragTo(draggable: HTMLElement, left: string, top: string) {
    draggable.style.left = left;
    draggable.style.top = top;
    draggable.classList.add("bloom-draggedToTarget");
}

function savedCopyOf(page: HTMLElement): HTMLElement {
    const copy = page.cloneNode(true) as HTMLElement;
    removePlayModeMarkupFromClone(copy);
    return copy.querySelector<HTMLElement>("[data-draggable-id]")!;
}

describe("preparePageForPlay", () => {
    let page: HTMLElement;
    let draggable: HTMLElement;
    beforeEach(() => {
        ({ page, draggable } = makeActivityPage());
    });

    it("prepares a page that is not in play mode", () => {
        expect(preparePageForPlay(page)).toBe(true);
        takePageOutOfPlayMode(page);
    });

    it("asked again while the page is in play, keeps the author's positions", () => {
        expect(preparePageForPlay(page)).toBe(true);
        dragTo(draggable, "300px", "400px");

        // Clicking the Play tab again, or the toolbox asking for the current tab again.
        expect(preparePageForPlay(page)).toBe(false);

        const saved = savedCopyOf(page);
        expect(saved.style.left).toBe(authoredLeft);
        expect(saved.style.top).toBe(authoredTop);
        expect(draggable.style.left, "the live page goes on being played").toBe(
            "300px",
        );

        takePageOutOfPlayMode(page);
        expect(draggable.style.left, "leaving Play puts it back").toBe(
            authoredLeft,
        );
        expect(draggable.style.top).toBe(authoredTop);
    });

    it("does not add a second set of order-sentence words", () => {
        const sentence = document.createElement("div");
        sentence.classList.add("drag-item-order-sentence");
        const content = document.createElement("div");
        content.classList.add("bloom-content1", "bloom-visibility-code-on");
        content.textContent = "one two three";
        sentence.appendChild(content);
        page.appendChild(sentence);

        preparePageForPlay(page);
        expect(
            page.getElementsByClassName("drag-item-random-sentence").length,
            "test setup: play mode adds the shuffled words",
        ).toBe(1);
        preparePageForPlay(page);
        expect(
            page.getElementsByClassName("drag-item-random-sentence").length,
        ).toBe(1);
        takePageOutOfPlayMode(page);
    });

    it("prepares the page again once play mode on it has ended", () => {
        preparePageForPlay(page);
        takePageOutOfPlayMode(page); // e.g. the Start tab is chosen
        expect(preparePageForPlay(page)).toBe(true);
        takePageOutOfPlayMode(page);
    });

    it("cleaning a copy does not end play mode on the live page", () => {
        preparePageForPlay(page);
        dragTo(draggable, "300px", "400px");
        savedCopyOf(page);
        expect(preparePageForPlay(page)).toBe(false);
        takePageOutOfPlayMode(page);
    });

    it("prepares a reloaded page, which is a new element", () => {
        preparePageForPlay(page);
        const reloaded = makeActivityPage();
        expect(preparePageForPlay(reloaded.page)).toBe(true);
        takePageOutOfPlayMode(reloaded.page);
    });
});

describe("removePlayModeMarkupFromClone", () => {
    it("removes what playing leaves on the page", () => {
        // (touch-action is checked in gamePlayMode.browser.spec.ts: jsdom ignores it.)
        const { page, draggable } = makeActivityPage();
        preparePageForPlay(page);

        // Mid-drag, with narration playing and a video that would not start.
        draggable.classList.add("bloom-ui-dragging");
        const text = document.createElement("span");
        text.classList.add("audio-sentence", "ui-audioCurrent");
        text.classList.add("ui-suppressHighlight");
        page.appendChild(text);
        const canvas = document.createElement("div");
        canvas.classList.add("bloom-canvas", "ui-audioCurrentImg");
        page.appendChild(canvas);
        const videoContainer = document.createElement("div");
        videoContainer.classList.add("bloom-videoContainer", "autoplayBlocked");
        videoContainer.appendChild(document.createElement("video"));
        const errorMessage = document.createElement("div");
        errorMessage.classList.add("video-error-message");
        videoContainer.appendChild(errorMessage);
        page.appendChild(videoContainer);
        // A draggable whose start position was not recorded, as for one added during play.
        const other = document.createElement("div");
        other.setAttribute("data-draggable-id", "d2");
        other.classList.add("bloom-draggedToTarget");
        page.appendChild(other);

        const copy = page.cloneNode(true) as HTMLElement;
        removePlayModeMarkupFromClone(copy);

        for (const className of [
            "bloom-ui-dragging",
            "bloom-draggedToTarget",
            "ui-audioCurrent",
            "ui-audioCurrentImg",
            "ui-suppressHighlight",
            "autoplayBlocked",
            "video-error-message",
        ]) {
            expect(
                copy.getElementsByClassName(className).length,
                `${className} should not be saved`,
            ).toBe(0);
        }
        const savedDraggable = copy.querySelector<HTMLElement>(
            "[data-draggable-id='d1']",
        )!;
        expect(savedDraggable.style.left, "authored styles stay").toBe(
            authoredLeft,
        );
        expect(
            copy.querySelector(".audio-sentence"),
            "the content itself stays",
        ).not.toBeNull();

        expect(
            draggable.classList.contains("bloom-ui-dragging"),
            "the live page is left alone",
        ).toBe(true);
        takePageOutOfPlayMode(page);
    });
});

describe("play mode, whichever tool is open", () => {
    // These go through the toolbox's own entry points, with no tool expanded: the tester can play
    // a game page with any tool open, or none, and the save and the page change must still deal
    // with play mode.

    it("a save while playing records the author's positions with no tool open", () => {
        const { page, draggable } = makeActivityPage();
        preparePageForPlay(page);
        dragTo(draggable, "300px", "400px");
        draggable.classList.add("bloom-ui-dragging");

        const copy = page.cloneNode(true) as HTMLElement;
        removeToolMarkupFromPageClone(copy);

        const saved = copy.querySelector<HTMLElement>("[data-draggable-id]")!;
        expect(saved.style.left).toBe(authoredLeft);
        expect(saved.style.top).toBe(authoredTop);
        expect(saved.classList.contains("bloom-ui-dragging")).toBe(false);
        expect(draggable.style.left, "the live page goes on being played").toBe(
            "300px",
        );
        takePageOutOfPlayMode(page);
    });

    it("a save while playing records canvas text as editable, as every other tab does", () => {
        const { page, draggable } = makeActivityPage();
        draggable.classList.add("bloom-canvas-element");
        const text = document.createElement("div");
        text.classList.add("bloom-editable");
        draggable.appendChild(text);
        preparePageForPlay(page);
        // What CanvasElementEditingSuspension does to the live page in Play.
        text.setAttribute("contenteditable", "false");

        const copy = page.cloneNode(true) as HTMLElement;
        removePlayModeMarkupFromClone(copy);

        expect(
            copy
                .querySelector(".bloom-editable")!
                .getAttribute("contenteditable"),
        ).toBe("true");
        expect(
            text.getAttribute("contenteditable"),
            "live page untouched",
        ).toBe("false");
        takePageOutOfPlayMode(page);
    });

    it("a save when no page is in play mode leaves the copy alone", () => {
        const { page, draggable } = makeActivityPage();
        // An authored position that differs from anything play recorded earlier.
        draggable.style.left = "55px";
        const copy = page.cloneNode(true) as HTMLElement;
        removePlayModeMarkupFromClone(copy);
        expect(
            copy.querySelector<HTMLElement>("[data-draggable-id]")!.style.left,
        ).toBe("55px");
    });

    it("leaving the page ends play mode with no tool open", () => {
        const { page, draggable } = makeActivityPage();
        preparePageForPlay(page);
        dragTo(draggable, "300px", "400px");

        removeToolboxMarkup();

        expect(draggable.style.left).toBe(authoredLeft);
        expect(
            preparePageForPlay(page),
            "play mode is over, so the page is prepared afresh",
        ).toBe(true);
        takePageOutOfPlayMode(page);
    });

    it("leaving a page that is not in play mode does nothing to it", () => {
        const { draggable } = makeActivityPage();
        draggable.style.left = "55px";
        endPlayModeAsPageUnloads();
        expect(draggable.style.left).toBe("55px");
    });
});
