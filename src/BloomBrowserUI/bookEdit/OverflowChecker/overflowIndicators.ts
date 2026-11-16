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
import { pageScrollsInsteadOfOverflowing } from "../js/scrollingLayouts";
import { kBloomButtonClass } from "../toolbox/canvas/canvasElementPageBridge";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";

const kLayerId = "bloom-overflow-indicator-layer";

// The boxes that OverflowChecker checks: text boxes, and the textareas that a calendar's day
// cells use. Keep in step with getElementsThatCanOverflowOrNeedToBeResized in OverflowChecker.ts.
const kBoxSelector = ".bloom-editable, textarea";

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

// Scrolling a box with too much text moves where its text is cut off.
function respondToBoxScrolling(e: Event) {
    if (
        e.target instanceof HTMLElement &&
        e.target.matches(`:is(${kBoxSelector}).overflow`)
    ) {
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

// The boxes whose overflow is shown, in document order: those with too much text in themselves
// (class "overflow") where showsTooMuchText says so, and those that push past a container
// ("thisOverflowingParent"); a box can be both. Boxes that are not displayed are left out: one in
// a language the book no longer shows can still carry the "overflow" class it was saved with,
// since OverflowChecker checks only the boxes that are showing.
function getOverflowingBoxes(
    page: HTMLElement,
    pageScrolls: boolean,
): HTMLElement[] {
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            `:is(${kBoxSelector}):is(.overflow, .thisOverflowingParent)`,
        ),
    ).filter(
        (box) =>
            box.getClientRects().length > 0 &&
            (showsTooMuchText(box, pageScrolls) ||
                box.classList.contains("thisOverflowingParent")),
    );
}

// Whether to show that `box` has too much text in itself. It is not shown on pages that scroll
// instead of overflowing (Device and Ebook layouts), except in canvas buttons. Boxes that push
// past their container are shown on every layout.
function showsTooMuchText(box: HTMLElement, pageScrolls: boolean): boolean {
    return (
        box.classList.contains("overflow") &&
        (!pageScrolls || !!box.closest("." + kBloomButtonClass))
    );
}

// Where a box is, in viewport (client) pixels. For a box in a rotated canvas element,
// `rectViewportPx` is where the box would be if the canvas element were upright, and `rotation`
// says how to turn it from there.
interface IPlacement {
    rectViewportPx: DOMRect;
    rotation?: IRotation;
}

// A canvas element's rotation: by `degrees` about its center, from `frameViewportPx`, the
// rectangle it would have upright.
interface IRotation {
    degrees: number;
    frameViewportPx: DOMRect;
}

// Where an element on a page scaled by viewportPxPerLayoutPx is. A rotated element's
// getBoundingClientRect is the upright rectangle around the turned element, so for an element in
// a rotated canvas element this works out where it would be upright instead, from the layout
// offsets (which ignore the rotation) and the canvas element's center (which the rotation does
// not move).
export function placementOf(
    e: HTMLElement,
    viewportPxPerLayoutPx: number,
): IPlacement {
    const canvasElement = e.closest<HTMLElement>("." + kRotatedClass);
    if (!canvasElement) {
        return { rectViewportPx: e.getBoundingClientRect() };
    }
    const boundsViewportPx = canvasElement.getBoundingClientRect();
    const widthViewportPx = canvasElement.offsetWidth * viewportPxPerLayoutPx;
    const heightViewportPx = canvasElement.offsetHeight * viewportPxPerLayoutPx;
    const frameViewportPx = new DOMRect(
        boundsViewportPx.left + (boundsViewportPx.width - widthViewportPx) / 2,
        boundsViewportPx.top + (boundsViewportPx.height - heightViewportPx) / 2,
        widthViewportPx,
        heightViewportPx,
    );
    // The element's position inside the canvas element's border box.
    let xLayoutPx = 0;
    let yLayoutPx = 0;
    for (
        let a: HTMLElement = e;
        a !== canvasElement;
        a = a.offsetParent as HTMLElement
    ) {
        const parent = a.offsetParent as HTMLElement;
        xLayoutPx += a.offsetLeft + parent.clientLeft;
        yLayoutPx += a.offsetTop + parent.clientTop;
    }
    return {
        rectViewportPx: new DOMRect(
            frameViewportPx.left + xLayoutPx * viewportPxPerLayoutPx,
            frameViewportPx.top + yLayoutPx * viewportPxPerLayoutPx,
            e.offsetWidth * viewportPxPerLayoutPx,
            e.offsetHeight * viewportPxPerLayoutPx,
        ),
        rotation: {
            degrees: getCanvasElementRotation(canvasElement),
            frameViewportPx,
        },
    };
}

