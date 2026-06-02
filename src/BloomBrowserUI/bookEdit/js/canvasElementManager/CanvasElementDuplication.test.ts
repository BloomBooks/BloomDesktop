import { describe, expect, test } from "vitest";

import { CanvasElementDuplication } from "./CanvasElementDuplication";

describe("CanvasElementDuplication clone cleanup", () => {
    test("removes data-book from duplicated images", () => {
        const duplication = Object.create(
            CanvasElementDuplication.prototype,
        ) as CanvasElementDuplication;
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-imageContainer"><img data-book="coverImage" id="source-image" src="cover.png" /></div>';

        const clonedHtml = (
            duplication as unknown as {
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
        const duplication = Object.create(
            CanvasElementDuplication.prototype,
        ) as CanvasElementDuplication;
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-editable" data-book="bookTitle">Title</div>';

        const clonedHtml = (
            duplication as unknown as {
                safelyCloneHtmlStructure: (element: HTMLElement) => string;
            }
        ).safelyCloneHtmlStructure(sourceCanvasElement);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = clonedHtml;
        const clonedEditable = wrapper.querySelector(".bloom-editable");

        expect(clonedEditable?.getAttribute("data-book")).toBe("bookTitle");
    });
});
