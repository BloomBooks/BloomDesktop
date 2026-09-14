// How big an image ought to be for the space it occupies on a page.
//
// There is exactly one place in Bloom that does this arithmetic: the image tooltip's
// "An image with W x H dots would fill this container" line and the AI image editor's
// suggested target both come from getSuggestedImageTargetForFraction below.
//
// The tooltip works on the page the user is looking at, so it measures the container itself.
// The AI image editor has to answer for every page in the book, and only the open page is laid
// out in a browser, so it works from the fraction of the page each slot takes up, which Bloom
// writes into the HTML whenever a page is saved (see recordFractionOfPageOnImageSlots).

import { kScrollingLayouts } from "./scrollingLayouts";

// This appears to be constant even on higher dpi screens.
// (See http://www.w3.org/TR/css3-values/#absolute-lengths)
export const kBrowserDpi = 96;

// What we tell people to aim for in a printed book.
export const kPrintDpi = 300;

// The screen a digital book is assumed to be shown on. These mirror the BloomPUB default
// image limits ImagePublishSettings.MaxWidth/MaxHeight in src/BloomExe/Book/PublishSettings.cs,
// which is what Bloom shrinks digital-book images to by default, so asking for more pixels
// than this would only produce data that the publish step throws away.
export const kDigitalScreenLongEdgePx = 1280;
export const kDigitalScreenShortEdgePx = 720;

// How much of its page an image slot takes up, as two numbers between 0 and 1 separated by a
// comma, e.g. "0.42,0.31". Bloom writes this onto every image container when a page is saved,
// which is the only way the size a slot wants can be known for a page that is not open in the
// editor. Named and shaped after data-imgsizebasedon (see CanvasElementResizeAdjustments.ts).
export const kFractionOfPageAttribute = "data-fraction-of-page";

// The class Bloom puts on an image slot. Spelled out here rather than imported from
// bloomImages.ts, because that module imports this one and we do not want the two of them
// importing each other. C# knows the same name as HtmlDom.kImageContainerClass.
const kImageContainerClassName = "bloom-imageContainer";

// How many decimal places of the fraction we keep in the HTML. Two is whole percent of the
// page, which is as fine as this needs to be: the answer only ever chooses an image size, and
// image models accept a handful of coarse size tiers rather than an exact pixel count.
const kFractionDecimalPlaces = 2;

// True if the given page is one of the page sizes meant to be read on a screen rather than
// printed. The list of those layouts lives in scrollingLayouts.ts.
export function isDeviceLayoutPage(page: Element): boolean {
    return kScrollingLayouts.some((layout) => page.classList.contains(layout));
}

// What we would like an image to be, in dots, given how much of its page it covers and how big
// that page is.
//
// `isDigital` says which of the two ways the answer was reached: on a screen-sized page the
// target is the slot's share of a 1280 x 720 screen, and on a paper page it is whatever fills
// the slot at 300 DPI. `memo` explains the number in plain words for a user; the AI image
// editor shows it verbatim under its size selector.
//
// Returns null when the inputs do not describe a real space, which happens when a page was
// never laid out (or, in tests, in jsdom). Callers treat that as "we don't know" rather than as
// an error.
export function getSuggestedImageTargetForFraction(
    fraction: { width: number; height: number },
    page: { widthPx: number; heightPx: number; isDigital: boolean },
): {
    width: number;
    height: number;
    isDigital: boolean;
    memo: string;
} | null {
    const containerWidthPx = multiplyWithoutFloatingPointNoise(
        fraction.width,
        page.widthPx,
    );
    const containerHeightPx = multiplyWithoutFloatingPointNoise(
        fraction.height,
        page.heightPx,
    );
    if (containerWidthPx <= 0 || containerHeightPx <= 0) return null;

    if (page.isDigital) {
        // Fit the whole page inside the screen on BOTH edges, then take the slot's share of
        // that. Scaling only the long edge would overshoot on the two ebook layouts that are
        // not 16x9: a 2x3 page taken to 1280 on its long edge is 853 across, and the publish
        // step, which caps the short edge at 720 as well, would throw those extra pixels away
        // (BloomPubMaker.cs uses MaxWidth as the long side and MaxHeight as the short one).
        const pageLongEdgePx = Math.max(page.widthPx, page.heightPx);
        const pageShortEdgePx = Math.min(page.widthPx, page.heightPx);
        const scale = Math.min(
            kDigitalScreenLongEdgePx / pageLongEdgePx,
            kDigitalScreenShortEdgePx / pageShortEdgePx,
        );
        const width = Math.ceil(containerWidthPx * scale);
        const height = Math.ceil(containerHeightPx * scale);
        return {
            width,
            height,
            isDigital: true,
            memo:
                `${width} x ${height} so that the image fills its share of a ` +
                `${kDigitalScreenLongEdgePx} x ${kDigitalScreenShortEdgePx} screen, ` +
                `the size Bloom's digital books use by default`,
        };
    }

    const width = Math.ceil((kPrintDpi * containerWidthPx) / kBrowserDpi);
    const height = Math.ceil((kPrintDpi * containerHeightPx) / kBrowserDpi);
    return {
        width,
        height,
        isDigital: false,
        memo:
            `${width} x ${height} in order to print at ${kPrintDpi} DPI in this ` +
            `${cssPixelsToWholeMillimeters(containerWidthPx)} mm x ` +
            `${cssPixelsToWholeMillimeters(containerHeightPx)} mm image container`,
    };
}

