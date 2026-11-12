// Shows the boxes that have more text than fits (BL-17109). OverflowChecker decides which boxes
// overflow, marking them with the classes "overflow" (the box has more text than fits in itself)
// and "thisOverflowingParent" (the box pushes past the bottom of an ancestor, which gets
// "childOverflowingThis"). This module only draws them: the last line that fits fades into the
// background, the bottom edge becomes a torn, zigzag line, and a chip on that edge says how many
// lines do not fit, such as "+3 lines".
// Nothing here touches the contents of the .bloom-page element, which are what gets saved: the
// drawings go in a layer that is a sibling of the page (styled by overflowIndicators.less).

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { pageScrollsInsteadOfOverflowing } from "../js/scrollingLayouts";
import { kBloomButtonClass } from "../toolbox/canvas/canvasElementPageBridge";

const kLayerId = "bloom-overflow-indicator-layer";
const kRedScrollBarClass = "bloom-overflow-scrollBar";

export interface IPastEdgeMeasurement {
    // Words whose middle lies below the edge.
    wordCount: number;
    // Lines of text below the edge, counting empty paragraphs as lines.
    lineCount: number;
}

// Finds the text in `box` that lies below `edgeY`, a y coordinate in the client coordinates of
// the box's window. A word counts as past the edge when its vertical middle is below it, so a line
// that the edge cuts through is counted once, on whichever side most of it is.
// `scrollOffset` is how far the content has been scrolled up out of view, in client pixels;
// it is added back so a box scrolled to show its end is measured as if it were not scrolled.
export function measureTextPastEdge(
    box: HTMLElement,
    edgeY: number,
    scrollOffset = 0,
): IPastEdgeMeasurement {
    const doc = box.ownerDocument;
    const lineRects: DOMRect[] = [];
    let wordCount = 0;
    const isPast = (rect: DOMRect) =>
        (rect.top + rect.bottom) / 2 + scrollOffset > edgeY;

    const walker = doc.createTreeWalker(box, NodeFilter.SHOW_TEXT);
    for (
        let node = walker.nextNode() as Text | null;
        node;
        node = walker.nextNode() as Text | null
    ) {
        if (isInIgnoredElement(node, box)) {
            continue;
        }
        for (const match of node.data.matchAll(/\S+/g)) {
            const range = doc.createRange();
            range.setStart(node, match.index);
            range.setEnd(node, match.index + match[0].length);
            // A word broken across lines (overflow-wrap: anywhere) has several rects.
            const pastRects = Array.from(range.getClientRects()).filter(
                (r) => r.height > 0 && isPast(r),
            );
            if (pastRects.length > 0) {
                wordCount++;
                lineRects.push(...pastRects);
            }
        }
    }

    // Paragraphs with no text still take a line each, and can be all that overflows.
    for (const p of Array.from(box.querySelectorAll("p"))) {
        if (p.textContent?.trim() || isInIgnoredElement(p, box)) {
            continue;
        }
        const rect = p.getBoundingClientRect();
        if (rect.height > 0 && isPast(rect)) {
            lineRects.push(rect);
        }
    }

    return { wordCount, lineCount: countLines(lineRects) };
}

// Counts the distinct lines that a set of rects (words, or empty paragraphs) sit on. Two rects
// are on one line when their vertical middles are closer than half the shorter one's height.
export function countLines(
    rects: { top: number; bottom: number; height: number }[],
): number {
    const sorted = [...rects].sort(
        (a, b) => a.top + a.bottom - (b.top + b.bottom),
    );
    let lines = 0;
    let lastMiddle = Number.NEGATIVE_INFINITY;
    let lastHeight = 0;
    for (const rect of sorted) {
        const middle = (rect.top + rect.bottom) / 2;
        if (middle - lastMiddle >= Math.min(lastHeight, rect.height) / 2) {
            lines++;
            lastMiddle = middle;
            lastHeight = rect.height;
        }
    }
    return lines;
}

// Text in things like the format button or a language label is not part of the box's text.
function isInIgnoredElement(node: Node, box: HTMLElement): boolean {
    for (
        let e = node instanceof Element ? node : node.parentElement;
        e && e !== box;
        e = e.parentElement
    ) {
        const style = e.ownerDocument.defaultView!.getComputedStyle(e);
        if (style.position === "absolute" || style.display === "none") {
            return true;
        }
    }
    return false;
}

// What the chips say, in English until the localized text arrives (see requestChipText).
// {0} is the number of lines.
const chipText = {
    oneLine: "+1 line",
    lines: "+{0} lines",
    pageTooFull: "Page too full",
};
let chipTextRequested = false;

