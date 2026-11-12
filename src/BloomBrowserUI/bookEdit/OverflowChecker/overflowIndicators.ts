// Shows the boxes that have more text than fits (BL-17109). OverflowChecker decides which boxes
// overflow, marking them with the classes "overflow" (the box has more text than fits in itself)
// and "thisOverflowingParent" (the box pushes past the bottom of an ancestor, which gets
// "childOverflowingThis"). This module only draws them. A box with too much text, or one that
// pushes past a small container such as a table cell, gets a faded last line, a torn, zigzag
// bottom edge, and a chip on that edge saying how many lines do not fit, such as "+3 lines"; its
// scroll bar turns red. Boxes that together push past the page's margin get a red-striped band
// over what is past it and a "Page too full" chip.
// Nothing here touches the contents of the .bloom-page element, which are what gets saved: the
// drawings go in a layer that is a sibling of the page (styled by overflowIndicators.less).

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getBorderThickness } from "../../utils/elementUtils";
import { EditableDivUtils } from "../js/editableDivUtils";
import { pageScrollsInsteadOfOverflowing } from "../js/scrollingLayouts";
import { kBloomButtonClass } from "../toolbox/canvas/canvasElementPageBridge";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";

const kLayerId = "bloom-overflow-indicator-layer";
const kRedScrollBarClass = "bloom-overflow-scrollBar";

