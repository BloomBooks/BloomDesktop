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
// TO DO (merge with master): the commands below have no menu on them yet, deliberately.
// master has since grown a right-click menu for text blocks, in
// bookEdit/textContextMenu/TextContextMenu.tsx (BL-16649), which is where they belong; this
// branch has no copy of that file to add them to. buildInlineImageMenuItems() returns them in
// the ILocalizableMenuItemProps shape that menu's items already use, so registering them
// there is a small edit. See the comment on buildInlineImageMenuItems for the one wrinkle:
// that menu's target lookup only recognizes a right-click on a <p>, which reaches the "add"
// command but never the commands that act on the image itself.
import * as React from "react";
import { default as AddImageIcon } from "@mui/icons-material/AddPhotoAlternateOutlined";
import { default as ChangeImageIcon } from "@mui/icons-material/Search";
import { default as CopyrightIcon } from "@mui/icons-material/Copyright";
import { default as DeleteIcon } from "@mui/icons-material/DeleteOutline";
import { ILocalizableMenuItemProps } from "../../react_components/localizableMenuItem";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";
import {
    doImageCommand,
    GetRawImageUrl,
    isPlaceHolderImage,
} from "./bloomImages";
import {
    commitPendingInlineImageUndo,
    getEditables,
    getInlineImage,
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
    prepareInlineImageUndo,
    removeInlineImage,
    setInlineImageDock,
    syncInlineImagesFromEditable,
} from "./inlineImages";
import { getWorkspaceBundleExports } from "./workspaceFrames";

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

// How much of the bottom of the block counts as "put the image below the text". Anything
// lower than this -- including below the block altogether -- means the bottom dock.
export const kInlineImageBottomZoneFraction = 0.2;

// A wider image than this leaves no room for text to wrap; a narrower one is too small to
// be worth wrapping around. Percentages of the editable's width.
export const kMinInlineImageWidthPercent = 10;
export const kMaxInlineImageWidthPercent = 95;

// A click wobbles by a pixel or two. Below this the gesture is a click, and nothing is
// mutated and no undo point recorded.
const kDragThresholdPx = 3;

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
 * - "add": a text block that is eligible for one and hasn't got one;
 * - "none": anywhere else, which offers no inline-image commands at all.
 *
 * A block is eligible if it is a bloom-editable directly inside a translation group and is
 * not inside a canvas element (those have their own context menu, which already knows about
 * their images). Since v1 allows at most one inline image per translation group, a block
 * whose group already has one is not offered "add"; the commands for that image belong to
 * the image itself.
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
    const wrapper = element.closest(
        "." + kInlineImageClass,
    ) as HTMLElement | null;
    if (wrapper)
        return { kind: "existing", translationGroup, editable, wrapper };
    if (getInlineImage(translationGroup)) return { kind: "none" };
    return { kind: "add", translationGroup, editable };
}

/**
 * Which dock a position calls for. The position is where the IMAGE is (or would be), not
 * where the cursor is -- see the grab offsets in IInlineImageDragState for why. Crossing a
 * third of the block's width switches between left, the middle band, and right; reaching the
 * bottom of the block means the bottom dock, whatever the horizontal position.
 */
export function computeInlineImageDock(
    imageCenter: { x: number; y: number },
    editableBox: IBox,
): InlineImageDock {
    const bottomZoneTop =
        editableBox.top +
        editableBox.height * (1 - kInlineImageBottomZoneFraction);
    if (imageCenter.y >= bottomZoneTop) return kInlineImageBottomClass;
    // A box with no width says nothing about thirds; the band is the neutral answer.
    const fraction =
        editableBox.width > 0
            ? (imageCenter.x - editableBox.left) / editableBox.width
            : 0.5;
    if (fraction < 1 / 3) return kInlineImageLeftClass;
    if (fraction > 2 / 3) return kInlineImageRightClass;
    return kInlineImageMiddleClass;
}

/**
 * The value to write to --inline-image-offset: whole pixels, never negative (the image
 * cannot sit above the top of its block), and no further down than the block is tall. A
 * maximum of zero or less means there is nothing sensible to clamp against, so only the
 * lower bound applies.
 */
