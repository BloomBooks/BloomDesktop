import {
    describe,
    it,
    expect,
    beforeEach,
    afterEach,
    afterAll,
    vi,
} from "vitest";

// The code under test asks the api in three ways whether AI image editing is turned on:
// setupInlineImageInteractions calls getFeatureStatusAsync, and the toolbar component that the
// tests below show uses both hooks. There is no api in these tests, so each of those requests
// would fail on the network, and what the menu contains would depend on when the failure
// arrived. So the mock answers them here, with "off", which is what the menu tests expect (no
// "Edit with AI"). The mock has to provide every export the component might use, because a
// module mock replaces the whole module, and a missing name throws inside React's render.
vi.mock("../../react_components/featureStatus", () => ({
    getFeatureStatusAsync: () => Promise.resolve(undefined),
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
    openBloomSubscriptionSettings: () => {},
}));
// There is no toolbox frame in these tests. A test that needs the reader tools to have a markup
// tool selected sets readerToolsHandleUndoKeys.
const toolboxStub = vi.hoisted(() => ({ readerToolsHandleUndoKeys: false }));
vi.mock("./workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./workspaceFrames")>()),
    getToolboxBundleExports: () => ({
        readerToolsHandleUndoKeys: () => toolboxStub.readerToolsHandleUndoKeys,
    }),
}));
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import {
    commitPendingInlineImageUndo,
    discardPendingInlineImageUndo,
    getInlineImage,
    getInlineImageInEditable,
    getInlineImages,
    inlineImageCanUndo,
    inlineImageUndo,
    insertInlineImage,
    kInlineImageBottomClass,
    kInlineImageClass,
    kInlineImageIdAttr,
    kInlineImageLeftClass,
    kInlineImageMiddleClass,
    kInlineImageOffsetBasedOnAttr,
    kInlineImageRightClass,
    kInlineImageSelectedClass,
    kInlineImageWidthVar,
    prepareInlineImageUndo,
    setInlineImageDock,
    syncInlineImagesFromEditable,
} from "./inlineImages";
import {
    adjustInlineImageOffsetsIfBlockSizeChanged,
    buildInlineImageMenuItems,
    cleanupInlineImageInteractions,
    clampInlineImageOffset,
    computeInlineImageOffsetForNewBlockHeight,
    clampInlineImageWidthPercent,
    computeBlockContentBox,
    computeInlineImageDragScrollLayoutPx,
    shouldRevertInlineImageMove,
    computeInlineImageClusterIndex,
    computeInlineImageDock,
    computeInlineImageWidthPercent,
    deselectAllInlineImages,
    getInlineImageActionTarget,
    getInlineImageDock,
    getInlineImageHandleHorizontalSign,
    getInlineImageMenuItemsForClick,
    inlineImagePictureChanged,
    kInlineImageContextControlsId,
    kInlineImageHandleClass,
    kInlineImageHandleFrameClass,
    kMaxInlineImageWidthPercent,
    keepInlineImageSelectionOutOfCkeditorSnapshots,
    kMinInlineImageWidthPercent,
    removeInlineImageSelectionFromHtml,
    selectInlineImage,
    setupInlineImageInteractions,
} from "./inlineImageInteractions";
import { resetChangeOrderForTests } from "../undo/changeOrder";
import BloomField from "../bloomField/BloomField";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { theOneUndoStack } from "../undo/UndoStack";

// jsdom reports every element's box as empty, so these tests cannot check where a drag put the
// image. The calculations that decide that are in exported functions with no side effects,
// and most of these tests are aimed at them. The tests that use the DOM cover selection and
// which commands a right-click offers in which places.

// jsdom does not implement the pointer capture API. This stub records which element has
// captured which pointer, which is what the drag tests check. It does not send events to the
// capturing element. Only a real browser can do that, so only the e2e suite can test it.
const capturedPointers = new Map<Element, number>();
Element.prototype.setPointerCapture = function (pointerId: number) {
    capturedPointers.set(this, pointerId);
};
Element.prototype.releasePointerCapture = function (pointerId: number) {
    if (capturedPointers.get(this) === pointerId) capturedPointers.delete(this);
};
Element.prototype.hasPointerCapture = function (pointerId: number) {
    return capturedPointers.get(this) === pointerId;
};

// jsdom has no PointerEvent, but a listener for "pointerdown" fires for any event of that
// type, and MouseEvent has the button and the coordinates this module reads. A MouseEvent has
// no pointerId, and capturing the pointer needs one, so it is added after the event is created.
// MouseEvent would ignore it if it were passed as an option.
const kTestPointerId = 7;
// A second finger, for the test that checks a second pointer's events do not affect the drag.
const kOtherTestPointerId = 9;
function pointerEvent(
    type: string,
    clientX: number,
    clientY: number,
    pointerId: number = kTestPointerId,
): MouseEvent {
    const event = new MouseEvent(type, {
        bubbles: true,
        cancelable: true,
        button: 0,
        clientX,
        clientY,
    });
    Object.defineProperty(event, "pointerId", { value: pointerId });
    return event;
}

// A block 300px wide and 200px tall at the origin, so that its thirds fall on round numbers
// (100 and 200).
const kEditableBox = { left: 0, top: 0, width: 300, height: 200 };
// Which dock a position calls for depends on how tall the image is, because the bottom dock
// starts where the middle band can no longer fit the image inside the block's content.
const kImageHeightViewportPx = 40;

let pageCounter = 0;

// Builds a page with one translation group, with one editable for each entry. This is the same
// page as in inlineImages.test.ts, including the data-page-id that the inline image undo code
// uses to tell pages apart.
function makeTranslationGroup(
    editables: { lang: string; classes?: string; content?: string }[],
    options?: { insideCanvasElement?: boolean; groupClasses?: string },
): HTMLElement {
    const root = getTestRoot();
    const groupHtml =
        `<div class="bloom-translationGroup ${
            options?.groupClasses ?? ""
        }" id="group">` +
        editables
            .map(
                (e) =>
                    `<div class="bloom-editable ${e.classes ?? ""}" lang="${e.lang}">${
                        e.content ?? ""
                    }</div>`,
            )
            .join("") +
        `</div>`;
    root.innerHTML =
        `<div class="bloom-page" data-page-id="test-page-${++pageCounter}">` +
        (options?.insideCanvasElement
            ? `<div class="bloom-canvas-element">${groupHtml}</div>`
            : groupHtml) +
        `</div>`;
    return root.querySelector("#group") as HTMLElement;
}

const makeSimpleGroup = (options?: {
    insideCanvasElement?: boolean;
    groupClasses?: string;
}) =>
    makeTranslationGroup(
        [
            {
                lang: "en",
                classes: "bloom-content1 bloom-visibility-code-on",
                content: "<p>English</p>",
            },
            { lang: "fr", content: "<p>French</p>" },
        ],
        options,
    );

const editableFor = (group: HTMLElement, lang: string) =>
    group.querySelector(`[lang="${lang}"]`) as HTMLElement;

