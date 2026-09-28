// While the user is asked to confirm removing the page being edited (the only page Remove Page
// removes), show them which page that is: bring it into view in the page frame and draw a big red X
// across it. With the other pages of the book showing (see bookGridView.ts), it can be anywhere.

const kSvgNamespace = "http://www.w3.org/2000/svg";
const kLineWidth = 4;
// Each line of the X stops this far short of the page's corners.
const kInsetFromCorners = 30;
const kLineColor = "#ff0000";
// Over everything in the page frame except the bar of controls fixed across its top (z-index 1000).
const kZIndex = 999;

/**
 * Scroll the page being edited into view and draw a red X across it. Returns a function that takes
 * the X away again.
 */
export function markPageForRemoval(): () => void {
    const page = document.querySelector(".bloom-page") as HTMLElement;
    bringIntoView(page);
    const mark = makeX(page.getBoundingClientRect());
    document.body.appendChild(mark);
    return () => mark.remove();
}

// Scroll this document only. scrollIntoView would also scroll the Edit tab's own containers, outside
// this frame (see restoreScrollPosition() in bookGridView.ts). A page that does not fit along one
// direction is centered along it, so at least the middle of the X shows.
function bringIntoView(pageBox: HTMLElement): void {
    const rect = pageBox.getBoundingClientRect();
    const controlsBar = document.querySelector("body > .bloom-controls-bar");
    const visibleTop = controlsBar
        ? controlsBar.getBoundingClientRect().bottom
        : 0;
    const visibleBottom = document.documentElement.clientHeight;
    const visibleRight = document.documentElement.clientWidth;
    let dx = 0;
    let dy = 0;
    if (rect.left < 0 || rect.right > visibleRight) {
        dx = rect.left - (visibleRight - rect.width) / 2;
    }
    if (rect.top < visibleTop || rect.bottom > visibleBottom) {
        dy =
            rect.top -
            (visibleTop + (visibleBottom - visibleTop - rect.height) / 2);
    }
    if (dx || dy) window.scrollBy(dx, dy);
}

function makeX(rect: DOMRect): SVGSVGElement {
    const svg = document.createElementNS(kSvgNamespace, "svg");
    // bloom-ui marks it as editing furniture, never part of the page.
    svg.classList.add("bloom-ui", "bloom-page-removal-mark");
    svg.setAttribute("width", `${rect.width}`);
    svg.setAttribute("height", `${rect.height}`);
    Object.assign(svg.style, {
        position: "fixed",
        left: `${rect.left}px`,
        top: `${rect.top}px`,
        pointerEvents: "none",
        zIndex: `${kZIndex}`,
    });
    const near = kInsetFromCorners;
    const farX = rect.width - kInsetFromCorners;
    const farY = rect.height - kInsetFromCorners;
    svg.appendChild(makeLine(near, near, farX, farY));
    svg.appendChild(makeLine(farX, near, near, farY));
    return svg;
}

function makeLine(
    x1: number,
    y1: number,
    x2: number,
    y2: number,
): SVGLineElement {
    const line = document.createElementNS(kSvgNamespace, "line");
    line.setAttribute("x1", `${x1}`);
    line.setAttribute("y1", `${y1}`);
    line.setAttribute("x2", `${x2}`);
    line.setAttribute("y2", `${y2}`);
    line.setAttribute("stroke", kLineColor);
    line.setAttribute("stroke-width", `${kLineWidth}`);
    return line;
}
