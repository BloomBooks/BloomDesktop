// The page number Bloom draws on the page being edited.
//
// Bloom draws a page's number with CSS, as the content of the numbered page's ::after pseudo-element
// (pageNumbers.less), from the data-page-number attribute it gives the page. A pseudo-element is not
// in the DOM, so a locator cannot find it; this reads its computed style and works out where it is
// drawn from that. Its position is absolute, against the page itself.

import type { Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";

/** What the page being edited shows of its page number. */
export interface IPageNumberShown {
    /** The page's own number, the data-page-number Bloom gave it; "" on an unnumbered page. */
    pageNumber: string;
    /** "side-left" or "side-right": which side of a spread Bloom put the page on. */
    side: string;
    /** False when the page draws no page number at all (display none, no content or no size). */
    drawn: boolean;
    /** The text the page number shows. */
    text: string;
    /** Its font size, in CSS px. */
    fontSizePx: number;
    /** The alpha of its colour, 0 to 1. */
    colorAlpha: number;
    /** Its box, in CSS px of the page, measured from the page's top left corner. */
    box: { left: number; top: number; right: number; bottom: number };
    /** The page's size, in CSS px. */
    pageWidth: number;
    pageHeight: number;
    /**
     * True when nothing of the page's own that paints (a background, a picture, a canvas, a video)
     * is above the number at the middle of its box, whether or not it takes the mouse. (A hit on
     * a pseudo-element reports the element it belongs to.)
     */
    onTop: boolean;
    /** What covers the middle of the number's box, when something does. */
    coveredBy: string;
}

/** The page number of the page being edited, as Bloom draws it. */
export async function getPageNumberShown(
    page: Page,
): Promise<IPageNumberShown> {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .evaluate((pageDiv) => {
            const el = pageDiv as HTMLElement;
            const pageStyle = getComputedStyle(el);
            if (pageStyle.position === "static")
                throw new Error(
                    "The page is not positioned, so its page number is not placed against it.",
                );
            const after = getComputedStyle(el, "::after");
            const px = (value: string) => parseFloat(value);
            const content = after.content;
            // content is "none", "normal", or a quoted string such as "\"3\"".
            const text =
                content.startsWith('"') && content.endsWith('"')
                    ? content.slice(1, -1)
                    : "";
            const width = px(after.width) || 0;
            const height = px(after.height) || 0;
            // left and top place the margin box. Device sizes centre the number with
            // `margin: 0 auto` between left and right of 0, so its own margin counts too.
            const left = px(after.left) + px(after.marginLeft);
            const top = px(after.top) + px(after.marginTop);
            const fontSizePx = px(after.fontSize);
            const alphaMatch = /rgba?\(([^)]*)\)/.exec(after.color);
            const colorParts = alphaMatch ? alphaMatch[1].split(",") : [];
            const colorAlpha =
                colorParts.length === 4 ? parseFloat(colorParts[3]) : 1;
            const drawn =
                after.display !== "none" &&
                text !== "" &&
                width > 0 &&
                height > 0 &&
                after.visibility === "visible";
            const box = {
                left,
                top,
                right: left + width,
                bottom: top + height,
            };

            // Is the number the top thing at its middle? Bring that point into the frame's view
            // first: elementFromPoint finds nothing outside it.
            let onTop = false;
            let coveredBy = "";
            if (drawn) {
                const rect = el.getBoundingClientRect();
                // The Edit tab zooms the page with a transform, which getBoundingClientRect sees and
                // the computed style does not.
                const scale = rect.width / el.offsetWidth;
                const middleX = (left + width / 2) * scale;
                const middleY = (top + height / 2) * scale;
                const wantX = rect.left + el.clientLeft * scale + middleX;
                const wantY = rect.top + el.clientTop * scale + middleY;
                window.scrollBy(
                    wantX - window.innerWidth / 2,
                    wantY - window.innerHeight / 2,
                );
                const scrolled = el.getBoundingClientRect();
                const x = scrolled.left + el.clientLeft * scale + middleX;
                const y = scrolled.top + el.clientTop * scale + middleY;
                // Hit testing skips anything with pointer-events: none, which can still be drawn
                // over the number, so let every element take the hit for this one test. The
                // style goes again before anything can be drawn or saved with it.
                const allHittable = document.createElement("style");
                allHittable.textContent =
                    "*, *::before, *::after { pointer-events: auto !important; }";
                document.head.appendChild(allHittable);
                let hits: Element[];
                try {
                    // Top first. The page is listed where its number is, the topmost thing of
                    // its own that is hit.
                    hits = document.elementsFromPoint(x, y);
                } finally {
                    allHittable.remove();
                }
                const pageAt = hits.indexOf(el);
                // What covers the number is the page's own content above it that paints
                // something there. The Edit tab's own controls outside the page, and the page's
                // transparent containers, cover nothing.
                const paints = (hit: Element) => {
                    const style = getComputedStyle(hit);
                    if (style.visibility !== "visible" || style.opacity === "0")
                        return false;
                    if (/^(IMG|CANVAS|VIDEO|SVG|svg)$/.test(hit.tagName))
                        return true;
                    const bg = /rgba?\(([^)]*)\)/.exec(style.backgroundColor);
                    const bgParts = bg ? bg[1].split(",") : [];
                    const bgAlpha =
                        bgParts.length === 4 ? parseFloat(bgParts[3]) : 1;
                    return bgAlpha > 0 || style.backgroundImage !== "none";
                };
                const cover = hits
                    .slice(0, Math.max(pageAt, 0))
                    .find((hit) => el.contains(hit) && paints(hit));
                onTop = pageAt >= 0 && !cover;
                if (cover)
                    coveredBy = `<${cover.tagName.toLowerCase()} class="${cover.getAttribute("class") ?? ""}">`;
                else if (pageAt < 0) coveredBy = "the page itself was not hit";
            }

            let side = "";
            if (el.classList.contains("side-left")) side = "side-left";
            else if (el.classList.contains("side-right")) side = "side-right";

            return {
                pageNumber: el.getAttribute("data-page-number") ?? "",
                side,
                drawn,
                text,
                fontSizePx,
                colorAlpha,
                box,
                pageWidth: el.clientWidth,
                pageHeight: el.clientHeight,
                onTop,
                coveredBy,
            };
        });
}
