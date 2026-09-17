// How big an image ought to be for the space it occupies on a page.
//
// There is exactly one place in Bloom that does this arithmetic: the image tooltip's
// "An image with W x H dots would fill this container" line and the AI image editor's
// suggested target both come from getSuggestedImageTargetForFraction below.
//
// The tooltip works on the page the user is looking at, so it measures a box directly. The AI
// image editor has to answer for every page in the book, and only the open page is laid out in
// a browser, so it works from the fraction of the page each slot takes up, which Bloom writes
// into the HTML whenever a page is saved (see recordFractionOfPageOnImageSlots).
//
// Which box to measure is not always the image container. A canvas BACKGROUND image is
// presented as the picture of the whole bloom-canvas, and a replacement committed from the AI
// image editor is re-fitted to fill that canvas, while the container itself holds only the part
// of the canvas the current image happens to reach (letterboxed, or cropped). So a background
// slot has to be measured against its bloom-canvas, and every other slot against the container.
// getElementThatDeterminesImageSlotSize below is the one place that decides which.

import {
    kBackgroundImageClass,
    kBloomCanvasClass,
    kCanvasElementClass,
    kImageContainerClass,
} from "../toolbox/canvas/canvasElementConstants";
import { kScrollingLayouts } from "./scrollingLayouts";

// This appears to be constant even on higher dpi screens.
// (See http://www.w3.org/TR/css3-values/#absolute-lengths)
export const kBrowserDpi = 96;

// What we tell people to aim for in a printed book.
export const kPrintDpi = 300;

// The screen a digital book is assumed to be shown on, which is the book's own BloomPUB image
// limit (Book Settings > BloomPUB > Resolution). Bloom shrinks every image to this when it
// publishes a digital book, so asking for more pixels than this would only produce data that
// the publish step throws away, and asking for fewer would waste a setting the user raised on
// purpose.
//
// As in BloomPubMaker.cs, the long edge is the width setting and the short edge the height
// setting, whichever way round the page is.
export interface IDigitalScreen {
    longEdgePx: number;
    shortEdgePx: number;
}

// This book's BloomPUB image limit, read out of the `publish` object the book/settings API
// replies with (PublishSettings in C#, so the names here are its JsonProperty ones).
//
// Throws when either number is missing or is not positive. C# always sends both — every
// BloomPubSettings builds an ImagePublishSettings, whose MaxWidth/MaxHeight have defaults — so
// there is no book for which this is a legitimate answer, and a silent substitute would have
// the AI image editor generate at a size nothing asked for.
export function parseBloomPubImageLimit(
    publishSettings:
        | {
              bloomPUB?: {
                  imageSettings?: {
                      maxWidth?: number;
                      maxHeight?: number;
                  };
              };
          }
        | undefined,
): IDigitalScreen {
    const imageSettings = publishSettings?.bloomPUB?.imageSettings;
    const longEdgePx = imageSettings?.maxWidth;
    const shortEdgePx = imageSettings?.maxHeight;
    if (!isUsableLength(longEdgePx) || !isUsableLength(shortEdgePx)) {
        throw new Error(
            "This book's settings have no BloomPUB image size " +
                `(maxWidth ${longEdgePx}, maxHeight ${shortEdgePx}); ` +
                "C# always sends both, so something is wrong with book/settings.",
        );
    }
    return { longEdgePx, shortEdgePx };
}

// How much of its page an image slot takes up, as two numbers between 0 and 1 separated by a
// comma, e.g. "0.4177,0.3125". Bloom writes this onto every image container when a page is saved,
// which is the only way the size a slot wants can be known for a page that is not open in the
// editor. Named and shaped after data-imgsizebasedon (see CanvasElementResizeAdjustments.ts).
export const kFractionOfPageAttribute = "data-fraction-of-page";

// How many decimal places of the fraction we keep in the HTML. Four is a hundredth of a
// percent of the page, which is finer than a pixel on any page we lay out, so the size worked
// out from the fraction matches the size worked out by measuring the container directly.
//
// Two decimals is not enough, even though the answer only chooses an image size. It rounds each
// edge independently by up to half a percent of the page, so the two edges can move in opposite
// directions and change the SHAPE the AI image editor is asked for: a 469 x 352 container came
// out as 1468 x 1088 in the editor while the tooltip, measuring the same container, said
// 1466 x 1100 (BL-16742). Two numbers for one container, visibly disagreeing, is worth four
// characters of HTML.
const kFractionDecimalPlaces = 4;

