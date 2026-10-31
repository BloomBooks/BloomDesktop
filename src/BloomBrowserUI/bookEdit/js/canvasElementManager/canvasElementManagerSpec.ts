import { afterEach, beforeEach, describe, it, expect, vi } from "vitest";
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
import { kTypingUndoDescriptionL10nKey } from "../../pageHistory/pageHistoryInput";
import { setCanvasElementRotation } from "./canvasElementRotation";

// vitest.setup.ts mocks comicaljs without Comical; updateAfterUndoOrRedo needs it. Rotate right
// asks Bubble for the box's bubble style (canRotateCanvasElement); a picture box has none.
const comical = vi.hoisted(() => ({
    activateElement: vi.fn(),
    update: vi.fn(),
    startEditing: vi.fn(),
}));
vi.mock("comicaljs", () => ({
    Bubble: Object.assign(vi.fn(), {
        getBubbleSpec: () => ({ style: "none" }),
    }),
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

    it("puts the old picture's rotation and mirror back when the change is undone", async () => {
        const { img } = makeRotatedAndMirroredPicture();
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            img.setAttribute("src", "new.png");
            updateForChangedImage(img);
        });
        // Sanity check: the change really took the transform away.
        expect(img.style.transform).toBe("");

        await history.undo();

        expect(img.getAttribute("src")).toBe("old.png");
        expect(img.style.transform).toBe("rotate(90deg) scale(-1, 1)");
        history.dispose();
    });

    it("puts a background element back in the old picture's shape when the change is undone", async () => {
        const { element, img } = makeRotatedAndMirroredPicture();
        element.classList.add("bloom-backgroundImage");
        element.style.transform = "";
        // The shape Rotate Right gave the old picture: portrait, centred in the picture area.
        element.style.width = "150px";
        element.style.height = "300px";
        element.style.left = "125px";
        element.style.top = "0px";
        img.style.width = "300px";
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            img.setAttribute("src", "new.png");
            updateForChangedImage(img);
            // What fitting the landscape replacement does to the element.
            element.style.width = "400px";
            element.style.height = "200px";
            element.style.left = "0px";
            element.style.top = "50px";
        });

        await history.undo();

        expect([
            element.style.width,
            element.style.height,
            element.style.left,
            element.style.top,
        ]).toEqual(["150px", "300px", "125px", "0px"]);
        expect(img.getAttribute("src")).toBe("old.png");
        history.dispose();
    });

    it("puts back the old picture's credits and crop when the change is undone", async () => {
        const { img } = makeRotatedAndMirroredPicture();
        img.setAttribute("data-copyright", "© 2024");
        img.setAttribute("data-creator", "Test Creator");
        img.setAttribute("data-license", "cc-by");
        img.style.width = "150px";
        img.style.height = "120px";
        img.style.left = "-25px";
        img.style.top = "-30px";
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        // As changeImage does: the new picture's file and credits, then the update, which takes
        // the old picture's crop away.
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            img.setAttribute("src", "new.png");
            img.setAttribute("data-copyright", "© 2025");
            img.setAttribute("data-creator", "Someone Else");
            img.setAttribute("data-license", "cc-by-sa");
            updateForChangedImage(img);
        });
        // Sanity check: the change took the crop away.
        expect(img.style.width).toBe("");
        expect(img.style.left).toBe("");

        await history.undo();

        expect(img.getAttribute("src")).toBe("old.png");
        expect(img.getAttribute("data-copyright")).toBe("© 2024");
        expect(img.getAttribute("data-creator")).toBe("Test Creator");
        expect(img.getAttribute("data-license")).toBe("cc-by");
        expect([
            img.style.width,
            img.style.height,
            img.style.left,
            img.style.top,
        ]).toEqual(["150px", "120px", "-25px", "-30px"]);
        history.dispose();
    });

    it("leaves a picture that had no crop with none when a crop of it is undone", async () => {
        const { img } = makeRotatedAndMirroredPicture();
        // Sanity check: no crop.
        expect(img.style.width).toBe("");
        expect(img.style.left).toBe("");
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        // A crop is the picture's box made bigger than the element and moved.
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            img.style.width = "100px";
            img.style.left = "-50px";
        });

        await history.undo();

        expect(img.style.width).toBe("");
        expect(img.style.left).toBe("");
        history.dispose();
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

    // Rotate right as a page-history step, with the box's angle before it as the test gives it.
    async function rotateRightAndUndo(angleBefore: number): Promise<{
        element: HTMLElement;
        transformAfterRotation: string;
    }> {
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
        setCanvasElementRotation(element, angleBefore);
        // The parts of the manager that Rotate right uses; the frame and the target need layout.
        const manager = {
            activeElement: element,
            rotateActiveElementRight90Degrees:
                CanvasElementManager.prototype
                    .rotateActiveElementRight90Degrees,
            adjustTarget: vi.fn(),
            alignControlFrameWithActiveElement: vi.fn(),
            adjustStuffRelatedToImage: vi.fn(),
        };
        const history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            expect(
                CanvasElementManager.prototype.rotateActiveImageRight90Degrees.call(
                    manager,
                ),
                "test setup: Rotate right should have rotated the box",
            ).toBe(true);
        });
        const transformAfterRotation = element.style.transform;
        await history.undo();
        history.dispose();
        return { element, transformAfterRotation };
    }

    it("puts back the box's angle and its mark when Rotate right is undone", async () => {
        const { element, transformAfterRotation } =
            await rotateRightAndUndo(30);

        expect(transformAfterRotation, "test setup").toBe("rotate(120deg)");
        expect(element.style.transform).toBe("rotate(30deg)");
        expect(element.classList.contains("bloom-rotated")).toBe(true);
    });

    it("leaves no transform and no mark when the undo of Rotate right brings the box upright", async () => {
        const { element, transformAfterRotation } = await rotateRightAndUndo(0);

        expect(transformAfterRotation, "test setup").toBe("rotate(90deg)");
        expect(element.style.transform).toBe("");
        expect(element.classList.contains("bloom-rotated")).toBe(false);
    });
});