export function clampInlineImageOffset(
    offsetPx: number,
    maxPx?: number,
): number {
    const rounded = Math.round(offsetPx);
    // Negatives and NaN both land here.
    if (!(rounded > 0)) return 0;
    if (maxPx !== undefined && maxPx > 0)
        return Math.min(rounded, Math.round(maxPx));
    return rounded;
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
    startWidthPx: number,
    deltaXPx: number,
    horizontalSign: number,
    editableWidthPx: number,
): number {
    const widthPx = startWidthPx + horizontalSign * deltaXPx;
    const percent = (widthPx / editableWidthPx) * 100;
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
}

/**
 * Installs the inline-image interaction listeners on the page's document. Called from
 * SetupElements, right after setupInlineImages; safe to call again for a container added
 * later, since the listeners are per document and installed once.
 */
export function setupInlineImageInteractions(container: HTMLElement): void {
    // A gesture cannot survive a re-setup: its state points at elements that may be gone.
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
    // Two things inlineImages.ts does behind our back, both of which leave the selection
    // needing attention. Both events bubble, so one listener at the document covers the page.
    doc.addEventListener(kInlineImagesRestoredEvent, onInlineImagesRestored);
    doc.addEventListener(kInlineImageChangedEvent, onInlineImageChanged);
}

/**
 * Takes the edit-time selection state off the inline images, for the page-save path
 * (Cleanup in bloomEditing.ts). The handles are bloom-ui and would be removed anyway, but
 * the selected class is on the wrapper itself, which IS saved, so it has to come off here.
 */
export function cleanupInlineImageInteractions(): void {
    dragState = undefined;
    resizeState = undefined;
    document.body.classList.remove(kInlineImageDraggingClass);
    deselectAllInlineImages(document);
}

// --- the commands ------------------------------------------------------------

/**
 * The commands to offer for what the user right-clicked, ready to hand to a menu.
 *
 * "Change image" and the credits dialog deliberately share their l10n ids with the canvas
 * element menu's equivalents, so that the same command reads the same wherever the user meets
 * it.
 *
 * TO DO (merge with master): register these in TextContextMenu.tsx, whose items are
 * LocalizableMenuItem/LocalizableSelectableMenuItem, which is why this returns
 * ILocalizableMenuItemProps. Two things to know when doing it:
 *  - That menu decides what it is looking at with findParagraphForTextContextMenu, which
 *    requires the click to be on a <p>. Pass that paragraph to getInlineImageActionTarget and
 *    the "add" command works as it should. But an inline image wrapper is a SIBLING of the
 *    paragraphs, never inside one, so a right-click on the image itself never reaches that
 *    menu at all, and the three commands that act on an existing image ("existing", below)
 *    stay unreachable until its lookup is broadened to return either a paragraph or a wrapper.
 *  - Its handler calls preventDefault/stopPropagation on the contextmenu event whenever it
 *    finds a paragraph, so a second contextmenu handler for the image commands must not
 *    compete with it. It does not have to: the two conditions are mutually exclusive.
 */
export function buildInlineImageMenuItems(
    target: InlineImageActionTarget,
): ILocalizableMenuItemProps[] {
    if (target.kind === "add") {
        return [
            {
                l10nId: "EditTab.InlineImage.AddImage",
                english: "Add Image",
                icon: React.createElement(AddImageIcon, null),
                onClick: () => addInlineImage(target.translationGroup),
            },
        ];
    }
    if (target.kind === "existing") {
        const img = getImageOf(target.wrapper);
        return [
            {
                l10nId: "EditTab.Image.ChangeImage",
                english: "Change image",
                icon: React.createElement(ChangeImageIcon, null),
                onClick: () => doImageCommand(img, "change"),
            },
            {
                l10nId: "EditTab.Image.EditMetadataOverlay",
                english: "Set image information...",
                subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
                icon: React.createElement(CopyrightIcon, null),
                // Nothing to describe until a real picture has been chosen.
                disabled: !img || isPlaceHolderImage(GetRawImageUrl(img)),
                onClick: () => showInlineImageMetadata(target.wrapper),
            },
            {
                l10nId: "EditTab.InlineImage.RemoveImage",
                english: "Remove Image",
                icon: React.createElement(DeleteIcon, null),
                onClick: () =>
                    removeInlineImageCommand(target.translationGroup),
            },
        ];
    }
    return [];
}