// Asks for the localized text of the chip once, redrawing when each piece arrives.
function requestChipText() {
    if (chipTextRequested) {
        return;
    }
    chipTextRequested = true;
    const ids: Record<keyof typeof chipText, string> = {
        oneLine: "EditTab.Overflow.OneMoreLine",
        lines: "EditTab.Overflow.MoreLines",
        pageTooFull: "EditTab.Overflow.PageTooFull",
    };
    for (const key of Object.keys(ids) as (keyof typeof chipText)[]) {
        theOneLocalizationManager
            .asyncGetText(ids[key], chipText[key], "")
            .done((text) => {
                chipText[key] = text;
                refreshOverflowIndicatorsSoon();
            });
    }
}

// What the chip on the edge says about the text past it. Text that reaches less than halfway
// into a line below the edge still counts as one line, so the chip never says "+0 lines".
export function describeTextPastEdge(m: IPastEdgeMeasurement): string {
    const lines = Math.max(m.lineCount, 1);
    return lines === 1
        ? chipText.oneLine
        : chipText.lines.replace("{0}", lines.toString());
}

let refreshPending = false;
let listening = false;

// Redraws the indicators after the current batch of overflow checks; any number of calls
// before then make one redraw.
export function refreshOverflowIndicatorsSoon(): void {
    if (!listening) {
        listening = true;
        window.addEventListener("resize", refreshOverflowIndicatorsSoon);
        // niceScroll replaces its scroll bars some time after each overflow check, and the new
        // ones need making red again.
        new MutationObserver((mutations) => {
            if (
                mutations.some((m) =>
                    Array.from(m.addedNodes).some(
                        (n) =>
                            n instanceof HTMLElement &&
                            n.classList.contains("nicescroll-rails"),
                    ),
                )
            ) {
                refreshOverflowIndicatorsSoon();
            }
        }).observe(document.body, { childList: true, subtree: true });
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

interface IOverflowingBox {
    box: HTMLElement;
    // The y (client coordinates) of the bottom of the box's padding, which the text should have
    // stayed above.
    edgeY: number;
    measurement: IPastEdgeMeasurement;
}

// The boxes whose overflow is shown, in document order. This leaves out boxes on pages that
// scroll instead of overflowing (Device and Ebook layouts), except in canvas buttons.
export function getOverflowingBoxes(page: HTMLElement): HTMLElement[] {
    const scrolls = pageScrollsInsteadOfOverflowing(page);
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            ".bloom-editable.overflow, .bloom-editable.thisOverflowingParent",
        ),
    ).filter(
        (box) =>
            (!scrolls || !!box.closest("." + kBloomButtonClass)) &&
            !box.closest(".bloom-ignoreOverflow"),
    );
}

// Measures a box that has more text than fits in itself (class "overflow").
function measureOverflowingBox(box: HTMLElement): IOverflowingBox {
    const rect = box.getBoundingClientRect();
    const scale = box.offsetHeight ? rect.height / box.offsetHeight : 1;
    const borderBottom = parseFloat(getComputedStyle(box).borderBottomWidth);
    const edgeY = rect.bottom - borderBottom * scale;
    return {
        box,
        edgeY,
        measurement: measureTextPastEdge(box, edgeY, box.scrollTop * scale),
    };
}

// Clears and redraws every indicator on the page.
export function refreshOverflowIndicators(): void {
    const page = document.querySelector<HTMLElement>(".bloom-page");
    const layer = getLayer(page);
    if (!page || !layer) {
        return;
    }
    layer.replaceChildren();
    page.querySelectorAll("." + kRedScrollBarClass).forEach((rail) =>
        rail.classList.remove(kRedScrollBarClass),
    );
    requestChipText();
    const draw = new LayerDrawer(page, layer);
    // Boxes that push past a container, grouped by the container.
    const pushedPast = new Map<HTMLElement, HTMLElement[]>();
    for (const box of getOverflowingBoxes(page)) {
        if (box.classList.contains("overflow")) {
            const m = measureOverflowingBox(box);
            // In this order, so the chip is on top of the torn edge.
            drawFadeLastLine(m, draw);
            drawTornEdge(m, draw);
            drawMoreChip(m, draw);
            findScrollBar(box)?.classList.add(kRedScrollBarClass);
            continue;
        }
        const container = box.parentElement!.closest<HTMLElement>(
            ".childOverflowingThis",
        );
        if (container) {
            pushedPast.set(container, [
                ...(pushedPast.get(container) ?? []),
                box,
            ]);
        }
    }
    for (const [container, boxes] of pushedPast) {
        drawContainerTooFull(container, boxes, draw);
    }
}

