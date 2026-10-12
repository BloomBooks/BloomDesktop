// This file handles what the user does to inline (Word-style) images while editing: the
// right-click menu commands that add and remove them, the toolbar under a selected image,
// selecting an image, dragging it to another dock, and resizing it. inlineImages.ts handles
// the markup, the custom properties that hold the size and position, keeping every language's
// copy the same, and undo.
//
// Two things explain much of the code here:
//
// 1. Every drag or resize ends with the same three steps, because of how inline images are
//    stored. It commits the undo point, copies the new size and position onto the other
//    languages' copies, and checks overflow again, since an image that just got bigger can
//    push the text past the bottom of the block. While the pointer is moving, only the wrapper
//    in this editable changes, which keeps each move fast. The copying happens once, at the
//    end. See commitInlineImageChange.
//
// 2. All the listeners are on the document, and none are on the wrappers. Wrapper elements
//    are replaced all the time: a sync puts new copies into the other editables, and an undo
//    rebuilds them from saved markup. A handler attached to one wrapper element would stop
//    working without any error, so every handler here starts from the event's target.
//
// The calculations (which dock a position calls for, how far down the image has been dragged,
// how wide a resize has made it) are in exported functions with no side effects, and the
// tests are aimed at those. jsdom does no layout, so the drags themselves cannot be unit
// tested.
//
// The commands reach the user through the text block's right-click menu, which belongs to
// bookEdit/textContextMenu/TextContextMenu.tsx (BL-16649) and also holds paragraph commands
// such as "No Indent". That menu calls getInlineImageMenuItemsForClick to find out what to
// offer for the click it is handling. So this module has no contextmenu listener of its own.
// Two handlers for the same event on the same elements would conflict, and that menu stops
// the event from propagating once it decides to open.
import * as React from "react";
import { default as InsertImageIcon } from "@mui/icons-material/AddPhotoAlternateOutlined";
import { default as DeleteIcon } from "@mui/icons-material/DeleteOutline";
import { getFeatureStatusAsync } from "../../react_components/featureStatus";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";
import { buildCanvasElementControlRegistryContext } from "../toolbox/canvas/buildCanvasElementControlRegistryContext";
import { imageAvailabilityRules } from "../toolbox/canvas/canvasControlAvailabilityRules";
import { getMenuSections } from "../toolbox/canvas/canvasControlResolution";
import {
    ICanvasElementControlConfiguration,
    IControlContext,
    IControlMenuRow,
    IControlRuntime,
} from "../toolbox/canvas/canvasControlTypes";
import {
    convertControlMenuRows,
    IMenuItemWithSubmenu,
    joinMenuSectionsWithSingleDividers,
} from "./canvasElementManager/canvasControlMenuRendering";
import {
    CanvasElementContextControls,
    IControlsForNonCanvasObject,
} from "./canvasElementManager/CanvasElementContextControls";
import { renderRoot } from "../../utils/reactRender";
import {
    clearImageContentTransform,
    FlipAxis,
    flipImageContent,
} from "./imageContentTransform";
import {
    commitPendingInlineImageUndo,
    getEditables,
    getFirstVisibleEditable,
    getInlineImageOffsetBaseline,
    getInlineImagesInEditable,
    getTranslationGroupsWithInlineImages,
    InlineImageDock,
    insertInlineImage,
    kInlineImageBottomClass,
    kInlineImageChangedEvent,
    kInlineImageClass,
    kInlineImageDockClasses,
    kInlineImageLeftClass,
    kInlineImageMiddleClass,
    kInlineImageOffsetVar,
    kInlineImageRightClass,
    kInlineImagesRestoredEvent,
    kInlineImageSelectedClass,
    kInlineImageWidthVar,
    noteInlineImageBlockWasEdited,
    prepareInlineImageUndo,
    recordInlineImageOffsetBaseline,
    removeInlineImage,
    setInlineImageDock,
    syncInlineImagesFromEditable,
} from "./inlineImages";

// Classes for the four resize handles and the frame they are positioned in. Both are bloom-ui,
// so Cleanup() removes them before the page is saved, and syncInlineImagesFromEditable leaves
// them out of the copies it puts into the other languages' editables.
export const kInlineImageHandleFrameClass = "bloom-ui-inlineImage-handle-frame";
export const kInlineImageHandleClass = "bloom-ui-inlineImage-handle";

// Set on the body while a drag or resize is in progress, so that moving the pointer does not
// also select text. The body is outside the bloom-page, so the class is never saved.
export const kInlineImageDraggingClass = "bloom-inlineImage-dragging";

// The corners as compass directions, matching the CSS for each corner in
// inlineImageEditing.less.
const kInlineImageHandleCorners = ["nw", "ne", "sw", "se"] as const;
export type InlineImageHandleCorner =
    (typeof kInlineImageHandleCorners)[number];
const kInlineImageCornerAttribute = "data-inline-image-corner";

// Limits on an image's width, as percentages of the editable's width. A wider image leaves no
// room for text to wrap beside it, and a narrower one is too small to be worth wrapping around.
export const kMinInlineImageWidthPercent = 10;
export const kMaxInlineImageWidthPercent = 95;

// The pointer often moves a pixel or two during a click. A movement smaller than this is
// treated as a click, so nothing changes and no undo point is recorded.
const kDragThresholdViewportPx = 3;

/** Just the parts of a DOMRect this module needs, so that callers can supply plain numbers. */
export interface IBox {
    left: number;
    top: number;
    width: number;
    height: number;
}

/**
 * What the user's pointer is over, as far as inline images are concerned:
 * - "existing": an inline image, which can be changed, given metadata, or removed.
 * - "add": a text block that can take inline images. There is no limit on how many.
 * - "none": anywhere else. No inline image commands are offered there.
 *
 * A text block can take inline images if it is a bloom-editable that is a direct child of a
 * translation group and is not inside a canvas element. Canvas elements have their own context
 * menu, which already handles their images. getInlineImageActionTarget also leaves out a few
 * other kinds of field; see there. The commands for an existing image are offered when the
 * click is on that image.
 */
export type InlineImageActionTarget =
    | { kind: "none" }
    | { kind: "add"; translationGroup: HTMLElement; editable: HTMLElement }
    | {
          kind: "existing";
          translationGroup: HTMLElement;
          editable: HTMLElement;
          wrapper: HTMLElement;
      };

/**
 * Whether Bloom stores this field's content somewhere else and writes it back into the page,
 * as opposed to keeping it on the page where the user typed it. An inline image cannot go in
 * such a field.
 *
 * A data-book field is stored once in the data div, as InnerXml, and written into every
 * element that has the same key (BookData's GatherDataItemsFromXElement and SetNodeXml). Front
 * and back matter pages are not even kept as they are shown: BringXmatterHtmlUpToDate deletes
 * and re-inserts them, so only the data div survives. When a picture was put in the cover
 * title, the book's stored title ended up holding the wrapper's markup. That title names the
 * book in the collection, in the title bar, and in AllTitles. The file also held four copies of
 * the wrapper for the one picture, with a bloom-contentNational2 class added to it. A field
 * with data-textonly="true" is worse: BookData sets its InnerText, which throws the picture
 * away.
 *
 * The way to put a picture on a cover in Bloom is a canvas element, which is stored on the
 * page, so Insert Image is not offered in these fields. The checks for a canvas element and for
 * an editable that is not a direct child of its group do not exclude these fields, so this
 * check is needed as well.
 */
function isFieldBloomWritesItself(editable: HTMLElement): boolean {
    if (editable.hasAttribute("data-book")) return true;
    const page = editable.closest(".bloom-page");
    return !!page?.hasAttribute("data-xmatter-page");
}

/** See InlineImageActionTarget. Takes the element the user pointed at. */
export function getInlineImageActionTarget(
    element: HTMLElement | undefined | null,
): InlineImageActionTarget {
    if (!element) return { kind: "none" };
    const editable = element.closest(".bloom-editable") as HTMLElement | null;
    if (!editable) return { kind: "none" };
    // The editables of a group are its direct children. Any other bloom-editable is something
    // else, such as the copy in a source bubble, and inline image commands do not apply to it.
    const translationGroup = editable.parentElement;
    if (!translationGroup?.classList.contains("bloom-translationGroup"))
        return { kind: "none" };
    if (editable.closest(kCanvasElementSelector)) return { kind: "none" };
    if (isFieldBloomWritesItself(editable)) return { kind: "none" };
    // An image description is a translation group of its own, inside the bloom-canvas but not
    // inside any canvas element, so none of the checks above exclude it. It is what a reader
    // hears in place of the picture, so a picture in it makes no sense. It is also not shown in
    // the book, so a picture put there could not be seen or removed.
    if (translationGroup.classList.contains("bloom-imageDescription"))
        return { kind: "none" };
    const wrapper = element.closest(
        "." + kInlineImageClass,
    ) as HTMLElement | null;
    if (wrapper)
        return { kind: "existing", translationGroup, editable, wrapper };
    // A block can hold any number of inline images, so "add" is offered whether or not the
    // group already has some. Each insert adds a new image with its own
    // data-bloom-inline-image-id.
    return { kind: "add", translationGroup, editable };
}

