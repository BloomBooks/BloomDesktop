// How far the things on the page being edited sit from the edge of the finished page, in
// millimetres, the way a person measures a printed book with a ruler.
//
// The finished page's edge is the cut line when the book has full bleed: Bloom then draws the page
// enlarged, with 3mm of bleed all round that the printer trims off (see pageBoxesSizing.less). With
// no bleed it is simply the edge of the page.
//
// Every number here is a ratio of on-screen distances, scaled by the page's own size in
// millimetres, so it does not depend on the window size, the Edit tab's zoom, or the display's DPI.
// That is why a test may compare these with millimetre values a theme sets, which geometry.ts tells
// tests not to do with pixels.
//
// A positive distance means inside the finished page; a negative one means past its edge, into the
// bleed.

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";

/** The bleed Bloom adds on each side of a full-bleed page, in millimetres. Matches @bleed. */
export const kBleedMm = 3;

/**
 * How far a measured distance may be from the expected one and still count as the same. Bloom's
 * own arithmetic is exact, but layout rounds to device pixels: at the Edit tab's zoom a pixel is
 * about 0.25mm, and a millimetre is far less than any difference a theme makes.
 */
export const kMarginToleranceMm = 0.5;

/** Distances from each edge of the finished page, in millimetres. */
export interface IDistancesFromEdge {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/**
 * How far the page's margin box sits from each edge of the finished page. The margin box is what
 * the page's margins leave room for: everything on a normal page is inside it, so this is the
 * page's margins as a person would measure them on paper.
 */
export async function getMarginBoxDistancesFromEdge(
    page: Page,
): Promise<IDistancesFromEdge> {
    return measureFromEdge(
        editablePageFrame(page).locator(".bloom-page .marginBox").first(),
        false,
    );
}

/**
 * How far the area inside the padding of the page's first text box sits from each edge of the
 * finished page. That is where the text itself starts, which is what Edge to Edge positions on
 * content pages, where the margin box itself runs to the edge.
 */
export async function getTextAreaDistancesFromEdge(
    page: Page,
): Promise<IDistancesFromEdge> {
    return measureFromEdge(
        editablePageFrame(page)
            .locator(".bloom-page .marginBox .bloom-translationGroup")
            .first(),
        true,
    );
}

/** How far the page's first picture box sits from each edge of the finished page. */
export async function getPictureDistancesFromEdge(
    page: Page,
): Promise<IDistancesFromEdge> {
    return measureFromEdge(
        editablePageFrame(page).locator(".bloom-page .bloom-canvas").first(),
        false,
    );
}

/**
 * Assert that each side given in `expected` is that many millimetres from the finished page's
 * edge, within kMarginToleranceMm. `what` names the thing measured, for the failure message.
 */
export function expectDistancesFromEdge(
    actual: IDistancesFromEdge,
    expected: Partial<IDistancesFromEdge>,
    what: string,
): void {
    const wrong = Object.entries(expected)
        .filter(
            ([side, mm]) =>
                Math.abs(actual[side as keyof IDistancesFromEdge] - mm) >
                kMarginToleranceMm,
        )
        .map(
            ([side, mm]) =>
                `${side} is ${actual[side as keyof IDistancesFromEdge].toFixed(1)}mm, expected ${mm}mm`,
        );
    expect(
        wrong,
        `${what}, measured from the edge of the finished page: ${wrong.join("; ")}`,
    ).toEqual([]);
}

/**
 * Assert that each side given is at least `mm` past the finished page's edge, into the bleed: what
 * a picture that runs off the page does.
 */
export function expectPastEdge(
    actual: IDistancesFromEdge,
    sides: (keyof IDistancesFromEdge)[],
    mm: number,
    what: string,
): void {
    const short = sides
        .filter((side) => actual[side] > -mm + kMarginToleranceMm)
        .map((side) => `${side} is ${actual[side].toFixed(1)}mm from the edge`);
    expect(
        short,
        `${what} should run at least ${mm}mm past the edge of the finished page: ${short.join("; ")}`,
    ).toEqual([]);
}

/**
 * Measure `target` against the finished page's edges. With `insidePadding`, measure the area inside
 * its padding rather than its border box.
 */
async function measureFromEdge(
    target: Locator,
    insidePadding: boolean,
): Promise<IDistancesFromEdge> {
    if ((await target.count()) === 0)
        throw new Error(
            `The page being edited has nothing matching ${target.toString()} to measure.`,
        );
    return target.evaluate(
        (element, args) => {
            const bloomPage = element.closest(".bloom-page") as HTMLElement;
            const fullBleed =
                element.ownerDocument.body.classList.contains(
                    "bloom-fullBleed",
                );
            // With full bleed, the media box is the page plus its bleed, and is what clips it.
            const box = (
                (fullBleed && bloomPage.closest(".bloom-mediaBox")) ||
                bloomPage
            ).getBoundingClientRect();
            const pageWidthMm = parseFloat(
                getComputedStyle(bloomPage).getPropertyValue("--page-width"),
            );
            if (!(pageWidthMm > 0))
                throw new Error(
                    `The page does not say how wide it is (--page-width is "${getComputedStyle(
                        bloomPage,
                    ).getPropertyValue("--page-width")}").`,
                );
            const bleedMm = fullBleed ? args.bleedMm : 0;
            const pxPerMm = box.width / (pageWidthMm + 2 * bleedMm);
            const edge = {
                left: box.left + bleedMm * pxPerMm,
                top: box.top + bleedMm * pxPerMm,
                right: box.right - bleedMm * pxPerMm,
                bottom: box.bottom - bleedMm * pxPerMm,
            };
            const rect = element.getBoundingClientRect();
            let { left, top, right, bottom } = rect;
            if (args.insidePadding) {
                // Padding is in the element's own coordinates; the page may be scaled.
                const scale = rect.width / (element as HTMLElement).offsetWidth;
                const style = getComputedStyle(element);
                left += parseFloat(style.paddingLeft) * scale;
                top += parseFloat(style.paddingTop) * scale;
                right -= parseFloat(style.paddingRight) * scale;
                bottom -= parseFloat(style.paddingBottom) * scale;
            }
            return {
                left: (left - edge.left) / pxPerMm,
                top: (top - edge.top) / pxPerMm,
                right: (edge.right - right) / pxPerMm,
                bottom: (edge.bottom - bottom) / pxPerMm,
            };
        },
        { insidePadding, bleedMm: kBleedMm },
    );
}
