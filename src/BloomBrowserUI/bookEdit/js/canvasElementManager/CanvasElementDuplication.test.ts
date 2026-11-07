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
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    post: vi.fn(),
    postData: vi.fn(),
    postJson: vi.fn(),
}));

import { post, postData, postJson } from "../../../utils/bloomApi";
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
        let rotationWhenMeasured: number | undefined;
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
            findBestLocationForNewCanvasElement: (parent: HTMLElement) => {
                // The copy's place is measured from the source's upright box, not from the
                // larger rectangle a rotated element covers.
                rotationWhenMeasured = getCanvasElementRotation(parent);
                return { getScaledX: () => 20, getScaledY: () => 20 };
            },
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
        expect(rotationWhenMeasured).toBe(0);
        expect(
            getCanvasElementRotation(source),
            "The source must get its angle back after the measuring",
        ).toBe(37);
        // The picture's own mirror comes with the copied contents.
        expect(copy!.querySelector("img")!.style.transform).toBe(
            "scale(-1, 1)",
        );
    });
});

describe("CanvasElementDuplication copies Talking Book audio", () => {
    test("through audio/copyAudioFile, under the copy's new id", () => {
        const source = document.createElement("div");
        source.innerHTML =
            '<div class="bloom-editable audio-sentence" id="i-source" data-audiorecordingmode="TextBox" data-duration="1.5"><p>One</p></div>';
        const copy = document.createElement("div");
        copy.innerHTML =
            '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox"><p>One</p></div>';
        vi.mocked(post).mockClear();
        vi.mocked(postData).mockClear();
        vi.mocked(postJson).mockClear();

        new CanvasElementDuplication(
            {} as ICanvasElementDuplicationHost,
        ).copyAnySoundFileAndAttributesForEditable(source, copy);

        const copiedEditable = copy.querySelector(".bloom-editable")!;
        const newId = copiedEditable.getAttribute("id");
        expect(newId).toBeTruthy();
        expect(newId).not.toBe("i-source");
        expect(copiedEditable.getAttribute("data-duration")).toBe("1.5");
        const requests = [
            ...vi.mocked(post).mock.calls,
            ...vi.mocked(postData).mock.calls,
            ...vi.mocked(postJson).mock.calls,
        ].map((call) => call[0]);
        expect(requests).toEqual([
            `audio/copyAudioFile?oldId=i-source&newId=${newId}`,
        ]);
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