// How big a laid-out page is, and whether it is one of the screen-sized layouts. This is what
// turns a slot's share of its page into a number of dots, and only a browser with the page in
// front of it can say any of it.
export interface IPageMetrics {
    widthPx: number;
    heightPx: number;
    isDigital: boolean;
}

// True if the given page is one of the page sizes meant to be read on a screen rather than
// printed. The list of those layouts lives in scrollingLayouts.ts.
export function isDeviceLayoutPage(page: Element): boolean {
    return kScrollingLayouts.some((layout) => page.classList.contains(layout));
}

// What we would like an image to be, in dots, given how much of its page it covers and how big
// that page is.
//
// `isDigital` says which of the two ways the answer was reached: on a screen-sized page the
// target is the slot's share of `digitalScreen`, this book's BloomPUB image limit, and on a
// paper page it is whatever fills the slot at 300 DPI. `memo` explains the number in plain
// words for a user; the AI image editor shows it verbatim under its size selector.
//
// A paper page ignores `digitalScreen`, so a caller that has not fetched the book's setting
// passes null there. A screen-sized page cannot be answered without it and throws, rather than
// quoting some other book's screen at the user.
//
// Returns null when the inputs do not describe a real space, which happens when a page was
// never laid out (or, in tests, in jsdom). Callers treat that as "we don't know" rather than as
// an error.
export function getSuggestedImageTargetForFraction(
    fraction: { width: number; height: number },
    page: IPageMetrics,
    digitalScreen: IDigitalScreen | null,
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
        if (!digitalScreen) {
            throw new Error(
                "A screen-sized page can only be sized against this book's BloomPUB image " +
                    "limit, and none was supplied.",
            );
        }
        // Fit the whole page inside the screen on BOTH edges, then take the slot's share of
        // that. Scaling only the long edge would overshoot on the two ebook layouts that are
        // not 16x9: a 2x3 page taken to 1280 on its long edge is 853 across, and the publish
        // step, which caps the short edge at 720 as well, would throw those extra pixels
        // away. (BloomPubMaker.cs uses MaxWidth as the long side and MaxHeight as the short
        // one, which is why the two edges are named rather than numbered here.)
        const pageLongEdgePx = Math.max(page.widthPx, page.heightPx);
        const pageShortEdgePx = Math.min(page.widthPx, page.heightPx);
        const scale = Math.min(
            digitalScreen.longEdgePx / pageLongEdgePx,
            digitalScreen.shortEdgePx / pageShortEdgePx,
        );
        const width = Math.ceil(containerWidthPx * scale);
        const height = Math.ceil(containerHeightPx * scale);
        return {
            width,
            height,
            isDigital: true,
            memo:
                `${width} x ${height} so that the image fills its share of a ` +
                `${digitalScreen.longEdgePx} x ${digitalScreen.shortEdgePx} screen, ` +
                `this book's BloomPUB image size setting`,
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
export function getSuggestedImageTargetForContainer(
    container: HTMLElement,
    digitalScreen: IDigitalScreen | null,
): {
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
            digitalScreen,
        );
    }

    // Pass the exact ratio rather than the rounded one we would write into the HTML, so
    // that the tooltip keeps giving the same answer it always has for the page in front of the
    // user.
    return getSuggestedImageTargetForFraction(
        {
            width: containerWidthPx / page.widthPx,
            height: containerHeightPx / page.heightPx,
        },
        page,
        digitalScreen,
    );
}

