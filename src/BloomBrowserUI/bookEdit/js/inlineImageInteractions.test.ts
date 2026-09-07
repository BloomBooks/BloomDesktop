import { describe, it, expect, beforeEach, afterAll, vi } from "vitest";

// setupInlineImageInteractions asks the api whether AI image editing is turned on. There is no
// api here, so the request would fail on the network and the menu's contents would depend on
// when that failure landed. Answer it here instead: off, which is what the menu tests below
// expect ("Edit with AI" absent).
vi.mock("../../react_components/featureStatus", () => ({
    getFeatureStatusAsync: () => Promise.resolve(undefined),
}));
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import {
    getInlineImage,
    getInlineImageInEditable,
    handleInlineImageChanged,
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
    recordInlineImageUndoPoint,
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
    computeViewportPxPerLayoutPx,
    shouldRevertInlineImageMove,
    computeInlineImageClusterIndex,
    computeInlineImageDock,
    computeInlineImageWidthPercent,
    deselectAllInlineImages,
    getInlineImageActionTarget,
    getInlineImageDock,
    getInlineImageHandleHorizontalSign,
    getInlineImageMenuItemsForClick,
    kInlineImageContextControlsId,
    kInlineImageHandleClass,
    kInlineImageHandleFrameClass,
    kMaxInlineImageWidthPercent,
    kMinInlineImageWidthPercent,
    selectInlineImage,
    setupInlineImageInteractions,
} from "./inlineImageInteractions";

// jsdom reports every element's box as empty, so a real drag cannot be checked for where it
// put the image; the arithmetic that decides that lives in the pure functions these tests
// aim at. The DOM tests cover selection and which commands a right-click offers where.

// jsdom has no PointerEvent, but a listener registered for "pointerdown" fires for any event
// of that type, and MouseEvent carries the button and the coordinates this module reads.
function pointerEvent(
    type: string,
    clientX: number,
    clientY: number,
): MouseEvent {
    return new MouseEvent(type, {
        bubbles: true,
        cancelable: true,
        button: 0,
        clientX,
        clientY,
    });
}

// A block 300px wide and 200px tall at the origin, so that thirds land on round numbers
// (100 and 200) and the bottom fifth starts at y=160.
const kEditableBox = { left: 0, top: 0, width: 300, height: 200 };
// The dock of a position depends on how tall the image is, because the bottom dock starts
// where the band can no longer fit the image inside the block's content.
const kImageHeightViewportPx = 40;

let pageCounter = 0;

