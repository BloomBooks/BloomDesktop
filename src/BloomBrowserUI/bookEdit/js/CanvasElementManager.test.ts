import { describe, expect, test } from "vitest";

import { CanvasElementManager } from "./CanvasElementManager";

describe("CanvasElementManager clone cleanup", () => {
    test("removes data-book from duplicated images", () => {
        const manager = Object.create(
            CanvasElementManager.prototype,
        ) as CanvasElementManager;
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-imageContainer"><img data-book="coverImage" id="source-image" src="cover.png" /></div>';

        const clonedHtml = (
            manager as unknown as {
                safelyCloneHtmlStructure: (element: HTMLElement) => string;
            }
        ).safelyCloneHtmlStructure(sourceCanvasElement);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = clonedHtml;
        const clonedImage = wrapper.querySelector("img");
        const sourceImage = sourceCanvasElement.querySelector("img");

        expect(sourceImage?.getAttribute("data-book")).toBe("coverImage");
        expect(clonedImage).not.toBeNull();
        expect(clonedImage?.hasAttribute("data-book")).toBe(false);
        expect(clonedImage?.id).toBe("");
    });

    test("keeps data-book on non-image cloned nodes", () => {
        const manager = Object.create(
            CanvasElementManager.prototype,
        ) as CanvasElementManager;
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-editable" data-book="bookTitle">Title</div>';

        const clonedHtml = (
            manager as unknown as {
                safelyCloneHtmlStructure: (element: HTMLElement) => string;
            }
        ).safelyCloneHtmlStructure(sourceCanvasElement);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = clonedHtml;
        const clonedEditable = wrapper.querySelector(".bloom-editable");

        expect(clonedEditable?.getAttribute("data-book")).toBe("bookTitle");
    });
});