// Adds an inline image to the block and goes straight on to choosing the picture, which is
// what the user would have to do next anyway. insertInlineImage records its own undo point
// and puts a copy in every editable of the group, so there is nothing to sync here; the
// chosen picture reaches the other languages through handleInlineImageChanged.
function addInlineImage(translationGroup: HTMLElement): void {
    const wrapper = insertInlineImage(translationGroup);
    selectInlineImage(wrapper);
    refreshOverflow(translationGroup);
    doImageCommand(getImageOf(wrapper), "change");
}

// removeInlineImage records its own undo point and clears the copy out of every language.
function removeInlineImageCommand(translationGroup: HTMLElement): void {
    removeInlineImage(translationGroup);
    refreshOverflow(translationGroup);
}

function showInlineImageMetadata(wrapper: HTMLElement): void {
    const img = getImageOf(wrapper);
    if (!img) return;
    // Launch via the workspace (top window) bundle, not this page iframe, so that saving the
    // metadata -- which reloads the page iframe -- doesn't tear the dialog down.
    getWorkspaceBundleExports().showCopyrightAndLicenseDialog(
        img.getAttribute("src") ?? "",
    );
}

// --- keeping the selection honest when inlineImages.ts replaces things -------

/**
 * The commands' entry point for a right-click, once there is a menu to put them on: the
 * commands for what was clicked, or an empty list if inline images have nothing to offer
 * there. Selecting an existing image as a side effect is deliberate -- it is what makes the
 * commands visibly apply to something, and what keeps ctrl+z routed to the inline-image undo
 * layer, whose gate is the selection.
 */
export function getInlineImageMenuItemsForClick(
    clickedElement: HTMLElement | undefined | null,
): ILocalizableMenuItemProps[] {
    const target = getInlineImageActionTarget(clickedElement);
    if (target.kind === "existing") selectInlineImage(target.wrapper);
    return buildInlineImageMenuItems(target);
}

// Undo replaces every wrapper in the group with a fresh element built from serialized markup,
// which cannot carry bloom-ui children, so the handles are gone. inlineImages.ts does put the
// selected class back on the restored copy in the editable that had it (so that a second
// ctrl+z still routes to its undo layer), which is exactly the wrapper whose handles we owe.
function onInlineImagesRestored(event: Event): void {
    const translationGroup = event.target as HTMLElement | null;
    if (!translationGroup) return;
    const selected = translationGroup.querySelector(
        "." + kInlineImageClass + "." + kInlineImageSelectedClass,
    ) as HTMLElement | null;
    if (selected) addHandles(selected);
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
    // thresholds that moved around underneath the gesture would be unusable.
    editableBox: IBox;
    // Where the image's center was in relation to the pointer when the drag began. The dock
    // follows the image, not the cursor: without this, grabbing a wide image near one edge
    // would re-dock it before it had moved at all.
    grabOffsetX: number;
    grabOffsetY: number;
    startX: number;
    startY: number;
    startOffsetPx: number;
    dock: InlineImageDock;
    started: boolean;
}

interface IInlineImageResizeState {
    wrapper: HTMLElement;
    editable: HTMLElement;
    startX: number;
    startWidthPx: number;
    editableWidthPx: number;
    horizontalSign: number;
    started: boolean;
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
    deselectAllInlineImages(target.ownerDocument);
}

function startDrag(
    event: PointerEvent,
    wrapper: HTMLElement,
    editable: HTMLElement,
): void {
    const imageBox = getImageBox(wrapper);
    dragState = {
        wrapper,
        editable,
        editableBox: getBox(editable),
        grabOffsetX: imageBox.left + imageBox.width / 2 - event.clientX,
        grabOffsetY: imageBox.top + imageBox.height / 2 - event.clientY,
        startX: event.clientX,
        startY: event.clientY,
        startOffsetPx: getInlineImageOffsetPx(wrapper),
        dock: getInlineImageDock(wrapper),
        started: false,
    };
    addPointerListeners(editable.ownerDocument);
}

