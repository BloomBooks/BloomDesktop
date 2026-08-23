import { describe, it, expect, beforeEach, vi } from "vitest";
import {
    IImageCropInfo,
    ImageUndoManager,
    ImageUndoManagerHost,
} from "./ImageUndoManager";
import { setCanvasElementRotation } from "./canvasElementManager/canvasElementRotation";

describe("ImageUndoManager crop preservation", () => {
    let manager: ImageUndoManager;
    let hostMock: Partial<ImageUndoManagerHost>;
    let containerDiv: HTMLElement;
    let imgElement: HTMLImageElement;

    beforeEach(() => {
        // Set up a mock host
        hostMock = {
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") || undefined,
            updateCanvasElementForChangedImage: vi.fn(
                (
                    imgOrImageContainer: HTMLElement,
                    cropInfo?: IImageCropInfo,
                ) => {
                    // Simulate part of what the real method does: clears crop styles, then
                    // sets new crop styles if provided. This allows us to verify that the undo manager.
                    if (imgOrImageContainer) {
                        const img =
                            imgOrImageContainer.tagName.toLowerCase() === "img"
                                ? (imgOrImageContainer as HTMLImageElement)
                                : (imgOrImageContainer.getElementsByTagName(
                                      "img",
                                  )[0] ?? undefined);
                        if (img) {
                            img.style.width = "";
                            img.style.height = "";
                            img.style.left = "";
                            img.style.top = "";
                            if (cropInfo) {
                                img.style.width = cropInfo.width;
                                img.style.height = cropInfo.height;
                                img.style.left = cropInfo.left;
                                img.style.top = cropInfo.top;
                            }
                        }
                    }
                },
            ),
            getActiveElement: vi.fn(() => undefined),
            setActiveElement: vi.fn(),
            removeDetachedTargets: vi.fn(),
            updateCanvasElementClass: vi.fn(),
        };

        manager = new ImageUndoManager(hostMock as ImageUndoManagerHost);

        // Create a page and container with image
        const page = document.createElement("div");
        page.className = "bloom-page";
        page.setAttribute("data-page-id", "test-page-1");
        document.body.appendChild(page);

        containerDiv = document.createElement("div");
        containerDiv.className = "bloom-imageContainer";
        page.appendChild(containerDiv);

        imgElement = document.createElement("img");
        imgElement.src = "test-image.png";
        imgElement.setAttribute("data-copyright", "© 2024");
        imgElement.setAttribute("data-creator", "Test Creator");
        imgElement.setAttribute("data-license", "CC-BY");
        // Set up virtual cropping: negative left/top with custom dimensions
        imgElement.style.width = "150px";
        imgElement.style.height = "120px";
        imgElement.style.left = "-25px";
        imgElement.style.top = "-30px";
        containerDiv.appendChild(imgElement);
    });

    it("preserves crop style (width, height, left, top) when undoing image change", () => {
        // Capture the original crop state
        expect(imgElement.src).toContain("test-image.png");
        expect(imgElement.getAttribute("data-copyright")).toBe("© 2024");
        expect(imgElement.style.width).toBe("150px");
        expect(imgElement.style.height).toBe("120px");
        expect(imgElement.style.left).toBe("-25px");
        expect(imgElement.style.top).toBe("-30px");

        // Prepare undo: captures crop state
        manager.prepareUndoForImageOperation(imgElement);

        // Simulate an image change that clears crop (as happens in normal flow)
        imgElement.src = "new-image.png";
        imgElement.setAttribute("data-copyright", "© 2025");
        imgElement.style.width = "";
        imgElement.style.height = "";
        imgElement.style.left = "";
        imgElement.style.top = "";

        // Verify crop was cleared
        expect(imgElement.src).toContain("new-image.png");
        expect(imgElement.getAttribute("data-copyright")).toBe("© 2025");
        expect(imgElement.style.width).toBe("");
        expect(imgElement.style.left).toBe("");

        // Commit the undo state (after the "change" has happened)
        manager.commitPendingImageOperationUndo(imgElement);

        // Now undo: should restore both image src and crop style
        const undoSucceeded = manager.undoImageOperation();

        expect(undoSucceeded).toBe(true);
        // Image metadata should be restored
        expect(imgElement.src).toContain("test-image.png");
        expect(imgElement.getAttribute("data-copyright")).toBe("© 2024");
        // Crop style should be restored
        expect(imgElement.style.width).toBe("150px");
        expect(imgElement.style.height).toBe("120px");
        expect(imgElement.style.left).toBe("-25px");
        expect(imgElement.style.top).toBe("-30px");
    });

    it("restores empty crop strings if image had no prior crop", () => {
        // Create an uncropped image
        imgElement.style.width = "";
        imgElement.style.height = "";
        imgElement.style.left = "";
        imgElement.style.top = "";

        manager.prepareUndoForImageOperation(containerDiv);

        // Apply some crop
        imgElement.style.width = "100px";
        imgElement.style.left = "-50px";

        manager.commitPendingImageOperationUndo(containerDiv);

        // Change and undo
        imgElement.src = "changed.png";
        manager.undoImageOperation();

        // Should restore to empty string (no crop)
        expect(imgElement.style.width).toBe("");
        expect(imgElement.style.left).toBe("");
    });
});