// The boxes in a container (usually the page's margin) each fit their text, but together they
// push past its bottom, so the indicator names the page rather than one box: a red line at the
// container's bottom, a red-striped band over everything that is past it, and a chip on the line
// saying the page is too full.
function drawContainerTooFull(
    container: HTMLElement,
    pushedBoxes: HTMLElement[],
    draw: LayerDrawer,
) {
    const rect = container.getBoundingClientRect();
    const bottom = Math.max(
        ...pushedBoxes.map((b) => b.getBoundingClientRect().bottom),
    );
    draw.add("bloom-overflow-pastContainer", {
        left: draw.x(rect.left),
        top: draw.y(rect.bottom),
        width: draw.length(rect.width),
        height: draw.length(bottom - rect.bottom),
    });
    draw.add(
        "bloom-overflow-moreChip",
        { left: draw.x(rect.right), top: draw.y(rect.bottom) },
        chipText.pageTooFull,
    );
}

// The vertical scroll bar that niceScroll (see niceScrollCleanup.ts) gives a box whose text does
// not fit: it puts its rail beside the box, not inside it, and records no link between them, so
// this finds the rail that sits against the box's right edge.
function findScrollBar(box: HTMLElement): HTMLElement | undefined {
    const rect = box.getBoundingClientRect();
    return Array.from(
        box.ownerDocument.querySelectorAll<HTMLElement>(".nicescroll-rails-vr"),
    ).find((rail) => {
        const r = rail.getBoundingClientRect();
        return (
            r.height > 0 &&
            Math.abs(r.right - rect.right) < 20 &&
            r.top < rect.bottom &&
            r.bottom > rect.top
        );
    });
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

// Places elements on the layer using client coordinates, converting them to the page's own
// unscaled pixels.
class LayerDrawer {
    private readonly pageRect: DOMRect;
    private readonly scale: number;
    public constructor(
        page: HTMLElement,
        private readonly layer: HTMLElement,
    ) {
        this.pageRect = page.getBoundingClientRect();
        this.scale = page.offsetWidth
            ? this.pageRect.width / page.offsetWidth
            : 1;
    }

    public x(clientX: number): number {
        return (clientX - this.pageRect.left) / this.scale;
    }
    public y(clientY: number): number {
        return (clientY - this.pageRect.top) / this.scale;
    }
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
        this.layer.appendChild(e);
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

// The last line that fits fades into the background. Anything showing below the edge (when the
// box pushes past an ancestor) is painted over.
function drawFadeLastLine(m: IOverflowingBox, draw: LayerDrawer) {
    const rect = m.box.getBoundingClientRect();
    const lineHeight = draw.length(lineHeightOf(m.box));
    const bottom = Math.max(draw.y(rect.bottom), draw.y(m.edgeY));
    const top = draw.y(m.edgeY) - lineHeight;
    const fade = draw.add("bloom-overflow-fadeLastLine", {
        left: draw.x(rect.left),
        top,
        width: draw.length(rect.width),
        height: bottom - top,
    });
    const solidFrom = Math.round((lineHeight / (bottom - top)) * 100);
    fade.style.background = `linear-gradient(to bottom, transparent, ${backgroundBehind(m.box)} ${solidFrom}%)`;
}

// The box's line height in client pixels.
function lineHeightOf(box: HTMLElement): number {
    const style = getComputedStyle(box.querySelector("p") ?? box);
    let lineHeight = parseFloat(style.lineHeight);
    if (isNaN(lineHeight)) {
        lineHeight = parseFloat(style.fontSize) * 1.2;
    }
    const rect = box.getBoundingClientRect();
    return lineHeight * (box.offsetHeight ? rect.height / box.offsetHeight : 1);
}

// The bottom edge becomes a torn, zigzag line, its teeth cutting into the bottom of the box.
function drawTornEdge(m: IOverflowingBox, draw: LayerDrawer) {
    const rect = m.box.getBoundingClientRect();
    const torn = draw.add("bloom-overflow-tornEdge", {
        left: draw.x(rect.left),
        top: draw.y(m.edgeY),
        width: draw.length(rect.width),
    });
    torn.style.setProperty("--torn-background", backgroundBehind(m.box));
}

// A small pill on the bottom edge saying how many lines do not fit.
function drawMoreChip(m: IOverflowingBox, draw: LayerDrawer) {
    const rect = m.box.getBoundingClientRect();
    draw.add(
        "bloom-overflow-moreChip",
        { left: draw.x(rect.right), top: draw.y(m.edgeY) },
        describeTextPastEdge(m.measurement),
    );
}
