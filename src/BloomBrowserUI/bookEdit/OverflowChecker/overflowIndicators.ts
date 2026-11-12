// Shows the boxes that have more text than fits (BL-17109). OverflowChecker decides which boxes
// overflow, marking them with the classes "overflow" (the box has more text than fits in itself)
// and "thisOverflowingParent" (the box pushes past the bottom of an ancestor, which gets
// "childOverflowingThis"). This module only draws them. A box with too much text, or one that
// pushes past a small container such as a table cell, gets a torn, zigzag line where its text is
// cut off, with an "Overflowing" chip on it. The line scrolls with the text, and the lines below it
// that the box shows when scrolled get a red wash; before the box is scrolled, the line is its
// bottom edge and the last line that fits fades out. Boxes that together push past the page's
// margin get a red-striped band over what is past it and a "Page overflowing" chip.
// Nothing here touches the contents of the .bloom-page element, which are what gets saved: the
// drawings go in a layer that is a sibling of the page (styled by overflowIndicators.less).

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getBorderThickness } from "../../utils/elementUtils";
import {
    getCanvasElementRotation,
    kRotatedClass,
} from "../js/canvasElementManager/canvasElementRotation";
import { EditableDivUtils } from "../js/editableDivUtils";
import {
    kScrollTopVariable,
    pageScrollsInsteadOfOverflowing,
} from "../js/scrollingLayouts";
import { kBloomButtonClass } from "../toolbox/canvas/canvasElementPageBridge";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";

const kLayerId = "bloom-overflow-indicator-layer";

// What the chips say: each one's l10n id and its text, in English until the localized text
// arrives (see requestChipText).
const chipText = {
    overflowing: { id: "EditTab.Overflow.Overflowing", text: "Overflowing" },
    pageOverflowing: {
        id: "EditTab.Overflow.PageOverflowing",
        text: "Page overflowing",
    },
};
let chipTextRequested = false;

// Asks for the localized text of the chips once, redrawing when each piece arrives.
function requestChipText() {
    if (chipTextRequested) {
        return;
    }
    chipTextRequested = true;
    for (const chip of Object.values(chipText)) {
        theOneLocalizationManager
            .asyncGetText(chip.id, chip.text, "")
            .done((text) => {
                chip.text = text;
                refreshOverflowIndicatorsSoon();
            });
    }
}

let refreshPending = false;
let refreshAfterChangesTimer: ReturnType<typeof setTimeout> | undefined;
let listening = false;

// Redraws the indicators after the current batch of overflow checks; any number of calls
// before then make one redraw.
export function refreshOverflowIndicatorsSoon(): void {
    if (!listening) {
        listening = true;
        window.addEventListener("resize", refreshOverflowIndicatorsSoon);
        // A scroll event does not bubble, so this listens in the capture phase.
        document.addEventListener("scroll", respondToBoxScrolling, true);
        new MutationObserver(respondToPageChanges).observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ["style"],
        });
    }
    if (refreshPending) {
        return;
    }
    refreshPending = true;
    requestAnimationFrame(() => {
        refreshPending = false;
        refreshOverflowIndicators();
    });
}

// When a text box scrolls, records how far in kScrollTopVariable, which keeps its language label
// (editMode.less) at the bottom of what the box shows; removeNiceScrollArtifacts takes it out
// before saving. Scrolling a box with too much text also moves where its text is cut off.
function respondToBoxScrolling(e: Event) {
    if (
        !(e.target instanceof HTMLElement) ||
        !e.target.matches(".bloom-editable")
    ) {
        return;
    }
    const box = e.target;
    if (box.scrollTop) {
        box.style.setProperty(kScrollTopVariable, box.scrollTop + "px");
    } else {
        box.style.removeProperty(kScrollTopVariable);
    }
    if (box.classList.contains("overflow")) {
        refreshOverflowIndicatorsSoon();
    }
}

// Keeps the drawings up to date when no overflow check runs. When a canvas element is moved or
// resized, or something is removed from the page, everything is redrawn once the
// changes stop for a moment, so that a drag does not redraw on every frame. Changes inside the
// layer itself are ignored, so a redraw never causes another. So are a node added and removed in
// one batch, like the clone that OverflowChecker measures a box with, and niceScroll adding and
// removing its scroll bars, which the drawings do not depend on.
function respondToPageChanges(mutations: MutationRecord[]) {
    let pageChanged = false;
    const addedInThisBatch = new Set(
        mutations.flatMap((m) => Array.from(m.addedNodes)),
    );
    for (const m of mutations) {
        const target = m.target instanceof Element ? m.target : undefined;
        if (
            !target ||
            target.closest("#" + kLayerId) ||
            target.closest(".nicescroll-rails")
        ) {
            continue;
        }
        if (m.type === "attributes") {
            pageChanged ||= target.matches(kCanvasElementSelector);
            continue;
        }
        pageChanged ||=
            Array.from(m.removedNodes).some(
                (n) =>
                    !addedInThisBatch.has(n) &&
                    !(
                        n instanceof Element &&
                        n.classList.contains("nicescroll-rails")
                    ),
            ) && !!target.closest(".bloom-page");
    }
    if (pageChanged) {
        clearTimeout(refreshAfterChangesTimer);
        refreshAfterChangesTimer = setTimeout(
            refreshOverflowIndicatorsSoon,
            150,
        );
    }
}