describe("ImageUndoManager rotate and flip", () => {
    let manager: ImageUndoManager;
    let updateAfterTransform: (
        canvasElement: HTMLElement,
        img: HTMLImageElement | undefined,
    ) => void;
    let canvasElement: HTMLElement;
    let imgElement: HTMLImageElement;

    beforeEach(() => {
        updateAfterTransform = vi.fn();
        const hostMock: Partial<ImageUndoManagerHost> = {
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") || undefined,
            updateCanvasElementAfterTransformChange: updateAfterTransform,
            getActiveElement: vi.fn(() => undefined),
            setActiveElement: vi.fn(),
            removeDetachedTargets: vi.fn(),
            updateCanvasElementClass: vi.fn(),
        };
        manager = new ImageUndoManager(hostMock as ImageUndoManagerHost);

        document.body.innerHTML = "";
        const page = document.createElement("div");
        page.className = "bloom-page";
        page.setAttribute("data-page-id", "rotate-test-page");
        document.body.appendChild(page);

        canvasElement = document.createElement("div");
        canvasElement.className = "bloom-canvas-element";
        page.appendChild(canvasElement);

        const container = document.createElement("div");
        container.className = "bloom-imageContainer";
        canvasElement.appendChild(container);

        imgElement = document.createElement("img");
        imgElement.src = "test-image.png";
        container.appendChild(imgElement);
    });

    it("undo puts back the rotation of the canvas element box", () => {
        canvasElement.style.transform = "rotate(30deg)";
        canvasElement.classList.add("bloom-rotated");

        manager.pushUndoForImageTransform(canvasElement);

        // Rotate right turns the box by another quarter turn.
        canvasElement.style.transform = "rotate(120deg)";
        expect(canvasElement.style.transform).toBe("rotate(120deg)");

        expect(manager.undoImageOperation()).toBe(true);
        expect(canvasElement.style.transform).toBe("rotate(30deg)");
        expect(canvasElement.classList.contains("bloom-rotated")).toBe(true);
        expect(updateAfterTransform).toHaveBeenCalledWith(
            canvasElement,
            imgElement,
        );
    });

    it("undo of a rotation back to upright leaves no transform behind", () => {
        manager.pushUndoForImageTransform(canvasElement);
        canvasElement.style.transform = "rotate(90deg)";
        canvasElement.classList.add("bloom-rotated");

        expect(manager.undoImageOperation()).toBe(true);
        expect(canvasElement.style.transform).toBe("");
        expect(canvasElement.classList.contains("bloom-rotated")).toBe(false);
    });

    it("undo puts back the turn of the picture and the crop that the turn removed", () => {
        imgElement.style.width = "150px";
        imgElement.style.height = "120px";
        imgElement.style.left = "-25px";
        imgElement.style.top = "-30px";

        manager.pushUndoForImageTransform(canvasElement);

        // Rotate right on a background image turns the picture and drops the crop.
        imgElement.style.transform = "rotate(90deg) scale(0.667, 0.667)";
        imgElement.style.width = "";
        imgElement.style.height = "";
        imgElement.style.left = "";
        imgElement.style.top = "";

        expect(manager.undoImageOperation()).toBe(true);
        expect(imgElement.style.transform).toBe("");
        expect(imgElement.style.width).toBe("150px");
        expect(imgElement.style.height).toBe("120px");
        expect(imgElement.style.left).toBe("-25px");
        expect(imgElement.style.top).toBe("-30px");
    });

    it("undo puts back the mirror of the picture", () => {
        imgElement.style.transform = "scale(-1, 1)";

        manager.pushUndoForImageTransform(canvasElement);

        // Flip horizontal on a picture that is already mirrored puts it back.
        imgElement.style.transform = "";

        expect(manager.undoImageOperation()).toBe(true);
        expect(imgElement.style.transform).toBe("scale(-1, 1)");
    });

    it("a second undo does nothing, because there is only one record", () => {
        manager.pushUndoForImageTransform(canvasElement);
        canvasElement.style.transform = "rotate(90deg)";

        expect(manager.undoImageOperation()).toBe(true);
        expect(manager.undoImageOperation()).toBe(false);
    });
});