// What is needed to draw one box with more text than it can show.
interface IBoxTooFull extends IPlacement {
    // The y of the bottom of what the box shows: the inside of its bottom border, or for a box
    // that pushes past a small container such as a table cell, the bottom of the container's
    // content, where the container cuts the text off.
    bottomViewportPx: number;
    // The y where the text is cut off: everything below it would not show if the box were not
    // scrolled. It is bottomViewportPx moved up by how far the box is scrolled, so it stays
    // between the same two lines of text as the box scrolls.
    cutViewportPx: number;
    lineHeightLayoutPx: number;
    background: string;
}

// The bottom of an element at `rectViewportPx`, less its border and, if asked, its padding.
function insideBottomViewportPx(
    e: HTMLElement,
    rectViewportPx: DOMRect,
    viewportPxPerLayoutPx: number,
    lessPadding: boolean,
): number {
    const insetLayoutPx =
        getBorderThickness(e).bottom +
        (lessPadding ? parseFloat(getComputedStyle(e).paddingBottom) : 0);
    return rectViewportPx.bottom - insetLayoutPx * viewportPxPerLayoutPx;
}

// Measures a box placed at `placement` that shows its text down to bottomViewportPx, scrolled up
// by scrollOffsetViewportPx.
function measureBoxTooFull(
    box: HTMLElement,
    placement: IPlacement,
    bottomViewportPx: number,
    scrollOffsetViewportPx: number,
): IBoxTooFull {
    return {
        ...placement,
        bottomViewportPx,
        cutViewportPx: bottomViewportPx - scrollOffsetViewportPx,
        lineHeightLayoutPx: lineHeightLayoutPxOf(box),
        background: backgroundBehind(box),
    };
}

// Clears and redraws every indicator on the page. The drawings collect in a fragment that is not
// in the document, so building them does not make the browser lay the page out again between
// measurements; they all go in the layer at the end.
function refreshOverflowIndicators(): void {
    const page = document.querySelector<HTMLElement>(".bloom-page");
    const layer = getLayer(page);
    if (!page || !layer) {
        return;
    }
    requestChipText();
    const viewportPxPerLayoutPx = EditableDivUtils.getPageScale();
    const draw = LayerDrawer.forLayer(layer);
    const pageScrolls = pageScrollsInsteadOfOverflowing(page);
    // For each container that boxes push past, the lowest bottom of those boxes.
    const pushedPastViewportPx = new Map<HTMLElement, number>();
    for (const box of getOverflowingBoxes(page, pageScrolls)) {
        const placement = placementOf(box, viewportPxPerLayoutPx);
        const tooMuchText = showsTooMuchText(box, pageScrolls);
        if (tooMuchText) {
            drawBoxTooFull(
                measureBoxTooFull(
                    box,
                    placement,
                    insideBottomViewportPx(
                        box,
                        placement.rectViewportPx,
                        viewportPxPerLayoutPx,
                        false,
                    ),
                    box.scrollTop * viewportPxPerLayoutPx,
                ),
                draw,
            );
        }
        if (!box.classList.contains("thisOverflowingParent")) {
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
            pushedPastViewportPx.set(
                container,
                Math.max(
                    pushedPastViewportPx.get(container) ??
                        Number.NEGATIVE_INFINITY,
                    box.getBoundingClientRect().bottom,
                ),
            );
        } else if (!tooMuchText) {
            // A box that pushes past a smaller container, such as a table cell, has more text
            // than its container can show, so it is shown like a box with too much text.
            drawBoxTooFull(
                measureBoxTooFull(
                    box,
                    placement,
                    insideBottomViewportPx(
                        container,
                        placementOf(container, viewportPxPerLayoutPx)
                            .rectViewportPx,
                        viewportPxPerLayoutPx,
                        true,
                    ),
                    0,
                ),
                draw,
            );
        }
    }
    for (const [margin, bottomViewportPx] of pushedPastViewportPx) {
        drawPageTooFull(margin.getBoundingClientRect(), bottomViewportPx, draw);
    }
    layer.replaceChildren(draw.drawings);
}