describe("inlineImageInteractions", () => {
    beforeEach(() => {
        cleanTestRoot();
        cleanupInlineImageInteractions();
        capturedPointers.clear();
        // Every inline image operation goes on the one undo stack, so each test starts it empty.
        theOneUndoStack.clear();
        theOneUndoStack.clearLegacyProviders();
        resetChangeOrderForTests();
        discardPendingInlineImageUndo();
        // Much of the code under test asks for an overflow check, which OverflowChecker runs on
        // a one-second timer. That timer would fire after the test has ended, when the canvas
        // element manager it calls is gone, and vitest reports the error as unhandled. The
        // overflow check needs real layout, which jsdom does not have, so it is not run here.
        vi.spyOn(
            OverflowChecker,
            "AdjustSizeOrMarkOverflowSoon",
        ).mockImplementation(() => {});
    });
    afterEach(() => {
        vi.restoreAllMocks();
    });
    afterAll(removeTestRoot);

    describe("computeInlineImageDock", () => {
        it("switches dock at the thirds of the block's width", () => {
            const dockAt = (x: number) =>
                computeInlineImageDock(
                    { x, y: 10 },
                    kEditableBox,
                    kImageHeightViewportPx,
                );
            expect(dockAt(1)).toBe(kInlineImageLeftClass);
            expect(dockAt(99)).toBe(kInlineImageLeftClass);
            // A position exactly on a boundary belongs to the middle band.
            expect(dockAt(100)).toBe(kInlineImageMiddleClass);
            expect(dockAt(150)).toBe(kInlineImageMiddleClass);
            expect(dockAt(200)).toBe(kInlineImageMiddleClass);
            expect(dockAt(201)).toBe(kInlineImageRightClass);
            expect(dockAt(299)).toBe(kInlineImageRightClass);
        });

        it("keeps the dock of a position beyond the sides of the block", () => {
            expect(
                computeInlineImageDock(
                    { x: -500, y: 10 },
                    kEditableBox,
                    kImageHeightViewportPx,
                ),
            ).toBe(kInlineImageLeftClass);
            expect(
                computeInlineImageDock(
                    { x: 900, y: 10 },
                    kEditableBox,
                    kImageHeightViewportPx,
                ),
            ).toBe(kInlineImageRightClass);
        });

        it("hands the middle third to the bottom dock exactly where the band can no longer fit the image", () => {
            // The block's content ends at 200 and the image is 40 tall, so the middle band can
            // hold it while its center is above 180, and not a pixel lower. Every position
            // above that belongs to the band, because a user moving the picture down expects to
            // be able to stop anywhere. (John: "it seems like I should be able to put it
            // vertically anywhere I want".)
            expect(
                computeInlineImageDock(
                    { x: 150, y: 179 },
                    kEditableBox,
                    kImageHeightViewportPx,
                ),
            ).toBe(kInlineImageMiddleClass);
            expect(
                computeInlineImageDock(
                    { x: 150, y: 180 },
                    kEditableBox,
                    kImageHeightViewportPx,
                ),
            ).toBe(kInlineImageBottomClass);
            // A taller image runs out of room higher up, since the bottom of the image has to
            // stay inside the content.
            expect(
                computeInlineImageDock({ x: 150, y: 150 }, kEditableBox, 100),
            ).toBe(kInlineImageBottomClass);
            // In the left and right thirds the side docks apply all the way down, so an image
            // can be placed in a lower corner.
            expect(
                computeInlineImageDock(
                    { x: 20, y: 199 },
                    kEditableBox,
                    kImageHeightViewportPx,
                ),
            ).toBe(kInlineImageLeftClass);
            expect(
                computeInlineImageDock(
                    { x: 280, y: 199 },
                    kEditableBox,
                    kImageHeightViewportPx,
                ),
            ).toBe(kInlineImageRightClass);
        });

        it("docks at the bottom for a position below the block altogether, whatever the horizontal position", () => {
            [20, 150, 280].forEach((x) => {
                expect(
                    computeInlineImageDock(
                        { x, y: 5000 },
                        kEditableBox,
                        kImageHeightViewportPx,
                    ),
                    `x=${x} below the block`,
                ).toBe(kInlineImageBottomClass);
            });
        });

        it("answers with the band for a block that has no width", () => {
            expect(
                computeInlineImageDock(
                    { x: 0, y: 0 },
                    { left: 0, top: 0, width: 0, height: 0 },
                    0,
                ),
            ).toBe(kInlineImageBottomClass);
            expect(
                computeInlineImageDock(
                    { x: 0, y: -10 },
                    { left: 0, top: 0, width: 0, height: 0 },
                    0,
                ),
            ).toBe(kInlineImageMiddleClass);
        });
    });

    // These numbers were measured in a running Bloom, on the page from the bug report: an A4
    // page nearly full of text, with an image near the bottom, changed to A6 portrait. The text
    // no longer fits the smaller page, so the block scrolls. Its rectangle is 532 screen pixels
    // tall and holds 484 layout pixels (the page is drawn at 110%), and the text in it is 841
    // layout pixels tall.
    const kA6Report = {
        visibleBoxViewportPx: {
            left: 71,
            top: 87,
            width: 353,
            height: 532.159,
        },
        // 532.159 screen pixels showing 484 layout pixels: the page is drawn at 110%.
        viewportPxPerLayoutPx: 532.159 / 484,
        scrollHeightLayoutPx: 841,
        // The image had an offset of 661 layout pixels on the A4 page, which puts it below
        // everything the smaller page can show at once.
        imageCenterYViewportPxWhenScrolledToTop: 888,
        // The picture of a ball was 40% as wide as the 353-pixel block, and about this tall on
        // screen.
        imageHeightViewportPx: 163,
    };

    describe("computeBlockContentBox", () => {
        it("gives a block that fits its text its own rectangle", () => {
            const box = { left: 71, top: 87, width: 353, height: 200 };
            expect(computeBlockContentBox(box, 200, 0, 1)).toEqual(box);
        });

        it("reaches past the bottom of a block whose text overflows", () => {
            const content = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.scrollHeightLayoutPx,
                0,
                kA6Report.viewportPxPerLayoutPx,
            );
            expect(content.top).toBe(87);
            // 841 layout pixels of text, drawn at 110%.
            expect(Math.round(content.height)).toBe(925);
            expect(Math.round(content.top + content.height)).toBe(1012);
        });

        it("follows the block as it is scrolled, so the content keeps one position", () => {
            const scrolled = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.scrollHeightLayoutPx,
                324.667,
                kA6Report.viewportPxPerLayoutPx,
            );
            expect(Math.round(scrolled.top)).toBe(-270);
            expect(Math.round(scrolled.top + scrolled.height)).toBe(655);
        });

        it("returns the rectangle unchanged when nothing is laid out (jsdom)", () => {
            const box = { left: 0, top: 0, width: 0, height: 0 };
            expect(computeBlockContentBox(box, 0, 0, 1)).toEqual(box);
        });
    });

    describe("computeInlineImageDragScrollLayoutPx", () => {
        // A block 200 screen pixels tall starting at 100, drawn at 110%.
        const visible = { left: 0, top: 100, width: 300, height: 200 };
        const scale = 1.1;

        it("asks for no scroll while the picture is showing", () => {
            expect(
                computeInlineImageDragScrollLayoutPx(
                    { left: 0, top: 150, width: 60, height: 50 },
                    visible,
                    scale,
                ),
            ).toBe(0);
        });

        it("scrolls down by exactly what hangs below the block, in layout pixels", () => {
            // Bottom at 320, which is 20 screen pixels below the block's 300.
            expect(
                computeInlineImageDragScrollLayoutPx(
                    { left: 0, top: 270, width: 60, height: 50 },
                    visible,
                    scale,
                ),
            ).toBeCloseTo(20 / 1.1);
        });

        it("scrolls up by exactly what is above the block", () => {
            // Top at 85, which is 15 screen pixels above the block's 100.
            expect(
                computeInlineImageDragScrollLayoutPx(
                    { left: 0, top: 85, width: 60, height: 50 },
                    visible,
                    scale,
                ),
            ).toBeCloseTo(-15 / 1.1);
        });

        it("shows the bottom of a picture too tall for the block", () => {
            const wanted = computeInlineImageDragScrollLayoutPx(
                { left: 0, top: 90, width: 60, height: 300 },
                visible,
                scale,
            );
            expect(wanted).toBeGreaterThan(0);
        });

        it("asks for nothing where nothing is laid out (jsdom)", () => {
            expect(
                computeInlineImageDragScrollLayoutPx(
                    { left: 0, top: 0, width: 0, height: 0 },
                    { left: 0, top: 0, width: 0, height: 0 },
                    0,
                ),
            ).toBe(0);
        });
    });

    describe("shouldRevertInlineImageMove", () => {
        it("undoes a move that pushed a fitting block into overflow", () => {
            expect(shouldRevertInlineImageMove(0, 40)).toBe(true);
        });

        it("keeps a move that left a fitting block fitting", () => {
            expect(shouldRevertInlineImageMove(0, 0)).toBe(false);
        });

        it("ignores a pixel of layout noise", () => {
            expect(shouldRevertInlineImageMove(0, 1)).toBe(false);
        });

        it("keeps every move in a block whose text already overflowed", () => {
            // The bug this rule was rewritten for: at offset 680 in the developer's A6 block
            // the text already needed 344 layout pixels more than the block had, and dragging
            // the image UP asks the text below it for more room still. Under the old rule that
            // added overflow, so the move was undone -- every time, in both directions, which
            // is what "the image is just stuck there" was.
            expect(shouldRevertInlineImageMove(344, 381)).toBe(false);
            expect(shouldRevertInlineImageMove(344, 344)).toBe(false);
            expect(shouldRevertInlineImageMove(344, 300)).toBe(false);
        });
    });

    // The bug report: "I added an image to the bottom right-hand corner of an A4 portrait page
    // that was pretty full of text. I then changed the page layout to be A6 portrait, the
    // text now requires scrolling. I was not able to reposition that image anymore. It was
    // just stuck there."
    describe("an image in a block whose text overflows can still be dragged", () => {
        it("does not read the image as being below the block", () => {
            const asShown = computeInlineImageDock(
                {
                    x: 350,
                    y: kA6Report.imageCenterYViewportPxWhenScrolledToTop,
                },
                kA6Report.visibleBoxViewportPx,
                kA6Report.imageHeightViewportPx,
            );
            // Measured against the block's rectangle, the image is below the bottom of what the
            // small page shows, so every drag of it asked for the bottom dock. That caused the
            // reported bug: the bottom dock does not fit in a block that is already too small,
            // so every move was undone and the image never moved.
            expect(asShown).toBe(kInlineImageBottomClass);

            const contentBox = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.scrollHeightLayoutPx,
                0,
                kA6Report.viewportPxPerLayoutPx,
            );
            expect(
                computeInlineImageDock(
                    {
                        x: 350,
                        y: kA6Report.imageCenterYViewportPxWhenScrolledToTop,
                    },
                    contentBox,
                    kA6Report.imageHeightViewportPx,
                ),
                "The image is inside the block's text, so a drag there is an ordinary move, " +
                    "not a request for the bottom dock.",
            ).toBe(kInlineImageRightClass);
        });

        it("leaves room below the image to drag it into", () => {
            const wrapperBottomViewportPx = 900; // the wrapper's bottom edge, on the screen
            const currentOffsetLayoutPx = 661;
            const roomByRectangle =
                kA6Report.visibleBoxViewportPx.top +
                kA6Report.visibleBoxViewportPx.height -
                wrapperBottomViewportPx;
            // Measured against the rectangle, the image is 281 pixels past the limit, so every
            // drag pulled it back up to the bottom of what is showing.
            expect(Math.round(roomByRectangle)).toBe(-281);
            expect(
                clampInlineImageOffset(
                    currentOffsetLayoutPx + roomByRectangle,
                    currentOffsetLayoutPx + roomByRectangle,
                ),
            ).toBe(380);

            const contentBox = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.scrollHeightLayoutPx,
                0,
                kA6Report.viewportPxPerLayoutPx,
            );
            const roomByContent =
                (contentBox.top + contentBox.height - wrapperBottomViewportPx) /
                kA6Report.viewportPxPerLayoutPx;
            expect(roomByContent).toBeGreaterThan(0);
            expect(
                clampInlineImageOffset(
                    currentOffsetLayoutPx + 20,
                    currentOffsetLayoutPx + roomByContent,
                ),
                "The image is inside the text, so it can still be pushed further down.",
            ).toBe(681);
        });
    });

    describe("computeInlineImageClusterIndex", () => {
        it("counts how many neighbors sit at or above the target", () => {
            expect(computeInlineImageClusterIndex(50, [])).toBe(0);
            expect(computeInlineImageClusterIndex(50, [100, 300])).toBe(0);
            expect(computeInlineImageClusterIndex(150, [100, 300])).toBe(1);
            expect(computeInlineImageClusterIndex(500, [100, 300])).toBe(2);
            // When two tops are equal, the image goes after the one already at that height.
            expect(computeInlineImageClusterIndex(100, [100, 300])).toBe(1);
        });
    });

    describe("clampInlineImageOffset", () => {
        it("never goes above the top of the block", () => {
            expect(clampInlineImageOffset(-1)).toBe(0);
            expect(clampInlineImageOffset(-9999)).toBe(0);
            expect(clampInlineImageOffset(0)).toBe(0);
        });

        it("rounds to whole pixels", () => {
            expect(clampInlineImageOffset(12.4)).toBe(12);
            expect(clampInlineImageOffset(12.6)).toBe(13);
        });

        it("stops at the maximum when there is one", () => {
            expect(clampInlineImageOffset(500, 200)).toBe(200);
            expect(clampInlineImageOffset(150, 200)).toBe(150);
        });

        it("pins the image to the top when the maximum is zero or negative (image fills the block)", () => {
            expect(clampInlineImageOffset(150, 0)).toBe(0);
            expect(clampInlineImageOffset(150, -5)).toBe(0);
        });

        it("applies no maximum when none is given (no box to measure against)", () => {
            expect(clampInlineImageOffset(150)).toBe(150);
        });
    });

    describe("computeInlineImageOffsetForNewBlockHeight", () => {
        it("keeps the picture the same share of the way down a shorter block", () => {
            // Two thirds of the way down a 900px block becomes two thirds of the way down a
            // 300px one.
            expect(
                computeInlineImageOffsetForNewBlockHeight(600, 900, 300),
            ).toBe(200);
        });

        it("keeps the same share of the way down a taller block", () => {
            expect(
                computeInlineImageOffsetForNewBlockHeight(200, 300, 900),
            ).toBe(600);
        });

        it("leaves an offset alone when the block is the size it was measured against", () => {
            expect(
                computeInlineImageOffsetForNewBlockHeight(431, 773, 773),
            ).toBe(431);
        });

        it("rounds to whole pixels, like every other offset", () => {
            expect(
                computeInlineImageOffsetForNewBlockHeight(431, 773, 516),
            ).toBe(288);
        });

        it("leaves the offset alone where there is no height to work from", () => {
            // Nothing is laid out (jsdom), or the block size the offset was measured against
            // was never recorded.
            expect(computeInlineImageOffsetForNewBlockHeight(431, 0, 516)).toBe(
                431,
            );
            expect(computeInlineImageOffsetForNewBlockHeight(431, 773, 0)).toBe(
                431,
            );
        });

        it("never puts the picture above the top of the block", () => {
            expect(
                computeInlineImageOffsetForNewBlockHeight(-5, 773, 516),
            ).toBe(0);
        });
    });

    describe("width clamping", () => {
        it("keeps a width inside the usable range", () => {
            expect(clampInlineImageWidthPercent(0)).toBe(
                kMinInlineImageWidthPercent,
            );
            expect(clampInlineImageWidthPercent(-40)).toBe(
                kMinInlineImageWidthPercent,
            );
            expect(clampInlineImageWidthPercent(1000)).toBe(
                kMaxInlineImageWidthPercent,
            );
            expect(clampInlineImageWidthPercent(42.5)).toBe(42.5);
        });

        it("turns a corner drag into a percentage of the block's width", () => {
            // 120px of a 300px block is 40%. Dragging an east corner 30px right adds 10%.
            expect(computeInlineImageWidthPercent(120, 30, 1, 300)).toBe(50);
            // The same movement on a west corner is inward, so it shrinks.
            expect(computeInlineImageWidthPercent(120, 30, -1, 300)).toBe(30);
            // Dragging a west corner outward (leftward) grows it.
            expect(computeInlineImageWidthPercent(120, -30, -1, 300)).toBe(50);
        });

        it("rounds a width to one decimal place", () => {
            // 121px of 300 is 40.333...%
            expect(computeInlineImageWidthPercent(121, 0, 1, 300)).toBe(40.3);
        });

        it("clamps a drag that would make the image unusably wide or narrow", () => {
            expect(computeInlineImageWidthPercent(120, 9999, 1, 300)).toBe(
                kMaxInlineImageWidthPercent,
            );
            expect(computeInlineImageWidthPercent(120, 9999, -1, 300)).toBe(
                kMinInlineImageWidthPercent,
            );
        });

        it("grows on an outward drag for every corner", () => {
            expect(getInlineImageHandleHorizontalSign("ne")).toBe(1);
            expect(getInlineImageHandleHorizontalSign("se")).toBe(1);
            expect(getInlineImageHandleHorizontalSign("nw")).toBe(-1);
            expect(getInlineImageHandleHorizontalSign("sw")).toBe(-1);
        });
    });

    describe("getInlineImageActionTarget", () => {
        it("offers adding an image in an eligible empty block", () => {
            const group = makeSimpleGroup();
            const editable = editableFor(group, "en");
            const target = getInlineImageActionTarget(
                editable.querySelector("p") as HTMLElement,
            );
            expect(target.kind).toBe("add");
            if (target.kind !== "add") return;
            expect(target.translationGroup).toBe(group);
            expect(target.editable).toBe(editable);
        });

        it("offers nothing for a block inside a canvas element", () => {
            const group = makeSimpleGroup({ insideCanvasElement: true });
            // A block inside a canvas element gets no inline image commands.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("none");
        });

        it("offers nothing inside an image description", () => {
            // An image description is a translation group, inside the bloom-canvas but not
            // inside a canvas element, so the checks for those do not exclude it. It should
            // not hold a picture, because it is what a reader hears in place of the picture.
            const group = makeSimpleGroup({
                groupClasses: "bloom-imageDescription",
            });
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("none");
        });

        it("offers nothing for a bloom-editable that is not a child of a translation group", () => {
            const root = getTestRoot();
            root.innerHTML = `<div class="bloom-page"><div class="bloom-editable"><p>loose</p></div></div>`;
            expect(
                getInlineImageActionTarget(
                    root.querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("none");
        });

        it("offers nothing outside any block", () => {
            const group = makeSimpleGroup();
            expect(getInlineImageActionTarget(group).kind).toBe("none");
            expect(getInlineImageActionTarget(undefined).kind).toBe("none");
        });

        it("offers the image's own commands when the wrapper is pointed at", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const target = getInlineImageActionTarget(
                wrapper.querySelector("img") as HTMLElement,
            );
            expect(target.kind).toBe("existing");
            if (target.kind !== "existing") return;
            expect(target.wrapper).toBe(wrapper);
            expect(target.editable).toBe(editableFor(group, "en"));
        });

        it("still offers adding in the text of a block whose group already has an image", () => {
            const group = makeSimpleGroup();
            insertInlineImage(group);
            // A group can hold any number of inline images, and each insert adds a new one.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("add");
            // The same is true in another language's block.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "fr").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("add");
        });
    });

    describe("buildInlineImageMenuItems", () => {
        it("offers only Insert Image where there is no image", () => {
            const group = makeSimpleGroup();
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ),
            );
            expect(items.map((i) => i.l10nId)).toEqual([
                "EditTab.InlineImage.InsertImage",
            ]);
        });

        it("offers the standard image menu plus Delete for an existing image", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            );
            // The registry's "image" section, filtered by its usual availability rules.
            // "Expand image to fill space" is only for background images, and Become
            // Background and Use for book thumbnail are excluded for an image inside a text
            // block. "Edit with AI" depends on a feature that is off here. Then come a divider,
            // the arrangement section without Rotate, another divider, and Delete.
            expect(items.map((i) => i.l10nId)).toEqual([
                "EditTab.Image.EditMetadataOverlay",
                "EditTab.Image.ChooseImage",
                "EditTab.Image.CopyImage",
                "EditTab.Image.PasteImage",
                "-",
                "EditTab.Image.Flip",
                "EditTab.Image.Transparency",
                "EditTab.Image.Reset",
                "-",
                "Common.Delete",
            ]);
            // A new inline image holds a placeholder, so there are no credits to edit, no
            // picture to copy, and no picture to make transparent.
            const byId = (id: string) => items.find((i) => i.l10nId === id)!;
            expect(byId("EditTab.Image.EditMetadataOverlay").disabled).toBe(
                true,
            );
            expect(byId("EditTab.Image.CopyImage").disabled).toBe(true);
            expect(byId("EditTab.Image.Transparency").disabled).toBe(true);
            // Choosing a picture is what the placeholder is there for, so that is enabled.
            expect(byId("EditTab.Image.ChooseImage").disabled).toBeFalsy();
            // Transparency is a submenu, as on the canvas element menu.
            expect(
                byId("EditTab.Image.Transparency").subMenu?.length,
            ).toBeGreaterThan(0);
        });

        it("enables the picture-dependent commands once a real picture is in place", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            wrapper.querySelector("img")!.setAttribute("src", "flower.jpg");
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            );
            const byId = (id: string) => items.find((i) => i.l10nId === id)!;
            expect(byId("EditTab.Image.EditMetadataOverlay").disabled).toBe(
                false,
            );
            expect(byId("EditTab.Image.CopyImage").disabled).toBe(false);
            expect(byId("EditTab.Image.Transparency").disabled).toBe(false);
        });

        it("offers nothing where there is nothing to act on", () => {
            expect(buildInlineImageMenuItems({ kind: "none" })).toEqual([]);
        });

        it("a standard command acts on the clicked image and syncs the other languages", async () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            wrapper.querySelector("img")!.setAttribute("src", "flower.jpg");
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            );
            const transparency = items.find(
                (i) => i.l10nId === "EditTab.Image.Transparency",
            )!;
            const transparent = transparency.subMenu!.find(
                (s) => s.l10nId === "EditTab.Image.Transparency.Transparent",
            )!;
            // Sanity check. The command finds its image through the registry's getImage, which
            // has to work with an inline image's markup, where the img is a direct child of the
            // wrapper and there is no bloom-imageContainer.
            expect(
                wrapper
                    .querySelector("img")!
                    .classList.contains("bloom-transparent"),
            ).toBe(false);

            (transparent.onClick as () => void)();

            // The command changed the copy that was clicked. The command is synchronous, but
            // the sync after it awaits it first, so the test waits for this state instead of
            // waiting for a fixed time.
            await vi.waitFor(() =>
                expect(
                    wrapper
                        .querySelector("img")!
                        .classList.contains("bloom-transparent"),
                ).toBe(true),
            );
            // The sync after the command copied the change to the other language.
            const frenchWrapper = getInlineImageInEditable(
                editableFor(group, "fr"),
            )!;
            expect(
                frenchWrapper
                    .querySelector("img")!
                    .classList.contains("bloom-transparent"),
            ).toBe(true);
        });

        it("undoes a command that mutates the image in place", async () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            (wrapper.querySelector("img") as HTMLImageElement).src =
                "flower.jpg";
            const items = getInlineImageMenuItemsForClick(
                wrapper.querySelector("img") as HTMLElement,
            );
            const transparency = items.find(
                (i) => i.l10nId === "EditTab.Image.Transparency",
            )!;
            const transparent = transparency.subMenu!.find(
                (i) => i.l10nId === "EditTab.Image.Transparency.Transparent",
            )!;
            (transparent.onClick as () => void)();
            await vi.waitFor(() =>
                expect(
                    wrapper
                        .querySelector("img")!
                        .classList.contains("bloom-transparent"),
                ).toBe(true),
            );

            expect(inlineImageCanUndo()).toBe(true);

            // This throws if the entry was recorded before the change was copied to the other
            // language, because the entry checks that every copy is as it left it.
            expect(inlineImageUndo()).toBe(true);

            // The undo has to take back the transparency, and leave the insert that put the
            // picture there. The commands come from the canvas element registry, which knows
            // nothing about inline image undo. Without an undo entry of their own, the newest
            // entry would be the insert, and ctrl+z would make the picture disappear.
            const restored = getInlineImage(group);
            expect(
                restored,
                "expected the picture to still be there",
            ).not.toBeNull();
            ["en", "fr"].forEach((lang) =>
                expect(
                    getInlineImageInEditable(editableFor(group, lang))!
                        .querySelector("img")!
                        .classList.contains("bloom-transparent"),
                    `expected the ${lang} copy to be opaque again`,
                ).toBe(false),
            );
        });

        it("Flip mirrors every language's copy, and Reset Image takes the mirror away", async () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            wrapper.querySelector("img")!.setAttribute("src", "flower.jpg");
            const frenchPicture = () =>
                getInlineImageInEditable(
                    editableFor(group, "fr"),
                )!.querySelector("img")!;
            const menuItem = (id: string) =>
                buildInlineImageMenuItems(
                    getInlineImageActionTarget(wrapper),
                ).find((i) => i.l10nId === id)!;
            // Sanity check: a picture that has not been flipped has nothing to reset.
            expect(menuItem("EditTab.Image.Reset").disabled).toBe(true);

            const flipHorizontal = menuItem("EditTab.Image.Flip").subMenu!.find(
                (s) => s.l10nId === "EditTab.Image.FlipHorizontal",
            )!;
            (flipHorizontal.onClick as () => void)();

            // The registry's Flip acts on the canvas element manager's active element, and an
            // inline image is never that, so this shows that the command reached this picture.
            await vi.waitFor(() =>
                expect(wrapper.querySelector("img")!.style.transform).toBe(
                    "scale(-1, 1)",
                ),
            );
            expect(frenchPicture().style.transform).toBe("scale(-1, 1)");
            expect(menuItem("EditTab.Image.Reset").disabled).toBe(false);

            (menuItem("EditTab.Image.Reset").onClick as () => void)();

            await vi.waitFor(() =>
                expect(wrapper.querySelector("img")!.style.transform).toBe(""),
            );
            expect(frenchPicture().style.transform).toBe("");
        });

        it("undoes a flip", async () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            (wrapper.querySelector("img") as HTMLImageElement).src =
                "flower.jpg";
            const flipVertical = getInlineImageMenuItemsForClick(
                wrapper.querySelector("img") as HTMLElement,
            )
                .find((i) => i.l10nId === "EditTab.Image.Flip")!
                .subMenu!.find(
                    (s) => s.l10nId === "EditTab.Image.FlipVertical",
                )!;
            (flipVertical.onClick as () => void)();
            await vi.waitFor(() =>
                expect(wrapper.querySelector("img")!.style.transform).toBe(
                    "scale(1, -1)",
                ),
            );

            expect(inlineImageUndo()).toBe(true);

            const restored = getInlineImage(group);
            expect(
                restored,
                "expected the picture to still be there",
            ).not.toBeNull();
            expect(restored!.querySelector("img")!.style.transform).toBe("");
        });

        it("deselects the image when the right-click landed on the text instead", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            // Sanity check.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );

            getInlineImageMenuItemsForClick(
                editableFor(group, "en").querySelector("p") as HTMLElement,
            );

            // If it stayed selected, it would look as if the commands on the menu applied to
            // the picture, which they do not.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
        });

        it("selects the image a right-click landed on, so its commands have a subject", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // Sanity check.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );

            const items = getInlineImageMenuItemsForClick(
                wrapper.querySelector("img") as HTMLElement,
            );

            expect(items.length).toBeGreaterThan(0);
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
        });

        it("selects nothing for a click that offers no commands", () => {
            const group = makeSimpleGroup();
            insertInlineImage(group);
            // A click outside any editable (here, the group itself) offers nothing.
            const items = getInlineImageMenuItemsForClick(group);
            expect(items).toEqual([]);
            expect(
                document.querySelector("." + kInlineImageSelectedClass),
            ).toBeNull();
        });

        it("Delete removes just the clicked image, in every language", () => {
            const group = makeSimpleGroup();
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const firstId = first.getAttribute(kInlineImageIdAttr);
            const secondId = second.getAttribute(kInlineImageIdAttr);
            // Sanity check: two different images, each with a copy in both languages.
            expect(firstId).toBeTruthy();
            expect(secondId).toBeTruthy();
            expect(firstId).not.toBe(secondId);
            expect(
                document.querySelectorAll(
                    `[${kInlineImageIdAttr}="${firstId}"]`,
                ).length,
            ).toBe(2);

            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(first),
            );
            const remove = items.find((i) => i.l10nId === "Common.Delete");
            expect(remove, "expected a Delete item").toBeTruthy();
            // Run the command itself, so that a failure when it runs is caught here.
            (remove!.onClick as () => void)();

            expect(
                document.querySelectorAll(
                    `[${kInlineImageIdAttr}="${firstId}"]`,
                ).length,
            ).toBe(0);
            // The other image survives in both languages.
            expect(
                document.querySelectorAll(
                    `[${kInlineImageIdAttr}="${secondId}"]`,
                ).length,
            ).toBe(2);
        });

        // Undo does not need a selected picture or the caret in the block: the delete is the
        // newest entry on the one undo stack, and that is all inlineImageCanUndo asks.
        it("Delete can be undone with nothing selected", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const id = wrapper.getAttribute(kInlineImageIdAttr);
            const remove = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            ).find((i) => i.l10nId === "Common.Delete")!;
            (remove.onClick as () => void)();
            // Sanity check: both copies are gone, and nothing is selected.
            expect(
                document.querySelectorAll(`[${kInlineImageIdAttr}="${id}"]`)
                    .length,
            ).toBe(0);
            expect(
                document.querySelector("." + kInlineImageSelectedClass),
            ).toBeNull();
            expect(inlineImageCanUndo()).toBe(true);

            expect(inlineImageUndo()).toBe(true);

            expect(
                document.querySelectorAll(`[${kInlineImageIdAttr}="${id}"]`)
                    .length,
            ).toBe(2);
        });

        // The toolbar is rendered into a div on the body, outside the wrapper, so deleting the
        // picture does not remove it. Without deselectAllInlineImages in the delete command,
        // it stayed on screen offering commands such as "Choose image" and "Copy image" for a
        // picture that was gone.
        it("Delete takes the bar of buttons down with the image", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const items = getInlineImageMenuItemsForClick(wrapper);
            // Sanity check: the right-click selected the image, which shows the toolbar.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(
                document.getElementById(kInlineImageContextControlsId),
                "expected the bar to be up before the delete",
            ).not.toBeNull();

            const remove = items.find((i) => i.l10nId === "Common.Delete");
            (remove!.onClick as () => void)();

            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).toBeNull();
            expect(
                document.querySelector("." + kInlineImageSelectedClass),
            ).toBeNull();
        });
    });

    // jsdom measures every box as empty, so these tests stub the two measurements this
    // function reads. That is enough to check which size changes it acts on.
    describe("adjustInlineImageOffsetsIfBlockSizeChanged", () => {
        // Gives an editable a size that jsdom will report.
        const setBlockSize = (
            editable: HTMLElement,
            widthLayoutPx: number,
            heightLayoutPx: number,
        ) => {
            Object.defineProperty(editable, "clientWidth", {
                value: widthLayoutPx,
                configurable: true,
            });
            Object.defineProperty(editable, "clientHeight", {
                value: heightLayoutPx,
                configurable: true,
            });
        };

        // A change of width alone must be acted on. The new width is recorded as the size the
        // offset was measured against either way, so a change missed here would never be
        // noticed later. Splitting a text
        // box in Change Layout makes this kind of change: the same height and less width, so
        // the same text wraps into more lines and the text after the picture can run off the
        // end of the block.
        it("acts on a block that changed only in width", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            const french = editableFor(group, "fr");
            insertInlineImage(group);
            setBlockSize(english, 300, 200);
            adjustInlineImageOffsetsIfBlockSizeChanged(group);
            // Sanity check: the block's size has been recorded.
            expect(
                getInlineImageInEditable(english)!.getAttribute(
                    kInlineImageOffsetBasedOnAttr,
                ),
            ).toBe("300,200");
            // Make the French copy different, so that the test can see whether a sync happens.
            getInlineImageInEditable(french)!.setAttribute(
                kInlineImageOffsetBasedOnAttr,
                "stale",
            );

            setBlockSize(english, 150, 200);
            adjustInlineImageOffsetsIfBlockSizeChanged(group);

            expect(
                getInlineImageInEditable(english)!.getAttribute(
                    kInlineImageOffsetBasedOnAttr,
                ),
            ).toBe("150,200");
            // The sync at the end of the function runs only when it decided the size changed.
            expect(
                getInlineImageInEditable(french)!.getAttribute(
                    kInlineImageOffsetBasedOnAttr,
                ),
            ).toBe("150,200");
        });

        it("leaves a block whose size has not changed alone", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            const french = editableFor(group, "fr");
            insertInlineImage(group);
            setBlockSize(english, 300, 200);
            adjustInlineImageOffsetsIfBlockSizeChanged(group);
            getInlineImageInEditable(french)!.setAttribute(
                kInlineImageOffsetBasedOnAttr,
                "untouched",
            );

            adjustInlineImageOffsetsIfBlockSizeChanged(group);

            expect(
                getInlineImageInEditable(french)!.getAttribute(
                    kInlineImageOffsetBasedOnAttr,
                ),
            ).toBe("untouched");
        });
    });

    // Two things leave an inline image unselected: an undo or redo, after which inlineImages.ts
    // has replaced the wrappers and sends kInlineImagesRestoredEvent, and a new picture, which
    // arrives through inlineImagePictureChanged. In both, the image has to be selected again,
    // so that the person keeps working on it, and the handles were on an element that may no
    // longer be in the document.
    describe("reacting to inlineImages.ts", () => {
        it("puts the handles back on the wrapper an undo restored", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            // Change the dock as an undoable operation, so that undo has something to put back.
            prepareInlineImageUndo(group);
            setInlineImageDock(wrapper, kInlineImageBottomClass);
            syncInlineImagesFromEditable(editableFor(group, "en"));
            commitPendingInlineImageUndo(group);

            expect(inlineImageUndo()).toBe(true);

            // Undo rebuilds the wrapper from saved markup, so this is a new element.
            const restored = getInlineImageInEditable(editableFor(group, "en"));
            expect(restored, "expected a restored wrapper").not.toBeNull();
            expect(restored).not.toBe(wrapper);
            expect(getInlineImageDock(restored!)).toBe(kInlineImageRightClass);
            // inlineImages.ts marks it as selected, so it needs handles. The saved markup does
            // not include them, since they are bloom-ui.
            expect(
                restored!.classList.contains(kInlineImageSelectedClass),
            ).toBe(true);
            expect(
                restored!.querySelector("." + kInlineImageHandleFrameClass),
                "expected the handles to be rebuilt",
            ).not.toBeNull();
        });

        it("puts the handles back on the wrapper a redo restored", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            prepareInlineImageUndo(group);
            setInlineImageDock(wrapper, kInlineImageBottomClass);
            syncInlineImagesFromEditable(editableFor(group, "en"));
            commitPendingInlineImageUndo(group);
            expect(inlineImageUndo()).toBe(true);
            const afterUndo = getInlineImageInEditable(
                editableFor(group, "en"),
            );
            // Sanity check: the undo put the picture back on the right.
            expect(getInlineImageDock(afterUndo!)).toBe(kInlineImageRightClass);

            void theOneUndoStack.redo();

            const redone = getInlineImageInEditable(editableFor(group, "en"));
            expect(redone, "expected a redone wrapper").not.toBeNull();
            expect(redone).not.toBe(afterUndo);
            expect(getInlineImageDock(redone!)).toBe(kInlineImageBottomClass);
            expect(redone!.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(
                redone!.querySelector("." + kInlineImageHandleFrameClass),
                "expected the handles to be rebuilt",
            ).not.toBeNull();
        });

        // The toolbar is a div on the body, so it stays when undo replaces the wrapper it was
        // built for. It has to be rebuilt so that its commands act on a picture that is still
        // in the document.
        it("rebuilds the toolbar for the wrapper an undo restored", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            prepareInlineImageUndo(group);
            setInlineImageDock(wrapper, kInlineImageBottomClass);
            syncInlineImagesFromEditable(editableFor(group, "en"));
            commitPendingInlineImageUndo(group);
            const barBuiltForTheOldWrapper = document.getElementById(
                kInlineImageContextControlsId,
            );
            // Sanity check: selecting the picture showed the toolbar.
            expect(barBuiltForTheOldWrapper).not.toBeNull();

            expect(inlineImageUndo()).toBe(true);

            const bar = document.getElementById(kInlineImageContextControlsId);
            expect(bar, "expected the toolbar to still be up").not.toBeNull();
            expect(bar).not.toBe(barBuiltForTheOldWrapper);
        });

        // Undoing an insert removes the picture, so the toolbar has nothing to act on and
        // nowhere to be.
        it("takes the toolbar down when the undo left no picture", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            selectInlineImage(insertInlineImage(group));
            // Sanity check: the picture is selected and its toolbar is showing.
            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).not.toBeNull();

            expect(inlineImageUndo()).toBe(true);

            expect(getInlineImages(group).length).toBe(0);
            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).toBeNull();
        });

        it("re-asserts the selection when a new picture arrives", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            img.setAttribute("src", "flower.jpg");
            // Choosing a picture in the image chooser can leave the focus back in the text,
            // which deselects the image. This does the same.
            deselectAllInlineImages(document);

            inlineImagePictureChanged(img);

            // The person chose a picture for this image, so they are still working on it.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).not.toBeNull();
        });
    });

    // BloomField undoes a Ctrl+A then Delete that removes a picture. It knows how many pictures a
    // block should have, and the commands that add and delete pictures on purpose have to keep
    // that number up to date.
    describe("protection from Ctrl+A then Delete", () => {
        const originalExecCommand = document.execCommand;
        let execCommand: ReturnType<typeof vi.fn>;
        beforeEach(() => {
            execCommand = vi.fn();
            document.execCommand =
                execCommand as unknown as typeof document.execCommand;
        });
        afterEach(() => {
            document.execCommand = originalExecCommand;
        });

        const runMenuCommand = (clicked: HTMLElement, l10nId: string) => {
            const item = buildInlineImageMenuItems(
                getInlineImageActionTarget(clicked),
            ).find((i) => i.l10nId === l10nId);
            expect(item, `expected a ${l10nId} item`).toBeTruthy();
            (item!.onClick as () => void)();
        };
        const releaseAKey = (editable: HTMLElement) =>
            editable.dispatchEvent(
                new KeyboardEvent("keyup", { bubbles: true }),
            );

        it("protects a picture inserted after the block was set up", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            // The block starts with no picture, so there is nothing to protect yet.
            BloomField.ManageField(english);
            runMenuCommand(
                english.querySelector("p") as HTMLElement,
                "EditTab.InlineImage.InsertImage",
            );
            // Sanity check: the picture is there, and nothing has asked for an undo.
            expect(getInlineImageInEditable(english)).not.toBeNull();
            releaseAKey(english);
            expect(execCommand).not.toHaveBeenCalled();

            // This is what Ctrl+A then Delete leaves.
            getInlineImageInEditable(english)!.remove();
            releaseAKey(english);

            expect(execCommand).toHaveBeenCalledWith("undo");
        });

        it("does not put back a picture deleted with the Delete command", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            insertInlineImage(group);
            // Set up after the insert, so the picture counts from the start, as in a saved book.
            BloomField.ManageField(english);

            runMenuCommand(getInlineImageInEditable(english)!, "Common.Delete");
            // Sanity check: the delete happened.
            expect(getInlineImageInEditable(english)).toBeNull();
            releaseAKey(english);

            expect(execCommand).not.toHaveBeenCalled();
        });

        it("counts the picture an undo of its delete brings back", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            insertInlineImage(group);
            BloomField.ManageField(english);
            runMenuCommand(getInlineImageInEditable(english)!, "Common.Delete");
            expect(inlineImageUndo()).toBe(true);
            // Sanity check: the undo brought the picture back.
            expect(getInlineImageInEditable(english)).not.toBeNull();

            getInlineImageInEditable(english)!.remove();
            releaseAKey(english);

            expect(execCommand).toHaveBeenCalledWith("undo");
        });
    });

    // CKEditor compares each editable with its latest snapshot on every keyup, and would take a
    // picture change for typing. So every finished picture change updates that snapshot.
    // CKEditor claims Ctrl+Z and Ctrl+Y inside a text block, and does nothing with them when it
    // has nothing of its own to undo or redo, so the page takes them first for picture changes.
    describe("Ctrl+Z and Ctrl+Y", () => {
        const pressKey = (
            target: HTMLElement,
            key: string,
            options: { shiftKey?: boolean } = {},
        ) => {
            const event = new KeyboardEvent("keydown", {
                key,
                ctrlKey: true,
                shiftKey: options.shiftKey ?? false,
                bubbles: true,
                cancelable: true,
            });
            target.dispatchEvent(event);
            return event;
        };
        // A resize as the page records it: prepare, change, sync, commit.
        const resizeTo = (group: HTMLElement, width: string) => {
            prepareInlineImageUndo(group);
            getInlineImageInEditable(
                editableFor(group, "en"),
            )!.style.setProperty(kInlineImageWidthVar, width);
            syncInlineImagesFromEditable(editableFor(group, "en"));
            commitPendingInlineImageUndo(group);
        };
        const widthIn = (group: HTMLElement, lang: string) =>
            getInlineImageInEditable(
                editableFor(group, lang),
            )!.style.getPropertyValue(kInlineImageWidthVar);

        it("Ctrl+Z undoes a picture change in every copy, before CKEditor sees the key", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const english = editableFor(group, "en");
            const reachedTheEditable = vi.fn();
            english.addEventListener("keydown", reachedTheEditable);
            // Sanity check: both copies have the new width.
            expect(widthIn(group, "fr")).toBe("60%");

            const event = pressKey(english, "z");

            expect(widthIn(group, "en")).toBe("40%");
            expect(widthIn(group, "fr")).toBe("40%");
            expect(event.defaultPrevented).toBe(true);
            expect(
                reachedTheEditable,
                "CKEditor listens on the editable, and must not also act on the key",
            ).not.toHaveBeenCalled();
        });

        it("recognizes Ctrl+Z on a non-Latin keyboard layout, as CKEditor does", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            // On a Russian layout the Z key gives "я"; CKEditor goes by keyCode 90.
            const event = new KeyboardEvent("keydown", {
                key: "я",
                keyCode: 90,
                ctrlKey: true,
                bubbles: true,
                cancelable: true,
            });

            editableFor(group, "en").dispatchEvent(event);

            expect(event.defaultPrevented).toBe(true);
            expect(widthIn(group, "fr")).toBe("40%");
        });

        it("leaves Ctrl+Z to a form field", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const input = document.createElement("input");
            getTestRoot().appendChild(input);

            const event = pressKey(input, "z");

            expect(event.defaultPrevented).toBe(false);
            expect(widthIn(group, "fr")).toBe("60%");
        });

        it("leaves Ctrl+Z alone when the next undo is not a picture change", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            // Sanity check: nothing on the stack.
            expect(inlineImageCanUndo()).toBe(false);

            const event = pressKey(editableFor(group, "en"), "z");

            expect(event.defaultPrevented).toBe(false);
        });

        it("Ctrl+Y and Ctrl+Shift+Z redo the picture change Ctrl+Z took back", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const english = editableFor(group, "en");
            pressKey(english, "z");
            // Sanity check: undone.
            expect(widthIn(group, "fr")).toBe("40%");

            const event = pressKey(english, "y");

            expect(event.defaultPrevented).toBe(true);
            expect(widthIn(group, "en")).toBe("60%");
            expect(widthIn(group, "fr")).toBe("60%");

            pressKey(english, "z");
            pressKey(english, "Z", { shiftKey: true });
            expect(widthIn(group, "fr")).toBe("60%");
        });

        it("leaves Ctrl+Y to CKEditor when it has a redo of its own", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const english = editableFor(group, "en") as HTMLElement & {
                bloomCkEditor?: unknown;
            };
            pressKey(english, "z");
            // CKEditor is at its first snapshot, with one after it to redo.
            english.bloomCkEditor = {
                undoManager: { index: 0, snapshots: [{}, {}] },
            };
            english.setAttribute("tabindex", "0");
            english.focus();
            // Sanity check: the focus is in the block, and the stack has a redo.
            expect(document.activeElement).toBe(english);
            expect(theOneUndoStack.canRedo()).toBe(true);

            const event = pressKey(english, "y");

            expect(event.defaultPrevented).toBe(false);
            expect(widthIn(group, "en")).toBe("40%");
        });

        // CKEditor's redoable() can say yes after a save has dropped its redo snapshots.
        it("takes Ctrl+Y when CKEditor's flag says it can redo but it has no snapshot to redo", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const english = editableFor(group, "en") as HTMLElement & {
                bloomCkEditor?: unknown;
            };
            pressKey(english, "z");
            english.bloomCkEditor = {
                undoManager: {
                    index: 1,
                    snapshots: [{}, {}],
                    redoable: () => true,
                },
            };
            english.setAttribute("tabindex", "0");
            english.focus();
            // Sanity check: the focus is in the block.
            expect(document.activeElement).toBe(english);

            const event = pressKey(english, "y");

            expect(event.defaultPrevented).toBe(true);
            expect(widthIn(group, "fr")).toBe("60%");
        });

        it("leaves both keys to origami in Change Layout mode", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const marginBox = document.createElement("div");
            marginBox.className = "marginBox origami-layout-mode";
            getTestRoot().appendChild(marginBox);

            const event = pressKey(editableFor(group, "en"), "z");

            expect(event.defaultPrevented).toBe(false);
            expect(widthIn(group, "fr")).toBe("60%");
        });

        it("leaves both keys to the reader tools while a markup tool is selected", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            resizeTo(group, "60%");
            const english = editableFor(group, "en");
            // Sanity check: without the reader tools, Ctrl+Z would be taken.
            expect(inlineImageCanUndo()).toBe(true);
            toolboxStub.readerToolsHandleUndoKeys = true;
            try {
                const undoEvent = pressKey(english, "z");
                const redoEvent = pressKey(english, "y");

                expect(undoEvent.defaultPrevented).toBe(false);
                expect(redoEvent.defaultPrevented).toBe(false);
                expect(widthIn(group, "fr")).toBe("60%");
            } finally {
                toolboxStub.readerToolsHandleUndoKeys = false;
            }
        });
    });

    // To CKEditor, a selection change looked like an edit, and an undo of typing first spent a
    // step taking the handles away.
    describe("keeping the selection out of CKEditor's snapshots", () => {
        it("snapshots a selected picture as if it were not selected", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            insertInlineImage(group);
            const unselected = english.innerHTML;
            selectInlineImage(getInlineImageInEditable(english)!);
            // Sanity check: selecting added handles and the class.
            expect(english.innerHTML).toContain(kInlineImageHandleFrameClass);
            expect(english.innerHTML).toContain(kInlineImageSelectedClass);

            expect(removeInlineImageSelectionFromHtml(english.innerHTML)).toBe(
                removeInlineImageSelectionFromHtml(unselected),
            );
        });

        it("keeps other bloom-ui, such as the format button", () => {
            const html =
                '<div class="bloom-inlineImage bloom-inlineImage-selected"><img src="a.png"></div>' +
                '<p>Text</p><div id="formatButton" class="bloom-ui"></div>';

            const filtered = removeInlineImageSelectionFromHtml(html);

            expect(filtered).toContain("formatButton");
            expect(filtered).not.toContain(kInlineImageSelectedClass);
        });

        it("deselects the picture when CKEditor's undo of typing replaces it", () => {
            const listeners = new Map<string, () => void>();
            let loadSnapshotPriority: number | undefined;
            keepInlineImageSelectionOutOfCkeditorSnapshots({
                on: (name, l, _scope, _data, priority) => {
                    listeners.set(name, l as () => void);
                    if (name === "loadSnapshot")
                        loadSnapshotPriority = priority;
                },
            });
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            insertInlineImage(group);
            selectInlineImage(getInlineImageInEditable(english)!);
            // Sanity check: the toolbar is showing.
            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).not.toBeNull();
            // CKEditor puts back a snapshot, which has no selection in it.
            english.innerHTML = removeInlineImageSelectionFromHtml(
                english.innerHTML,
            );

            listeners.get("loadSnapshot")!();

            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).toBeNull();
            expect(
                loadSnapshotPriority,
                "it must run after the editable's own loadSnapshot listener, at 10",
            ).toBeGreaterThan(10);
        });

        it("keeps a selection that CKEditor's undo did not replace", () => {
            const listeners = new Map<string, () => void>();
            keepInlineImageSelectionOutOfCkeditorSnapshots({
                on: (name, l) => listeners.set(name, l as () => void),
            });
            const group = makeSimpleGroup();
            insertInlineImage(group);
            const wrapper = getInlineImageInEditable(editableFor(group, "en"))!;
            selectInlineImage(wrapper);

            listeners.get("loadSnapshot")!();

            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).not.toBeNull();
        });

        it("filters what the editable's own listener put in the snapshot", () => {
            let listener: ((evt: { data?: unknown }) => void) | undefined;
            let priority: number | undefined;
            keepInlineImageSelectionOutOfCkeditorSnapshots({
                on: (name, l, _scope, _data, p) => {
                    if (name !== "getSnapshot") return;
                    listener = l;
                    priority = p;
                },
            });
            const evt: { data?: unknown } = {
                data: '<div class="bloom-inlineImage bloom-inlineImage-selected"></div>',
            };

            listener!(evt);

            expect(
                priority,
                "it must run after the editable's listener, at 10",
            ).toBeGreaterThan(10);
            expect(evt.data).not.toContain(kInlineImageSelectedClass);
        });
    });

    describe("resetting CKEditor's history after a picture change", () => {
        // Stands in for CKEditor's undo Image, which takes a snapshot of an editor; the second
        // argument says whether to leave out the selection.
        class FakeImage {
            public contentOnly: boolean;
            constructor(_editor: unknown, contentOnly: boolean) {
                this.contentOnly = contentOnly;
            }
        }
        beforeEach(() => {
            (globalThis as unknown as { CKEDITOR?: unknown }).CKEDITOR = {
                plugins: { undo: { Image: FakeImage } },
            };
        });
        afterEach(() => {
            delete (globalThis as unknown as { CKEDITOR?: unknown }).CKEDITOR;
        });

        // Gives each editable a stand-in for the CKEditor instance Bloom attaches to it.
        const attachFakeEditors = (group: HTMLElement, hasSnapshot = true) =>
            ["en", "fr"].map((lang) => {
                const undoManager = {
                    currentImage: hasSnapshot ? {} : undefined,
                    reset: vi.fn(),
                    save: vi.fn(),
                    refreshState: vi.fn(),
                    editingHandler: {
                        keyEventsStack: { resetInputs: vi.fn() },
                    },
                };
                (
                    editableFor(group, lang) as HTMLElement & {
                        bloomCkEditor?: unknown;
                    }
                ).bloomCkEditor = { undoManager };
                return undoManager;
            });

        it("starts every editable's history again after an undo restores the pictures", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            const undoManagers = attachFakeEditors(group);

            expect(inlineImageUndo()).toBe(true);

            // Sanity check: the undo took the picture out.
            expect(getInlineImages(group).length).toBe(0);
            undoManagers.forEach((undoManager) => {
                expect(undoManager.reset).toHaveBeenCalled();
                expect(undoManager.save).toHaveBeenCalledTimes(1);
                const [onContentOnly, image, autoFireChange] =
                    undoManager.save.mock.calls[0];
                // Recording the selection of editables that do not have the focus scrolled the
                // page.
                expect(
                    (image as FakeImage).contentOnly,
                    "expected a snapshot of the content only",
                ).toBe(true);
                expect(onContentOnly).toBe(true);
                expect(
                    autoFireChange,
                    "a change event would count the picture change as typing",
                ).toBe(false);
                expect(
                    undoManager.reset.mock.invocationCallOrder[0],
                ).toBeLessThan(undoManager.save.mock.invocationCallOrder[0]);
            });
        });

        it("starts every editable's history again after a menu command", async () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            (wrapper.querySelector("img") as HTMLImageElement).src =
                "flower.jpg";
            const undoManagers = attachFakeEditors(group);
            const transparent = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            )
                .find((i) => i.l10nId === "EditTab.Image.Transparency")!
                .subMenu!.find(
                    (i) =>
                        i.l10nId === "EditTab.Image.Transparency.Transparent",
                )!;

            (transparent.onClick as () => void)();

            await vi.waitFor(() =>
                undoManagers.forEach((undoManager) =>
                    expect(undoManager.reset).toHaveBeenCalled(),
                ),
            );
        });

        // An input event with no keydown before it (a spelling suggestion, a paste from the menu)
        // leaves CKEditor counting typing in progress, and it then records typing on every keyup,
        // which discarded what the undo stack could redo.
        it("clears CKEditor's count of typing in progress", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            const undoManagers = attachFakeEditors(group);

            expect(inlineImageUndo()).toBe(true);

            undoManagers.forEach((undoManager) =>
                expect(
                    undoManager.editingHandler.keyEventsStack.resetInputs,
                ).toHaveBeenCalled(),
            );
        });

        it("leaves alone an editor that has not recorded a snapshot yet", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            insertInlineImage(group);
            const undoManagers = attachFakeEditors(group, false);

            expect(inlineImageUndo()).toBe(true);

            undoManagers.forEach((undoManager) =>
                expect(undoManager.reset).not.toHaveBeenCalled(),
            );
        });
    });

    describe("selection", () => {
        it("marks the wrapper and gives it four resize handles", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // Sanity check: nothing is selected until we say so.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );

            selectInlineImage(wrapper);

            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            const frame = wrapper.querySelector(
                "." + kInlineImageHandleFrameClass,
            ) as HTMLElement;
            expect(frame, "expected a handle frame").not.toBeNull();
            // Everything inlineImageInteractions.ts adds inside the wrapper has to be bloom-ui,
            // or it would be saved and copied to the other languages.
            expect(frame.classList.contains("bloom-ui")).toBe(true);
            expect(
                frame.querySelectorAll("." + kInlineImageHandleClass).length,
            ).toBe(4);
        });

        it("does not add a second set of handles when selected again", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            selectInlineImage(wrapper);
            expect(
                wrapper.querySelectorAll("." + kInlineImageHandleFrameClass)
                    .length,
            ).toBe(1);
        });

        it("selects only one image at a time", () => {
            const group = makeSimpleGroup();
            insertInlineImage(group);
            const english = getInlineImageInEditable(
                editableFor(group, "en"),
            ) as HTMLElement;
            const french = getInlineImageInEditable(
                editableFor(group, "fr"),
            ) as HTMLElement;
            selectInlineImage(english);
            expect(english.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );

            selectInlineImage(french);

            expect(english.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(
                english.querySelector("." + kInlineImageHandleFrameClass),
            ).toBeNull();
            expect(french.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
        });

        it("takes the marker and the handles off again on deselect", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);

            deselectAllInlineImages(document);

            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).toBeNull();
        });

        it("puts the toolbar up on select and takes it down on deselect", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // Sanity check: no toolbar before anything is selected.
            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).toBeNull();

            selectInlineImage(wrapper);

            const bar = document.getElementById(kInlineImageContextControlsId);
            expect(
                bar,
                "expected the toolbar under the picture",
            ).not.toBeNull();
            // It is on the body, outside the page, so it is never saved with the page.
            expect(bar!.parentElement).toBe(document.body);
            expect(bar!.closest(".bloom-page")).toBeNull();

            deselectAllInlineImages(document);

            expect(
                document.getElementById(kInlineImageContextControlsId),
            ).toBeNull();
        });

        it("clears the selection for the page-save path", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            document.body.classList.add("bloom-inlineImage-dragging");

            cleanupInlineImageInteractions();

            // This class is on the wrapper, which is saved, so this is the check that matters
            // most.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).toBeNull();
            expect(
                document.body.classList.contains("bloom-inlineImage-dragging"),
            ).toBe(false);
        });
    });

    describe("getInlineImageDock", () => {
        it("reads the dock a wrapper is in", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // A new inline image is docked right.
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageRightClass);
            wrapper.classList.remove(kInlineImageRightClass);
            wrapper.classList.add(kInlineImageBottomClass);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageBottomClass);
        });

        it("falls back to the dock a new image gets when the markup has none", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            wrapper.classList.remove(kInlineImageRightClass);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageRightClass);
        });
    });

    // While a drag is in progress it changes only the wrapper being dragged, and it copies the
    // result to the other languages once, at the end. Otherwise every mouse move would rewrite
    // every language's copy. These tests send events to the module's own listeners through a
    // whole drag. They do not check where the drag ends up, because with every box empty,
    // jsdom always puts the image in the middle band. They check when the other language's
    // copy was rewritten. Each sync replaces that copy entirely, so the replacements can be
    // counted.
    describe("a whole drag gesture", () => {
        // Counts how many times the given editable's inline image has been replaced since the
        // last call. takeRecords is synchronous, so this needs no waiting.
        function makeWrapperReplacementCounter(editable: HTMLElement): {
            count: () => number;
            stop: () => void;
        } {
            const observer = new MutationObserver(() => {
                // Nothing to do here; count() below collects the records.
            });
            observer.observe(editable, { childList: true });
            return {
                count: () =>
                    observer
                        .takeRecords()
                        .filter((record) =>
                            Array.from(record.removedNodes).some((node) =>
                                (node as HTMLElement).classList?.contains(
                                    kInlineImageClass,
                                ),
                            ),
                        ).length,
                stop: () => observer.disconnect(),
            };
        }

        it("rewrites the other languages' copies once, at the end", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            const frenchEditable = editableFor(group, "fr");
            const french = makeWrapperReplacementCounter(frenchEditable);

            img.dispatchEvent(pointerEvent("pointerdown", 200, 100));
            // Pressing on the image selects it, and changes nothing else.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(french.count()).toBe(0);

            // Several moves, each well past the distance that counts as a click.
            document.dispatchEvent(pointerEvent("pointermove", 160, 80));
            document.dispatchEvent(pointerEvent("pointermove", 120, 60));
            document.dispatchEvent(pointerEvent("pointermove", 100, 50));
            // Sanity check: the drag moved the image.
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageMiddleClass);
            // But it changed only the block the image is being dragged in.
            expect(french.count()).toBe(0);

            document.dispatchEvent(pointerEvent("pointerup", 100, 50));

            // At least one. The sync matches copies by their id and keeps the floating wrappers
            // in order, so a single sync may replace another language's wrapper more than once,
            // and the exact count does not tell how many syncs ran. What matters is that the
            // replacement happened here and not earlier.
            expect(french.count()).toBeGreaterThan(0);
            const frenchWrapper = getInlineImageInEditable(frenchEditable);
            expect(
                frenchWrapper,
                "expected French to still have a copy",
            ).not.toBeNull();
            expect(getInlineImageDock(frenchWrapper!)).toBe(
                kInlineImageMiddleClass,
            );
            // The copy the user dragged is still the one getInlineImage returns, which the
            // other copies are made from.
            expect(getInlineImage(group)).toBe(wrapper);
            french.stop();
        });

        // The pointer listeners are on the page's document, so a release over the toolbox,
        // over other parts of Bloom's window, or outside the window would never reach
        // onPointerEnd, and the drag would never end. Capturing the pointer makes the browser
        // deliver that pointerup here anyway. jsdom does not send events to the capturing
        // element, so a unit test can only check that the capture is taken and released at the
        // end.
        it("captures the pointer, so a release anywhere still ends the gesture", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            // Sanity check: nothing holds a capture before the press.
            expect(capturedPointers.size).toBe(0);

            img.dispatchEvent(pointerEvent("pointerdown", 200, 100));

            expect(capturedPointers.get(wrapper)).toBe(kTestPointerId);

            document.dispatchEvent(pointerEvent("pointerup", 100, 50));

            expect(capturedPointers.has(wrapper)).toBe(false);
        });

        // The listeners are on the document, so they see every pointer, and the browser
        // delivers a second pointer's events even while the first holds a capture. Acting on
        // them would end the user's drag at whatever position the other pointer reports. A
        // stray touch on a laptop's trackpad is enough to cause that.
        it("ignores a second pointer while a gesture is under way", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;

            img.dispatchEvent(pointerEvent("pointerdown", 200, 100));
            document.dispatchEvent(pointerEvent("pointermove", 160, 80));
            document.dispatchEvent(pointerEvent("pointermove", 100, 50));
            // Sanity check: the drag has started and has moved the image.
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageMiddleClass);

            document.dispatchEvent(
                pointerEvent("pointermove", 250, 190, kOtherTestPointerId),
            );
            document.dispatchEvent(
                pointerEvent("pointerup", 250, 190, kOtherTestPointerId),
            );

            // The first pointer still has the capture, and the image is where it left it.
            expect(capturedPointers.get(wrapper)).toBe(kTestPointerId);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageMiddleClass);

            // The first pointer's own release ends the drag.
            document.dispatchEvent(pointerEvent("pointerup", 100, 50));
            expect(capturedPointers.has(wrapper)).toBe(false);
        });

        it("changes nothing for a press that never became a drag", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            const french = makeWrapperReplacementCounter(
                editableFor(group, "fr"),
            );

            img.dispatchEvent(pointerEvent("pointerdown", 200, 100));
            // The pointer often moves a pixel or two during a click, and that is not a drag.
            document.dispatchEvent(pointerEvent("pointermove", 201, 101));
            document.dispatchEvent(pointerEvent("pointerup", 201, 101));

            expect(french.count()).toBe(0);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageRightClass);
            // It is still a click, so the image ends up selected.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            french.stop();
        });
    });
});