function continueDrag(state: IInlineImageDragState, event: PointerEvent): void {
    if (
        !beginGestureIfMoved(
            state,
            Math.abs(event.clientX - state.startX),
            Math.abs(event.clientY - state.startY),
        )
    )
        return;
    const dock = computeInlineImageDock(
        {
            x: event.clientX + state.grabOffsetX,
            y: event.clientY + state.grabOffsetY,
        },
        state.editableBox,
    );
    if (dock !== state.dock) {
        // setInlineImageDock also moves the wrapper between the first-child and last-child
        // slots, which is the only DOM difference between the bottom dock and the others.
        setInlineImageDock(state.wrapper, dock);
        state.dock = dock;
    }
    // The bottom dock is in normal flow at the end of the block, so it has no offset; the
    // value stays in the style attribute, ready for when the image is dragged back up.
    if (dock === kInlineImageBottomClass) return;
    const offsetPx = clampInlineImageOffset(
        state.startOffsetPx + (event.clientY - state.startY),
        state.editableBox.height,
    );
    state.wrapper.style.setProperty(kInlineImageOffsetVar, `${offsetPx}px`);
}

function startResize(event: PointerEvent, handle: HTMLElement): void {
    const wrapper = handle.closest(
        "." + kInlineImageClass,
    ) as HTMLElement | null;
    const editable = wrapper?.closest(".bloom-editable") as HTMLElement | null;
    if (!wrapper || !editable) return;
    const editableWidthPx = editable.clientWidth;
    // With no width to be a percentage of, a resize could only write a nonsense number.
    if (editableWidthPx <= 0) return;
    const corner = (handle.getAttribute(kInlineImageCornerAttribute) ??
        "se") as InlineImageHandleCorner;
    resizeState = {
        wrapper,
        editable,
        startX: event.clientX,
        startWidthPx: getImageBox(wrapper).width,
        editableWidthPx,
        horizontalSign: getInlineImageHandleHorizontalSign(corner),
        started: false,
    };
    // Grabbing a handle is not the start of a text selection.
    event.preventDefault();
    addPointerListeners(editable.ownerDocument);
}

function continueResize(
    state: IInlineImageResizeState,
    event: PointerEvent,
): void {
    if (!beginGestureIfMoved(state, Math.abs(event.clientX - state.startX), 0))
        return;
    const percent = computeInlineImageWidthPercent(
        state.startWidthPx,
        event.clientX - state.startX,
        state.horizontalSign,
        state.editableWidthPx,
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
    if (absDeltaX < kDragThresholdPx && absDeltaY < kDragThresholdPx)
        return false;
    state.started = true;
    prepareInlineImageUndo(state.wrapper);
    state.wrapper.ownerDocument.body.classList.add(kInlineImageDraggingClass);
    return true;
}

function onPointerMove(event: PointerEvent): void {
    if (resizeState) continueResize(resizeState, event);
    else if (dragState) continueDrag(dragState, event);
}

// Both gestures end the same way, including a cancelled one: whatever was applied to the
// wrapper is on the screen, so it had better be replicated and undoable.
function onPointerEnd(): void {
    const state = resizeState ?? dragState;
    resizeState = undefined;
    dragState = undefined;
    removePointerListeners();
    if (!state) return;
    state.wrapper.ownerDocument.body.classList.remove(
        kInlineImageDraggingClass,
    );
    if (!state.started) return; // it was a click: nothing changed, nothing prepared
    commitInlineImageChange(state.wrapper, state.editable);
}

// The end of any completed change to an inline image's geometry.
function commitInlineImageChange(
    wrapper: HTMLElement,
    editable: HTMLElement,
): void {
    commitPendingInlineImageUndo(wrapper);
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

function getInlineImageOffsetPx(wrapper: HTMLElement): number {
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