// Marks where a box's text is cut off: a torn, zigzag line there with a chip on it, and a red
// wash over the lines below it that the box shows when scrolled. In this order, so the chip is
// on top of the torn edge. A box in a rotated canvas element is drawn in a frame turned the same
// way.
function drawBoxTooFull(box: IBoxTooFull, layerDraw: LayerDrawer) {
    const draw = box.rotation ? layerDraw.rotated(box.rotation) : layerDraw;
    const leftLayoutPx = draw.xLayoutPx(box.rectViewportPx.left);
    const widthLayoutPx = draw.lengthLayoutPx(box.rectViewportPx.width);
    const topLayoutPx = draw.yLayoutPx(box.rectViewportPx.top);
    const bottomLayoutPx = draw.yLayoutPx(box.bottomViewportPx);
    const cutLayoutPx = draw.yLayoutPx(box.cutViewportPx);
    // The lines below the cut that are scrolled into view.
    const washTopLayoutPx = Math.max(cutLayoutPx, topLayoutPx);
    if (bottomLayoutPx > washTopLayoutPx) {
        draw.add("bloom-overflow-cutOffText", {
            leftLayoutPx,
            topLayoutPx: washTopLayoutPx,
            widthLayoutPx,
            heightLayoutPx: bottomLayoutPx - washTopLayoutPx,
        });
    }
    if (cutLayoutPx < topLayoutPx) {
        // Scrolled so far that every line in view is cut off; the wash says so.
        return;
    }
    if (cutLayoutPx >= bottomLayoutPx) {
        // Not scrolled: the last line that fits fades into the background, since the text goes
        // on below the edge.
        draw.add("bloom-overflow-fadeLastLine", {
            leftLayoutPx,
            topLayoutPx: cutLayoutPx - box.lineHeightLayoutPx,
            widthLayoutPx,
            heightLayoutPx: box.lineHeightLayoutPx,
        }).style.setProperty("--overflow-background", box.background);
    }
    // Trying the page without the torn line; it may come back. The page thumbnails still have it.
    // A torn, zigzag line where the text is cut off, its teeth cutting into the line above.
    // draw.add("bloom-overflow-tornEdge", {
    //     leftLayoutPx,
    //     topLayoutPx: cutLayoutPx,
    //     widthLayoutPx,
    // }).style.setProperty("--overflow-background", box.background);
    draw.chip(box.rectViewportPx.right, cutLayoutPx, chipText.overflowing.text);
}

