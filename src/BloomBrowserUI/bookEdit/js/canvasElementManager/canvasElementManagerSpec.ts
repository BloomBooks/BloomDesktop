import { describe, it, expect, vi } from "vitest";
import { getTestRoot, removeTestRoot } from "../../../utils/testHelper";
import { CanvasElementManager } from "./CanvasElementManager";
import jQuery from "jquery";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
    type RestoredContent,
} from "../../pageHistory/pageHistoryTypes";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { setCanvasElementRotation } from "./canvasElementRotation";

// vitest.setup.ts mocks comicaljs without Comical; updateAfterUndoOrRedo needs it.
const comical = vi.hoisted(() => ({
    activateElement: vi.fn(),
    update: vi.fn(),
    startEditing: vi.fn(),
}));
vi.mock("comicaljs", () => ({
    Bubble: vi.fn(),
    Node: vi.fn(),
    Parser: vi.fn(),
    BubbleSpec: {},
    TailSpec: {},
    Comical: comical,
}));

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
    function updateForChangedImage(img: HTMLElement): void {
        (
            CanvasElementManager.prototype
                .updateCanvasElementForChangedImage as (
                ...args: unknown[]
            ) => void
        ).call(manager, img);
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

    it("puts the old box rotation back when the change is undone", async () => {
        const { element, img } = makeRotatedAndMirroredPicture();
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        // A picture change is one step, as changeImage makes it.
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            img.setAttribute("src", "new.png");
            updateForChangedImage(img);
        });
        // Sanity check: the change really straightened the box.
        expect(element.style.transform).toBe("");

        await history.undo();

        expect(element.style.transform).toBe("rotate(30deg)");
        expect(element.classList.contains("bloom-rotated")).toBe(true);
        expect(img.getAttribute("src")).toBe("old.png");
        history.dispose();
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
});