// What we would like an image in this container to be, in dots; see
// getSuggestedImageTargetForFraction, which this measures the inputs for.
//
// Returns null when the container or its page has no usable size.
//
// Measures with offsetWidth/offsetHeight rather than getBoundingClientRect because Bloom scales
// the whole page frame with a CSS transform for zoom (EditingModel.cs), and a bounding rect
// would report the zoomed size instead of the layout size.
export function getSuggestedImageTargetForContainer(container: HTMLElement): {
    width: number;
    height: number;
    isDigital: boolean;
    memo: string;
} | null {
    const containerWidthPx = container.offsetWidth;
    const containerHeightPx = container.offsetHeight;
    if (
        !isUsableLength(containerWidthPx) ||
        !isUsableLength(containerHeightPx)
    ) {
        return null;
    }

    // Ask about this container's own page, never whichever page happens to be first in the
    // document: getOpenPageMetrics falls back to that when given nothing, and measuring a
    // container against an unrelated page would put it on the wrong side of the paper/screen
    // split as well as scaling it by the wrong size.
    const pageElement = container.closest(".bloom-page");
    const page = pageElement ? getOpenPageMetrics(pageElement) : null;
    if (!page) {
        // The container has a size but its page does not, which a container outside any
        // .bloom-page does. Treat the container as the whole of a paper page, which is what
        // this did before pages came into it.
        return getSuggestedImageTargetForFraction(
            { width: 1, height: 1 },
            {
                widthPx: containerWidthPx,
                heightPx: containerHeightPx,
                isDigital: false,
            },
        );
    }

    // Pass the exact ratio rather than the two-decimal one we would write into the HTML, so
    // that the tooltip keeps giving the same answer it always has for the page in front of the
    // user.
    return getSuggestedImageTargetForFraction(
        {
            width: containerWidthPx / page.widthPx,
            height: containerHeightPx / page.heightPx,
        },
        page,
    );
}

// The size and kind of a laid-out page, which is what turns a slot's share of its page into a
// number of dots. Pass the page element; with nothing passed it finds the page the editor
// currently has open. Null when there is no page, or it has not been laid out.
export function getOpenPageMetrics(
    pageRoot?: Element | null,
): { widthPx: number; heightPx: number; isDigital: boolean } | null {
    const page = (pageRoot ?? document.querySelector(".bloom-page")) as
        | HTMLElement
        | null
        | undefined;
    if (!page) return null;
    if (!isUsableLength(page.offsetWidth) || !isUsableLength(page.offsetHeight))
        return null;
    return {
        widthPx: page.offsetWidth,
        heightPx: page.offsetHeight,
        isDigital: isDeviceLayoutPage(page),
    };
}

// Writes each image slot's share of its page onto the slot, so that the size it wants can be
// worked out later for a page nobody has open. Called as a page is saved (see
// extractAndStripPageContentForSave in bloomEditing.ts), which is both the ordinary Edit-tab
// save and the off-screen pass "Update Book" makes over every page.
//
// A slot whose size cannot be measured keeps whatever value it already had: a stale fraction
// from the last save is better evidence than none, and guessing would have the AI image editor
// generate at the wrong resolution.
export function recordFractionOfPageOnImageSlots(pageRoot: Element): void {
    const pageElement =
        pageRoot.closest(".bloom-page") ??
        pageRoot.querySelector(".bloom-page");
    if (!pageElement) return;
    const page = getOpenPageMetrics(pageElement);
    if (!page) return;
    Array.from(
        pageRoot.querySelectorAll("." + kImageContainerClassName),
    ).forEach((slot) => {
        // Bloom injects controls into the live page; they are not slots, and a save strips
        // them anyway.
        if (slot.closest(".bloom-ui")) return;
        const container = slot as HTMLElement;
        if (
            !isUsableLength(container.offsetWidth) ||
            !isUsableLength(container.offsetHeight)
        ) {
            return;
        }
        const width = roundToTwoDecimals(container.offsetWidth / page.widthPx);
        const height = roundToTwoDecimals(
            container.offsetHeight / page.heightPx,
        );
        container.setAttribute(kFractionOfPageAttribute, `${width},${height}`);
    });
}

// Reads back what recordFractionOfPageOnImageSlots wrote. Null for anything that is not two
// positive numbers, which includes the empty attribute and anything hand-edited.
export function parseFractionOfPage(
    value: string | null | undefined,
): { width: number; height: number } | null {
    if (!value) return null;
    const parts = value.split(",");
    if (parts.length !== 2) return null;
    const width = Number(parts[0].trim());
    const height = Number(parts[1].trim());
    if (
        parts[0].trim() === "" ||
        parts[1].trim() === "" ||
        !isUsableLength(width) ||
        !isUsableLength(height)
    ) {
        return null;
    }
    return { width, height };
}

// A length we can do arithmetic with: a real, positive number of pixels (or share of a page).
function isUsableLength(value: number): boolean {
    return isFinite(value) && value > 0;
}

// Multiplies a fraction of a page back out into pixels, dropping the last few digits of the
// answer. Without that, a fraction that came from dividing by the page width does not
// multiply back to the pixel count it started from (469 / 559 * 559 is 468.99999999999994),
// and the Math.ceil below it would then sometimes land a pixel high.
function multiplyWithoutFloatingPointNoise(
    fraction: number,
    lengthPx: number,
): number {
    if (!isFinite(fraction) || !isFinite(lengthPx)) return 0;
    return Math.round(fraction * lengthPx * 1000000) / 1000000;
}

// The fraction as it goes into the HTML: whole percent of the page.
function roundToTwoDecimals(value: number): number {
    const scale = Math.pow(10, kFractionDecimalPlaces);
    return Math.round(value * scale) / scale;
}

// The length in whole millimeters of the given number of CSS pixels, for saying how big a
// container is in units a user of a paper book thinks in.
function cssPixelsToWholeMillimeters(px: number): number {
    return Math.round((px * 25.4) / kBrowserDpi);
}