/**
 * Which dock a position calls for. The position is the center of the image (or where it would
 * be), which is different from where the cursor is; see the grab offsets in
 * IInlineImageDragState for why. The block's width is divided into thirds: the left third
 * means the left dock, the middle third the middle band, and the right third the right dock.
 *
 * In the middle third, the bottom dock starts exactly where the middle band can go no further.
 * The band's offset is limited so that the whole wrapper stays inside the block's content (see
 * the maximum in continueDrag). So a position that would put the bottom of the image past the
 * end of the content is somewhere the band cannot go, and it is taken to mean the dock below
 * the text. Any position below the block is the bottom dock, wherever it is horizontally.
 *
 * Two rules come from testing in the running Bloom:
 *  - In the left and right thirds, the side docks apply all the way down. If the bottom dock
 *    took over the lower corners, an image could not be placed in a lower corner at all.
 *  - The bottom dock does not start at a fixed fraction of the block's height. When it started
 *    in the bottom fifth, that fifth was five lines of text in a block on an A6 page whose text
 *    overflowed. Every middle band position in it became the bottom dock, so the user could put
 *    the picture at the bottom or five lines higher and nowhere between. (John: "it seems like
 *    I should be able to put it vertically anywhere I want".)
 */
export function computeInlineImageDock(
    imageCenterViewportPx: { x: number; y: number },
    editableContentBoxViewportPx: IBox,
    imageHeightViewportPx: number,
): InlineImageDock {
    // A box with no width cannot be divided into thirds, so treat the position as the middle.
    const fraction =
        editableContentBoxViewportPx.width > 0
            ? (imageCenterViewportPx.x - editableContentBoxViewportPx.left) /
              editableContentBoxViewportPx.width
            : 0.5;
    const contentBottomViewportPx =
        editableContentBoxViewportPx.top + editableContentBoxViewportPx.height;
    if (imageCenterViewportPx.y >= contentBottomViewportPx)
        return kInlineImageBottomClass;
    const inMiddleThird = fraction >= 1 / 3 && fraction <= 2 / 3;
    if (
        inMiddleThird &&
        imageCenterViewportPx.y + imageHeightViewportPx / 2 >=
            contentBottomViewportPx
    )
        return kInlineImageBottomClass;
    if (fraction < 1 / 3) return kInlineImageLeftClass;
    if (fraction > 2 / 3) return kInlineImageRightClass;
    return kInlineImageMiddleClass;
}

/**
 * The box that holds all of a text block's content, in viewport pixels. A drag measures
 * positions against this box. It can be bigger than the block's rectangle: a block too small
 * for its text scrolls, so its rectangle shows only part of the content, and the user can put
 * an image in the part of the text below what is showing.
 *
 * Everything the drag reads from the pointer is in viewport pixels, and the block's scroll
 * measurements are in layout pixels. The two differ whenever the page is zoomed. The rectangle
 * and clientHeight measure the same distance from edge to edge, so dividing one by the other
 * gives the page's scale, and no caller has to know the zoom.
 *
 * For a block whose text fits, this returns the block's own rectangle. With a clientHeight of
 * zero (in jsdom, where nothing is laid out) it returns the rectangle unchanged.
 */
export function computeBlockContentBox(
    visibleBoxViewportPx: IBox,
    clientHeightLayoutPx: number,
    scrollHeightLayoutPx: number,
    scrollTopLayoutPx: number,
): IBox {
    if (!(clientHeightLayoutPx > 0)) return visibleBoxViewportPx;
    const viewportPxPerLayoutPx =
        visibleBoxViewportPx.height / clientHeightLayoutPx;
    return {
        left: visibleBoxViewportPx.left,
        width: visibleBoxViewportPx.width,
        top:
            visibleBoxViewportPx.top -
            scrollTopLayoutPx * viewportPxPerLayoutPx,
        height: scrollHeightLayoutPx * viewportPxPerLayoutPx,
    };
}

/**
 * How far the block has to scroll, in layout pixels, to bring a dragged picture back into the
 * part of the block that is showing. A positive value scrolls the text up, showing more of what
 * follows. A negative value scrolls it down. Zero means the picture is already showing.
 *
 * A block too small for its text scrolls, and clampInlineImageOffset only keeps the picture
 * inside the block's content. So dragging downward can move the picture into text that is not
 * on the screen, and the user loses sight of what they are moving. (John: "when scrolling is
 * needed, the scrolling doesn't follow the drag of the image. So if you drag the image off the
 * top or the bottom, you can't see it anymore".) Scrolling by exactly the amount that sticks
 * out keeps up with the drag without getting ahead of it.
 *
 * When the picture is taller than the part of the block that is showing, it cannot be shown
 * whole, so this brings its bottom edge into view.
 */
export function computeInlineImageDragScrollLayoutPx(
    imageBoxViewportPx: IBox,
    visibleBoxViewportPx: IBox,
    viewportPxPerLayoutPx: number,
): number {
    // Nothing is laid out (jsdom), so nothing can be off the screen.
    if (!(viewportPxPerLayoutPx > 0)) return 0;
    const belowViewportPx =
        imageBoxViewportPx.top +
        imageBoxViewportPx.height -
        (visibleBoxViewportPx.top + visibleBoxViewportPx.height);
    if (belowViewportPx > 0) return belowViewportPx / viewportPxPerLayoutPx;
    const aboveViewportPx = visibleBoxViewportPx.top - imageBoxViewportPx.top;
    if (aboveViewportPx > 0) return -aboveViewportPx / viewportPxPerLayoutPx;
    return 0;
}

/**
 * Whether a move has to be undone because it left the text with no room: the block fitted its
 * text before the drag began, and the move made it overflow.
 *
 * A block that already overflowed when the drag began never has its moves undone, and that is
 * the reason this is a separate function. Moving an image anywhere changes how much room the
 * text needs, because an image higher up pushes more text below it. So in a block whose text
 * does not fit, undoing every move that added overflow would undo most moves, including every
 * move back up, and the image could not be moved at all. (In testing, an image with a large
 * offset in an A6 block could not be moved, because every upward drag was undone.) Such a
 * block already shows Bloom's overflow warning, so the user knows about it. In that case
 * clampInlineImageOffset is what keeps the image somewhere the user can reach, by keeping the
 * whole wrapper inside the block's content.
 *
 * The one pixel of allowance covers rounding in the layout, which would otherwise look like
 * overflow caused by the move.
 */
export function shouldRevertInlineImageMove(
    startOverflowLayoutPx: number,
    currentOverflowLayoutPx: number,
): boolean {
    if (startOverflowLayoutPx > 0) return false;
    return currentOverflowLayoutPx > startOverflowLayoutPx + 1;
}

/**
 * How much bigger a distance in viewport pixels is than the same distance in layout pixels,
 * which is the page's zoom. Offsets are written in layout pixels, because the custom property
 * is used inside the scaled page, while a drag measures in viewport pixels. So every distance
 * taken from the pointer is divided by this before it is used as an offset. Returns 1 when
 * there is nothing to measure (jsdom).
 */
export function computeViewportPxPerLayoutPx(
    visibleBoxViewportPx: IBox,
    clientHeightLayoutPx: number,
): number {
    if (!(clientHeightLayoutPx > 0) || !(visibleBoxViewportPx.height > 0))
        return 1;
    return visibleBoxViewportPx.height / clientHeightLayoutPx;
}

/**
 * The value to write to --inline-image-offset. It is rounded to whole pixels. It is never
 * negative, because the image cannot sit above the top of its block. It is no larger than
 * maxLayoutPx, which is how far down the image can start and still fit inside the block. A
 * maximum of zero or less is a valid value: it means the image already fills the block, so it
 * stays at the top. Pass undefined when there is no box to measure against (nothing is laid
 * out), and then only the lower limit applies.
 */
export function clampInlineImageOffset(
    offsetLayoutPx: number,
    maxLayoutPx?: number,
): number {
    const rounded = Math.round(offsetLayoutPx);
    // Negatives and NaN both land here.
    if (!(rounded > 0)) return 0;
    if (maxLayoutPx !== undefined)
        return Math.min(rounded, Math.max(0, Math.round(maxLayoutPx)));
    return rounded;
}

/**
 * Scales an offset measured in a block of one height to a block of another height, so that a
 * picture two thirds of the way down its text stays two thirds of the way down. The result is
 * in whole pixels and never negative. The offset is unchanged when either height is zero or
 * missing (nothing is laid out, or no baseline was recorded).
 *
 * Scaling alone does not always keep the text inside the block. The text beside a float
 * rewraps when the block changes width, so a scaled offset can still leave the block with more
 * text than it can show. adjustInlineImageOffsetsIfBlockSizeChanged reduces the offset
 * afterwards when that happens, so the block ends up fitting its text, as it does after a drag.
 */
export function computeInlineImageOffsetForNewBlockHeight(
    offsetLayoutPx: number,
    oldBlockHeightLayoutPx: number,
    newBlockHeightLayoutPx: number,
): number {
    if (!(oldBlockHeightLayoutPx > 0) || !(newBlockHeightLayoutPx > 0))
        return clampInlineImageOffset(offsetLayoutPx);
    return clampInlineImageOffset(
        (offsetLayoutPx * newBlockHeightLayoutPx) / oldBlockHeightLayoutPx,
    );
}

/** Keeps a width within the range that leaves both the image and the text usable. */
export function clampInlineImageWidthPercent(percent: number): number {
    return Math.min(
        kMaxInlineImageWidthPercent,
        Math.max(kMinInlineImageWidthPercent, percent),
    );
}

/**
 * The width, as a percentage of the editable, that dragging a corner handle has reached. Only
 * the horizontal movement counts, because the wrapper's aspect ratio sets the height.
 * horizontalSign says which direction makes the image bigger for the corner being dragged (see
 * getInlineImageHandleHorizontalSign). Assumes a positive editableWidthViewportPx; startResize
 * does not begin a resize without one.
 */
