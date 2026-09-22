import { describe, expect, test } from "vitest";

import { inferCanvasElementType } from "./canvasElementTypeInference";

const elementHolding = (innerHtml: string): HTMLElement => {
    const canvasElement = document.createElement("div");
    canvasElement.classList.add("bloom-canvas-element");
    canvasElement.innerHTML = innerHtml;
    return canvasElement;
};

describe("inferCanvasElementType", () => {
    test("a table is a table whatever its cells hold", () => {
        // A cell can hold a video, a picture or a text box. Each of those has a type of its
        // own, and none of them may win over the table that holds it: the type decides which
        // toolbar the element gets and which subscription rules gate it.
        const cellContents = {
            video: '<div class="bloom-videoContainer"></div>',
            picture:
                '<div class="bloom-canvas"><div class="bloom-imageContainer"><img src="a.png"></div></div>',
            text: '<div class="bloom-translationGroup"><div class="bloom-editable normal-style"></div></div>',
        };
        for (const [kind, content] of Object.entries(cellContents)) {
            const table = elementHolding(
                `<div class="bloom-table"><div class="bloom-cell">${content}</div></div>`,
            );
            expect(
                inferCanvasElementType(table),
                `a table with a ${kind} cell`,
            ).toBe("table");
        }
    });

    test("a video outside a table is still a video", () => {
        expect(
            inferCanvasElementType(
                elementHolding('<div class="bloom-videoContainer"></div>'),
            ),
        ).toBe("video");
    });
});
