// Inline (Word-style) images: the edit-time interaction layer. inlineImages.ts owns what an
// inline image IS -- the markup, the geometry custom properties, keeping every language's
// copy in step, and undo. This file owns what the user DOES to one: the right-click menu
// that adds and removes it, selecting it, dragging it between docks, and resizing it.
//
// Two things shape the code here:
//
// 1. Every gesture ends in the same three steps, because of how inline images are stored:
//    commit the undo point, stamp the new geometry onto the other languages' copies, and
//    re-check overflow (an image that just got bigger can push the text past the bottom of
//    the block). During a gesture we touch only the local wrapper, so the preview is cheap;
//    the sync happens once, at the end. See commitInlineImageChange.
//
// 2. All the listeners are on the document, not on the wrappers. Wrapper elements are
//    replaced out from under us as a matter of course -- a sync stamps fresh copies onto the
//    sibling editables, an undo rebuilds one from serialized markup -- so a handler bound to
//    a particular element would quietly stop working. Everything here hit-tests from the
//    event's target instead.
//
// The interesting arithmetic (which dock a position means, how far down the image has been
// dragged, how wide a resize has made it) is in pure exported functions, which is where the
// tests are aimed; jsdom has no layout, so the gestures themselves are not unit-testable.
//
// The commands reach the user through the text block's right-click menu, which is not ours:
// bookEdit/textContextMenu/TextContextMenu.tsx (BL-16649) owns it and also carries paragraph
// commands like "No Indent". It asks getInlineImageMenuItemsForClick what we have to offer for
// the click it is handling. So this module has no contextmenu listener of its own -- two
// handlers for one event on the same elements would fight, and that menu stops propagation
// once it decides to act.
import * as React from "react";
import { default as AddImageIcon } from "@mui/icons-material/AddPhotoAlternateOutlined";
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

// The four resize handles, and the frame they hang off. Both are bloom-ui, so Cleanup()
// strips them before the page is saved and syncInlineImagesFromEditable leaves them out of
// the copies it stamps onto the other languages.
export const kInlineImageHandleFrameClass = "bloom-ui-inlineImage-handle-frame";
export const kInlineImageHandleClass = "bloom-ui-inlineImage-handle";

// Set on the body (which is above the bloom-page, so never saved) for the duration of a
// drag or resize, to stop the gesture from also sweeping out a text selection.
export const kInlineImageDraggingClass = "bloom-inlineImage-dragging";

// Compass directions, matching the per-corner CSS in editMode.less.
const kInlineImageHandleCorners = ["nw", "ne", "sw", "se"] as const;
export type InlineImageHandleCorner =
    (typeof kInlineImageHandleCorners)[number];
const kInlineImageCornerAttribute = "data-inline-image-corner";

// A wider image than this leaves no room for text to wrap; a narrower one is too small to
// be worth wrapping around. Percentages of the editable's width.
export const kMinInlineImageWidthPercent = 10;
export const kMaxInlineImageWidthPercent = 95;

// A click wobbles by a pixel or two. Below this the gesture is a click, and nothing is
// mutated and no undo point recorded.
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
 * - "existing": an inline image, which can be changed, documented or removed;
 * - "add": a text block eligible for inline images (there is no limit on how many);
 * - "none": anywhere else, which offers no inline-image commands at all.
 *
 * A block is eligible if it is a bloom-editable directly inside a translation group and is
 * not inside a canvas element (those have their own context menu, which already knows about
 * their images). The commands for an existing image belong to the image itself.
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

/** See InlineImageActionTarget. Takes the element the user pointed at. */
/**
 * Whether this is a field whose content Bloom stores for itself and writes back out, rather than
 * one that simply lives on the page where the person typed it. An inline image cannot go in one.
 *
 * A data-book field is stored once in the data div, as InnerXml, and written into EVERY element
 * carrying the same key (BookData's GatherDataItemsFromXElement and SetNodeXml). Front and back
 * matter is not even kept where it is shown: BringXmatterHtmlUpToDate deletes and re-injects those
 * pages, so the data div is the only thing that survives. Measured on the cover title, a picture
 * put there left the book's STORED TITLE holding the wrapper's markup -- and that title is what
 * names the book in the collection, in the title bar, and in AllTitles -- with four copies of the
 * wrapper in the file for the one picture, and a bloom-contentNational2 class stamped onto it.
 * A field with data-textonly="true" is worse still: BookData assigns InnerText to itself, which
 * discards the picture outright.
 *
 * Bloom's own way to put a picture on a cover is a canvas element, which is stored on the page.
 * So the command is not offered here. Nothing decided that it should be: the two exclusions above
 * (a canvas element, and an editable that is not a group's own child) happened to leave it open.
 */
function isFieldBloomWritesItself(editable: HTMLElement): boolean {
    if (editable.hasAttribute("data-book")) return true;
    const page = editable.closest(".bloom-page");
    return !!page?.hasAttribute("data-xmatter-page");
}