// The boxes whose overflow is shown, in document order. A box with too much text in itself is
// left out on pages that scroll instead of overflowing (Device and Ebook layouts), except in
// canvas buttons; boxes that together push past their container are shown on every layout.
// Boxes that are not displayed are left out: one in a language the book no longer shows can
// still carry the "overflow" class it was saved with, since OverflowChecker checks only the
// boxes that are showing.
function getOverflowingBoxes(page: HTMLElement): HTMLElement[] {
    const scrolls = pageScrollsInsteadOfOverflowing(page);
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            ".bloom-editable.overflow, .bloom-editable.thisOverflowingParent",
        ),
    ).filter(
        (box) =>
            box.getClientRects().length > 0 &&
            (!scrolls ||
                !box.classList.contains("overflow") ||
                !!box.closest("." + kBloomButtonClass)),
    );
}

// Where a box is, in client coordinates. For a box in a rotated canvas element, `rect` is where
// the box would be if the canvas element were upright, and `rotation` says how to turn it from
// there.
interface IPlacement {
    rect: DOMRect;
    rotation?: IRotation;
}

// A canvas element's rotation: by `degrees` about its center, from `frame`, the client rectangle
// it would have upright.
interface IRotation {
    degrees: number;
    frame: DOMRect;
}

// Where a box is. A rotated element's getBoundingClientRect is the upright rectangle around the
// turned element, so for a box in a rotated canvas element this works out where the box would be
// upright instead, from the layout offsets (which ignore the rotation) and the canvas element's
// center (which the rotation does not move).
export function placementOf(box: HTMLElement, page: HTMLElement): IPlacement {
    const canvasElement = box.closest<HTMLElement>("." + kRotatedClass);
    if (!canvasElement) {
        return { rect: box.getBoundingClientRect() };
    }
    const scale = page.getBoundingClientRect().width / page.offsetWidth;
    const bounds = canvasElement.getBoundingClientRect();
    const width = canvasElement.offsetWidth * scale;
    const height = canvasElement.offsetHeight * scale;
    const frame = new DOMRect(
        bounds.left + (bounds.width - width) / 2,
        bounds.top + (bounds.height - height) / 2,
        width,
        height,
    );
    // The box's position inside the canvas element's border box, in page pixels.
    let x = 0;
    let y = 0;
    for (
        let e: HTMLElement = box;
        e !== canvasElement;
        e = e.offsetParent as HTMLElement
    ) {
        const parent = e.offsetParent as HTMLElement;
        x += e.offsetLeft + parent.clientLeft;
        y += e.offsetTop + parent.clientTop;
    }
    return {
        rect: new DOMRect(
            frame.left + x * scale,
            frame.top + y * scale,
            box.offsetWidth * scale,
            box.offsetHeight * scale,
        ),
        rotation: { degrees: getCanvasElementRotation(canvasElement), frame },
    };
}

// What is needed to draw one box with more text than it can show.
interface IBoxTooFull {
    rect: DOMRect;
    // Set when the box is in a rotated canvas element; the other coordinates are then those of
    // the box upright.
    rotation?: IRotation;
    // The y (client coordinates) of the bottom of what the box shows: the inside of its bottom
    // border, or for a box that pushes past a small container such as a table cell, the bottom
    // of the container's content, where the container cuts the text off.
    bottomY: number;
    // The y (client coordinates) where the text is cut off: everything below it would not show
    // if the box were not scrolled. It is bottomY moved up by how far the box is scrolled, so it
    // stays between the same two lines of text as the box scrolls.
    cutY: number;
    // The box's line height in page pixels.
    lineHeight: number;
    background: string;
}

// The bottom of an element whose client rectangle is `rect`, less its border and, if asked, its
// padding, in client coordinates.
function insideBottom(
    e: HTMLElement,
    rect: DOMRect,
    scale: number,
    lessPadding: boolean,
): number {
    const inset =
        getBorderThickness(e).bottom +
        (lessPadding ? parseFloat(getComputedStyle(e).paddingBottom) : 0);
    return rect.bottom - inset * scale;
}

