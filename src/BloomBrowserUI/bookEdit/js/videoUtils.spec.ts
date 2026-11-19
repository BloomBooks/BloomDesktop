import { describe, expect, it } from "vitest";
import { selectVideoContainer, videoContainerToRecordInto } from "./videoUtils";

// A canvas element holding a table with a video in each of two cells.
const setUpTableWithTwoVideos = (): {
    canvasElement: HTMLElement;
    first: HTMLElement;
    second: HTMLElement;
} => {
    document.body.innerHTML = `<div class="bloom-canvas-element">
            <div class="bloom-table">
                <div class="bloom-videoContainer" id="first"></div>
                <div class="bloom-videoContainer" id="second"></div>
            </div>
        </div>`;
    return {
        canvasElement: document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement,
        first: document.getElementById("first") as HTMLElement,
        second: document.getElementById("second") as HTMLElement,
    };
};

describe("videoContainerToRecordInto", () => {
    it("is the first video container when none is selected", () => {
        const { canvasElement, first } = setUpTableWithTwoVideos();
        expect(
            canvasElement.querySelector(".bloom-videoContainer.bloom-selected"),
        ).toBeNull();

        expect(videoContainerToRecordInto(canvasElement)).toBe(first);
    });

    it("is the selected video container, even when it is not the first", () => {
        const { canvasElement, second } = setUpTableWithTwoVideos();
        selectVideoContainer(second, false);
        expect(second.classList.contains("bloom-selected")).toBe(true);

        expect(videoContainerToRecordInto(canvasElement)).toBe(second);
    });

    it("keeps the clicked video selected when the canvas element is activated", () => {
        const { canvasElement, first, second } = setUpTableWithTwoVideos();
        // What a click on the second video does, followed by what activating its canvas element
        // does (CanvasElementManager.setActiveElement).
        selectVideoContainer(second, false);
        selectVideoContainer(videoContainerToRecordInto(canvasElement), false);

        expect(second.classList.contains("bloom-selected")).toBe(true);
        expect(first.classList.contains("bloom-selected")).toBe(false);
    });
});