export function computeInlineImageWidthPercent(
    startWidthViewportPx: number,
    deltaXViewportPx: number,
    horizontalSign: number,
    editableWidthViewportPx: number,
): number {
    const widthViewportPx =
        startWidthViewportPx + horizontalSign * deltaXViewportPx;
    const percent = (widthViewportPx / editableWidthViewportPx) * 100;
    // One decimal place is finer than a pixel in any block Bloom lays out. Rounding keeps the
    // style attribute short, and that attribute is saved and copied to every language's copy.
    return clampInlineImageWidthPercent(Math.round(percent * 10) / 10);
}

/**
 * Which direction makes the image bigger when this corner is dragged. Dragging a right-hand
 * corner to the right, or a left-hand corner to the left, makes it bigger, whichever dock the
 * image is in.
 */
export function getInlineImageHandleHorizontalSign(
    corner: InlineImageHandleCorner,
): number {
    return corner === "ne" || corner === "se" ? 1 : -1;
}

/** The dock an existing wrapper is in. */
export function getInlineImageDock(wrapper: HTMLElement): InlineImageDock {
    const found = kInlineImageDockClasses.find((dockClass) =>
        wrapper.classList.contains(dockClass),
    ) as InlineImageDock | undefined;
    // The fallback is the dock a new inline image gets (see makeInlineImageWrapper). Only
    // hand-edited HTML could give a wrapper no dock class at all.
    return found ?? kInlineImageRightClass;
}

/**
 * Selects this inline image. The wrapper gets kInlineImageSelectedClass, which gives it the
 * move cursor and tells inlineImageCanUndo that an inline image is what the user is working
 * on. The resize handles and the toolbar appear on it. Only one inline image can be selected
 * at a time, so this deselects any other first.
 */
export function selectInlineImage(wrapper: HTMLElement): void {
    deselectAllInlineImages(wrapper.ownerDocument);
    wrapper.classList.add(kInlineImageSelectedClass);
    addHandles(wrapper);
    showInlineImageContextControls(wrapper);
}

/**
 * Deselects every inline image in the document, removing the handles and the toolbar. Removing
 * kInlineImageSelectedClass matters for more than appearance, because the wrapper it is on is
 * saved with the book; see cleanupInlineImageInteractions.
 */
export function deselectAllInlineImages(doc: Document): void {
    Array.from(doc.querySelectorAll("." + kInlineImageSelectedClass)).forEach(
        (wrapper) => wrapper.classList.remove(kInlineImageSelectedClass),
    );
    Array.from(
        doc.querySelectorAll("." + kInlineImageHandleFrameClass),
    ).forEach((frame) => frame.remove());
    removeInlineImageContextControls(doc);
}

// How many times fitInlineImageOffsetsToBlock may reduce an offset. Each pass reduces it by
// exactly the amount the block now overflows by, so one pass is normally enough. The extra
// passes are for when reducing the offset rewraps the text beside the float and changes that
// amount again.
const kMaxOffsetFitPasses = 4;

/**
 * Works out every inline image's offset again for the size its block is now, and records the
 * new baseline. Call it when the page is set up, after each language's copy has been made to
 * match.
 *
 * The offset is a distance in pixels (see kInlineImageOffsetBasedOnAttr), and only a drag
 * writes it. A drag undoes any move that would leave the block with more text than it can show.
 * Without this function, nothing would change the offset when the block itself changes size.
 * Showing the book at a shorter page size would then cause the problem the drag prevents: the
 * picture stays the same distance below the start of the text, and the lines after it are
 * pushed off the end of the block. Bloom does not warn about it either, because it does not
 * count a block it has allowed to scroll as overflowing. With the picture near the bottom of
 * the text on an A5 Portrait page, changing the book to A5 Landscape pushed eleven lines off
 * the end.
 *
 * Only the editable the reader sees is laid out. The others are display:none and measure zero.
 * So this measures that one, and syncInlineImagesFromEditable copies the result to the rest.
 */
export function adjustInlineImageOffsetsIfBlockSizeChanged(
    container: HTMLElement,
): void {
    getTranslationGroupsWithInlineImages(container).forEach((group) => {
        const editable = getFirstVisibleEditable(group);
        if (!editable || !(editable.clientHeight > 0)) return;
        const wrappers = getInlineImagesInEditable(editable);
        const baselines = wrappers.map((wrapper) =>
            getInlineImageOffsetBaseline(wrapper),
        );
        // An image with no baseline was saved before Bloom recorded one. There is nothing to
        // scale its offset from, so it keeps the offset it has, and its current baseline is
        // recorded below.
        //
        // A change of width counts as a change, as a change of height does. Only the height is
        // used to scale the offset, because the offset is a vertical distance. But a narrower
        // block wraps the same text into more lines, so the text below the picture can now run
        // off the end. The baseline is recorded again below in every case, so a width change
        // not acted on here would become the new baseline and would never be noticed.
        const changed = wrappers.some(
            (wrapper, i) =>
                baselines[i] !== undefined &&
                (baselines[i]!.heightLayoutPx !== editable.clientHeight ||
                    baselines[i]!.widthLayoutPx !== editable.clientWidth),
        );
        wrappers.forEach((wrapper, i) => {
            const baseline = baselines[i];
            if (!baseline) return;
            setInlineImageOffset(
                wrapper,
                computeInlineImageOffsetForNewBlockHeight(
                    getInlineImageOffsetLayoutPx(wrapper),
                    baseline.heightLayoutPx,
                    editable.clientHeight,
                ),
            );
        });
        if (changed) fitInlineImageOffsetsToBlock(editable, wrappers);
        wrappers.forEach((wrapper) =>
            recordInlineImageOffsetBaseline(wrapper, editable),
        );
        if (changed) syncInlineImagesFromEditable(editable);
    });
}

// Reduces the offset of the lowest picture that has a positive offset, by the amount the block
// now overflows by. The offset is space above a picture, so the text after the lowest picture
// is what has gone off the end of the block. If the block still overflows with the offset at
// zero, it has more text than it can hold wherever the pictures are. That is for the user to
// fix, and a drag gives the same result when the largest offset it may use is zero.
function fitInlineImageOffsetsToBlock(
    editable: HTMLElement,
    wrappers: HTMLElement[],
): void {
    for (let pass = 0; pass < kMaxOffsetFitPasses; pass++) {
        const overflowLayoutPx = editable.scrollHeight - editable.clientHeight;
        if (overflowLayoutPx <= 0) return;
        const wrapper = [...wrappers]
            .reverse()
            .find((each) => getInlineImageOffsetLayoutPx(each) > 0);
        if (!wrapper) return;
        setInlineImageOffset(
            wrapper,
            clampInlineImageOffset(
                getInlineImageOffsetLayoutPx(wrapper) - overflowLayoutPx,
            ),
        );
    }
}

function setInlineImageOffset(
    wrapper: HTMLElement,
    offsetLayoutPx: number,
): void {
    wrapper.style.setProperty(kInlineImageOffsetVar, `${offsetLayoutPx}px`);
}

export function setupInlineImageInteractions(container: HTMLElement): void {
    // Setting up again ends any drag or resize, because its state may point at elements that
    // are no longer in the page.
    stopEdgeScrollTimer(dragState);
    releaseGesturePointer(resizeState ?? dragState);
    dragState = undefined;
    resizeState = undefined;
    const doc = container.ownerDocument;
    if (documentsWithInlineImageListeners.has(doc)) return;
    documentsWithInlineImageListeners.add(doc);
    // These listen in the capture phase. CKEditor manages the editables, and these events have
    // to reach this code even if a handler closer to the target stops them.
    doc.addEventListener("pointerdown", onPointerDown, true);
    doc.addEventListener("mousedown", onMouseDown, true);
    doc.addEventListener("focusin", onFocusIn);
    // This helps decide whether ctrl+z is for an inline image or for the text. The inline image
    // undo code compares the block's content with its snapshot, but that comparison misses an
    // edit that cancelled itself out, such as a word typed and then deleted, while CKEditor
    // holds undo points for both the typing and the deleting. So the typing itself is reported
    // with noteInlineImageBlockWasEdited. The "input" event fires for every way text arrives,
    // including paste and CKEditor's own commands, and it bubbles out of the editable.
    doc.addEventListener("input", onInput);
    // inlineImages.ts sends these two events when it changes wrappers itself, and after each
    // one the selection has to be updated. Both events bubble, so one listener on the document
    // covers the page.
    doc.addEventListener(kInlineImagesRestoredEvent, onInlineImagesRestored);
    doc.addEventListener(kInlineImageChangedEvent, onInlineImageChanged);
    // See aiImageEditingIsAvailable. It is fetched here because the menu is built
    // synchronously when the user right-clicks, long after this promise resolves.
    void getFeatureStatusAsync("AiImageEditing").then((status) => {
        aiImageEditingIsAvailable = status?.visible ?? false;
    });
}

function onInput(event: Event): void {
    const editable = (event.target as HTMLElement | null)?.closest(
        ".bloom-editable",
    ) as HTMLElement | null;
    if (editable) noteInlineImageBlockWasEdited(editable);
}

/**
 * Ends any drag or resize and deselects the inline images, before the page is saved (Cleanup
 * in bloomEditing.ts calls this). The handles are bloom-ui and would be removed anyway, but
 * kInlineImageSelectedClass is on the wrapper itself, which is saved, so it has to be removed
 * here.
 */
export function cleanupInlineImageInteractions(): void {
    stopEdgeScrollTimer(dragState);
    releaseGesturePointer(resizeState ?? dragState);
    dragState = undefined;
    resizeState = undefined;
    document.body.classList.remove(kInlineImageDraggingClass);
    deselectAllInlineImages(document);
}

// --- the commands ------------------------------------------------------------