// The size and kind of a laid-out page, which is what turns a slot's share of its page into a
// number of dots. Pass the page element; with nothing passed it finds the page the editor
// currently has open. Null when there is no page, or it has not been laid out.
export function getOpenPageMetrics(
    pageRoot?: Element | null,
): IPageMetrics | null {
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

// The element whose size says how big the image in the given slot ought to be.
//
// For nearly every slot that is the image container itself. A canvas BACKGROUND image is the
// exception: it is the picture of the whole bloom-canvas, its container covers only as much of
// that canvas as the current image reaches, and a replacement is re-fitted to fill the canvas
// again (TryMakeCroppedViewOfSlotImage and the re-fit in AiImageEditorApi.cs). Measuring the
// container there would ask for a picture the size of the old image's letterbox.
//
// A background set to "contain" rather than "cover" is letterboxed inside the canvas instead of
// filling it, so for those the canvas is an upper bound and we ask for slightly more than the
// picture will finally occupy. That is the direction to err in: the replacement's aspect ratio
// is not known until it exists, and too many dots costs nothing but a resize.
//
// Pass the image container. Returns the container itself for anything that is not a canvas
// background, and for a background whose bloom-canvas cannot be found.
export function getElementThatDeterminesImageSlotSize(
    container: HTMLElement,
): HTMLElement {
    const canvasElement = container.closest("." + kCanvasElementClass);
    if (!canvasElement?.classList.contains(kBackgroundImageClass)) {
        return container;
    }
    return (
        (container.closest("." + kBloomCanvasClass) as HTMLElement | null) ??
        container
    );
}

// Writes each image slot's share of its page onto the slot, so that the size it wants can be
// worked out later for a page nobody has open. Called as a page is saved (see
// cleanCloneOfBodyForSave in bloomEditing.ts): the ordinary Edit-tab save, and the
// off-screen pass over every page that "Update Book" runs on demand and that Bloom runs by itself
// when the AI image editor is launched on a book that has not had it (BL-16852).
//
// A slot whose size cannot be measured keeps whatever value it already had: a stale fraction
// from the last save is better evidence than none, and guessing would have the AI image editor
// generate at the wrong resolution.
//
// For the same reason, a value recorded under one page size or layout survives a change to
// either, and a page nobody reopens keeps it until it is next saved. A share is a proportion, so
// it only goes wrong where the new layout gives that slot a different proportion of its page, and
// the cost is a suggested size somewhat off rather than a broken picture. Both the whole-book
// passes above put it right in one go -- and a page-size change makes the book due for that pass
// again, so the next AI image editor launch re-measures every slot at the new size. Deliberately
// not re-measured any more eagerly than that.
//
// The measurements come from pageRoot, which must be laid out. The attribute is written to the
// corresponding slot under writeTo, which defaults to pageRoot itself; the live editor's save passes
// the detached clone it is about to hand to C#, so that measuring the live page for a save does not
// also change it. writeTo must then be an untouched copy of pageRoot, so that the Nth slot in each
// is the same slot.
export function recordFractionOfPageOnImageSlots(
    pageRoot: Element,
    writeTo: Element = pageRoot,
): void {
    const pageElement =
        pageRoot.closest(".bloom-page") ??
        pageRoot.querySelector(".bloom-page");
    if (!pageElement) return;
    const page = getOpenPageMetrics(pageElement);
    if (!page) return;
    const liveSlots = Array.from(
        pageRoot.querySelectorAll("." + kImageContainerClass),
    );
    const targetSlots =
        writeTo === pageRoot
            ? liveSlots
            : Array.from(writeTo.querySelectorAll("." + kImageContainerClass));
    if (targetSlots.length !== liveSlots.length) {
        throw new Error(
            `recordFractionOfPageOnImageSlots(): the copy has ${targetSlots.length} image slots but the live page has ${liveSlots.length}. The copy must be an untouched copy of the live page.`,
        );
    }
    liveSlots.forEach((slot, index) => {
        // Bloom injects controls into the live page; they are not slots, and a save strips
        // them anyway.
        if (slot.closest(".bloom-ui")) return;
        // A Bloom Games target holds a copy of its draggable's content, so its picture is
        // not separately editable and the AI image editor is never offered it (C#'s
        // IsSlotInsideGameTarget, which looks for the same ancestor attribute).
        if (slot.parentElement?.closest("[data-target-of]")) return;
        const container = slot as HTMLElement;
        // The value always goes ON the container, because that is what C# enumerates
        // (EnumerateBookImages), but for a canvas background it is the canvas that gets
        // measured.
        const box = getElementThatDeterminesImageSlotSize(container);
        if (
            !isUsableLength(box.offsetWidth) ||
            !isUsableLength(box.offsetHeight)
        ) {
            return;
        }
        const width = roundFraction(box.offsetWidth / page.widthPx);
        const height = roundFraction(box.offsetHeight / page.heightPx);
        targetSlots[index].setAttribute(
            kFractionOfPageAttribute,
            `${width},${height}`,
        );
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
function isUsableLength(value: number | null | undefined): value is number {
    return typeof value === "number" && isFinite(value) && value > 0;
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

// The fraction as it goes into the HTML, to kFractionDecimalPlaces.
function roundFraction(value: number): number {
    const scale = Math.pow(10, kFractionDecimalPlaces);
    return Math.round(value * scale) / scale;
}

// The length in whole millimeters of the given number of CSS pixels, for saying how big a
// container is in units a user of a paper book thinks in.
function cssPixelsToWholeMillimeters(px: number): number {
    return Math.round((px * 25.4) / kBrowserDpi);
}