export function getInlineImageActionTarget(
    element: HTMLElement | undefined | null,
): InlineImageActionTarget {
    if (!element) return { kind: "none" };
    const editable = element.closest(".bloom-editable") as HTMLElement | null;
    if (!editable) return { kind: "none" };
    // The editables of a group are its direct children, so anything else is some other kind
    // of bloom-editable (a source bubble's clone, for instance) and not ours to act on.
    const translationGroup = editable.parentElement;
    if (!translationGroup?.classList.contains("bloom-translationGroup"))
        return { kind: "none" };
    if (editable.closest(kCanvasElementSelector)) return { kind: "none" };
    if (isFieldBloomWritesItself(editable)) return { kind: "none" };
    // An image description is a translation group in its own right, sitting in the
    // bloom-canvas but outside any canvas element, so neither exclusion above catches it.
    // It is what a reader hears in place of the picture, so a picture in it makes no sense
    // -- and it is not shown in the book, so one put there could not be seen or removed.
    if (translationGroup.classList.contains("bloom-imageDescription"))
        return { kind: "none" };
    const wrapper = element.closest(
        "." + kInlineImageClass,
    ) as HTMLElement | null;
    if (wrapper)
        return { kind: "existing", translationGroup, editable, wrapper };
    // There is no limit on how many inline images a block can hold, so "add" is offered
    // whether or not the group already has some; each insert appends a new image with its
    // own identity.
    return { kind: "add", translationGroup, editable };
}

/**
 * Which dock a position calls for. The position is where the IMAGE is (or would be), not
 * where the cursor is -- see the grab offsets in IInlineImageDragState for why. Crossing a
 * third of the block's width switches between left, the middle band, and right.
 *
 * The bottom dock takes over in the MIDDLE third at exactly the point where the band gives
 * out: the band's offset is clamped so that the whole wrapper stays inside the block's
 * content (see the maximum in continueDrag), so a position that would put the image's bottom
 * past the end of the content is not a place the band can go, and is read as asking for the
 * dock below the text. Anywhere below the block altogether is the bottom dock whatever the
 * horizontal position.
 *
 * Two things this must not do, both found in live testing:
 *  - claim the lower corners. In the outer thirds the side docks win all the way down, or an
 *    image cannot be parked in a lower corner at all.
 *  - claim a zone measured as a share of the block. It was the bottom fifth, and in a block
 *    whose text overflows a fifth is a large distance -- five lines of the reported A6 page.
 *    Every band position in it turned into the bottom dock, so the person could put the
 *    picture at the bottom or five lines higher and nowhere in between (John: "it seems like
 *    I should be able to put it vertically anywhere I want").
 */