// An existing inline image gets the standard image menu. It comes from the same "image" section
// of canvasControlRegistry that the canvas element menu uses, filtered by the same availability
// rules, so an image offers the same commands with the same wording wherever the user finds
// one. This configuration lists only the differences, which are the commands that cannot apply
// to an image inside a text block. "Expand image to fill space" needs no entry, because its
// usual rule already limits it to background images.
const inlineImageControlConfiguration: ICanvasElementControlConfiguration = {
    // An inline image is not a canvas element, but the commands do act on an image, and the
    // code that builds the menu does not look at the type.
    type: "image",
    menuSections: ["image", "imageArrangement"],
    // The same toolbar that an image on a canvas gets (imageCanvasElementControls), so that a
    // picture offers the same buttons wherever the user finds one. "expandToFillSpace" does not
    // need to be excluded, because its own rule already limits it to background images.
    toolbar: [
        "missingMetadata",
        "chooseImage",
        "pasteImage",
        "expandToFillSpace",
        "spacer",
        "delete",
    ],
    toolPanel: [],
    availabilityRules: {
        ...imageAvailabilityRules,
        // These two make the image the page's background image or the source of the book's
        // thumbnail. An image inside a text block cannot be either.
        becomeBackground: "exclude",
        imageFieldType: "exclude",
        // Duplicate copies a canvas element, and this is not one. To add another picture to the
        // block, the user chooses Insert Image from the text's right-click menu.
        duplicate: "exclude",
        // A quarter turn swaps the picture's width and height, and the way an inline image's
        // size and wrapping are calculated does not allow for that. Flip and Reset Image are
        // offered, and withInlineImageTransforms makes them act on this image's own picture.
        rotateRight: "exclude",
    },
};

// Whether the experimental "Edit with AI" feature is on. The availability rules need to know
// this synchronously when the user right-clicks, but the status comes from the C# side, so it
// is fetched when the page is set up and kept here.
let aiImageEditingIsAvailable = false;

/** What a menu item calls to close the menu it is on. See IControlRuntime.closeMenu. */
export type CloseMenuFunction = (launchingDialog?: boolean) => void;

/**
 * The commands to offer for what the user right-clicked, in the form TextContextMenu renders.
 * For an existing image this is the standard image menu (see inlineImageControlConfiguration
 * above) plus Delete. For a text block that can take inline images it is Insert Image. The
 * commands call closeMenu to close the menu they were chosen from. The default does nothing,
 * for tests that only look at the items.
 */
export function buildInlineImageMenuItems(
    target: InlineImageActionTarget,
    closeMenu: CloseMenuFunction = () => {},
): IMenuItemWithSubmenu[] {
    if (target.kind === "add") {
        return [
            {
                l10nId: "EditTab.InlineImage.InsertImage",
                english: "Insert Image",
                icon: React.createElement(InsertImageIcon, null),
                onClick: () => {
                    closeMenu();
                    addInlineImage(target.translationGroup);
                },
            },
        ];
    }
    if (target.kind === "existing") {
        const runtime: IControlRuntime = { closeMenu };
        const ctx: IControlContext = {
            ...buildCanvasElementControlRegistryContext(target.wrapper),
            aiImageEditingAvailable: aiImageEditingIsAvailable,
        };
        const imageSections = getMenuSections(
            inlineImageControlConfiguration,
            ctx,
            runtime,
        ).map((section) =>
            convertControlMenuRows(
                withInlineImageSync(
                    section
                        .map(
                            (item) =>
                                item.menuRow &&
                                withInlineImageTransforms(
                                    item.menuRow,
                                    item.control.id,
                                    target.wrapper,
                                ),
                        )
                        .filter((row): row is IControlMenuRow => !!row),
                    target,
                ),
                ctx,
                runtime,
            ),
        );
        return joinMenuSectionsWithSingleDividers([
            ...imageSections,
            [
                // The same words and icon as Delete on the canvas element menu, with a
                // different action. Deleting an inline image means removing this image's copy
                // from every language's editable, and the canvas element manager that the
                // registry's delete command calls knows nothing about that.
                {
                    l10nId: "Common.Delete",
                    english: "Delete",
                    icon: React.createElement(DeleteIcon, null),
                    onClick: () => {
                        closeMenu();
                        removeInlineImageCommand(target.wrapper);
                    },
                },
            ],
        ]);
    }
    return [];
}

// The registry's Flip and Reset Image act on the canvas element manager's active element, and
// an inline image is never that. This makes them act on the wrapper's own picture instead,
// using the same functions the canvas element manager calls. Like the other commands, they then
// go through withInlineImageSync, which records the undo point and copies the change to the
// other languages. Every other row is returned unchanged.
function withInlineImageTransforms(
    row: IControlMenuRow,
    controlId: string,
    wrapper: HTMLElement,
): IControlMenuRow {
    // The img is looked up when the command runs, because choosing a new picture may replace
    // it.
    const picture = () => wrapper.querySelector("img") as HTMLImageElement;
    switch (controlId) {
        case "flipImage":
            return {
                ...row,
                subMenuItems: row.subMenuItems!.map((subRow) => ({
                    ...subRow,
                    onSelect: () =>
                        flipImageContent(picture(), flipAxisOf(subRow)),
                })),
            };
        case "resetImage":
            return {
                ...row,
                onSelect: () => clearImageContentTransform(picture()),
            };
        default:
            return row;
    }
}

// Which way one of the Flip submenu's rows mirrors the picture.
function flipAxisOf(row: IControlMenuRow): FlipAxis {
    switch (row.l10nId) {
        case "EditTab.Image.FlipHorizontal":
            return "horizontal";
        case "EditTab.Image.FlipVertical":
            return "vertical";
        default:
            throw new Error(
                `Unexpected row in the Flip submenu: ${row.l10nId}`,
            );
    }
}

// The registry's commands were written for images in canvas elements, so they change only the
// img they are given, which for an inline image is one language's copy. Ending every command
// with syncInlineImagesFromEditable copies the change onto the other languages' copies, and
// then overflow is checked again, as at the end of a drag. Commands that change the picture
// itself go through changeImageInfo, which already copies it (see handleInlineImageChanged),
// so for them this repeats a copy of markup that has not changed, which does no harm. Commands
// that change the img where it is, such as the transparency submenu, rely on this alone.
function withInlineImageSync(
    rows: IControlMenuRow[],
    target: InlineImageActionTarget & { kind: "existing" },
): IControlMenuRow[] {
    return rows.map((row) => ({
        ...row,
        subMenuItems: row.subMenuItems
            ? withInlineImageSync(row.subMenuItems, target)
            : undefined,
        onSelect: async (rowCtx, rowRuntime) => {
            // The registry's commands know nothing about inline image undo, so the undo point
            // has to be recorded here. Otherwise the last undo point would still be whatever
            // put the picture there, and ctrl+z after making an image transparent would remove
            // the image. The undo point is prepared before the command and committed after it,
            // because the command may be asynchronous and may end up changing nothing.
            prepareInlineImageUndo(target.wrapper);
            await row.onSelect(rowCtx, rowRuntime);
            commitPendingInlineImageUndo(target.wrapper);
            syncInlineImagesFromEditable(target.editable);
            refreshOverflow(target.translationGroup);
        },
    }));
}

// Adds an inline image to the block, selected and holding a placeholder. It does not open the
// image chooser. The user often wants to move the image into place before picking a picture,
// and a dialog that opened by itself would get in the way of that. The picture is chosen
// later, with "Change image" on the image's menu. insertInlineImage records its own undo point
// and puts a copy in every editable of the group, so nothing has to be copied here.
function addInlineImage(translationGroup: HTMLElement): void {
    const wrapper = insertInlineImage(translationGroup);
    selectInlineImage(wrapper);
    refreshOverflow(translationGroup);
}

// removeInlineImage records its own undo point and removes this image's copy, found by its
// data-bloom-inline-image-id, from every language's editable. Other inline images stay where
// they are. Removing an image above another frees the space it took up, which would make the
// lower image jump upward, so this function moves the lower images back to where they were.
// (John: moving one image must not move the others.)
function removeInlineImageCommand(wrapper: HTMLElement): void {
    const translationGroup = wrapper.closest(
        ".bloom-translationGroup",
    ) as HTMLElement;
    const editable = wrapper.closest(".bloom-editable") as HTMLElement;
    const survivors = getFloatingWrappersIn(editable).filter(
        (other) => other !== wrapper,
    );
    const keptTops = new Map(
        survivors.map((other) => [other, getImageBox(other).top]),
    );
    removeInlineImage(wrapper);
    // The toolbar is a div on the body (see kInlineImageContextControlsId), so removing the
    // picture does not remove it. Without this it would stay on screen under a picture that is
    // gone, offering commands for it. Undoing the delete does not need the image to be
    // selected, because inlineImageCanUndo recognizes a deleted image from where the caret is.
    deselectAllInlineImages(editable.ownerDocument);
    if (editable.getBoundingClientRect().height > 0) {
        const viewportPxPerLayoutPx =
            getViewportPxPerLayoutPxOfEditable(editable);
        for (let pass = 0; pass < 2; pass++) {
            survivors.forEach((other) => {
                if (!other.isConnected) return;
                const wanted = keptTops.get(other);
                if (wanted === undefined) return;
                const current = getImageBox(other).top;
                if (Math.abs(wanted - current) <= 1) return;
                nudgeInlineImageOffset(
                    other,
                    wanted - current,
                    viewportPxPerLayoutPx,
                );
            });
        }
        // The offsets above were corrected in this editable, so copy them to the other
        // languages.
        syncInlineImagesFromEditable(editable);
    }
    refreshOverflow(translationGroup);
}

// --- the toolbar -------------------------------------------------------------