// Counts the lines of text in `box` that lie below `edgeY`, a y coordinate in the client
// coordinates of the box's window, counting empty paragraphs as lines. A word counts as past the
// edge when its vertical middle is below it, so a line that the edge cuts through is counted
// once, on whichever side most of it is.
// `scrollOffset` is how far the content has been scrolled up out of view, in client pixels;
// it is added back so a box scrolled to show its end is measured as if it were not scrolled.
export function countLinesPastEdge(
    box: HTMLElement,
    edgeY: number,
    scrollOffset = 0,
): number {
    const doc = box.ownerDocument;
    const lineRects: DOMRect[] = [];
    const isPast = (rect: DOMRect) =>
        (rect.top + rect.bottom) / 2 + scrollOffset > edgeY;

    const walker = doc.createTreeWalker(box, NodeFilter.SHOW_TEXT);
    for (
        let node = walker.nextNode() as Text | null;
        node;
        node = walker.nextNode() as Text | null
    ) {
        // Text that ends above the edge has no word past it; most of a box's text is like that,
        // so this saves measuring it word by word.
        const nodeRange = doc.createRange();
        nodeRange.selectNodeContents(node);
        if (
            nodeRange.getBoundingClientRect().bottom + scrollOffset <= edgeY ||
            isInIgnoredElement(node, box)
        ) {
            continue;
        }
        for (const match of node.data.matchAll(/\S+/g)) {
            const range = doc.createRange();
            range.setStart(node, match.index);
            range.setEnd(node, match.index + match[0].length);
            // A word broken across lines (overflow-wrap: anywhere) has several rects.
            lineRects.push(
                ...Array.from(range.getClientRects()).filter(
                    (r) => r.height > 0 && isPast(r),
                ),
            );
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

    // A line break can start a line that has no words, such as the blank lines of
    // "Title<br><br><br>". A break on a line with words adds nothing, since countLines puts it on
    // that line.
    for (const br of Array.from(box.querySelectorAll("br"))) {
        if (isInIgnoredElement(br, box)) {
            continue;
        }
        const range = doc.createRange();
        range.selectNode(br);
        lineRects.push(
            ...Array.from(range.getClientRects()).filter(
                (r) => r.height > 0 && isPast(r),
            ),
        );
    }

    return countLines(lineRects);
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

// What the chips say: each one's l10n id and its text, in English until the localized text
// arrives (see requestChipText). {0} is the number of lines.
const chipText = {
    oneLine: { id: "EditTab.Overflow.OneMoreLine", text: "+1 line" },
    lines: { id: "EditTab.Overflow.MoreLines", text: "+{0} lines" },
    pageTooFull: { id: "EditTab.Overflow.PageTooFull", text: "Page too full" },
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

// What the chip on the edge says about the lines past it. Text that reaches less than halfway
// into a line below the edge still counts as one line, so the chip never says "+0 lines".
export function describeLinesPastEdge(lineCount: number): string {
    const lines = Math.max(lineCount, 1);
    return lines === 1
        ? chipText.oneLine.text
        : chipText.lines.text.replace("{0}", lines.toString());
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

// Keeps the drawings up to date when no overflow check runs. When niceScroll replaces its scroll
// bars (some time after each check), the new ones are made red again. When a canvas element is
// moved or resized, or something is removed from the page, everything is redrawn once the
// changes stop for a moment, so that a drag does not redraw on every frame. Changes inside the
// layer itself are ignored, so a redraw never causes another.
function respondToPageChanges(mutations: MutationRecord[]) {
    let railsAdded = false;
    let pageChanged = false;
    for (const m of mutations) {
        const target = m.target instanceof Element ? m.target : undefined;
        if (!target || target.closest("#" + kLayerId)) {
            continue;
        }
        if (m.type === "attributes") {
            pageChanged ||= target.matches(kCanvasElementSelector);
            continue;
        }
        railsAdded ||= Array.from(m.addedNodes).some(
            (n) =>
                n instanceof HTMLElement &&
                n.classList.contains("nicescroll-rails"),
        );
        pageChanged ||=
            m.removedNodes.length > 0 && !!target.closest(".bloom-page");
    }
    if (railsAdded) {
        const page = document.querySelector<HTMLElement>(".bloom-page");
        if (page) {
            markRedScrollBars(page);
        }
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
            !box.closest(".bloom-ignoreOverflow") &&
            (!scrolls ||
                !box.classList.contains("overflow") ||
                !!box.closest("." + kBloomButtonClass)),
    );
}

// What is needed to draw one box with more text than it can show.
interface IBoxTooFull {
    rect: DOMRect;
    // The y (client coordinates) that the text should have stayed above: the bottom of the box's
    // padding, or for a box that pushes past a small container such as a table cell, the bottom
    // of the container's content, where the container cuts the text off.
    edgeY: number;
    lineCount: number;
    // The box's line height in page pixels.
    lineHeight: number;
    background: string;
}

// The bottom of an element, less its border and, if asked, its padding, in client coordinates.
function insideBottom(
    e: HTMLElement,
    scale: number,
    lessPadding: boolean,
): number {
    const inset =
        getBorderThickness(e).bottom +
        (lessPadding ? parseFloat(getComputedStyle(e).paddingBottom) : 0);
    return e.getBoundingClientRect().bottom - inset * scale;
}

// Measures a box whose text should have stayed above edgeY.
function measureBoxTooFull(
    box: HTMLElement,
    edgeY: number,
    scrollOffset: number,
): IBoxTooFull {
    return {
        rect: box.getBoundingClientRect(),
        edgeY,
        lineCount: countLinesPastEdge(box, edgeY, scrollOffset),
        lineHeight: lineHeightOf(box),
        background: backgroundBehind(box),
    };
}

// Makes red the scroll bars of the boxes with more text than fits.
function markRedScrollBars(page: HTMLElement) {
    // niceScroll may put a rail outside the page, so look in the whole document.
    document
        .querySelectorAll("." + kRedScrollBarClass)
        .forEach((rail) => rail.classList.remove(kRedScrollBarClass));
    for (const box of getOverflowingBoxes(page)) {
        if (box.classList.contains("overflow")) {
            findScrollBar(box)?.classList.add(kRedScrollBarClass);
        }
    }
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
            boxesTooFull.push(
                measureBoxTooFull(
                    box,
                    insideBottom(box, scale, false),
                    box.scrollTop * scale,
                ),
            );
            continue;
        }
        const container = box.parentElement!.closest<HTMLElement>(
            ".childOverflowingThis",
        );
        if (!container) {
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
        boxesTooFull.push(
            measureBoxTooFull(box, insideBottom(container, scale, true), 0),
        );
    }
    const pastMargin = Array.from(pushedPast, ([container, bottom]) => ({
        rect: container.getBoundingClientRect(),
        bottom,
    }));

    const draw = new LayerDrawer(layer);
    for (const box of boxesTooFull) {
        drawBoxTooFull(box, draw);
    }
    for (const margin of pastMargin) {
        drawPageTooFull(margin.rect, margin.bottom, draw);
    }
    layer.replaceChildren(draw.drawings);
    markRedScrollBars(page);
}

// The faded last line, torn edge and line count of a box with more text than it can show. In
// this order, so the chip is on top of the torn edge.
function drawBoxTooFull(box: IBoxTooFull, draw: LayerDrawer) {
    const left = draw.x(box.rect.left);
    const width = draw.length(box.rect.width);
    const edge = draw.y(box.edgeY);
    // The last line that fits fades into the background.
    draw.add("bloom-overflow-fadeLastLine", {
        left,
        top: edge - box.lineHeight,
        width,
        height: box.lineHeight,
    }).style.background =
        `linear-gradient(to bottom, transparent, ${box.background})`;
    // The bottom edge becomes a torn, zigzag line, its teeth cutting into the bottom of the box.
    draw.add("bloom-overflow-tornEdge", {
        left,
        top: edge,
        width,
    }).style.setProperty("--torn-background", box.background);
    // A small pill on the bottom edge saying how many lines do not fit.
    draw.add(
        "bloom-overflow-moreChip",
        { left: draw.x(box.rect.right), top: edge },
        describeLinesPastEdge(box.lineCount),
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
        "bloom-overflow-moreChip",
        { left: draw.x(margin.right), top: draw.y(margin.bottom) },
        chipText.pageTooFull.text,
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

// Builds the drawings, placing each by client coordinates converted to the layer's own pixels.
// The conversion is measured on the layer, not the page, because a full-bleed page has a
// transform of its own that the layer does not share. The drawings collect in `drawings` until
// they are put in the layer all at once.
class LayerDrawer {
    public readonly drawings = document.createDocumentFragment();
    private readonly layerRect: DOMRect;
    private readonly scale: number;
    public constructor(layer: HTMLElement) {
        this.layerRect = layer.getBoundingClientRect();
        this.scale = this.layerRect.width / layer.offsetWidth;
    }

    // The layer's x coordinate of a client x coordinate.
    public x(clientX: number): number {
        return (clientX - this.layerRect.left) / this.scale;
    }

    // The layer's y coordinate of a client y coordinate.
    public y(clientY: number): number {
        return (clientY - this.layerRect.top) / this.scale;
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
        this.drawings.appendChild(e);
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
