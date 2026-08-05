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
    commitPendingInlineImageUndo,
    getEditables,
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
    // There is no limit on how many inline images a block can hold, so "add" is offered
    // whether or not the group already has some; each insert appends a new image with its
    // own identity.
    return { kind: "add", translationGroup, editable };
}

/**
 * Which dock a position calls for. The position is where the IMAGE is (or would be), not
 * where the cursor is -- see the grab offsets in IInlineImageDragState for why. Crossing a
 * third of the block's width switches between left, the middle band, and right. The bottom
 * dock claims the bottom zone only in the MIDDLE third (and anywhere below the block
 * itself): in the outer thirds the side docks win all the way down, so the image can be
 * parked in a lower corner -- with the whole bottom strip going to the bottom dock, the
 * corners were unreachable (John, live testing).
 */
export function computeInlineImageDock(
    imageCenter: { x: number; y: number },
    editableBox: IBox,
): InlineImageDock {
    // A box with no width says nothing about thirds; the band is the neutral answer.
    const fraction =
        editableBox.width > 0
            ? (imageCenter.x - editableBox.left) / editableBox.width
            : 0.5;
    if (imageCenter.y >= editableBox.top + editableBox.height)
        return kInlineImageBottomClass;
    const bottomZoneTop =
        editableBox.top +
        editableBox.height * (1 - kInlineImageBottomZoneFraction);
    const inMiddleThird = fraction >= 1 / 3 && fraction <= 2 / 3;
    if (imageCenter.y >= bottomZoneTop && inMiddleThird)
        return kInlineImageBottomClass;
    if (fraction < 1 / 3) return kInlineImageLeftClass;
    if (fraction > 2 / 3) return kInlineImageRightClass;
    return kInlineImageMiddleClass;
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
    offsetPx: number,
    maxPx?: number,
): number {
    const rounded = Math.round(offsetPx);
    // Negatives and NaN both land here.
    if (!(rounded > 0)) return 0;
    if (maxPx !== undefined)
        return Math.min(rounded, Math.max(0, Math.round(maxPx)));
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
    // See aiImageEditingIsAvailable: fetched here because the menu is composed
    // synchronously at right-click time, long after this resolves.
    void getFeatureStatusAsync("AiImageEditing").then((status) => {
        aiImageEditingIsAvailable = status?.visible ?? false;
    });
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
    toolbar: [],
    toolPanel: [],
    availabilityRules: {
        ...imageAvailabilityRules,
        // Both of these turn the image into canvas furniture (the page's background image,
        // the book-thumbnail source), which an image inside a text block cannot become.
        becomeBackground: "exclude",
        imageFieldType: "exclude",
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
            await row.onSelect(rowCtx, rowRuntime);
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
    if (editable.getBoundingClientRect().height > 0) {
        for (let pass = 0; pass < 2; pass++) {
            survivors.forEach((other) => {
                if (!other.isConnected) return;
                const wanted = keptTops.get(other);
                if (wanted === undefined) return;
                const current = getImageBox(other).top;
                if (Math.abs(wanted - current) <= 1) return;
                nudgeInlineImageOffset(other, wanted - current);
            });
        }
        // The offset corrections above happened in this editable; the other languages
        // get the same values.
        syncInlineImagesFromEditable(editable);
    }
    refreshOverflow(translationGroup);
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
    return buildInlineImageMenuItems(target, closeMenu);
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
    // Every OTHER floating image's absolute image-box top at drag start: moving one
    // image must not move the others, so they are held at these positions on every move
    // of the drag.
    neighborImageTops: Map<HTMLElement, number>;
    // The block's scroll overflow before the drag began. The fit test at the end of each
    // move is "did this move ADD overflow", measured on the whole block, because the
    // dragged image can fit while having pushed a NEIGHBOR out (a full-width band
    // crossing another image's level displaces it -- floats cannot overlap).
    startScrollOverflow: number;
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
        neighborImageTops: new Map(
            getFloatingWrappersIn(editable)
                .filter((other) => other !== wrapper)
                .map((other) => [other, getImageBox(other).top]),
        ),
        startScrollOverflow: editable.scrollHeight - editable.clientHeight,
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
    // Snapshot of the start-of-move state, which fit inside the block (each move either
    // ends fitting or reverts to this, so by induction every move starts from a fitting
    // arrangement). The next sibling pins the wrapper's cluster position; it is stable
    // during a gesture, since only the dragged wrapper moves in the DOM.
    const previousDock = state.dock;
    const previousOffsetPx = getInlineImageOffsetPx(state.wrapper);
    const previousNextSibling = state.wrapper.nextElementSibling;
    const imageCenter = {
        x: event.clientX + state.grabOffsetX,
        y: event.clientY + state.grabOffsetY,
    };
    const dock = computeInlineImageDock(imageCenter, state.editableBox);
    // jsdom reports every box as empty; there we keep the simple delta arithmetic the
    // gesture tests exercise and skip the geometry that needs real layout.
    const degenerate = !(state.editableBox.height > 0);
    const blockBottom = state.editableBox.top + state.editableBox.height;
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
        const targetTop = imageCenter.y - getImageBox(state.wrapper).height / 2;
        // DOM order within the floating cluster is the images' vertical order (each float
        // starts below the earlier ones it must clear), so dragging an image above a
        // neighbor has to reorder them -- that is what frees the space ABOVE an image
        // whose offset padding otherwise fills its column from the top, and what lets
        // several images share one side (John, live testing).
        if (!degenerate) reorderInFloatingCluster(state, targetTop);
        // A dock switch or reorder changes where the NEIGHBORS start; hold them at their
        // drag-start positions before measuring anything for this wrapper.
        if (!degenerate) restoreNeighborImagePositions(state);
        // The maximum keeps the whole wrapper (offset padding + image) inside the block:
        // an offset that pushes the image past the bottom makes the block scroll (John,
        // live testing). The offset is measured from where the float NATURALLY starts
        // (below any earlier float it clears), so the room left is computed from the
        // wrapper's live rendered bottom: however far that sits above the block's bottom
        // is how much further the current offset may grow.
        let maxPx: number | undefined;
        const currentOffsetPx = getInlineImageOffsetPx(state.wrapper);
        if (!degenerate) {
            const wrapperBottom = state.wrapper.getBoundingClientRect().bottom;
            maxPx = currentOffsetPx + (blockBottom - wrapperBottom);
        }
        // In a real layout the offset is target-based (where should the image's top be,
        // given where it is right now), which stays correct across reorders and reflows.
        const offsetPx = clampInlineImageOffset(
            degenerate
                ? state.startOffsetPx + (event.clientY - state.startY)
                : currentOffsetPx +
                      (targetTop - getImageBox(state.wrapper).top),
            maxPx,
        );
        state.wrapper.style.setProperty(kInlineImageOffsetVar, `${offsetPx}px`);
        if (!degenerate) {
            // The maximum above was measured before this move's offset was applied, so a
            // fast move can land a few pixels long; take back any remainder.
            const over =
                state.wrapper.getBoundingClientRect().bottom - blockBottom;
            if (over > 0) {
                state.wrapper.style.setProperty(
                    kInlineImageOffsetVar,
                    `${clampInlineImageOffset(offsetPx - over)}px`,
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
        state.editable.scrollHeight - state.editable.clientHeight >
        state.startScrollOverflow + 1
    ) {
        state.editable.insertBefore(state.wrapper, previousNextSibling);
        setInlineImageDock(state.wrapper, previousDock);
        state.wrapper.style.setProperty(
            kInlineImageOffsetVar,
            `${previousOffsetPx}px`,
        );
        state.dock = previousDock;
        restoreNeighborImagePositions(state);
    }
}

/**
 * Where in the floating cluster an image whose image-box top will be targetTop belongs:
 * after every image whose own image-box top is at or above it. Exported for tests.
 */
export function computeInlineImageClusterIndex(
    targetTop: number,
    otherImageTops: number[],
): number {
    return otherImageTops.filter((top) => top <= targetTop).length;
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
// its natural start). Only restoreNeighborImagePositions uses this.
function nudgeInlineImageOffset(wrapper: HTMLElement, deltaPx: number): void {
    wrapper.style.setProperty(
        kInlineImageOffsetVar,
        `${clampInlineImageOffset(getInlineImageOffsetPx(wrapper) + deltaPx)}px`,
    );
}

// Moves the dragged wrapper to the cluster position its target vertical position calls
// for. Purely a DOM move: the neighbors this displaces are held in place separately by
// restoreNeighborImagePositions, which runs on every move of the drag.
function reorderInFloatingCluster(
    state: IInlineImageDragState,
    targetTop: number,
): void {
    const floats = getFloatingWrappersIn(state.editable);
    if (floats.length < 2) return;
    const currentIndex = floats.indexOf(state.wrapper);
    if (currentIndex < 0) return;
    const others = floats.filter((w) => w !== state.wrapper);
    const desiredIndex = computeInlineImageClusterIndex(
        targetTop,
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
    const drag = dragState;
    const state = resizeState ?? dragState;
    resizeState = undefined;
    dragState = undefined;
    removePointerListeners();
    if (!state) return;
    state.wrapper.ownerDocument.body.classList.remove(
        kInlineImageDraggingClass,
    );
    if (!state.started) return; // it was a click: nothing changed, nothing prepared
    if (drag && drag === state) {
        restoreNeighborImagePositions(drag);
        normalizeFloatingClusterOrder(drag.editable, drag.editableBox);
    }
    commitInlineImageChange(state.wrapper, state.editable);
}

// Rewrites the floating cluster's DOM order to match the images' visual order, keeping
// every image exactly where it is. DOM order is what each float clears past, so an order
// that contradicts the visual order (which can arrive from a book edited before this
// rule, or from insertions) quietly limits where the images can go; a drag is the moment
// the user is rearranging things, so its end is the moment to straighten this out.
function normalizeFloatingClusterOrder(
    editable: HTMLElement,
    editableBox: IBox,
): void {
    if (!(editableBox.height > 0)) return; // no real layout to measure (jsdom)
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
            nudgeInlineImageOffset(w, wanted - current);
        });
    }
}

// Puts every image the drag did NOT move back exactly where it was when the drag began,
// as far as the new geometry allows (an image cannot rise above its natural start, so a
// neighbor whose old spot is now occupied lands as close below it as floats permit). Two
// passes, because correcting an earlier float shifts where the later ones start.
function restoreNeighborImagePositions(state: IInlineImageDragState): void {
    if (state.editableBox.height <= 0) return; // no real layout to measure (jsdom)
    for (let pass = 0; pass < 2; pass++) {
        getFloatingWrappersIn(state.editable).forEach((wrapper) => {
            if (wrapper === state.wrapper) return;
            const wantedTop = state.neighborImageTops.get(wrapper);
            if (wantedTop === undefined) return;
            const currentTop = getImageBox(wrapper).top;
            if (Math.abs(wantedTop - currentTop) <= 1) return;
            nudgeInlineImageOffset(wrapper, wantedTop - currentTop);
        });
    }
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