// The toolbar under the selected picture is the same component a canvas element uses
// (CanvasElementContextControls), given this module's control configuration and menu, so the
// buttons, their icons and their wording are the same as for an image anywhere else in Bloom.
//
// It is rendered into its own div on the body. The body is outside the bloom-page, so the div
// is never saved, and the cleanup before saving never sees it. The canvas element's toolbar has
// its own div in the same place. They have different ids because different code shows and
// removes each one, and if one removed the other's, the bug would be hard to see.
export const kInlineImageContextControlsId = "inline-image-context-controls";

// How far below the picture the toolbar sits, the same as for a canvas element.
const kInlineImageContextControlsGapLayoutPx = 11;

// The toolbar is centered in a box this wide, which is wider than the toolbar ever is. The
// canvas element's toolbar is centered the same way.
const kInlineImageContextControlsBoxWidthLayoutPx = 300;

// Set on the toolbar while a drag or resize is in progress, so it does not follow the picture
// around. The canvas element's toolbar uses the same class name, and the same rule in
// editMode.less applies to both.
const kMovingClass = "moving";

/**
 * Shows the toolbar under this picture, or moves it there if it is already showing. This is
 * called whenever an inline image is selected, which is when the user expects the buttons:
 * right after Insert Image, and when the picture is clicked.
 */
export function showInlineImageContextControls(wrapper: HTMLElement): void {
    const doc = wrapper.ownerDocument;
    let root = doc.getElementById(kInlineImageContextControlsId);
    if (!root) {
        root = doc.createElement("div");
        root.setAttribute("id", kInlineImageContextControlsId);
        doc.body.appendChild(root);
    }
    renderInlineImageContextControls(wrapper, false);
}

// Renders the toolbar with its menu open or closed. The "..." button opens its menu this way:
// the component asks its parent to render it again with the menu in the state it wants.
function renderInlineImageContextControls(
    wrapper: HTMLElement,
    menuOpen: boolean,
): void {
    const root = wrapper.ownerDocument.getElementById(
        kInlineImageContextControlsId,
    );
    if (!root) return;
    renderRoot(
        React.createElement(CanvasElementContextControls, {
            canvasElement: wrapper,
            menuOpen,
            setMenuOpen: (open: boolean) =>
                renderInlineImageContextControls(wrapper, open),
            controlsForNonCanvasObject: buildInlineImageControls(wrapper),
        }),
        root,
    );
    positionInlineImageContextControls(wrapper);
}

// What CanvasElementContextControls needs in order to work on an inline image instead of a
// canvas element: which controls to offer, the same menu that right-clicking the picture
// gives, how to delete the picture, and the copying to the other languages that every command
// has to end with.
function buildInlineImageControls(
    wrapper: HTMLElement,
): IControlsForNonCanvasObject {
    const target = getInlineImageActionTarget(wrapper);
    return {
        configuration: inlineImageControlConfiguration,
        menuItems: buildInlineImageMenuItems(target, () =>
            renderInlineImageContextControls(wrapper, false),
        ),
        contextAdditions: {
            aiImageEditingAvailable: aiImageEditingIsAvailable,
            deleteThisObject: () => removeInlineImageCommand(wrapper),
        },
        afterToolbarCommand: () => {
            if (target.kind !== "existing") return;
            syncInlineImagesFromEditable(target.editable);
            refreshOverflow(target.translationGroup);
            // A command can change the picture's shape, so the toolbar has to be moved back
            // under it. A command can also delete the picture, and then there is nothing to
            // move it under.
            if (wrapper.isConnected)
                positionInlineImageContextControls(wrapper);
        },
    };
}

/**
 * Centers the toolbar under the picture. The toolbar is not inside the scaled page, so it is
 * given the page's transform. Without that it would be drawn at 100% over a page drawn at some
 * other zoom.
 */
export function positionInlineImageContextControls(wrapper: HTMLElement): void {
    const doc = wrapper.ownerDocument;
    const root = doc.getElementById(kInlineImageContextControlsId);
    if (!root) return;
    const scalingContainer = doc.getElementById("page-scaling-container");
    root.style.transform = scalingContainer?.style.transform ?? "";
    const image = getImageBox(wrapper);
    const editable = wrapper.closest(".bloom-editable") as HTMLElement | null;
    const viewportPxPerLayoutPx = editable
        ? getViewportPxPerLayoutPxOfEditable(editable)
        : 1;
    root.style.left =
        image.left +
        doc.defaultView!.scrollX +
        image.width / 2 -
        (kInlineImageContextControlsBoxWidthLayoutPx / 2) *
            viewportPxPerLayoutPx +
        "px";
    root.style.top =
        image.top +
        doc.defaultView!.scrollY +
        image.height +
        kInlineImageContextControlsGapLayoutPx +
        "px";
    root.style.width = kInlineImageContextControlsBoxWidthLayoutPx + "px";
}

/** Removes the toolbar. deselectAllInlineImages calls this. */
export function removeInlineImageContextControls(doc: Document): void {
    doc.getElementById(kInlineImageContextControlsId)?.remove();
}

// Hides the toolbar while a drag or resize is in progress, and shows it again afterward under
// wherever the picture ended up. A toolbar that moved along with a drag would get in the way.
function setInlineImageContextControlsMoving(
    doc: Document,
    moving: boolean,
): void {
    doc.getElementById(kInlineImageContextControlsId)?.classList.toggle(
        kMovingClass,
        moving,
    );
}

// --- the right-click menu, and the selection when inlineImages.ts replaces wrappers ---

/**
 * The inline image commands for the text block's right-click menu: the commands for what was
 * clicked, or an empty array if there are none. TextContextMenu calls this for every
 * right-click it handles, and an empty array tells it there are no inline image commands.
 *
 * When the click is on an existing image, this also selects it, on purpose. Selecting it shows
 * the user what the commands will act on, and inlineImageCanUndo looks for a selected image
 * before it sends ctrl+z to the inline image undo code. That is why the name ends in "ForClick": call
 * it once for each right-click, and not each time the menu renders.
 */
export function getInlineImageMenuItemsForClick(
    clickedElement: HTMLElement | undefined | null,
    closeMenu: CloseMenuFunction = () => {},
): IMenuItemWithSubmenu[] {
    const target = getInlineImageActionTarget(clickedElement);
    if (target.kind === "existing") selectInlineImage(target.wrapper);
    // A right-click anywhere else is not about a picture, and a picture left selected would
    // suggest the commands on the menu apply to it. A selected picture would also make ctrl+z
    // go to the inline image undo code when the user means to undo in the text they just
    // clicked in.
    else if (clickedElement)
        deselectAllInlineImages(clickedElement.ownerDocument);
    return buildInlineImageMenuItems(target, closeMenu);
}

// Undo replaces every wrapper in the group with a new element built from saved markup, which
// has no bloom-ui children. inlineImages.ts puts kInlineImageSelectedClass back on the
// restored wrapper in the editable that had it, and that wrapper needs its handles and toolbar
// set up again. The handles cannot have survived. The toolbar is a div on the body that was
// built for the element undo has just replaced, and still refers to it. If it were left alone
// it would offer Choose image, Copy image and Delete for an element that is no longer in the
// document, and it would stay where the old picture used to be.
//
// When no wrapper comes back selected, the toolbar has to be removed. That is what happens
// when an insert is undone: undo has removed the picture the toolbar belonged to.
function onInlineImagesRestored(event: Event): void {
    const translationGroup = event.target as HTMLElement | null;
    if (!translationGroup) return;
    const selected = translationGroup.querySelector(
        "." + kInlineImageClass + "." + kInlineImageSelectedClass,
    ) as HTMLElement | null;
    if (selected) selectInlineImage(selected);
    else deselectAllInlineImages(translationGroup.ownerDocument);
}

// A new picture has been put in an inline image. Choosing it in the image chooser can leave
// the focus back in the text, which deselects the image, and the change (and the insert before
// it, if any) can only be undone while the image is selected. So this selects it again. A
// change of picture keeps the same wrapper element, so this is the one the user chose a
// picture for.
function onInlineImageChanged(event: Event): void {
    const wrapper = (event as CustomEvent).detail as HTMLElement | undefined;
    if (wrapper) selectInlineImage(wrapper);
}

// --- selection and gestures --------------------------------------------------

interface IInlineImageDragState {
    wrapper: HTMLElement;
    editable: HTMLElement;
    // Measured once, when the drag starts. Switching to the bottom dock lays out the block
    // again, and the drag would be unusable if the dock boundaries moved during it. This is
    // the box that holds the block's content (computeBlockContentBox), which can be bigger than
    // the block's rectangle, so that the image can still be dragged into the part of an
    // overflowing block's text that is scrolled out of sight.
    editableContentBoxViewportPx: IBox;
    // Viewport pixels per layout pixel; see computeViewportPxPerLayoutPx.
    viewportPxPerLayoutPx: number;
    // Where the image's center was in relation to the pointer when the drag began. The dock is
    // chosen from where the image is, and not from where the cursor is. Otherwise grabbing a
    // wide image near one edge would change its dock before it had moved at all.
    grabOffsetXViewportPx: number;
    grabOffsetYViewportPx: number;
    startXViewportPx: number;
    startYViewportPx: number;
    startOffsetLayoutPx: number;
    dock: InlineImageDock;
    started: boolean;
    // The top of every other floating image in the block when the drag began. Moving one
    // image must not move the others, so after every move of the drag they are put back at
    // these positions.
    neighborImageTopsViewportPx: Map<HTMLElement, number>;
    // How far the block overflowed before the drag began. At the end of each move,
    // shouldRevertInlineImageMove checks whether the move added overflow to the whole block,
    // because the dragged image can fit while pushing another image out. Floats cannot
    // overlap, so a full-width band dragged to another image's level pushes that image down.
    startScrollOverflowLayoutPx: number;
    // The last position of the pointer, so that the timer started by startEdgeScrollTimer can
    // apply the user's move again when there is no new pointer event.
    lastPointViewportPx: { x: number; y: number };
    edgeScrollTimerId: number | undefined;
    // The pointer this gesture captured; see captureGesturePointer.
    pointerId: number;
}