describe("ImageUndoManager rotation handle drag", () => {
    let manager: ImageUndoManager;
    let updateAfterTransform: (
        canvasElement: HTMLElement,
        img: HTMLImageElement | undefined,
    ) => void;
    let activeElement: HTMLElement | undefined;
    let page: HTMLElement;

    // A drag of the rotation handle records the angle the element had before the drag, so
    // the tests set the new angle first and then push the old one, as the drag does.
    beforeEach(() => {
        updateAfterTransform = vi.fn();
        activeElement = undefined;
        const hostMock: Partial<ImageUndoManagerHost> = {
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") || undefined,
            updateCanvasElementAfterTransformChange: updateAfterTransform,
            getActiveElement: () => activeElement,
            setActiveElement: vi.fn(),
            removeDetachedTargets: vi.fn(),
            updateCanvasElementClass: vi.fn(),
        };
        manager = new ImageUndoManager(hostMock as ImageUndoManagerHost);

        document.body.innerHTML = "";
        page = document.createElement("div");
        page.className = "bloom-page";
        page.setAttribute("data-page-id", "drag-test-page");
        document.body.appendChild(page);
    });

    function makeTextCanvasElement(): HTMLElement {
        const element = document.createElement("div");
        element.className = "bloom-canvas-element";
        const editable = document.createElement("div");
        editable.className = "bloom-editable";
        editable.textContent = "some words";
        element.appendChild(editable);
        page.appendChild(element);
        return element;
    }

    it("undo puts back the angle a text box had before the drag", () => {
        const textBox = makeTextCanvasElement();
        setCanvasElementRotation(textBox, 10);
        expect(textBox.style.transform).toBe("rotate(10deg)");

        // The drag turns the box, then records where it started.
        setCanvasElementRotation(textBox, 75);
        manager.pushUndoForCanvasElementRotation(textBox, 10);
        expect(textBox.style.transform).toBe("rotate(75deg)");

        expect(manager.undoImageOperation()).toBe(true);
        expect(textBox.style.transform).toBe("rotate(10deg)");
        expect(updateAfterTransform).toHaveBeenCalledWith(textBox, undefined);
    });

    it("a rotated text box is undoable while it is the selected element", () => {
        const textBox = makeTextCanvasElement();
        const otherBox = makeTextCanvasElement();
        setCanvasElementRotation(textBox, 45);
        manager.pushUndoForCanvasElementRotation(textBox, 0);

        activeElement = textBox;
        expect(manager.canUndoImageOperation()).toBe(true);

        // Another element is selected, so this undo belongs to nothing the user is looking at.
        activeElement = otherBox;
        expect(manager.canUndoImageOperation()).toBe(false);
    });
});
