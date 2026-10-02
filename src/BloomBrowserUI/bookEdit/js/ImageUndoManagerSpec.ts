import { describe, it, expect, beforeEach, vi } from "vitest";
import {
    IImageCropInfo,
    ImageUndoManager,
    ImageUndoManagerHost,
} from "./ImageUndoManager";

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

    it("captures the canvas element's box as well as the image's", () => {
        // A cropped picture is two boxes, not one: the canvas element is the shape of the
        // part that shows, and the img inside it is larger and pushed up and left. Replacing
        // the picture reshapes both, so undo has to be able to put both back (BL-16868).
        const canvasElement = document.createElement("div");
        canvasElement.className = "bloom-canvas-element";
        canvasElement.style.width = "100px";
        canvasElement.style.height = "80px";
        canvasElement.style.left = "10px";
        canvasElement.style.top = "20px";
        containerDiv.replaceWith(canvasElement);
        canvasElement.appendChild(containerDiv);

        manager.prepareUndoForImageOperation(imgElement);
        manager.commitPendingImageOperationUndo(imgElement);

        // The replacement reshapes both boxes to suit the new picture.
        canvasElement.style.width = "200px";
        canvasElement.style.height = "50px";
        canvasElement.style.left = "0px";
        canvasElement.style.top = "35px";
        imgElement.src = "new-image.png";

        expect(manager.undoImageOperation()).toBe(true);

        const cropInfo = (
            hostMock.updateCanvasElementForChangedImage as ReturnType<
                typeof vi.fn
            >
        ).mock.calls[0][1];
        expect(cropInfo.width).toBe("150px");
        expect(cropInfo.left).toBe("-25px");
        expect(cropInfo.canvasElement).toEqual({
            width: "100px",
            height: "80px",
            left: "10px",
            top: "20px",
        });
    });
});