interface IInlineImageResizeState {
    wrapper: HTMLElement;
    editable: HTMLElement;
    startXViewportPx: number;
    startWidthViewportPx: number;
    editableWidthViewportPx: number;
    horizontalSign: number;
    started: boolean;
    // The pointer this gesture captured; see captureGesturePointer.
    pointerId: number;
}

let dragState: IInlineImageDragState | undefined;
let resizeState: IInlineImageResizeState | undefined;
let documentWithPointerListeners: Document | undefined;
const documentsWithInlineImageListeners = new WeakSet<Document>();

function onPointerDown(event: PointerEvent): void {
    if (event.button !== 0) return; // TextContextMenu handles the right button
    const target = event.target as HTMLElement | null;
    if (!target) return;
    // A press outside the page, such as in the image's menu or toolbar or in the other
    // editing controls around the page, must not deselect the image. Otherwise choosing a
    // command would deselect the image the command is for.
    if (!target.closest(".bloom-page")) return;
    const handle = target.closest(
        "." + kInlineImageHandleClass,
    ) as HTMLElement | null;
    if (handle) {
        startResize(event, handle);
        return;
    }
    const wrapper = target.closest(
        "." + kInlineImageClass,
    ) as HTMLElement | null;
    if (!wrapper) {
        deselectAllInlineImages(target.ownerDocument);
        return;
    }
    const editable = wrapper.closest(".bloom-editable") as HTMLElement | null;
    if (!editable) return;
    selectInlineImage(wrapper);
    startDrag(event, wrapper, editable);
}

// Clicking the image selects it. The click must not also put the caret in the text or start
// selecting text, which is what the browser does with a press inside a contenteditable.
// Cancelling pointerdown does not reliably stop that, so this cancels mousedown as well.
function onMouseDown(event: MouseEvent): void {
    if (event.button !== 0) return;
    const target = event.target as HTMLElement | null;
    if (target?.closest("." + kInlineImageClass)) event.preventDefault();
}

// When the caret goes back into the text, the image is deselected. Focus moving into the
// image's menu, which is outside the page, does not deselect it.
function onFocusIn(event: FocusEvent): void {
    const target = event.target as HTMLElement | null;
    if (!target?.closest(".bloom-page")) return;
    if (target.closest("." + kInlineImageClass)) return;
    // Clicking the wrapper, which is contenteditable=false, always moves keyboard focus to the
    // editable that contains it, just after onPointerDown has selected the image. That focus
    // change comes from the click that selected the image, and the caret has not gone back to
    // the text, so it must not deselect the image. A press on the text itself deselects in
    // onPointerDown. Without this check, the first click on an image always deselected it
    // again and only a second click kept it selected (checked in WebView2 over CDP).
    const selected = target.ownerDocument.querySelector(
        "." + kInlineImageSelectedClass,
    );
    if (selected && target.contains(selected)) return;
    deselectAllInlineImages(target.ownerDocument);
}

/**
 * Sends every event from this pointer to the wrapper until the button is released, wherever
 * the pointer goes in the meantime.
 *
 * A drag or resize has to end even when the button is released somewhere this module's
 * listeners cannot see. addPointerListeners puts them on the page's document, so a pointerup
 * delivered to another document (the toolbox iframe), to other parts of Bloom's window, or
 * outside the window never reaches onPointerEnd. Then the drag never ends. The timer started
 * by startEdgeScrollTimer keeps moving the picture to a pointer position that is no longer
 * updated, the body keeps kInlineImageDraggingClass so text cannot be selected, the toolbar
 * stays hidden, and the change is never copied to the other languages or given an undo point.
 * Capturing the pointer makes the browser deliver that pointerup here wherever it happens.
 *
 * No other drag in Bloom crosses from one document to another, which is why nothing else
 * captures the pointer.
 */
function captureGesturePointer(wrapper: HTMLElement, pointerId: number): void {
    wrapper.setPointerCapture(pointerId);
}

/**
 * Releases the pointer capture. Every path that ends a drag or resize calls this, including
 * the two that discard the state without a pointerup (setting up the page again, and the
 * cleanup before saving). A wrapper that kept the capture would go on receiving all of the
 * pointer's events.
 */
function releaseGesturePointer(
    state: { wrapper: HTMLElement; pointerId: number } | undefined,
): void {
    if (!state) return;
    if (state.wrapper.hasPointerCapture(state.pointerId))
        state.wrapper.releasePointerCapture(state.pointerId);
}

function startDrag(
    event: PointerEvent,
    wrapper: HTMLElement,
    editable: HTMLElement,
): void {
    const imageBox = getImageBox(wrapper);
    const visibleBoxViewportPx = getBox(editable);
    dragState = {
        wrapper,
        editable,
        editableContentBoxViewportPx: computeBlockContentBox(
            visibleBoxViewportPx,
            editable.clientHeight,
            editable.scrollHeight,
            editable.scrollTop,
        ),
        viewportPxPerLayoutPx: computeViewportPxPerLayoutPx(
            visibleBoxViewportPx,
            editable.clientHeight,
        ),
        grabOffsetXViewportPx:
            imageBox.left + imageBox.width / 2 - event.clientX,
        grabOffsetYViewportPx:
            imageBox.top + imageBox.height / 2 - event.clientY,
        startXViewportPx: event.clientX,
        startYViewportPx: event.clientY,
        startOffsetLayoutPx: getInlineImageOffsetLayoutPx(wrapper),
        dock: getInlineImageDock(wrapper),
        started: false,
        neighborImageTopsViewportPx: new Map(
            getFloatingWrappersIn(editable)
                .filter((other) => other !== wrapper)
                .map((other) => [other, getImageBox(other).top]),
        ),
        startScrollOverflowLayoutPx:
            editable.scrollHeight - editable.clientHeight,
        lastPointViewportPx: { x: event.clientX, y: event.clientY },
        edgeScrollTimerId: undefined,
        pointerId: event.pointerId,
    };
    addPointerListeners(editable.ownerDocument);
    captureGesturePointer(wrapper, event.pointerId);
    startEdgeScrollTimer(dragState);
}

// How often the block scrolls further while the picture is held against one of its edges.
const kEdgeScrollIntervalMs = 50;

// Applies the user's current move again at intervals, so that holding the pointer still
// against an edge keeps moving the picture through the text. No pointer events arrive while
// the pointer is still, and a drag that acted only on movement would stop as soon as the user
// held the mouse still, which is what they do at an edge.
//
// It applies the whole move again. Scrolling only when the picture has left the screen does
// not work: the picture leaves the screen only because the offset moved it there, and the
// offset changes only when the move is applied, so neither would ever happen. A picture dragged
// to the top edge then stayed a line and a half below the start of the text, and nothing could
// move it. Applying the move again does no harm when nothing has to change: the offset is
// calculated from where the picture should end up, so the same pointer position gives the
// position the picture is already in.
function startEdgeScrollTimer(state: IInlineImageDragState): void {
    const view = state.editable.ownerDocument.defaultView;
    if (!view) return;
    state.edgeScrollTimerId = view.setInterval(() => {
        if (dragState !== state || !state.started) return;
        continueDrag(state, state.lastPointViewportPx);
    }, kEdgeScrollIntervalMs);
}

/**
 * Stops the interval that startEdgeScrollTimer started for a drag that is over. Every place
 * that discards dragState has to call this. The interval holds its own reference to the state,
 * and would otherwise go on applying the move from a pointer position that is no longer
 * updated, to elements that a page reload may have replaced.
 */
function stopEdgeScrollTimer(state: IInlineImageDragState | undefined): void {
    if (state?.edgeScrollTimerId === undefined) return;
    state.editable.ownerDocument.defaultView?.clearInterval(
        state.edgeScrollTimerId,
    );
    state.edgeScrollTimerId = undefined;
}

