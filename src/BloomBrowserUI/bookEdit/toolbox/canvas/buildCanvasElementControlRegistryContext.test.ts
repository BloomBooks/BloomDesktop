import { describe, expect, test } from "vitest";

import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";

// Tests for `canChooseAudioForElement`, the flag behind the canvas element menu's
// "play when touched" item. The item went missing from ordinary pages when the canvas
// controls became a declarative registry: the flag was gated on being inside a draggable
// game, which left no way at all to attach a click sound to a picture on a normal page.
// These tests pin the gate where it belongs -- on having an image, not on the kind of page.

// Build a page containing one canvas element, and return that element.
// `activity` set to a "drag-*" value makes it a draggable game page.
function makeCanvasElement(options: {
    kind: "image" | "text";
    activity?: string;
}): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    if (options.activity) {
        page.setAttribute("data-activity", options.activity);
    }

    const canvasElement = document.createElement("div");
    canvasElement.className = "bloom-canvas-element";

    if (options.kind === "image") {
        const container = document.createElement("div");
        container.className = "bloom-imageContainer";
        container.appendChild(document.createElement("img"));
        canvasElement.appendChild(container);
    } else {
        const editable = document.createElement("div");
        editable.className = "bloom-editable normal-style";
        canvasElement.appendChild(editable);
    }

    page.appendChild(canvasElement);
    document.body.appendChild(page);
    return canvasElement;
}

describe("canChooseAudioForElement", () => {
    test("setup: the helper really does build the two element kinds we mean to test", () => {
        // If inference ever stops recognizing these shapes, the assertions below would be
        // testing the "none" fallback instead of images and text, and would pass for the
        // wrong reason.
        expect(
            buildCanvasElementControlRegistryContext(
                makeCanvasElement({ kind: "image" }),
            ).elementType,
        ).toBe("image");

        const textCtx = buildCanvasElementControlRegistryContext(
            makeCanvasElement({ kind: "text" }),
        );
        expect(textCtx.elementType).toBe("speech");
        expect(textCtx.hasText).toBe(true);
    });

    test("offered for a picture on an ordinary page", () => {
        // This is the case that regressed: no draggable game anywhere in sight.
        const ctx = buildCanvasElementControlRegistryContext(
            makeCanvasElement({ kind: "image" }),
        );

        expect(ctx.isInDraggableGame).toBe(false);
        expect(ctx.canChooseAudioForElement).toBe(true);
    });

    test("still offered for a picture inside a draggable game", () => {
        const ctx = buildCanvasElementControlRegistryContext(
            makeCanvasElement({
                kind: "image",
                activity: "drag-letter-to-target",
            }),
        );

        expect(ctx.isInDraggableGame).toBe(true);
        expect(ctx.canChooseAudioForElement).toBe(true);
    });

    test("not offered for text on an ordinary page", () => {
        // Text never had a click sound; its form of this menu item only points at the
        // Talking Book tool, so broadening the image case must not drag text along.
        const ctx = buildCanvasElementControlRegistryContext(
            makeCanvasElement({ kind: "text" }),
        );

        expect(ctx.isInDraggableGame).toBe(false);
        expect(ctx.canChooseAudioForElement).toBe(false);
    });

    test("still offered for text inside a draggable game", () => {
        const ctx = buildCanvasElementControlRegistryContext(
            makeCanvasElement({
                kind: "text",
                activity: "drag-letter-to-target",
            }),
        );

        expect(ctx.isInDraggableGame).toBe(true);
        expect(ctx.canChooseAudioForElement).toBe(true);
    });
});