// Builds a page with one translation group, one editable per entry. Same shape as
// inlineImages.test.ts, since the undo layer keys on the page id.
function makeTranslationGroup(
    editables: { lang: string; classes?: string; content?: string }[],
    options?: { insideCanvasElement?: boolean },
): HTMLElement {
    const root = getTestRoot();
    const groupHtml =
        `<div class="bloom-translationGroup" id="group">` +
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

const makeSimpleGroup = (options?: { insideCanvasElement?: boolean }) =>
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
            // Exactly on a boundary belongs to the band, not to the side it came from.
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
            // The block's content ends at 200 and the image is 40 tall, so the band can hold
            // it while its center is above 180 and not a pixel lower. Every position above
            // that is the band's, which is the point: the band's position down the block is a
            // distance, and a person moving the picture down expects to be able to stop
            // anywhere (John: "it seems like I should be able to put it vertically anywhere I
            // want"). This used to be the bottom FIFTH of the block, which in an overflowing
            // block was several lines of unreachable positions.
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
            // A taller image runs out of room higher up, since it is the image's BOTTOM that
            // has to stay inside the content.
            expect(
                computeInlineImageDock({ x: 150, y: 150 }, kEditableBox, 100),
            ).toBe(kInlineImageBottomClass);
            // The side docks win all the way down, so an image can be parked in a lower
            // corner (with clear:both zones stealing the whole strip, the corners were
            // unreachable).
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

    // The numbers here were measured in a running Bloom, on the page that produced the
    // report: an A4 page whose text nearly filled it, with an image parked near the bottom,
    // changed to A6 portrait. The text no longer fits the smaller page, so the block
    // scrolls: its rectangle is 532 screen pixels tall and holds 484 layout pixels (the page
    // is drawn at 110%), while the text it contains is 841 layout pixels tall.
    const kA6Report = {
        visibleBoxViewportPx: {
            left: 71,
            top: 87,
            width: 353,
            height: 532.159,
        },
        clientHeightLayoutPx: 484,
        scrollHeightLayoutPx: 841,
        // The image had been given an offset of 661 layout pixels on the A4 page, which puts
        // it below everything the smaller page can show at once.
        imageCenterYViewportPxWhenScrolledToTop: 888,
        // The ball picture was 40% of a 353-pixel block wide, and about this tall on screen.
        imageHeightViewportPx: 163,
    };

    describe("computeBlockContentBox", () => {
        it("gives a block that fits its text its own rectangle", () => {
            const box = { left: 71, top: 87, width: 353, height: 200 };
            expect(computeBlockContentBox(box, 200, 200, 0)).toEqual(box);
        });

        it("reaches past the bottom of a block whose text overflows", () => {
            const content = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.clientHeightLayoutPx,
                kA6Report.scrollHeightLayoutPx,
                0,
            );
            expect(content.top).toBe(87);
            // 841 layout pixels of text, drawn at 110%.
            expect(Math.round(content.height)).toBe(925);
            expect(Math.round(content.top + content.height)).toBe(1012);
        });

        it("follows the block as it is scrolled, so the content keeps one position", () => {
            const scrolled = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.clientHeightLayoutPx,
                kA6Report.scrollHeightLayoutPx,
                324.667,
            );
            expect(Math.round(scrolled.top)).toBe(-270);
            expect(Math.round(scrolled.top + scrolled.height)).toBe(655);
        });

        it("returns the rectangle unchanged when nothing is laid out (jsdom)", () => {
            const box = { left: 0, top: 0, width: 0, height: 0 };
            expect(computeBlockContentBox(box, 0, 0, 0)).toEqual(box);
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

    describe("computeViewportPxPerLayoutPx", () => {
        it("is the ratio of the screen rectangle to the laid-out height", () => {
            expect(
                computeViewportPxPerLayoutPx(
                    kA6Report.visibleBoxViewportPx,
                    kA6Report.clientHeightLayoutPx,
                ),
            ).toBeCloseTo(1.0995, 4);
        });

        it("is 1 when there is nothing to measure", () => {
            expect(
                computeViewportPxPerLayoutPx(
                    { left: 0, top: 0, width: 0, height: 0 },
                    0,
                ),
            ).toBe(1);
        });
    });

    // The report: "I added an image to the bottom right-hand corner of an A4 portrait page
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
            // What the block's rectangle says, and the whole of the reported bug: the image
            // sits below the bottom of what the small page shows, so every drag of it asked
            // for the bottom dock -- which does not fit in a block that is already too
            // small, so every move was undone and the image never went anywhere.
            expect(asShown).toBe(kInlineImageBottomClass);

            const contentBox = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.clientHeightLayoutPx,
                kA6Report.scrollHeightLayoutPx,
                0,
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
            // Measured against the rectangle, the image is 281 pixels PAST the limit, so
            // every drag clamped it back up to the fold.
            expect(Math.round(roomByRectangle)).toBe(-281);
            expect(
                clampInlineImageOffset(
                    currentOffsetLayoutPx + roomByRectangle,
                    currentOffsetLayoutPx + roomByRectangle,
                ),
            ).toBe(380);

            const contentBox = computeBlockContentBox(
                kA6Report.visibleBoxViewportPx,
                kA6Report.clientHeightLayoutPx,
                kA6Report.scrollHeightLayoutPx,
                0,
            );
            const viewportPxPerLayoutPx = computeViewportPxPerLayoutPx(
                kA6Report.visibleBoxViewportPx,
                kA6Report.clientHeightLayoutPx,
            );
            const roomByContent =
                (contentBox.top + contentBox.height - wrapperBottomViewportPx) /
                viewportPxPerLayoutPx;
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
            // A tie belongs after the neighbor already at that height.
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
            // Two thirds of the way down a 900px block is two thirds of the way down a 300px one.
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
            // Nothing is laid out (jsdom), or no baseline was ever recorded.
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
            // 120px of a 300px block is 40%; dragging an east corner 30px right adds 10%.
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
            // Sanity check: the same block outside a canvas element would be eligible.
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
            // There is no limit on inline images per group; each add appends a new one.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("add");
            // ...from a sibling language's block too.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "fr").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("add");
        });
    });

    describe("buildInlineImageMenuItems", () => {
        it("offers only Add Image where there is no image", () => {
            const group = makeSimpleGroup();
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ),
            );
            expect(items.map((i) => i.l10nId)).toEqual([
                "EditTab.InlineImage.AddImage",
            ]);
        });

        it("offers the standard image menu plus Delete for an existing image", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            );
            // The registry's "image" section, filtered by its normal availability rules:
            // "Expand image to fill space" is for background images only, and Become
            // Background / Use for book thumbnail are excluded for an image inside a text
            // block. "Edit with AI" is behind a feature flag that is off here. Then a
            // divider and Delete.
            expect(items.map((i) => i.l10nId)).toEqual([
                "EditTab.Image.EditMetadataOverlay",
                "EditTab.Image.ChooseImage",
                "EditTab.Image.CopyImage",
                "EditTab.Image.PasteImage",
                "EditTab.Image.Reset",
                "EditTab.Image.Transparency",
                "-",
                "Common.Delete",
            ]);
            // A new inline image holds a placeholder: no credits to edit, nothing to copy,
            // and no transparency of a real picture to control...
            const byId = (id: string) => items.find((i) => i.l10nId === id)!;
            expect(byId("EditTab.Image.EditMetadataOverlay").disabled).toBe(
                true,
            );
            expect(byId("EditTab.Image.CopyImage").disabled).toBe(true);
            expect(byId("EditTab.Image.Transparency").disabled).toBe(true);
            // ...but choosing a picture is exactly what a placeholder is waiting for.
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
            // Sanity check: the command finds its image through the registry's own
            // plumbing (getImage), which must cope with the inline wrapper's shape --
            // an img that is a direct child, with no bloom-imageContainer.
            expect(
                wrapper
                    .querySelector("img")!
                    .classList.contains("bloom-transparent"),
            ).toBe(false);

            (transparent.onClick as () => void)();
            // The command itself is synchronous, but the sync that follows it awaits it
            // first; give the microtask a beat.
            await new Promise((resolve) => setTimeout(resolve));

            // It acted on the clicked copy...
            expect(
                wrapper
                    .querySelector("img")!
                    .classList.contains("bloom-transparent"),
            ).toBe(true);
            // ...and the follow-up sync stamped the result onto the other language.
            const frenchWrapper = getInlineImageInEditable(
                editableFor(group, "fr"),
            )!;
            expect(
                frenchWrapper
                    .querySelector("img")!
                    .classList.contains("bloom-transparent"),
            ).toBe(true);
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
            // The undo layer's gate is the selection, so this is not merely cosmetic.
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
            // Sanity: two distinct images, each present in both languages.
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
            // Actually invoke the command (a gap a real runtime break slipped through once).
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

        // The bar of buttons is rendered into a div on the body, not inside the wrapper, so
        // deleting the picture does not take it with it: it stayed on screen offering
        // commands ("Choose image", "Copy image"...) for a picture that was gone.
        it("Delete takes the bar of buttons down with the image", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const items = getInlineImageMenuItemsForClick(wrapper);
            // Sanity check: the right-click selected it, which is what puts the bar up.
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

    // jsdom measures every box as empty, so these stub the two measurements this function
    // reads. That is enough for what is being checked: which changes it decides to act on.
    describe("adjustInlineImageOffsetsIfBlockSizeChanged", () => {
        // Give an editable a size jsdom will report.
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

        // A width change on its own used to be ignored -- and then the new width was recorded
        // as the baseline, so it could never be noticed afterwards either. Splitting a text box
        // in Change Layout does exactly this: same height, less width, so the same text wraps
        // into more lines and what follows the picture can run off the end of the block.
        it("acts on a block that changed only in width", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            const french = editableFor(group, "fr");
            insertInlineImage(group);
            setBlockSize(english, 300, 200);
            adjustInlineImageOffsetsIfBlockSizeChanged(group);
            // Sanity check: the baseline has been recorded, and both copies agree.
            const baselineAttr = kInlineImageOffsetBasedOnAttr;
            expect(
                getInlineImageInEditable(english)!.getAttribute(baselineAttr),
            ).toBe("300,200");
            // Make the sibling's copy differ, so that a sync is visible.
            getInlineImageInEditable(french)!.setAttribute(
                baselineAttr,
                "stale",
            );

            setBlockSize(english, 150, 200);
            adjustInlineImageOffsetsIfBlockSizeChanged(group);

            expect(
                getInlineImageInEditable(english)!.getAttribute(baselineAttr),
            ).toBe("150,200");
            // The sync at the end of the function only runs when it decided something changed.
            expect(
                getInlineImageInEditable(french)!.getAttribute(baselineAttr),
            ).toBe("150,200");
        });

        it("leaves a block whose size has not changed alone", () => {
            const group = makeSimpleGroup();
            const english = editableFor(group, "en");
            const french = editableFor(group, "fr");
            insertInlineImage(group);
            setBlockSize(english, 300, 200);
            adjustInlineImageOffsetsIfBlockSizeChanged(group);
            const baselineAttr = kInlineImageOffsetBasedOnAttr;
            getInlineImageInEditable(french)!.setAttribute(
                baselineAttr,
                "untouched",
            );

            adjustInlineImageOffsetsIfBlockSizeChanged(group);

            expect(
                getInlineImageInEditable(french)!.getAttribute(baselineAttr),
            ).toBe("untouched");
        });
    });

    // inlineImages.ts replaces wrappers behind our back in two cases, and tells us about each
    // with an event, because in both the selection (which is the inline-image undo layer's
    // gate) needs re-asserting and the handles live on an element that may be gone.
    describe("reacting to inlineImages.ts", () => {
        it("puts the handles back on the wrapper an undo restored", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            // A dock change, recorded so that undo has something to put back.
            recordInlineImageUndoPoint(group);
            setInlineImageDock(wrapper, kInlineImageBottomClass);
            syncInlineImagesFromEditable(editableFor(group, "en"));

            expect(inlineImageUndo()).toBe(true);

            // Undo rebuilds the wrapper from serialized markup, so this is a new element...
            const restored = getInlineImageInEditable(editableFor(group, "en"));
            expect(restored, "expected a restored wrapper").not.toBeNull();
            expect(restored).not.toBe(wrapper);
            expect(getInlineImageDock(restored!)).toBe(kInlineImageRightClass);
            // ...which inlineImages.ts marks as selected, and which therefore needs handles:
            // serialized markup cannot carry them, since they are bloom-ui.
            expect(
                restored!.classList.contains(kInlineImageSelectedClass),
            ).toBe(true);
            expect(
                restored!.querySelector("." + kInlineImageHandleFrameClass),
                "expected the handles to be rebuilt",
            ).not.toBeNull();
        });

        it("re-asserts the selection when a new picture arrives", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            img.setAttribute("src", "flower.jpg");
            // The trip out to the image chooser can leave the focus back in the text, which
            // is what dropping the selection looks like.
            deselectAllInlineImages(document);

            handleInlineImageChanged(img);

            // Without this the change (and the insert that led to it) would not be undoable.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).not.toBeNull();
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
            // Everything the interaction layer adds inside the wrapper has to be bloom-ui, or
            // it would be saved and replicated to the other languages.
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
            // It lives on the body, above the page, so the page save never sees it.
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

            // The class is on the wrapper, which IS saved, so this is the one that matters.
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

    // A drag touches only the local wrapper while it is in progress and stamps the result onto
    // the other languages once, at the end -- otherwise every mouse move would rewrite every
    // language's copy. These drive the module's own listeners through a whole gesture. Where
    // the drag ends up is not the point (with every box empty, jsdom's answer to "which third
    // is this?" is always the band); how many times the other language's copy got rewritten
    // is, and each sync replaces that copy wholesale, so the replacements can be counted.
    describe("a whole drag gesture", () => {
        // Counts how many times the given editable's inline image has been replaced since the
        // last call. takeRecords is synchronous, so this needs no waiting.
        function makeWrapperReplacementCounter(editable: HTMLElement): {
            count: () => number;
            stop: () => void;
        } {
            const observer = new MutationObserver(() => {
                // Nothing to do on delivery; the records are collected by count() below.
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

            // Well past the click threshold, and repeatedly.
            document.dispatchEvent(pointerEvent("pointermove", 160, 80));
            document.dispatchEvent(pointerEvent("pointermove", 120, 60));
            document.dispatchEvent(pointerEvent("pointermove", 100, 50));
            // Sanity check: the drag really did move the image...
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageMiddleClass);
            // ...but only in the block being dragged in. A live preview is local.
            expect(french.count()).toBe(0);

            document.dispatchEvent(pointerEvent("pointerup", 100, 50));

            // At least one: since the sync matches copies up by id and keeps the cluster in
            // order, it may legitimately rewrite a sibling's wrapper more than once in a single
            // pass, so the exact count is not a proxy for "how many syncs". What matters is
            // that it happened here and not above.
            expect(french.count()).toBeGreaterThan(0);
            const frenchWrapper = getInlineImageInEditable(frenchEditable);
            expect(
                frenchWrapper,
                "expected French to still have a copy",
            ).not.toBeNull();
            expect(getInlineImageDock(frenchWrapper!)).toBe(
                kInlineImageMiddleClass,
            );
            // The copy the user dragged is still the canonical one.
            expect(getInlineImage(group)).toBe(wrapper);
            french.stop();
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
            // A click wobbles by a pixel or two; that is not a drag.
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