function continueDrag(
    state: IInlineImageDragState,
    pointViewportPx: { x: number; y: number },
): void {
    state.lastPointViewportPx = pointViewportPx;
    if (
        !beginGestureIfMoved(
            state,
            Math.abs(pointViewportPx.x - state.startXViewportPx),
            Math.abs(pointViewportPx.y - state.startYViewportPx),
        )
    )
        return;
    // Remember how things were at the start of this move, when everything fitted inside the
    // block. Each move either ends with everything fitting or is undone back to this, so every
    // move starts from an arrangement that fits. The next sibling records where the wrapper is
    // among the floating wrappers. It does not change during a drag, because only the dragged
    // wrapper moves in the DOM.
    const previousDock = state.dock;
    const previousOffsetLayoutPx = getInlineImageOffsetLayoutPx(state.wrapper);
    const previousNextSibling = state.wrapper.nextElementSibling;
    const imageCenterViewportPx = {
        x: pointViewportPx.x + state.grabOffsetXViewportPx,
        y: pointViewportPx.y + state.grabOffsetYViewportPx,
    };
    const dock = computeInlineImageDock(
        imageCenterViewportPx,
        state.editableContentBoxViewportPx,
        getImageBox(state.wrapper).height,
    );
    // jsdom reports every box as empty. In that case this uses the simple calculation from the
    // pointer's movement that the drag tests check, and skips the measurements that need a
    // real layout.
    const degenerate = !(state.editableContentBoxViewportPx.height > 0);
    const blockBottomViewportPx =
        state.editableContentBoxViewportPx.top +
        state.editableContentBoxViewportPx.height;
    if (dock !== previousDock) {
        // setInlineImageDock also moves the wrapper between the start and the end of the
        // editable, which is the only difference in the DOM between the bottom dock and the
        // others.
        setInlineImageDock(state.wrapper, dock);
        state.dock = dock;
    }
    if (dock === kInlineImageBottomClass) {
        // The bottom dock is in normal flow at the end of the block, so it uses no offset. The
        // value stays in the style attribute, ready for when the image is dragged back up.
    } else {
        // Where the top of the image should end up, from the pointer and the grab offsets.
        const targetTopViewportPx =
            imageCenterViewportPx.y - getImageBox(state.wrapper).height / 2;
        // The DOM order of the floating wrappers is the images' order from top to bottom,
        // because each float starts below the earlier ones it has to clear. So dragging an
        // image above another one has to change their order. That frees the space above an
        // image whose offset padding would otherwise fill its side of the block from the top,
        // and it lets several images share one side.
        if (!degenerate) reorderInFloatingCluster(state, targetTopViewportPx);
        // Changing the dock or the order changes where the other images start, so put them
        // back where they were when the drag began before measuring anything for this wrapper.
        if (!degenerate) restoreNeighborImagePositions(state);
        // The maximum keeps the whole wrapper, offset padding and image, inside the block. An
        // offset that pushes the image past the bottom makes the block scroll. The offset is
        // measured from where the float would start without it, below any earlier float it
        // clears, so the room left is measured from the bottom of the wrapper as it is drawn
        // now. The distance from there to the block's bottom is how much more the current
        // offset may grow.
        let maxLayoutPx: number | undefined;
        const currentOffsetLayoutPx = getInlineImageOffsetLayoutPx(
            state.wrapper,
        );
        if (!degenerate) {
            const wrapperBottomViewportPx =
                state.wrapper.getBoundingClientRect().bottom;
            maxLayoutPx =
                currentOffsetLayoutPx +
                (blockBottomViewportPx - wrapperBottomViewportPx) /
                    state.viewportPxPerLayoutPx;
        }
        // In a real layout the offset is calculated from where the image's top should be and
        // where it is now, which stays correct when the order changes or the text rewraps.
        // When nothing is laid out, this adds a distance in viewport pixels to an offset in
        // layout pixels without dividing by the scale. That is correct only because nothing is
        // scaled in jsdom, where one viewport pixel is one layout pixel.
        const offsetLayoutPx = clampInlineImageOffset(
            degenerate
                ? state.startOffsetLayoutPx +
                      (pointViewportPx.y - state.startYViewportPx)
                : currentOffsetLayoutPx +
                      (targetTopViewportPx - getImageBox(state.wrapper).top) /
                          state.viewportPxPerLayoutPx,
            maxLayoutPx,
        );
        state.wrapper.style.setProperty(
            kInlineImageOffsetVar,
            `${offsetLayoutPx}px`,
        );
        if (!degenerate) {
            // The maximum above was measured before this move's offset was applied, so a fast
            // move can go a few pixels too far. Take back whatever is over.
            const overViewportPx =
                state.wrapper.getBoundingClientRect().bottom -
                blockBottomViewportPx;
            if (overViewportPx > 0) {
                state.wrapper.style.setProperty(
                    kInlineImageOffsetVar,
                    `${clampInlineImageOffset(offsetLayoutPx - overViewportPx / state.viewportPxPerLayoutPx)}px`,
                );
            }
        }
    }
    if (degenerate) return;
    // Whatever this move did, the other images stay exactly where the user put them. This is
    // done after every move, as well as at the end (John).
    restoreNeighborImagePositions(state);
    // With the other images back in place, check whether the whole block still fits its
    // text. This checks the whole block, because a move can leave the dragged image fitting
    // while pushing another image out. If the move added overflow, it went somewhere with no
    // room: a full side, the bottom dock of a full block, or a band at the level of another
    // image. Then the whole move is undone (the dock, the position among the floating
    // wrappers, and the offset), going back to the arrangement at the start of the move, which
    // fitted. Nothing may hang below the block, where it makes the text scroll and cannot even
    // be clicked.
    if (
        shouldRevertInlineImageMove(
            state.startScrollOverflowLayoutPx,
            state.editable.scrollHeight - state.editable.clientHeight,
        )
    ) {
        state.editable.insertBefore(state.wrapper, previousNextSibling);
        setInlineImageDock(state.wrapper, previousDock);
        state.wrapper.style.setProperty(
            kInlineImageOffsetVar,
            `${previousOffsetLayoutPx}px`,
        );
        state.dock = previousDock;
        restoreNeighborImagePositions(state);
    }
    scrollBlockToKeepDraggedImageInView(state);
}

// Scrolls the block so that the picture being dragged stays on the screen, and moves the
// positions the drag remembered by however far the block scrolled.
function scrollBlockToKeepDraggedImageInView(
    state: IInlineImageDragState,
): void {
    const wantedLayoutPx = computeInlineImageDragScrollLayoutPx(
        getImageBox(state.wrapper),
        getBox(state.editable),
        state.viewportPxPerLayoutPx,
    );
    if (wantedLayoutPx === 0) return;
    const beforeLayoutPx = state.editable.scrollTop;
    state.editable.scrollTop = beforeLayoutPx + wantedLayoutPx;
    const movedLayoutPx = state.editable.scrollTop - beforeLayoutPx;
    if (movedLayoutPx === 0) return; // the text is already at that end
    // The drag measured its positions on the screen: where the block's content begins and
    // ends, and where each of the other images was. The content has just scrolled, so those
    // positions have to move by the same amount. Otherwise the dock would be chosen against
    // the block's old position and the other images would be put back in the wrong places.
    // The offset is measured within the content, so scrolling does not change it. The next
    // move increases it to bring the picture back to the pointer, which is how holding the
    // picture at the edge keeps moving it down the text.
    const movedViewportPx = movedLayoutPx * state.viewportPxPerLayoutPx;
    state.editableContentBoxViewportPx = {
        ...state.editableContentBoxViewportPx,
        top: state.editableContentBoxViewportPx.top - movedViewportPx,
    };
    for (const [wrapper, topViewportPx] of state.neighborImageTopsViewportPx)
        state.neighborImageTopsViewportPx.set(
            wrapper,
            topViewportPx - movedViewportPx,
        );
}

/**
 * Where, among the floating wrappers, an image whose top will be at targetTopViewportPx
 * belongs: after every image whose top is at or above that. Exported for tests.
 */
export function computeInlineImageClusterIndex(
    targetTopViewportPx: number,
    otherImageTopsViewportPx: number[],
): number {
    return otherImageTopsViewportPx.filter((top) => top <= targetTopViewportPx)
        .length;
}

// The inline images of this editable that are not in the bottom dock, in DOM order. That is
// also their order from top to bottom, since each one starts below the earlier floats it has
// to clear.
function getFloatingWrappersIn(editable: HTMLElement): HTMLElement[] {
    return Array.from(
        editable.querySelectorAll(
            `:scope > .${kInlineImageClass}:not(.${kInlineImageBottomClass})`,
        ),
    ) as HTMLElement[];
}

// Changes an image's offset so that the image moves deltaViewportPx from where it is now, but
// not above where it would start with no offset. The distance is measured on the screen, so it
// is divided by the page's scale to get the layout pixels the offset is written in.
function nudgeInlineImageOffset(
    wrapper: HTMLElement,
    deltaViewportPx: number,
    viewportPxPerLayoutPx: number,
): void {
    wrapper.style.setProperty(
        kInlineImageOffsetVar,
        `${clampInlineImageOffset(getInlineImageOffsetLayoutPx(wrapper) + deltaViewportPx / viewportPxPerLayoutPx)}px`,
    );
}

// The page's scale, measured on the block an image is in. This is for callers that do not
// have drag state. A drag measures the scale once and keeps it.
function getViewportPxPerLayoutPxOfEditable(editable: HTMLElement): number {
    return computeViewportPxPerLayoutPx(
        getBox(editable),
        editable.clientHeight,
    );
}

// Moves the dragged wrapper to the place among the floating wrappers that its target height
// calls for. This only moves it in the DOM. The other images this pushes around are put back
// in place by restoreNeighborImagePositions, which runs on every move of the drag.
function reorderInFloatingCluster(
    state: IInlineImageDragState,
    targetTopViewportPx: number,
): void {
    const floats = getFloatingWrappersIn(state.editable);
    if (floats.length < 2) return;
    const currentIndex = floats.indexOf(state.wrapper);
    if (currentIndex < 0) return;
    const others = floats.filter((w) => w !== state.wrapper);
    const desiredIndex = computeInlineImageClusterIndex(
        targetTopViewportPx,
        others.map((other) => getImageBox(other).top),
    );
    if (desiredIndex === currentIndex) return;
    state.editable.insertBefore(
        state.wrapper,
        others[desiredIndex] ??
            others[others.length - 1].nextElementSibling ??
            null,
    );
}