export function computeInlineImageDock(
    imageCenterViewportPx: { x: number; y: number },
    editableContentBoxViewportPx: IBox,
    imageHeightViewportPx: number,
): InlineImageDock {
    // A box with no width says nothing about thirds; the band is the neutral answer.
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
 * The whole of what a text block holds, in viewport pixels, which is what a drag has to
 * measure against. It is NOT the block's rectangle: a block too small for its text scrolls,
 * so its rectangle shows only a window onto the content, and the part of the text below the
 * window is a real place the user can put an image.
 *
 * Everything the drag reads from the pointer is in viewport pixels, and the block's own
 * scroll measurements are in layout pixels, which differ whenever the page is zoomed. The
 * rectangle and clientHeight measure the same edge-to-edge distance, so their ratio is the
 * page's scale, and no caller has to know the zoom.
 *
 * A block that fits its text returns its own rectangle, so nothing changes in the ordinary
 * case. Pass a clientHeight of zero (jsdom, where nothing is laid out) to get the rectangle
 * back unchanged.
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
 * How far the block has to scroll, in layout pixels, to bring a dragged picture back inside
 * the part of the block that is on the screen. Positive scrolls the text up (showing more of
 * what follows), negative scrolls it down, zero when the picture is already showing.
 *
 * A block too small for its text scrolls, and the offset clamp only keeps the picture inside
 * the block's CONTENT, so dragging downwards walks the picture into text that is not on the
 * screen and the person loses sight of the thing they are moving (John, live testing: "when
 * scrolling is needed, the scrolling doesn't follow the drag of the image. So if you drag the
 * image off the top or the bottom, you can't see it anymore"). Scrolling by exactly the
 * amount that sticks out follows the drag instead of running ahead of it.
 *
 * The bottom edge wins when the picture is taller than the window, since a picture that big
 * cannot be shown whole and the drag is heading downwards.
 */
export function computeInlineImageDragScrollLayoutPx(
    imageBoxViewportPx: IBox,
    visibleBoxViewportPx: IBox,
    viewportPxPerLayoutPx: number,
): number {
    // Nothing is laid out (jsdom), so there is no screen for anything to be off.
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
 * Whether a move has to be undone because it left the text with nowhere to go: it added
 * scroll overflow to a block that fitted before the gesture began.
 *
 * A block that ALREADY overflowed is exempt, and that exemption is the whole point of this
 * being a named rule. Moving an image anywhere changes how much room the text needs -- an
 * image higher up displaces more text below it -- so in a block whose text does not fit,
 * a plain "this move added overflow" test vetoes most moves, including every move back up,
 * and the image cannot be repositioned at all (John, live testing: an image at a large
 * offset in an A6 block was frozen, every upward drag undone). Such a block is already
 * showing Bloom's overflow warning, so the person has been told; what keeps the image
 * somewhere reachable is the offset clamp, which holds the whole wrapper inside the
 * block's content, not this rule.
 *
 * The one pixel of slack absorbs sub-pixel layout noise, which would otherwise read as
 * overflow that the move caused.
 */
export function shouldRevertInlineImageMove(
    startOverflowLayoutPx: number,
    currentOverflowLayoutPx: number,
): boolean {
    if (startOverflowLayoutPx > 0) return false;
    return currentOverflowLayoutPx > startOverflowLayoutPx + 1;
}

/**
 * How much bigger a viewport distance is than the layout distance it stands for, which is
 * the page's zoom. Offsets are written in layout pixels (the custom property is used inside
 * the scaled page), while a drag measures in viewport pixels, so every distance taken from
 * the pointer is divided by this before it becomes an offset. Returns 1 when there is
 * nothing to measure (jsdom).
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
 * The value to write to --inline-image-offset: whole pixels, never negative (the image
 * cannot sit above the top of its block), and no further than the given maximum, which is
 * "how far down can the image start and still fit inside the block". A maximum at or below
 * zero is a real answer -- the image already fills the block, so it stays pinned to the
 * top. Pass undefined when there is no box to measure against (a degenerate layout), and
 * only the lower bound applies.
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
 * Where an offset measured against a block of one height belongs in a block of another, as a
 * share of the height: a picture two thirds of the way down its text stays two thirds of the way
 * down. Whole pixels, never negative, and unchanged when either height is unusable (nothing is
 * laid out, or no baseline was recorded).
 *
 * Proportion is the whole of the intent, and it is deliberately not the whole of the fix: the
 * text beside a float rewraps when the block changes width, so a proportional offset can still
 * leave the block holding more text than it can show. The caller takes that back afterwards
 * (adjustInlineImageOffsetsIfBlockSizeChanged), which is the invariant a drag already keeps.
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
 * The width (as a percentage of the editable) that dragging a corner handle has reached.
 * Only the horizontal movement counts: the wrapper's aspect ratio decides the height, so
 * pulling a corner sideways is the whole gesture. horizontalSign says which way is bigger
 * for the corner being dragged (see getInlineImageHandleHorizontalSign).
 * Assumes a positive editableWidthPx; startResize does not begin a resize without one.
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
    // One decimal is finer than a pixel on any block we lay out, and keeps the style
    // attribute -- which is saved, and stamped onto every language's copy -- tidy.
    return clampInlineImageWidthPercent(Math.round(percent * 10) / 10);
}

/**
 * Which direction makes the image bigger when this corner is dragged: outward. Dragging the
 * right-hand corners right, or the left-hand corners left, grows it, whichever dock the
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
    // The fallback matches what a new inline image gets (see makeInlineImageWrapper); a
    // wrapper with no dock class at all could only come from hand-edited HTML.
    return found ?? kInlineImageRightClass;
}

/**
 * Makes this inline image the selected object: the wrapper gets the marker class (which
 * draws the outline and the move cursor, and is what tells the undo layer that an inline
 * image is the active thing), and the resize handles appear on it. Only one inline image is
 * ever selected, so this deselects any other first.
 */
export function selectInlineImage(wrapper: HTMLElement): void {
    deselectAllInlineImages(wrapper.ownerDocument);
    wrapper.classList.add(kInlineImageSelectedClass);
    addHandles(wrapper);
    showInlineImageContextControls(wrapper);
}

/**
 * Drops the selection: no outline, no handles. The marker class matters here beyond
 * appearances, because the wrapper it sits on is real saved content -- see
 * cleanupInlineImageInteractions.
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

/**
 * Installs the inline-image interaction listeners on the page's document. Called from
 * SetupElements, right after setupInlineImages; safe to call again for a container added
 * later, since the listeners are per document and installed once.
 */
// How many times the fit pass may reduce an offset. Each pass takes back exactly the amount by
// which the block now overflows, so one is normally enough; the rest are for the case where
// reducing the offset rewraps the text beside the float and changes the amount again.
const kMaxOffsetFitPasses = 4;

/**
 * Re-measures every inline image's offset for the block it now finds itself in, and records the
 * new baseline. Call it at page setup, after the per-language copies have been made to agree.
 *
 * WHY. The offset is an absolute distance (see kInlineImageOffsetBasedOnAttr), and it is written
 * only by a drag -- which refuses any move that would leave the block holding more text than it
 * can show. Nothing re-measures it when the block itself changes size, so drawing the book at a
 * shorter page size reaches exactly the state the drag refuses to create: the picture stays the
 * same distance below the start of the text, and the lines that follow it are pushed off the end
 * of the block. Bloom does not even warn, since it treats a block it has allowed to scroll as
 * not overflowing. Measured on an A5 Portrait page with the picture near the bottom of the text,
 * changing the book to A5 Landscape pushed eleven lines off the end.
 *
 * Only the editable the reader sees is laid out (the others are display:none and measure zero),
 * so that one is re-measured and syncInlineImagesFromEditable carries the result to the rest.
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
        // An image with no baseline is one saved before Bloom recorded it: there is nothing to
        // re-measure from, so it keeps the offset it has and we record where it stands now.
        //
        // Width counts as much as height. Only the height goes into re-computing the offset
        // (the offset is a vertical distance), but a narrower block wraps the same text into
        // more lines, so the text below the picture can now run off the end -- and since we
        // re-record the baseline below either way, a width change we did not act on here would
        // become the new baseline and never be noticed again.
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

// Takes back as much offset as the block now overflows by, from the bottom-most picture that has
// any to give. That is the one holding the text down: the offset is space above a picture, so the
// text that follows the lowest picture is what has gone off the end of the block. Reducing to
// zero and still overflowing is a block with more text than it can hold whatever the pictures
// do, which is the person's own doing and not ours to correct -- the same answer a drag gives
// when the maximum it may use is zero.
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
    // A gesture cannot survive a re-setup: its state points at elements that may be gone.
    stopEdgeScrollTimer(dragState);
    releaseGesturePointer(resizeState ?? dragState);
    dragState = undefined;
    resizeState = undefined;
    const doc = container.ownerDocument;
    if (documentsWithInlineImageListeners.has(doc)) return;
    documentsWithInlineImageListeners.add(doc);
    // Capture phase: the editables are managed by CKEditor, and these gestures have to reach
    // us whether or not something closer to the target has opinions about them.
    doc.addEventListener("pointerdown", onPointerDown, true);
    doc.addEventListener("mousedown", onMouseDown, true);
    doc.addEventListener("focusin", onFocusIn);
    // Whose ctrl+z is it? The undo layer compares the block's content with its snapshot, but
    // that cannot see an edit that undid itself -- a word typed and deleted again -- while
    // CKEditor holds undo points for both halves of it. So the page reports the typing
    // itself. "input" covers every way text arrives, including paste and CKEditor's own
    // commands, and it bubbles out of the editable.
    doc.addEventListener("input", onInput);
    // Two things inlineImages.ts does behind our back, both of which leave the selection
    // needing attention. Both events bubble, so one listener at the document covers the page.
    doc.addEventListener(kInlineImagesRestoredEvent, onInlineImagesRestored);
    doc.addEventListener(kInlineImageChangedEvent, onInlineImageChanged);
    // See aiImageEditingIsAvailable: fetched here because the menu is composed
    // synchronously at right-click time, long after this resolves.
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
 * Takes the edit-time selection state off the inline images, for the page-save path
 * (Cleanup in bloomEditing.ts). The handles are bloom-ui and would be removed anyway, but
 * the selected class is on the wrapper itself, which IS saved, so it has to come off here.
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

// An existing inline image gets the STANDARD image menu: the same "image" section of
// canvasControlRegistry the canvas element menu draws from, resolved through the same
// availability rules, so an image offers the same commands with the same wording wherever
// the user meets one. This configuration says only what is different here, which is what
// cannot apply to an image living inside a text block. ("Expand image to fill space" needs
// no entry: its normal rule already limits it to background images.)
const inlineImageControlConfiguration: ICanvasElementControlConfiguration = {
    // Not really a canvas element, but "image" is the truth about what the commands act on,
    // and nothing in menu resolution consults the type.
    type: "image",
    menuSections: ["image"],
    // The same toolbar an image on a canvas gets (imageCanvasElementControls), so that a
    // picture offers the same buttons wherever the user meets one. "expandToFillSpace" needs
    // no exclusion here: its own rule already limits it to background images.
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
        // Both of these turn the image into canvas furniture (the page's background image,
        // the book-thumbnail source), which an image inside a text block cannot become.
        becomeBackground: "exclude",
        imageFieldType: "exclude",
        // Duplicating means duplicating a canvas element, which this is not. Adding a second
        // picture to the block is Add Image on the text's own menu.
        duplicate: "exclude",
    },
};

// Whether the experimental "Edit with AI" feature is on, which the availability rules need
// synchronously at right-click time; the status lives on the C# side, so it is fetched at
// page setup and remembered.
let aiImageEditingIsAvailable = false;

/** What a menu item calls to dismiss the menu it is on. See IControlRuntime.closeMenu. */
export type CloseMenuFunction = (launchingDialog?: boolean) => void;

/**
 * The commands to offer for what the user right-clicked, in the shape TextContextMenu
 * renders. For an existing image this is the standard image menu (see
 * inlineImageControlConfiguration above) plus Delete; for an eligible text block it is
 * Add Image. closeMenu is how the commands dismiss the menu they are chosen from; it
 * defaults to a no-op for tests that only inspect the items.
 */
export function buildInlineImageMenuItems(
    target: InlineImageActionTarget,
    closeMenu: CloseMenuFunction = () => {},
): IMenuItemWithSubmenu[] {
    if (target.kind === "add") {
        return [
            {
                l10nId: "EditTab.InlineImage.AddImage",
                english: "Add Image",
                icon: React.createElement(AddImageIcon, null),
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
                        .map((item) => item.menuRow)
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
                // The same words and icon as the canvas element menu's Delete, but the
                // action is ours: deleting an inline image means removing THIS image's
                // copy from every language's editable, which the canvas-element manager
                // behind the registry's delete knows nothing about.
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

// The registry's commands were written for canvas element images, so they mutate only the
// img they are given -- which for an inline image is one language's copy. Ending every
// command with a sync stamps whatever it did onto the other languages' copies and re-checks
// overflow, exactly like the end of a drag. Commands that change the picture itself go out
// through changeImageInfo, which already syncs (see handleInlineImageChanged), so for them
// this is a harmless second pass over unchanged markup; the ones that mutate the img in
// place (the transparency submenu) have only this.
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
            // The registry's commands know nothing about this undo layer, so the undo point
            // has to be taken here or the last thing recorded stays whatever put the picture
            // there -- and ctrl+z after making an image transparent removed the image.
            // Prepared and committed rather than recorded straight, because the command may
            // be asynchronous and may end up changing nothing.
            prepareInlineImageUndo(target.wrapper);
            await row.onSelect(rowCtx, rowRuntime);
            commitPendingInlineImageUndo(target.wrapper);
            syncInlineImagesFromEditable(target.editable);
            refreshOverflow(target.translationGroup);
        },
    }));
}

// Adds an inline image to the block, leaving it selected and holding a placeholder. It
// deliberately does NOT go on to open the image chooser: the user's next move is often to put
// the image where they want it rather than to pick a picture, and a dialog that opens itself
// takes that choice away. The picture is chosen later, from the same menu's "Change image".
// insertInlineImage records its own undo point and puts a copy in every editable of the group,
// so there is nothing to sync here.
function addInlineImage(translationGroup: HTMLElement): void {
    const wrapper = insertInlineImage(translationGroup);
    selectInlineImage(wrapper);
    refreshOverflow(translationGroup);
}

// removeInlineImage records its own undo point and clears THIS image's copy (matched by its
// identity attribute) out of every language, leaving any other inline images alone -- and
// leaving them WHERE THEY ARE: removing an image above another frees the space it cleared,
// which would otherwise make the lower one spring upward (John: moving one image must not
// move the others).
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
    // The bar of buttons is a div on the body (see kInlineImageContextControlsId), so it does
    // not go with the picture: without this it stays on screen, under a picture that is no
    // longer there, offering commands for it. Undoing the delete does not need the selection
    // -- inlineImageCanUndo recognizes the deleted-image case from the caret instead.
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
        // The offset corrections above happened in this editable; the other languages
        // get the same values.
        syncInlineImagesFromEditable(editable);
    }
    refreshOverflow(translationGroup);
}

