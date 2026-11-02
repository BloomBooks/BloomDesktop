import { beforeAll, describe, it, expect, vi } from "vitest";
import { getTestRoot, removeTestRoot } from "../../../utils/testHelper";
import { CanvasElementManager } from "./CanvasElementManager";
import jQuery from "jquery";
import {
    canUndoImageOperation,
    ImageUndoManager,
    ImageUndoManagerHost,
    initializeImageUndoManager,
    pushUndoForImageTransform,
} from "../ImageUndoManager";
import { setCanvasElementRotation } from "./canvasElementRotation";

// A (currently very incomplete) set of tests for CanvasElementManager.

describe("CanvasElementManager.getLabeledNumber", () => {
    // beforeEach(() => {
    // });

    // // Politely clean up for the next test suite.
    // afterAll(removeTestRoot);

    it("extracts integer size from style", () => {
        const result = CanvasElementManager.getLabeledNumberInPx(
            "width",
            "left: 224px; top: 79.6px; width: 66px; height: 30px;",
        );
        expect(result).toBe(66);
    });

    it("extracts float size from style", () => {
        const result = CanvasElementManager.getLabeledNumberInPx(
            "top",
            "left: 224px; top: 79.6px; width: 66px; height: 30px;",
        );
        expect(result).toBe(79.6);
    });
    it("extracts negative size from style", () => {
        const result = CanvasElementManager.getLabeledNumberInPx(
            "left",
            "left: -10.4px; top: 79.6px; width: 66px; height: 30px;",
        );
        expect(result).toBe(-10.4);
    });
});

describe("CanvasElementManager.adjustLabeledNumber", () => {
    // beforeEach(() => {
    // });

    // // Politely clean up for the next test suite.
    // afterAll(removeTestRoot);

    it("adjusts center of text box", () => {
        const result = CanvasElementManager.adjustCenterOfTextBox(
            "left",
            "left: 30px; top: 79.6px; width: 66px; height: 30px;",
            2, // making stuff 2x larger
            7, // the old area being resized was 7px from the left
            13, // the new area is 13px from the left
            20, // the object we're adjusting is 20px wide
        );
        // So, the point we're adjusting is the center, originally 40px from the container left
        // That's 33px from the left of the area being scaled, which becomes 66px
        // from the left of the new area, which is now 13px from the container left,
        // which we add to get 79. The left is still 10px (half the width) less,
        // so we get 69
        expect(result).toBe(
            "left: 69px; top: 79.6px; width: 66px; height: 30px;",
        );
    });
});