// A text box's rotation, by the knob, and the edits that come after it, each a page-history step.
describe("undo of a text box's rotation and the edits after it", () => {
    const kOriginalText =
        '<p><a href="https://example.org/a">some</a> words</p>';
    let history: PageHistory;
    let textBox: HTMLElement;
    let editable: HTMLElement;

    beforeEach(() => {
        document.body.innerHTML = `
            <div class="bloom-page" data-page-id="page1">
                <div class="bloom-canvas">
                    <div class="bloom-canvas-element">
                        <div class="bloom-translationGroup">
                            <div class="bloom-editable" lang="en">${kOriginalText}</div>
                        </div>
                    </div>
                </div>
            </div>`;
        textBox = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        editable = textBox.querySelector(".bloom-editable") as HTMLElement;
        history = createPageHistory({
            root: document.querySelector(".bloom-page") as HTMLElement,
            filter: createEditTimeMarkupFilter(),
        });
    });

    afterEach(() => history.dispose());

    // A drag of the knob turns the box as the pointer moves, in one step.
    function dragKnobTo(...angles: number[]): Promise<unknown> {
        return history.doTransaction(kGenericUndoDescriptionL10nKey, () =>
            angles.forEach((angle) => setCanvasElementRotation(textBox, angle)),
        );
    }

    it("puts back the angle the box had before a drag of the knob", async () => {
        history.doOutsideHistory(() => setCanvasElementRotation(textBox, 10));
        await dragKnobTo(30, 55, 75);
        // Sanity check
        expect(textBox.style.transform).toBe("rotate(75deg)");

        await history.undo();

        expect(textBox.style.transform).toBe("rotate(10deg)");
    });

    it.each([
        [
            "typing",
            `<p><a href="https://example.org/a">some</a> words and more</p>`,
        ],
        [
            "formatting",
            `<p><strong><a href="https://example.org/a">some</a> words</strong></p>`,
        ],
        [
            "a changed link",
            `<p><a href="https://example.org/b">some</a> words</p>`,
        ],
        [
            "a line break",
            `<p><a href="https://example.org/a">some</a> <br>words</p>`,
        ],
        [
            "a word break",
            `<p><a href="https://example.org/a">some</a> ​words</p>`,
        ],
    ])(
        "takes back %s after a rotation first, and then the rotation",
        async (_edit, editedHtml) => {
            await dragKnobTo(45);
            await history.doTransaction(kTypingUndoDescriptionL10nKey, () => {
                editable.innerHTML = editedHtml;
            });
            // Sanity check: the edit changed the box.
            expect(editable.innerHTML).toBe(editedHtml);

            await history.undo();
            expect(editable.innerHTML).toBe(kOriginalText);
            expect(
                textBox.style.transform,
                "the first undo should take back only the edit",
            ).toBe("rotate(45deg)");

            await history.undo();
            expect(textBox.style.transform).toBe("");
            expect(editable.innerHTML).toBe(kOriginalText);
        },
    );

    it("takes back the rotation next once typing in an empty box is undone", async () => {
        // An empty box, as a fresh Text Block is.
        history.doOutsideHistory(() => (editable.innerHTML = "<p><br></p>"));
        await dragKnobTo(45);
        await history.doTransaction(kTypingUndoDescriptionL10nKey, () => {
            editable.innerHTML = "<p>xyz</p>";
        });

        await history.undo();
        expect(editable.innerHTML).toBe("<p><br></p>");
        expect(history.undoDescriptionL10nKey).toBe(
            kGenericUndoDescriptionL10nKey,
        );

        await history.undo();
        expect(textBox.style.transform).toBe("");
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