// The boxes in the page's margin each fit their text, but together they push past its bottom,
// so the indicator names the page rather than one box: a red line at the margin's bottom, a
// red-striped band over everything that is past it, down to `bottomViewportPx`, and a chip on
// the line saying the page is too full.
function drawPageTooFull(
    marginViewportPx: DOMRect,
    bottomViewportPx: number,
    draw: LayerDrawer,
) {
    const lineLayoutPx = draw.yLayoutPx(marginViewportPx.bottom);
    draw.add("bloom-overflow-pastContainer", {
        leftLayoutPx: draw.xLayoutPx(marginViewportPx.left),
        topLayoutPx: lineLayoutPx,
        widthLayoutPx: draw.lengthLayoutPx(marginViewportPx.width),
        heightLayoutPx: draw.lengthLayoutPx(
            bottomViewportPx - marginViewportPx.bottom,
        ),
    });
    draw.chip(
        marginViewportPx.right,
        lineLayoutPx,
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
    layer.style.left = page.offsetLeft + "px";
    layer.style.top = page.offsetTop + "px";
    layer.style.width = page.offsetWidth + "px";
    layer.style.height = page.offsetHeight + "px";
    return layer;
}

// Builds the drawings, placing each by viewport (client) coordinates converted to the layout
// pixels of the element it goes in: the layer, or a rotated frame in it (see rotated()). The
// conversion is measured on the layer, not the page, because a full-bleed page has a transform
// of its own that the layer does not share. The drawings collect in `drawings` until they are
// put in the layer all at once.
class LayerDrawer {
    private constructor(
        public readonly drawings: DocumentFragment,
        // Where the drawings go: `drawings`, or a rotated frame in it.
        private readonly parent: Node,
        // The viewport coordinates of the parent's top left corner.
        private readonly originViewportPx: { left: number; top: number },
        private readonly viewportPxPerLayoutPx: number,
    ) {}

    // A drawer for the layer itself.
    public static forLayer(layer: HTMLElement): LayerDrawer {
        const layerViewportPx = layer.getBoundingClientRect();
        const drawings = document.createDocumentFragment();
        return new LayerDrawer(
            drawings,
            drawings,
            layerViewportPx,
            layerViewportPx.width / layer.offsetWidth,
        );
    }

    // A drawer for things in a rotated canvas element, which are measured as if it were upright:
    // it draws them in a frame that covers the upright canvas element and is turned the same way.
    public rotated(rotation: IRotation): LayerDrawer {
        const frame = this.add("bloom-overflow-rotatedFrame", {
            leftLayoutPx: this.xLayoutPx(rotation.frameViewportPx.left),
            topLayoutPx: this.yLayoutPx(rotation.frameViewportPx.top),
            widthLayoutPx: this.lengthLayoutPx(rotation.frameViewportPx.width),
            heightLayoutPx: this.lengthLayoutPx(
                rotation.frameViewportPx.height,
            ),
        });
        frame.style.transform = `rotate(${rotation.degrees}deg)`;
        return new LayerDrawer(
            this.drawings,
            frame,
            rotation.frameViewportPx,
            this.viewportPxPerLayoutPx,
        );
    }

    // The parent's x coordinate of a viewport x coordinate.
    public xLayoutPx(xViewportPx: number): number {
        return (
            (xViewportPx - this.originViewportPx.left) /
            this.viewportPxPerLayoutPx
        );
    }

    // The parent's y coordinate of a viewport y coordinate.
    public yLayoutPx(yViewportPx: number): number {
        return (
            (yViewportPx - this.originViewportPx.top) /
            this.viewportPxPerLayoutPx
        );
    }

    // A viewport length in layout pixels.
    public lengthLayoutPx(lengthViewportPx: number): number {
        return lengthViewportPx / this.viewportPxPerLayoutPx;
    }

    // Adds an element with the given class, positioned in the parent's layout pixels.
    public add(
        className: string,
        position: {
            leftLayoutPx: number;
            topLayoutPx: number;
            widthLayoutPx?: number;
            heightLayoutPx?: number;
        },
    ): HTMLElement {
        const e = document.createElement("div");
        e.className = className;
        e.style.left = position.leftLayoutPx + "px";
        e.style.top = position.topLayoutPx + "px";
        if (position.widthLayoutPx !== undefined) {
            e.style.width = position.widthLayoutPx + "px";
        }
        if (position.heightLayoutPx !== undefined) {
            e.style.height = position.heightLayoutPx + "px";
        }
        this.parent.appendChild(e);
        return e;
    }

    // Adds a small pill saying `text`, ending at the viewport x `rightViewportPx` and centered on
    // the line at `topLayoutPx`.
    public chip(rightViewportPx: number, topLayoutPx: number, text: string) {
        this.add("bloom-overflow-chip", {
            leftLayoutPx: this.xLayoutPx(rightViewportPx),
            topLayoutPx,
        }).textContent = text;
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

// The box's line height. (getComputedStyle gives it in pixels even when the stylesheet gives a
// unitless number such as 1.5.)
function lineHeightLayoutPxOf(box: HTMLElement): number {
    const style = getComputedStyle(box.querySelector("p") ?? box);
    const lineHeightLayoutPx = parseFloat(style.lineHeight);
    return isNaN(lineHeightLayoutPx)
        ? parseFloat(style.fontSize) * 1.2
        : lineHeightLayoutPx;
}
