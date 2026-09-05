import { describe, expect, test } from "vitest";

import { cloneCanvasElementHtmlStructure } from "./canvasElementCloneCleanup";

describe("CanvasElementDuplication clone cleanup", () => {
    test("removes data-book from duplicated images", () => {
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-imageContainer"><img data-book="coverImage" id="source-image" src="cover.png" /></div>';

        const clonedHtml = cloneCanvasElementHtmlStructure(sourceCanvasElement);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = clonedHtml;
        const clonedImage = wrapper.querySelector("img");
        const sourceImage = sourceCanvasElement.querySelector("img");

        expect(sourceImage?.getAttribute("data-book")).toBe("coverImage");
        expect(clonedImage).not.toBeNull();
        expect(clonedImage?.hasAttribute("data-book")).toBe(false);
        expect(clonedImage?.id).toBe("");
    });

    test("drops the wired-up marker from a duplicated table", () => {
        // attachSingleTable in tableEditing.ts writes data-table-attached on a table it has
        // wired up and skips any table that already carries it. A copy has never been wired
        // up, so the marker must not travel with it.
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-table" data-table-attached="1" data-column-widths="fill,fill">' +
            '<div class="bloom-cell" data-content-type="text"></div></div>';
        expect(
            sourceCanvasElement
                .querySelector(".bloom-table")
                ?.hasAttribute("data-table-attached"),
        ).toBe(true);

        const clonedHtml = cloneCanvasElementHtmlStructure(sourceCanvasElement);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = clonedHtml;
        const clonedTable = wrapper.querySelector(".bloom-table");

        expect(clonedTable).not.toBeNull();
        expect(clonedTable?.hasAttribute("data-table-attached")).toBe(false);
        expect(clonedTable?.getAttribute("data-column-widths")).toBe(
            "fill,fill",
        );
    });

    test("keeps data-book on non-image cloned nodes", () => {
        const sourceCanvasElement = document.createElement("div");
        sourceCanvasElement.innerHTML =
            '<div class="bloom-editable" data-book="bookTitle">Title</div>';

        const clonedHtml = cloneCanvasElementHtmlStructure(sourceCanvasElement);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = clonedHtml;
        const clonedEditable = wrapper.querySelector(".bloom-editable");

        expect(clonedEditable?.getAttribute("data-book")).toBe("bookTitle");
    });
});