function startResize(event: PointerEvent, handle: HTMLElement): void {
    const wrapper = handle.closest(
        "." + kInlineImageClass,
    ) as HTMLElement | null;
    const editable = wrapper?.closest(".bloom-editable") as HTMLElement | null;
    if (!wrapper || !editable) return;
    // This has to be in viewport pixels, like the image width and the pointer movement it is
    // compared with. clientWidth is in layout pixels, which differ from viewport pixels
    // whenever the page is zoomed, and mixing the two made every resize wrong by the zoom.
    const editableWidthViewportPx =
        editable.clientWidth *
        computeViewportPxPerLayoutPx(getBox(editable), editable.clientHeight);
    // With no width to take a percentage of, a resize would write a meaningless number.
    if (editableWidthViewportPx <= 0) return;
    const corner = (handle.getAttribute(kInlineImageCornerAttribute) ??
        "se") as InlineImageHandleCorner;
    resizeState = {
        wrapper,
        editable,
        startXViewportPx: event.clientX,
        startWidthViewportPx: getImageBox(wrapper).width,
        editableWidthViewportPx,
        horizontalSign: getInlineImageHandleHorizontalSign(corner),
        started: false,
        pointerId: event.pointerId,
    };
    // Grabbing a handle is not the start of a text selection.
    event.preventDefault();
    addPointerListeners(editable.ownerDocument);
    captureGesturePointer(wrapper, event.pointerId);
}

function continueResize(
    state: IInlineImageResizeState,
    event: PointerEvent,
): void {
    if (
        !beginGestureIfMoved(
            state,
            Math.abs(event.clientX - state.startXViewportPx),
            0,
        )
    )
        return;
    const percent = computeInlineImageWidthPercent(
        state.startWidthViewportPx,
        event.clientX - state.startXViewportPx,
        state.horizontalSign,
        state.editableWidthViewportPx,
    );
    state.wrapper.style.setProperty(kInlineImageWidthVar, `${percent}%`);
}

// A press that has not moved far enough is still a click, so nothing changes and no undo point
// is recorded until it has. The undo point is prepared here, and onPointerEnd commits it
// through commitInlineImageChange, since even a real drag may end up changing nothing. Returns
// whether the caller should go on to apply this move.
function beginGestureIfMoved(
    state: { wrapper: HTMLElement; started: boolean },
    absDeltaX: number,
    absDeltaY: number,
): boolean {
    if (state.started) return true;
    if (
        absDeltaX < kDragThresholdViewportPx &&
        absDeltaY < kDragThresholdViewportPx
    )
        return false;
    state.started = true;
    prepareInlineImageUndo(state.wrapper);
    state.wrapper.ownerDocument.body.classList.add(kInlineImageDraggingClass);
    setInlineImageContextControlsMoving(state.wrapper.ownerDocument, true);
    return true;
}

// The listeners are on the document, so they see every pointer, and the browser still
// delivers a second pointer's events while the first one holds its capture. Without this
// check, a second finger on a touch screen, or a brush of a trackpad during a mouse drag,
// would move the picture to wherever that pointer is and end the drag there.
function isThisGesturesPointer(event: PointerEvent): boolean {
    const state = resizeState ?? dragState;
    return !!state && state.pointerId === event.pointerId;
}

function onPointerMove(event: PointerEvent): void {
    if (!isThisGesturesPointer(event)) return;
    if (resizeState) continueResize(resizeState, event);
    else if (dragState)
        continueDrag(dragState, { x: event.clientX, y: event.clientY });
}

// A drag and a resize end the same way, even when the browser cancels the pointer. Whatever
// was applied to the wrapper is on the screen, so it has to be copied to the other languages
// and be undoable.
function onPointerEnd(event: PointerEvent): void {
    if (!isThisGesturesPointer(event)) return;
    const drag = dragState;
    const state = resizeState ?? dragState;
    resizeState = undefined;
    dragState = undefined;
    stopEdgeScrollTimer(drag);
    releaseGesturePointer(state);
    removePointerListeners();
    if (!state) return;
    state.wrapper.ownerDocument.body.classList.remove(
        kInlineImageDraggingClass,
    );
    setInlineImageContextControlsMoving(state.wrapper.ownerDocument, false);
    if (!state.started) return; // a click changes nothing and prepares no undo point
    if (drag && drag === state) {
        restoreNeighborImagePositions(drag);
        normalizeFloatingClusterOrder(
            drag.editable,
            drag.editableContentBoxViewportPx,
            drag.viewportPxPerLayoutPx,
        );
    }
    commitInlineImageChange(state.wrapper, state.editable);
    // The picture has moved, so move the toolbar under it.
    positionInlineImageContextControls(state.wrapper);
}

// Puts the floating wrappers in the DOM in the same order as the images appear from top to
// bottom, keeping every image exactly where it is. Each float clears the floats before it in
// the DOM, so an order that differs from the order on the screen limits where the images can
// go, without the user being able to see why. Such an order can come from an older book or
// from inserting images. The user is rearranging things during a drag, so the end of a drag is
// the time to fix the order.
function normalizeFloatingClusterOrder(
    editable: HTMLElement,
    editableContentBoxViewportPx: IBox,
    viewportPxPerLayoutPx: number,
): void {
    if (!(editableContentBoxViewportPx.height > 0)) return; // no real layout to measure (jsdom)
    const floats = getFloatingWrappersIn(editable);
    if (floats.length < 2) return;
    const wantedTops = new Map(floats.map((w) => [w, getImageBox(w).top]));
    const sorted = [...floats].sort(
        (a, b) => wantedTops.get(a)! - wantedTops.get(b)!,
    );
    if (sorted.every((w, i) => w === floats[i])) return;
    const anchor = floats[floats.length - 1].nextElementSibling;
    sorted.forEach((w) => editable.insertBefore(w, anchor));
    // The new order changed which floats each one clears, so put them all back where they
    // were. This takes two passes, since correcting an earlier float moves the later ones.
    for (let pass = 0; pass < 2; pass++) {
        getFloatingWrappersIn(editable).forEach((w) => {
            const wanted = wantedTops.get(w);
            if (wanted === undefined) return;
            const current = getImageBox(w).top;
            if (Math.abs(wanted - current) <= 1) return;
            nudgeInlineImageOffset(w, wanted - current, viewportPxPerLayoutPx);
        });
    }
}

// Puts every image that is not being dragged back where it was when the drag began, as far as
// the new layout allows. An image cannot go above where it would start with no offset, so an
// image whose old place is now taken ends up as close below it as the floats allow. This takes
// two passes, because correcting an earlier float moves where the later ones start.
function restoreNeighborImagePositions(state: IInlineImageDragState): void {
    if (state.editableContentBoxViewportPx.height <= 0) return; // no real layout to measure (jsdom)
    for (let pass = 0; pass < 2; pass++) {
        getFloatingWrappersIn(state.editable).forEach((wrapper) => {
            if (wrapper === state.wrapper) return;
            const wantedTop = state.neighborImageTopsViewportPx.get(wrapper);
            if (wantedTop === undefined) return;
            const currentTop = getImageBox(wrapper).top;
            if (Math.abs(wantedTop - currentTop) <= 1) return;
            nudgeInlineImageOffset(
                wrapper,
                wantedTop - currentTop,
                state.viewportPxPerLayoutPx,
            );
        });
    }
}

// Called at the end of every completed drag or resize of an inline image.
function commitInlineImageChange(
    wrapper: HTMLElement,
    editable: HTMLElement,
): void {
    commitPendingInlineImageUndo(wrapper);
    // Record the baseline for every image in the block, because a move changes the offsets of
    // the other images too, and all of them were measured against the block as it is now.
    getInlineImagesInEditable(editable).forEach((each) =>
        recordInlineImageOffsetBaseline(each, editable),
    );
    syncInlineImagesFromEditable(editable);
    OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
}

function addPointerListeners(doc: Document): void {
    removePointerListeners();
    documentWithPointerListeners = doc;
    // These are on the document, in the capture phase, so a drag or resize still gets its
    // moves and its end when the pointer goes off the image or off the page.
    doc.addEventListener("pointermove", onPointerMove, true);
    doc.addEventListener("pointerup", onPointerEnd, true);
    doc.addEventListener("pointercancel", onPointerEnd, true);
}

function removePointerListeners(): void {
    const doc = documentWithPointerListeners;
    if (!doc) return;
    documentWithPointerListeners = undefined;
    doc.removeEventListener("pointermove", onPointerMove, true);
    doc.removeEventListener("pointerup", onPointerEnd, true);
    doc.removeEventListener("pointercancel", onPointerEnd, true);
}

// --- internals ---------------------------------------------------------------

function addHandles(wrapper: HTMLElement): void {
    if (wrapper.querySelector(":scope > ." + kInlineImageHandleFrameClass))
        return;
    const doc = wrapper.ownerDocument;
    const frame = doc.createElement("div");
    frame.className = "bloom-ui " + kInlineImageHandleFrameClass;
    kInlineImageHandleCorners.forEach((corner) => {
        const handle = doc.createElement("div");
        handle.className = `bloom-ui ${kInlineImageHandleClass} ${kInlineImageHandleClass}-${corner}`;
        handle.setAttribute(kInlineImageCornerAttribute, corner);
        frame.appendChild(handle);
    });
    wrapper.appendChild(frame);
}

const getImageOf = (wrapper: HTMLElement): HTMLElement | undefined =>
    (wrapper.querySelector("img") as HTMLElement | null) ?? undefined;

// The image's box. It is smaller than the wrapper's box, because the vertical offset is
// transparent padding at the top of the wrapper, and in the middle band a full-width wrapper
// holds a narrower image.
function getImageBox(wrapper: HTMLElement): IBox {
    return getBox(getImageOf(wrapper) ?? wrapper);
}

function getBox(element: HTMLElement): IBox {
    const rect = element.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
    };
}

function getInlineImageOffsetLayoutPx(wrapper: HTMLElement): number {
    const value = parseFloat(
        wrapper.style.getPropertyValue(kInlineImageOffsetVar),
    );
    return Number.isNaN(value) ? 0 : value;
}

// Adding or removing an image changes how much room the text has, in every language.
function refreshOverflow(translationGroup: HTMLElement): void {
    getEditables(translationGroup).forEach((editable) =>
        OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable),
    );
}