// --- the toolbar -------------------------------------------------------------

// The bar of buttons under the selected picture. It is the very component a canvas element
// uses, given this module's own control configuration and menu, so the buttons, their icons
// and their wording are the same ones an image has anywhere else in Bloom.
//
// It is rendered into a div of our own on the body, which is above the bloom-page and so is
// never saved, and never seen by the page-save cleanup. The canvas element's bar has a div
// of its own in the same place; two ids, because each is put up and taken down by its own
// code, and one taking down the other's would be a hard bug to see.
export const kInlineImageContextControlsId = "inline-image-context-controls";

// How far below the picture the bar sits, matching the canvas element's bar.
const kInlineImageContextControlsGapLayoutPx = 11;

// The bar is centered in a box this wide, which is wider than the bar ever is. The canvas
// element's bar is centered the same way.
const kInlineImageContextControlsBoxWidthLayoutPx = 300;

// Set on the bar while a drag or a resize is running, so it does not follow the picture
// around. Same name as the canvas element's, and the same rule in editMode.less.
const kMovingClass = "moving";

/**
 * Puts the toolbar under this picture, or moves it there when it is already up. Called
 * whenever an inline image becomes the selected object, which is the moment the user
 * expects the buttons: right after Add Image, and on a click on the picture.
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

// The same render with the menu open or closed, which is how the "..." button opens its own
// menu (the component asks its parent to re-render it in the state it wants).
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

// What the shared bar needs in order to be about an inline image rather than a canvas
// element: which controls to offer, the menu the picture's right-click gives, how to delete
// it, and the sync every command has to end with.
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
            // A command can change the picture's shape, so the bar has to be put back under
            // it. It can also delete it, and then there is nothing to be under.
            if (wrapper.isConnected)
                positionInlineImageContextControls(wrapper);
        },
    };
}

/**
 * Centers the toolbar under the picture. The bar is not inside the scaled page, so it is
 * given the page's own transform; without that it would be drawn at 100% over a page drawn
 * at some other zoom.
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

/** Takes the toolbar down. Part of dropping the selection. */
export function removeInlineImageContextControls(doc: Document): void {
    doc.getElementById(kInlineImageContextControlsId)?.remove();
}

