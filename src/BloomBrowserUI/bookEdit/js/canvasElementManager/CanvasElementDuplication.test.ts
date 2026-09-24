import { describe, expect, test, vi } from "vitest";

vi.mock("comicaljs", () => ({
    Bubble: class {
        public content: HTMLElement;
        public constructor(content: HTMLElement) {
            this.content = content;
        }
    },
    Comical: {
        update: vi.fn(),
        findRelatives: () => [],
        convertBubbleJsonToCanvas: vi.fn(),
    },
}));
vi.mock("../../../utils/bloomApi", () => ({
    postData: vi.fn(),
    postJson: vi.fn(),
}));

import { cloneCanvasElementHtmlStructure } from "./canvasElementCloneCleanup";
import {
    CanvasElementDuplication,
    ICanvasElementDuplicationHost,
} from "./CanvasElementDuplication";
import {
    getCanvasElementRotation,
    setCanvasElementRotation,
} from "./canvasElementRotation";

describe("CanvasElementDuplication keeps the angle of a rotated box (BL-16741)", () => {
    test("the copy of a rotated picture box is rotated by the same angle", () => {
        document.body.innerHTML = "";
        const bloomCanvas = document.createElement("div");
        bloomCanvas.className = "bloom-canvas";
        document.body.appendChild(bloomCanvas);
        const source = document.createElement("div");
        source.className = "bloom-canvas-element";
        source.innerHTML =
            '<div class="bloom-imageContainer"><img src="a.png" style="transform: scale(-1, 1)" /></div>';
        bloomCanvas.appendChild(source);
        setCanvasElementRotation(source, 37);
        // Sanity check: the source really is rotated.
        expect(getCanvasElementRotation(source)).toBe(37);

        let active: HTMLElement | undefined = source;
        const spec = { style: "none", tails: [], level: 1, version: "1" };
        const host = {
            getPatriarchBubbleOfActiveElement: () => ({ content: source }),
            setActiveElement: (e: HTMLElement | undefined) => (active = e),
            getSelectedItemBubbleSpec: () => spec,
            updateSelectedItemBubbleSpec: vi.fn(),
            refreshCanvasElementEditing: vi.fn(),
            removeJQueryResizableWidget: vi.fn(),
            initializeCanvasElementEditing: vi.fn(),
            addCanvasElementFromOriginal: () => {
                const copy = document.createElement("div");
                copy.className = "bloom-canvas-element";
                bloomCanvas.appendChild(copy);
                return copy;
            },
            findBestLocationForNewCanvasElement: () => ({
                getScaledX: () => 20,
                getScaledY: () => 20,
            }),
            reorderRectangleCanvasElement: vi.fn(),
            addChildInternal: vi.fn(),
            adjustRelativePointToBloomCanvas: vi.fn(),
        } as unknown as ICanvasElementDuplicationHost;

        const copy = new CanvasElementDuplication(
            host,
        ).duplicateCanvasElementBox(source);

        expect(copy).toBeDefined();
        expect(copy).not.toBe(source);
        expect(active).toBe(copy);
        expect(getCanvasElementRotation(copy!)).toBe(37);
        // The picture's own mirror comes with the copied contents.
        expect(copy!.querySelector("img")!.style.transform).toBe(
            "scale(-1, 1)",
        );
    });
});

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