describe("CanvasElementManager.updateCanvasElementForChangedImage", () => {
    // The constructor needs much more of Bloom than a test has, so the method runs against a
    // stand-in that has just the two size adjusters it calls. Those need layout, which jsdom
    // does not do.
    const manager = {
        adjustContainerAspectRatio: vi.fn(),
        adjustBackgroundImageSize: vi.fn(),
    };
    function updateForChangedImage(img: HTMLElement, ...rest: unknown[]): void {
        (
            CanvasElementManager.prototype
                .updateCanvasElementForChangedImage as (
                ...args: unknown[]
            ) => void
        ).call(manager, img, ...rest);
    }

    // A picture canvas element whose box is rotated 30 degrees and whose picture Rotate Right
    // and Flip have rotated and mirrored.
    function makeRotatedAndMirroredPicture(): {
        element: HTMLElement;
        img: HTMLImageElement;
    } {
        document.body.innerHTML = `
            <div class="bloom-page" data-page-id="page1">
                <div class="bloom-canvas">
                    <div class="bloom-canvas-element bloom-rotated" style="transform: rotate(30deg)">
                        <div class="bloom-imageContainer">
                            <img src="old.png" style="transform: rotate(90deg) scale(-1, 1)">
                        </div>
                    </div>
                </div>
            </div>`;
        return {
            element: document.querySelector(
                ".bloom-canvas-element",
            ) as HTMLElement,
            img: document.querySelector("img") as HTMLImageElement,
        };
    }

    it("gives a new picture none of the old picture's rotation and mirror, and straightens the box", () => {
        const { element, img } = makeRotatedAndMirroredPicture();
        expect(img.style.transform).toBe("rotate(90deg) scale(-1, 1)");
        expect(element.style.transform).toBe("rotate(30deg)");

        img.setAttribute("src", "new.png");
        updateForChangedImage(img);

        expect(img.style.transform).toBe("");
        expect(element.style.transform).toBe("");
        expect(element.classList.contains("bloom-rotated")).toBe(false);
        expect(manager.adjustContainerAspectRatio).toHaveBeenCalled();
    });

    it("puts the old box rotation back when the change is undone", () => {
        const { element, img } = makeRotatedAndMirroredPicture();
        const undoManager = new ImageUndoManager({
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") ?? undefined,
            updateCanvasElementForChangedImage: updateForChangedImage,
            getActiveElement: () => undefined,
        } as unknown as ImageUndoManagerHost);
        undoManager.prepareUndoForImageOperation(img);
        img.setAttribute("src", "new.png");
        updateForChangedImage(img);
        undoManager.commitPendingImageOperationUndo(img);
        // Sanity check: the change really straightened the box.
        expect(element.style.transform).toBe("");

        expect(undoManager.undoImageOperation()).toBe(true);

        expect(element.style.transform).toBe("rotate(30deg)");
        expect(element.classList.contains("bloom-rotated")).toBe(true);
    });

    it("clears the picture's rotation before a background element is fitted to the new picture", () => {
        const { element, img } = makeRotatedAndMirroredPicture();
        element.classList.add("bloom-backgroundImage");
        let transformWhenFitted: string | undefined;
        manager.adjustBackgroundImageSize.mockImplementationOnce(() => {
            transformWhenFitted = img.style.transform;
        });

        img.setAttribute("src", "new.png");
        updateForChangedImage(img);

        expect(manager.adjustBackgroundImageSize).toHaveBeenCalled();
        expect(transformWhenFitted).toBe("");
    });

    it("puts the old picture's rotation and mirror back when the change is undone", () => {
        const { img } = makeRotatedAndMirroredPicture();
        const undoManager = new ImageUndoManager({
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") ?? undefined,
            updateCanvasElementForChangedImage: updateForChangedImage,
            getActiveElement: () => undefined,
        } as unknown as ImageUndoManagerHost);
        undoManager.prepareUndoForImageOperation(img);
        img.setAttribute("src", "new.png");
        updateForChangedImage(img);
        undoManager.commitPendingImageOperationUndo(img);
        // Sanity check: the change really took the transform away.
        expect(img.style.transform).toBe("");

        expect(undoManager.undoImageOperation()).toBe(true);

        expect(img.getAttribute("src")).toBe("old.png");
        expect(img.style.transform).toBe("rotate(90deg) scale(-1, 1)");
    });

    it("puts a background element back in the old picture's shape before it is fitted on undo", () => {
        const { element, img } = makeRotatedAndMirroredPicture();
        element.classList.add("bloom-backgroundImage");
        element.style.transform = "";
        // The shape Rotate Right gave the old picture: portrait, centred in the picture area.
        element.style.width = "150px";
        element.style.height = "300px";
        element.style.left = "125px";
        element.style.top = "0px";
        img.style.width = "300px";
        const undoManager = new ImageUndoManager({
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") ?? undefined,
            updateCanvasElementForChangedImage: updateForChangedImage,
            getActiveElement: () => undefined,
        } as unknown as ImageUndoManagerHost);
        undoManager.prepareUndoForImageOperation(img);
        img.setAttribute("src", "new.png");
        updateForChangedImage(img);
        // What fitting the landscape replacement does to the element.
        element.style.width = "400px";
        element.style.height = "200px";
        element.style.left = "0px";
        element.style.top = "50px";
        undoManager.commitPendingImageOperationUndo(img);
        let geometryWhenFitted: string[] | undefined;
        manager.adjustBackgroundImageSize.mockImplementationOnce(() => {
            geometryWhenFitted = [
                element.style.width,
                element.style.height,
                element.style.left,
                element.style.top,
            ];
        });

        expect(undoManager.undoImageOperation()).toBe(true);

        expect(geometryWhenFitted).toEqual(["150px", "300px", "125px", "0px"]);
    });
});