// Hides the bar for the length of a gesture and puts it back, under wherever the picture
// ended up. A bar that jumped along with a drag would be in the way of the drag.
function setInlineImageContextControlsMoving(
    doc: Document,
    moving: boolean,
): void {
    doc.getElementById(kInlineImageContextControlsId)?.classList.toggle(
        kMovingClass,
        moving,
    );
}

// --- keeping the selection honest when inlineImages.ts replaces things -------

/**
 * What inline images contribute to the text block's right-click menu: the commands for what
 * was clicked, or an empty list if there are none to offer there. TextContextMenu calls this
 * for every right-click it sees, and an empty list is how it learns we are not interested.
 *
 * Selecting an existing image as a side effect is deliberate -- it is what makes the commands
 * visibly apply to something, and what keeps ctrl+z routed to the inline-image undo layer,
 * whose gate is the selection. Hence "ForClick": call it once per right-click, not on every
 * render of the menu.
 */
export function getInlineImageMenuItemsForClick(
    clickedElement: HTMLElement | undefined | null,
    closeMenu: CloseMenuFunction = () => {},
): IMenuItemWithSubmenu[] {
    const target = getInlineImageActionTarget(clickedElement);
    if (target.kind === "existing") selectInlineImage(target.wrapper);
    // A right-click anywhere else is not about the picture, and leaving one selected says the
    // commands on show apply to it. It also keeps ctrl+z routed to the inline-image undo
    // layer, whose gate is the selection, when the person means the text they just clicked in.
    else if (clickedElement)
        deselectAllInlineImages(clickedElement.ownerDocument);
    return buildInlineImageMenuItems(target, closeMenu);
}