// Measures a box placed at `placement` that shows its text down to bottomY, scrolled up by
// scrollOffset client pixels.
function measureBoxTooFull(
    box: HTMLElement,
    placement: IPlacement,
    bottomY: number,
    scrollOffset: number,
): IBoxTooFull {
    return {
        rect: placement.rect,
        rotation: placement.rotation,
        bottomY,
        cutY: bottomY - scrollOffset,
        lineHeight: lineHeightOf(box),
        background: backgroundBehind(box),
    };
}

// Clears and redraws every indicator on the page. Everything is measured first and drawn after,
// so that adding the drawings does not make the browser lay the page out again between
// measurements.
function refreshOverflowIndicators(): void {
    const page = document.querySelector<HTMLElement>(".bloom-page");
    const layer = getLayer(page);
    if (!page || !layer) {
        return;
    }
    requestChipText();
    const scale = EditableDivUtils.getPageScale();
    const boxesTooFull: IBoxTooFull[] = [];
    // For each container that boxes push past, the lowest bottom of those boxes.
    const pushedPast = new Map<HTMLElement, number>();
    for (const box of getOverflowingBoxes(page)) {
        if (box.classList.contains("overflow")) {
            const placement = placementOf(box, page);
            boxesTooFull.push(
                measureBoxTooFull(
                    box,
                    placement,
                    insideBottom(box, placement.rect, scale, false),
                    box.scrollTop * scale,
                ),
            );
            continue;
        }
        const container = box.parentElement!.closest<HTMLElement>(
            ".childOverflowingThis",
        );
        // A container with the class "bloom-ignoreOverflow", such as an arithmetic page's row of
        // numbers, is not marked when boxes push past it. A box in it with too much text in
        // itself is still marked, above.
        if (
            !container ||
            container.classList.contains("bloom-ignoreOverflow")
        ) {
            continue;
        }
        if (container.classList.contains("marginBox")) {
            pushedPast.set(
                container,
                Math.max(
                    pushedPast.get(container) ?? Number.NEGATIVE_INFINITY,
                    box.getBoundingClientRect().bottom,
                ),
            );
            continue;
        }
        // A box that pushes past a smaller container, such as a table cell, has more text than
        // its container can show, so it is shown like a box with too much text.
        // (Tables and the other small containers cannot be rotated.)
        boxesTooFull.push(
            measureBoxTooFull(
                box,
                { rect: box.getBoundingClientRect() },
                insideBottom(
                    container,
                    container.getBoundingClientRect(),
                    scale,
                    true,
                ),
                0,
            ),
        );
    }
    const pastMargin = Array.from(pushedPast, ([container, bottom]) => ({
        rect: container.getBoundingClientRect(),
        bottom,
    }));

    const draw = LayerDrawer.forLayer(layer);
    for (const box of boxesTooFull) {
        drawBoxTooFull(box, box.rotation ? draw.rotated(box.rotation) : draw);
    }
    for (const margin of pastMargin) {
        drawPageTooFull(margin.rect, margin.bottom, draw);
    }
    layer.replaceChildren(draw.drawings);
}

// Marks where a box's text is cut off: a torn, zigzag line there with a chip on it, and a red
// wash over the lines below it that the box shows when scrolled. In this order, so the chip is
// on top of the torn edge.
function drawBoxTooFull(box: IBoxTooFull, draw: LayerDrawer) {
    const left = draw.x(box.rect.left);
    const width = draw.length(box.rect.width);
    const top = draw.y(box.rect.top);
    const bottom = draw.y(box.bottomY);
    const cut = draw.y(box.cutY);
    // The lines below the cut that are scrolled into view.
    const washTop = Math.max(cut, top);
    if (bottom > washTop) {
        draw.add("bloom-overflow-cutOffText", {
            left,
            top: washTop,
            width,
            height: bottom - washTop,
        });
    }
    if (cut < top) {
        // Scrolled so far that every line in view is cut off; the wash says so.
        return;
    }
    if (cut >= bottom) {
        // Not scrolled: the last line that fits fades into the background, since the text goes
        // on below the edge.
        draw.add("bloom-overflow-fadeLastLine", {
            left,
            top: cut - box.lineHeight,
            width,
            height: box.lineHeight,
        }).style.background =
            `linear-gradient(to bottom, transparent, ${box.background})`;
    }
    // A torn, zigzag line where the text is cut off, its teeth cutting into the line above.
    draw.add("bloom-overflow-tornEdge", {
        left,
        top: cut,
        width,
    }).style.setProperty("--torn-background", box.background);
    // A small pill on the cut saying the box is overflowing.
    draw.add(
        "bloom-overflow-chip",
        { left: draw.x(box.rect.right), top: cut },
        chipText.overflowing.text,
    );
}