describe("CanvasElementManager.resetImage", () => {
    // resetImage reaches the page's one undo manager through module functions, so it has
    // to exist. The selected element is whatever a test puts here.
    let selected: HTMLElement | undefined;
    beforeAll(() => {
        initializeImageUndoManager({
            getCurrentPage: () =>
                document.querySelector<HTMLElement>(".bloom-page") ?? undefined,
            getActiveElement: () => selected,
        } as unknown as ImageUndoManagerHost);
    });

    it("straightens a picture's box as well as taking away its crop, rotation and mirror", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-canvas-element bloom-rotated" style="transform: rotate(90deg)">
                    <div class="bloom-imageContainer">
                        <img src="picture.png" style="transform: scale(-1, 1)">
                    </div>
                </div>
            </div>`;
        const element = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        const img = element.querySelector("img") as HTMLImageElement;
        // resetCropping and the frame update need layout, which jsdom does not do.
        const manager = {
            activeElement: element,
            resetCropping: vi.fn(),
            adjustStuffRelatedToImage: vi.fn(),
        };
        // Sanity check: Rotate right turned the box and Flip mirrored the picture.
        expect(element.style.transform).toBe("rotate(90deg)");
        expect(img.style.transform).toBe("scale(-1, 1)");

        CanvasElementManager.prototype.resetImage.call(manager);

        expect(img.style.transform).toBe("");
        expect(element.style.transform).toBe("");
        expect(element.classList.contains("bloom-rotated")).toBe(false);
        expect(manager.resetCropping).toHaveBeenCalled();
    });

    it("leaves no earlier rotation for Undo to put back on the reset picture", () => {
        document.body.innerHTML = `
            <div class="bloom-page" data-page-id="page1">
                <div class="bloom-canvas">
                    <div class="bloom-canvas-element">
                        <div class="bloom-imageContainer"><img src="picture.png"></div>
                    </div>
                </div>
            </div>`;
        const element = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        selected = element;
        // Turn the box to 45 degrees, then to 90.
        pushUndoForImageTransform(element);
        setCanvasElementRotation(element, 45);
        pushUndoForImageTransform(element);
        setCanvasElementRotation(element, 90);
        // Sanity check: before the reset, Undo is offered.
        expect(canUndoImageOperation()).toBe(true);
        const manager = {
            activeElement: element,
            resetCropping: vi.fn(),
            adjustStuffRelatedToImage: vi.fn(),
        };

        CanvasElementManager.prototype.resetImage.call(manager);

        expect(element.style.transform).toBe("");
        expect(canUndoImageOperation()).toBe(false);
    });
});

describe("CanvasElementManager.rotateActiveImageRight90Degrees", () => {
    it("rotates nothing in an empty picture slot, even one whose box can rotate", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-canvas-element">
                    <div class="bloom-imageContainer"><img src="placeHolder.png"></div>
                </div>
            </div>`;
        const element = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        const manager = {
            activeElement: element,
            rotateActiveElementRight90Degrees: vi.fn(),
            adjustStuffRelatedToImage: vi.fn(),
        };

        const rotated =
            CanvasElementManager.prototype.rotateActiveImageRight90Degrees.call(
                manager,
            );

        expect(rotated).toBe(false);
        expect(
            manager.rotateActiveElementRight90Degrees,
        ).not.toHaveBeenCalled();
        expect(element.style.transform).toBe("");
    });

    it("rotates nothing on a navigation button, neither the button nor its picture", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-canvas-element bloom-canvas-button">
                    <div class="bloom-imageContainer"><img src="picture.png"></div>
                </div>
            </div>`;
        const element = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        const img = element.querySelector("img") as HTMLImageElement;
        const manager = {
            activeElement: element,
            rotateActiveElementRight90Degrees: vi.fn(),
            adjustStuffRelatedToImage: vi.fn(),
        };

        const rotated =
            CanvasElementManager.prototype.rotateActiveImageRight90Degrees.call(
                manager,
            );

        expect(rotated).toBe(false);
        expect(
            manager.rotateActiveElementRight90Degrees,
        ).not.toHaveBeenCalled();
        expect(element.style.transform).toBe("");
        expect(img.style.transform).toBe("");
    });
});