// Undo replaces every wrapper in the group with a fresh element built from serialized markup,
// which cannot carry bloom-ui children. inlineImages.ts puts the selected class back on the
// restored copy in the editable that had it, and that wrapper needs the whole of the selection
// UI re-derived from it: the handles, which cannot have survived, and the toolbar, which is a
// div on the body that was built for -- and still holds -- the element undo has just replaced.
// Left alone it offers Choose image, Copy image and Delete for something detached from the
// document, and sits wherever the old picture used to be.
//
// When nothing comes back selected the toolbar has to come DOWN. That is what undoing an
// insert looks like: the picture the bar belongs to is the one undo took away.
function onInlineImagesRestored(event: Event): void {
    const translationGroup = event.target as HTMLElement | null;
    if (!translationGroup) return;
    const selected = translationGroup.querySelector(
        "." + kInlineImageClass + "." + kInlineImageSelectedClass,
    ) as HTMLElement | null;
    if (selected) selectInlineImage(selected);
    else deselectAllInlineImages(translationGroup.ownerDocument);
}

// A new picture has arrived in an inline image. The trip out to the image chooser can leave
// the focus back in the text, which would have dropped the selection, and the change (and the
// insert that may have led to it) is only undoable while the image is selected. So re-assert
// it; the wrapper element itself survives a change, so this is the same one the user chose for.
function onInlineImageChanged(event: Event): void {
    const wrapper = (event as CustomEvent).detail as HTMLElement | undefined;
    if (wrapper) selectInlineImage(wrapper);
}

// --- selection and gestures --------------------------------------------------