describe("CanvasElementManager.resetImage", () => {
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

    // Reset Image is a step like any other: its undo puts back the rotation it took away, and
    // the undos before it take the box back through the rotations that came before.
    it("puts back the rotation it took away when it is undone, and the rotations before it", async () => {
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
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        const step = (fn: () => void) =>
            history.doTransaction(kGenericUndoDescriptionL10nKey, fn);
        // Turn the box to 45 degrees, then to 90, each a step.
        await step(() => setCanvasElementRotation(element, 45));
        await step(() => setCanvasElementRotation(element, 90));
        const manager = {
            activeElement: element,
            resetCropping: vi.fn(),
            adjustStuffRelatedToImage: vi.fn(),
        };

        await step(() =>
            CanvasElementManager.prototype.resetImage.call(manager),
        );
        // Sanity check: the reset straightened the box.
        expect(element.style.transform).toBe("");

        await history.undo();
        expect(element.style.transform).toBe("rotate(90deg)");
        await history.undo();
        expect(element.style.transform).toBe("rotate(45deg)");
        await history.undo();
        expect(element.style.transform).toBe("");
        history.dispose();
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

describe("CanvasElementManager.updateAfterUndoOrRedo", () => {
    // As above, the method runs against a stand-in with just what it uses. The stand-in's
    // canvas elements before the undo are those on the page when it is made, and the undo
    // changed none of them, so the method selects nothing new
    // (canvasElementUndoSelectionSpec.ts tests what it selects).
    function makeManager(activeElement: HTMLElement | undefined) {
        const allCanvasElementsOnPage = () =>
            Array.from(
                document.querySelectorAll<HTMLElement>(".bloom-canvas-element"),
            );
        return {
            isCanvasElementEditingOn: true,
            activeElement,
            theCanvasElementWeAreTextEditing: activeElement,
            canvasElementsBeforeUndoOrRedo: allCanvasElementsOnPage(),
            // As noteCanvasElementsBeforeUndoOrRedo records it: the active element before the undo.
            activeElementBeforeUndoOrRedo: activeElement,
            getAllCanvasElementsOnPage: allCanvasElementsOnPage,
            getAllBloomCanvasesOnPage: () =>
                Array.from(
                    document.querySelectorAll<HTMLElement>(".bloom-canvas"),
                ),
            setupControlFrame: vi.fn(),
            doNotifyChange: vi.fn(),
            redrawBubblesFromPage:
                CanvasElementManager.prototype.redrawBubblesFromPage,
            // A private method; canvasElementUndoFocusSpec.ts tests what it does.
            takeFocusOutOfCanvasElementsOtherThanActive:
                CanvasElementManager.prototype[
                    "takeFocusOutOfCanvasElementsOtherThanActive"
                ],
        };
    }
    const nothingRestored: RestoredContent = { roots: [], direction: "undo" };
    const history = {
        doOutsideHistory: <T>(fn: () => T) => fn(),
    } as unknown as PageHistory;

    it("lets go of an active element that the undo took out of the page", () => {
        document.body.innerHTML = `<div class="bloom-canvas-element" data-bloom-active="true"></div>`;
        const removed = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        removed.remove();
        const manager = makeManager(removed);

        CanvasElementManager.prototype.updateAfterUndoOrRedo.call(
            manager,
            nothingRestored,
            history,
        );

        expect(manager.activeElement).toBeUndefined();
        expect(manager.theCanvasElementWeAreTextEditing).toBeUndefined();
        expect(manager.setupControlFrame).toHaveBeenCalled();
        expect(manager.doNotifyChange).toHaveBeenCalled();
    });

    it("keeps an active element that is still in the page, and marks only it active", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas-element" id="a"></div>
            <div class="bloom-canvas-element" id="b" data-bloom-active="true"></div>`;
        const active = document.getElementById("a") as HTMLElement;
        const manager = makeManager(active);

        CanvasElementManager.prototype.updateAfterUndoOrRedo.call(
            manager,
            nothingRestored,
            history,
        );

        expect(manager.activeElement).toBe(active);
        expect(active.getAttribute("data-bloom-active")).toBe("true");
        expect(
            document.getElementById("b")!.hasAttribute("data-bloom-active"),
        ).toBe(false);
    });

    it("draws again the bubbles of a bloom-canvas Comical edits, and starts editing one it does not", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas" id="edited"><canvas class="comical-generated"></canvas></div>
            <div class="bloom-canvas" id="new"></div>`;
        comical.update.mockClear();
        comical.startEditing.mockClear();
        const manager = makeManager(undefined);

        CanvasElementManager.prototype.updateAfterUndoOrRedo.call(
            manager,
            nothingRestored,
            history,
        );

        expect(comical.update).toHaveBeenCalledWith(
            document.getElementById("edited"),
        );
        expect(comical.startEditing).toHaveBeenCalledWith([
            document.getElementById("new"),
        ]);
        expect(comical.update).toHaveBeenCalledTimes(1);
        expect(comical.startEditing).toHaveBeenCalledTimes(1);
    });

    it("does nothing while canvas element editing is off", () => {
        const manager = {
            ...makeManager(undefined),
            isCanvasElementEditingOn: false,
        };

        CanvasElementManager.prototype.updateAfterUndoOrRedo.call(
            manager,
            nothingRestored,
            history,
        );

        expect(manager.setupControlFrame).not.toHaveBeenCalled();
    });
});

describe("CanvasElementManager.redrawBubblesFromPage", () => {
    function makeManager(isCanvasElementEditingOn: boolean) {
        document.body.innerHTML = `
            <div class="bloom-canvas"><canvas class="comical-generated"></canvas>
                <div class="bloom-canvas-element" id="active"></div>
            </div>`;
        return {
            isCanvasElementEditingOn,
            activeElement: document.getElementById("active") as HTMLElement,
            getAllBloomCanvasesOnPage: () =>
                Array.from(
                    document.querySelectorAll<HTMLElement>(".bloom-canvas"),
                ),
        };
    }

    it("draws the bubbles again, then activates the active element again, since redrawing drops its handles", () => {
        comical.update.mockClear();
        comical.activateElement.mockClear();
        const manager = makeManager(true);

        CanvasElementManager.prototype.redrawBubblesFromPage.call(manager);

        expect(comical.update).toHaveBeenCalledTimes(1);
        expect(comical.activateElement).toHaveBeenCalledWith(
            manager.activeElement,
        );
        expect(
            comical.activateElement.mock.invocationCallOrder[0],
        ).toBeGreaterThan(comical.update.mock.invocationCallOrder[0]);
    });

    it("does nothing while canvas element editing is off", () => {
        comical.update.mockClear();
        comical.startEditing.mockClear();
        comical.activateElement.mockClear();

        CanvasElementManager.prototype.redrawBubblesFromPage.call(
            makeManager(false),
        );

        expect(comical.update).not.toHaveBeenCalled();
        expect(comical.startEditing).not.toHaveBeenCalled();
        expect(comical.activateElement).not.toHaveBeenCalled();
    });
});