// The boxes in the page's margin each fit their text, but together they push past its bottom,
// so the indicator names the page rather than one box: a red line at the margin's bottom, a
// red-striped band over everything that is past it, down to `bottom`, and a chip on the line
// saying the page is too full.
function drawPageTooFull(margin: DOMRect, bottom: number, draw: LayerDrawer) {
    draw.add("bloom-overflow-pastContainer", {
        left: draw.x(margin.left),
        top: draw.y(margin.bottom),
        width: draw.length(margin.width),
        height: draw.length(bottom - margin.bottom),
    });
    draw.add(
        "bloom-overflow-chip",
        { left: draw.x(margin.right), top: draw.y(margin.bottom) },
        chipText.pageOverflowing.text,
    );
}

// The layer that holds the drawings: an absolutely positioned sibling of the page that covers it
// exactly. Being a sibling, it is scaled and scrolled with the page but not saved.
function getLayer(page: HTMLElement | null): HTMLElement | undefined {
    let layer = document.getElementById(kLayerId);
    if (!page) {
        layer?.remove();
        return undefined;
    }
    if (!layer) {
        layer = document.createElement("div");
        layer.id = kLayerId;
    }
    if (layer.previousElementSibling !== page) {
        page.after(layer);
    }
    const left = page.offsetLeft;
    const top = page.offsetTop;
    const width = page.offsetWidth;
    const height = page.offsetHeight;
    layer.style.left = left + "px";
    layer.style.top = top + "px";
    layer.style.width = width + "px";
    layer.style.height = height + "px";
    return layer;
}

// Builds the drawings, placing each by client coordinates converted to the pixels of the element
// it goes in: the layer, or a rotated frame in it (see rotated()). The conversion is measured on
// the layer, not the page, because a full-bleed page has a transform of its own that the layer
// does not share. The drawings collect in `drawings` until they are put in the layer all at once.
class LayerDrawer {
    public readonly drawings: DocumentFragment;
    private constructor(
        // Where the drawings go: `drawings`, or a rotated frame in it.
        private readonly parent: Node,
        // The client coordinates of the parent's top left corner.
        private readonly origin: { left: number; top: number },
        // Client pixels per layer pixel.
        private readonly scale: number,
        drawings: DocumentFragment,
    ) {
        this.drawings = drawings;
    }

    // A drawer for the layer itself.
    public static forLayer(layer: HTMLElement): LayerDrawer {
        const layerRect = layer.getBoundingClientRect();
        const drawings = document.createDocumentFragment();
        return new LayerDrawer(
            drawings,
            layerRect,
            layerRect.width / layer.offsetWidth,
            drawings,
        );
    }

    // A drawer for things in a rotated canvas element, which are measured as if it were upright:
    // it draws them in a frame that covers the upright canvas element and is turned the same way.
    public rotated(rotation: IRotation): LayerDrawer {
        const frame = this.add("bloom-overflow-rotatedFrame", {
            left: this.x(rotation.frame.left),
            top: this.y(rotation.frame.top),
            width: this.length(rotation.frame.width),
            height: this.length(rotation.frame.height),
        });
        frame.style.transform = `rotate(${rotation.degrees}deg)`;
        return new LayerDrawer(
            frame,
            rotation.frame,
            this.scale,
            this.drawings,
        );
    }

    // The parent's x coordinate of a client x coordinate.
    public x(clientX: number): number {
        return (clientX - this.origin.left) / this.scale;
    }

    // The parent's y coordinate of a client y coordinate.
    public y(clientY: number): number {
        return (clientY - this.origin.top) / this.scale;
    }

    // A client length in the layer's pixels.
    public length(clientLength: number): number {
        return clientLength / this.scale;
    }

    // Adds an element with the given class, positioned in page pixels.
    public add(
        className: string,
        position: {
            left: number;
            top: number;
            width?: number;
            height?: number;
        },
        text?: string,
    ): HTMLElement {
        const e = document.createElement("div");
        e.className = className;
        e.style.left = position.left + "px";
        e.style.top = position.top + "px";
        if (position.width !== undefined) {
            e.style.width = position.width + "px";
        }
        if (position.height !== undefined) {
            e.style.height = position.height + "px";
        }
        if (text) {
            e.textContent = text;
        }
        this.parent.appendChild(e);
        return e;
    }
}

// The color showing behind a box, for painting over its text.
function backgroundBehind(e: HTMLElement): string {
    for (let a: HTMLElement | null = e; a; a = a.parentElement) {
        const color = getComputedStyle(a).backgroundColor;
        if (color && color !== "transparent" && !/,\s*0\)$/.test(color)) {
            return color;
        }
    }
    return "white";
}

// The box's line height in page pixels.
function lineHeightOf(box: HTMLElement): number {
    const style = getComputedStyle(box.querySelector("p") ?? box);
    const lineHeight = parseFloat(style.lineHeight);
    return isNaN(lineHeight) ? parseFloat(style.fontSize) * 1.2 : lineHeight;
}