interface IInlineImageDragState {
    wrapper: HTMLElement;
    editable: HTMLElement;
    // Measured once, at the start: switching to the bottom dock re-lays out the block, and
    // thresholds that moved around underneath the gesture would be unusable. This is the
    // block's CONTENT box (computeBlockContentBox), not its rectangle, so that the part of
    // an overflowing block's text that is scrolled out of sight is still somewhere the
    // image can be dragged to.
    editableContentBoxViewportPx: IBox;
    // Viewport pixels per layout pixel; see computeViewportPxPerLayoutPx.
    viewportPxPerLayoutPx: number;
    // Where the image's center was in relation to the pointer when the drag began. The dock
    // follows the image, not the cursor: without this, grabbing a wide image near one edge
    // would re-dock it before it had moved at all.
    grabOffsetXViewportPx: number;
    grabOffsetYViewportPx: number;
    startXViewportPx: number;
    startYViewportPx: number;
    startOffsetLayoutPx: number;
    dock: InlineImageDock;
    started: boolean;
    // Every OTHER floating image's absolute image-box top at drag start: moving one
    // image must not move the others, so they are held at these positions on every move
    // of the drag.
    neighborImageTopsViewportPx: Map<HTMLElement, number>;
    // The block's scroll overflow before the drag began. The fit test at the end of each
    // move is "did this move ADD overflow", measured on the whole block, because the
    // dragged image can fit while having pushed a NEIGHBOR out (a full-width band
    // crossing another image's level displaces it -- floats cannot overlap).
    startScrollOverflowLayoutPx: number;
    // The last place the pointer was, so that the edge-scroll timer can re-apply the move
    // the person is already making without a fresh pointer event.
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
    if (event.button !== 0) return; // the right button belongs to onContextMenu
    const target = event.target as HTMLElement | null;
    if (!target) return;
    // A press outside the page -- in our own menu, or the editing furniture around the page
    // -- must not drop the selection, or choosing a command would deselect the very image
    // the command is about.
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

// Clicking the image selects the image; it must not also put the caret in the text or start
// sweeping out a selection, which is what the browser does with a press inside a
// contenteditable. Cancelling pointerdown is not a reliable way to stop that, so we cancel
// the mousedown as well.
function onMouseDown(event: MouseEvent): void {
    if (event.button !== 0) return;
    const target = event.target as HTMLElement | null;
    if (target?.closest("." + kInlineImageClass)) event.preventDefault();
}

// The caret going back into the text ends the image's turn as the selected object. Focus
// landing in our own menu (which is outside the page) is not that.
function onFocusIn(event: FocusEvent): void {
    const target = event.target as HTMLElement | null;
    if (!target?.closest(".bloom-page")) return;
    if (target.closest("." + kInlineImageClass)) return;
    // Clicking the wrapper (contenteditable=false) inevitably lands keyboard focus on the
    // editable that CONTAINS it, a beat after pointerdown selected it. That focus change is
    // a side effect of the selecting click, not the caret returning to the text, so it must
    // not drop the selection (a press on the text itself deselects in onPointerDown instead).
    // Without this, the first click on an image always self-cancels and only a second click
    // sticks (verified live over CDP against WebView2).
    const selected = target.ownerDocument.querySelector(
        "." + kInlineImageSelectedClass,
    );
    if (selected && target.contains(selected)) return;
    deselectAllInlineImages(target.ownerDocument);
}

/**
 * Makes the wrapper the sole destination of everything this pointer does from now until it is
 * let go, wherever it goes in the meantime.
 *
 * Both gestures have to end even when the button is released somewhere our listeners cannot
 * see it. addPointerListeners puts them on the PAGE's document, so a pointerup delivered to
 * another document -- the toolbox iframe -- or to Bloom's own window furniture, or outside the
 * window altogether, never reaches onPointerEnd. And then the gesture simply does not end: the
 * edge-scroll timer goes on carrying the picture from a pointer position nothing is updating,
 * the body keeps the dragging class so text cannot be selected, the toolbar stays hidden, and
 * neither the sync to the other languages nor the undo point ever happens. Capturing the
 * pointer is what makes the browser deliver that pointerup here regardless.
 *
 * No other Bloom gesture spans a document boundary, which is why nothing else has needed one.
 */
function captureGesturePointer(wrapper: HTMLElement, pointerId: number): void {
    wrapper.setPointerCapture(pointerId);
}

/**
 * Gives the capture back. Every path that abandons a gesture comes through here, including the
 * two that throw the state away without a pointerup (a re-setup and the page-save cleanup):
 * a wrapper still holding a capture would go on swallowing the pointer.
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

// How often the block scrolls on while the picture is held against one of its edges.
const kEdgeScrollIntervalMs = 50;

// Re-applies the move the person is already making, so that holding the pointer still against
// an edge goes on carrying the picture through the text. There are no more pointer events to
// drive it, and a drag that only acted on movement would stop the moment the person held the
// mouse where they wanted it -- which at an edge is exactly what they do.
//
// It re-applies the whole move rather than only scrolling when the picture has left the screen,
// which deadlocks: the picture only leaves the screen because the offset moved it there, and the
// offset only moves when the move is applied. That left a picture dragged to the top edge
// resting a line and a half below the start of the text, with nothing able to shift it.
// Re-applying is harmless where nothing has to change: the offset is computed from where the
// picture should end up, so a second application of the same pointer position asks for the
// position it is already in.
function startEdgeScrollTimer(state: IInlineImageDragState): void {
    const view = state.editable.ownerDocument.defaultView;
    if (!view) return;
    state.edgeScrollTimerId = view.setInterval(() => {
        if (dragState !== state || !state.started) return;
        continueDrag(state, state.lastPointViewportPx);
    }, kEdgeScrollIntervalMs);
}

/**
 * Stops the edge-scroll interval of a drag that is over. Every place that abandons dragState has
 * to come through here: the interval holds its own reference to the state and would go on
 * re-applying the move, from a pointer position nothing is updating any more, against elements
 * that a page reload may have replaced.
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
    // Snapshot of the start-of-move state, which fit inside the block (each move either
    // ends fitting or reverts to this, so by induction every move starts from a fitting
    // arrangement). The next sibling pins the wrapper's cluster position; it is stable
    // during a gesture, since only the dragged wrapper moves in the DOM.
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
    // jsdom reports every box as empty; there we keep the simple delta arithmetic the
    // gesture tests exercise and skip the geometry that needs real layout.
    const degenerate = !(state.editableContentBoxViewportPx.height > 0);
    const blockBottomViewportPx =
        state.editableContentBoxViewportPx.top +
        state.editableContentBoxViewportPx.height;
    if (dock !== previousDock) {
        // setInlineImageDock also moves the wrapper between the leading and trailing
        // clusters, which is the only DOM difference between the bottom dock and the others.
        setInlineImageDock(state.wrapper, dock);
        state.dock = dock;
    }
    if (dock === kInlineImageBottomClass) {
        // The bottom dock is in normal flow at the end of the block, so it has no offset;
        // the value stays in the style attribute, ready for when the image is dragged
        // back up.
    } else {
        // Where the top of the IMAGE should end up, from the pointer and the grab offsets.
        const targetTopViewportPx =
            imageCenterViewportPx.y - getImageBox(state.wrapper).height / 2;
        // DOM order within the floating cluster is the images' vertical order (each float
        // starts below the earlier ones it must clear), so dragging an image above a
        // neighbor has to reorder them -- that is what frees the space ABOVE an image
        // whose offset padding otherwise fills its column from the top, and what lets
        // several images share one side (John, live testing).
        if (!degenerate) reorderInFloatingCluster(state, targetTopViewportPx);
        // A dock switch or reorder changes where the NEIGHBORS start; hold them at their
        // drag-start positions before measuring anything for this wrapper.
        if (!degenerate) restoreNeighborImagePositions(state);
        // The maximum keeps the whole wrapper (offset padding + image) inside the block:
        // an offset that pushes the image past the bottom makes the block scroll (John,
        // live testing). The offset is measured from where the float NATURALLY starts
        // (below any earlier float it clears), so the room left is computed from the
        // wrapper's live rendered bottom: however far that sits above the block's bottom
        // is how much further the current offset may grow.
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
        // In a real layout the offset is target-based (where should the image's top be,
        // given where it is right now), which stays correct across reorders and reflows.
        // The degenerate branch adds a viewport distance to a layout offset without
        // dividing, which is right only because it runs where nothing is laid out and so
        // nothing is scaled: in jsdom one viewport pixel IS one layout pixel.
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
            // The maximum above was measured before this move's offset was applied, so a
            // fast move can land a few pixels long; take back any remainder.
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
    // Whatever this move did, the OTHER images stay exactly where the user put them --
    // on every move, not just at the end (John).
    restoreNeighborImagePositions(state);
    // FIT OR REVERT. With the neighbors held in place, either the whole block still fits
    // (no NEW scroll overflow -- measured on the block, not just this wrapper, because a
    // move can fit the dragged image while pushing a neighbor out) or this move went
    // somewhere with no room: a full side, the bottom dock of a full block, or a band
    // crossing another image's level. Then the whole move is undone -- dock, cluster
    // position, offset -- returning to the start-of-move arrangement, which fit. Nothing
    // may hang below the block, where it scrolls the text and cannot even be clicked.
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

// Scrolls the block so that the picture being dragged stays on the screen, and slides the
// gesture's remembered geometry by however far the block actually scrolled.
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
    // Everything the gesture measured, it measured against the screen: where the block's
    // content begins and ends, and where each neighbor image was left. The content has just
    // slid under all of it, so those positions slide the same way, or the docks would be
    // judged against a stale block and the neighbors held at stale places. The offset is not
    // among them: it is written in the content's own terms, so scrolling does not touch it,
    // and the next move raises it to bring the picture back to the pointer -- which is how a
    // drag at the edge goes on moving the picture down the text.
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
 * Where in the floating cluster an image whose image-box top will be targetTop belongs:
 * after every image whose own image-box top is at or above it. Exported for tests.
 */
export function computeInlineImageClusterIndex(
    targetTopViewportPx: number,
    otherImageTopsViewportPx: number[],
): number {
    return otherImageTopsViewportPx.filter((top) => top <= targetTopViewportPx)
        .length;
}

// The floating (non-bottom) inline images of this editable, in DOM order, which is also
// their vertical order since each one starts below the earlier floats it has to clear.
function getFloatingWrappersIn(editable: HTMLElement): HTMLElement[] {
    return Array.from(
        editable.querySelectorAll(
            `:scope > .${kInlineImageClass}:not(.${kInlineImageBottomClass})`,
        ),
    ) as HTMLElement[];
}

// Sets a neighbor's offset so its image lands deltaPx from where it is now (clamped at
// its natural start). The distance is measured on the screen, so it is divided by the
// page's scale to become the layout pixels the offset is written in.
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

// The page's scale as measured on the block an image lives in. For callers that hold no
// drag state of their own; a drag measures it once and keeps it.
function getViewportPxPerLayoutPxOfEditable(editable: HTMLElement): number {
    return computeViewportPxPerLayoutPx(
        getBox(editable),
        editable.clientHeight,
    );
}

// Moves the dragged wrapper to the cluster position its target vertical position calls
// for. Purely a DOM move: the neighbors this displaces are held in place separately by
// restoreNeighborImagePositions, which runs on every move of the drag.
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
    // In viewport pixels, like the image width and the pointer deltas it is compared with:
    // clientWidth is in layout pixels, which are smaller than viewport pixels whenever the
    // page is zoomed, and mixing the two made every resize off by the zoom.
    const editableWidthViewportPx =
        editable.clientWidth *
        computeViewportPxPerLayoutPx(getBox(editable), editable.clientHeight);
    // With no width to be a percentage of, a resize could only write a nonsense number.
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

// A press that hasn't travelled far enough is still a click, so nothing is mutated and no
// undo point recorded until it has. The undo point is prepared rather than recorded, since
// even a real drag may be abandoned; endGesture commits it. Returns whether the caller
// should go on to apply this move.
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

// The listeners are on the document, so they see every pointer, and the browser goes on
// delivering a second one's events while the first holds its capture. A second finger on a
// touch screen, or a stray brush of a trackpad during a mouse drag, would otherwise move the
// picture to wherever that pointer is and end the gesture there.
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

// Both gestures end the same way, including a cancelled one: whatever was applied to the
// wrapper is on the screen, so it had better be replicated and undoable.
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
    if (!state.started) return; // it was a click: nothing changed, nothing prepared
    if (drag && drag === state) {
        restoreNeighborImagePositions(drag);
        normalizeFloatingClusterOrder(
            drag.editable,
            drag.editableContentBoxViewportPx,
            drag.viewportPxPerLayoutPx,
        );
    }
    commitInlineImageChange(state.wrapper, state.editable);
    // The picture is somewhere else now, so the bar goes with it.
    positionInlineImageContextControls(state.wrapper);
}

// Rewrites the floating cluster's DOM order to match the images' visual order, keeping
// every image exactly where it is. DOM order is what each float clears past, so an order
// that contradicts the visual order (which can arrive from a book edited before this
// rule, or from insertions) quietly limits where the images can go; a drag is the moment
// the user is rearranging things, so its end is the moment to straighten this out.
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
    // The reordering changed what each float clears, so put them all back where they
    // were; two passes, since correcting an earlier float shifts the later ones.
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

// Puts every image the drag did NOT move back exactly where it was when the drag began,
// as far as the new geometry allows (an image cannot rise above its natural start, so a
// neighbor whose old spot is now occupied lands as close below it as floats permit). Two
// passes, because correcting an earlier float shifts where the later ones start.
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

// The end of any completed change to an inline image's geometry.
function commitInlineImageChange(
    wrapper: HTMLElement,
    editable: HTMLElement,
): void {
    commitPendingInlineImageUndo(wrapper);
    // Every image in the block, not just the dragged one: a move nudges its neighbors' offsets
    // too, and all of them were measured against the block as it is now.
    getInlineImagesInEditable(editable).forEach((each) =>
        recordInlineImageOffsetBaseline(each, editable),
    );
    syncInlineImagesFromEditable(editable);
    OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
}

function addPointerListeners(doc: Document): void {
    removePointerListeners();
    documentWithPointerListeners = doc;
    // On the document, in the capture phase, so a gesture that wanders off the image -- or
    // off the page -- still gets its moves and its end.
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

// The image's box, which is not the wrapper's: the vertical offset is transparent padding at
// the top of the wrapper, and the middle band is a full-width wrapper around a narrower
// image.
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
