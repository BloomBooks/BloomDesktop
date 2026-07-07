import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
    kCanvasElementSelector,
} from "../toolbox/canvas/canvasElementConstants";
import { EditableDivUtils } from "./editableDivUtils";

// ── Auto-fit image/text origami splits (off-screen process-book only) ───────────────────────────
//
// A page that is a single illustration in the first pane and a single text block in the second pane
// is usually saved at whatever ratio it was authored with (commonly 50/50). That often leaves a lot
// of empty space after the text while the image is much smaller than it could be. This grows the
// image pane (shrinking the text pane) as far as it can WITHOUT making the text overflow, but no
// further than the point where the image already fills the constraining page dimension (for
// top/bottom splits, the page width; for left/right splits, the page height). Growing past that just
// adds whitespace around the image.
//
// Governing rule: never leave the text overflowing. We don't estimate font/text sizes — we measure
// the real browser layout with OverflowChecker, and bias toward a hair more text room than the
// strict minimum. Wasted space is only a cosmetic ding; clipped text is a failure.
//
// Called by captureContentForExternalProcessing() (when process-book asks for it) so the grown
// split persists into the saved HTML. Mutates the live DOM; relies on the fresh disposable browser
// per page that the off-screen path uses.

// Give the text a little more room than the exact overflow boundary (percent of split-pane height).
const kFitTextCushionPercent = 1.5;
// Never shrink the text pane below this percent of the split-pane height.
const kFitMinTextPercent = 5;

// Auto-fit every qualifying image/text page in the document. Returns true if any page's split was
// changed (so the caller knows to re-fit the background images afterward).
export function fitImageOverTextSplits(): boolean {
    const pages = Array.from(
        document.querySelectorAll(".bloom-page"),
    ) as HTMLElement[];
    let changedAny = false;
    for (const page of pages) {
        if (fitImageOverTextSplitOnPage(page)) changedAny = true;
    }
    return changedAny;
}

type SplitOrientation = "horizontal" | "vertical";

interface SplitConfig {
    orientation: SplitOrientation;
    firstComponent: HTMLElement;
    secondComponent: HTMLElement;
    divider: HTMLElement;
    firstInner: Element;
    secondInner: Element;
}

// Returns true if it changed this page's split (grew the image), false if it left the page alone.
function fitImageOverTextSplitOnPage(page: HTMLElement): boolean {
    const marginBox = page.querySelector(".marginBox");
    if (!marginBox) return false;

    // We only handle the simple case: the marginBox's content is a single top-level two-pane split
    // with no nested splits.
    const splitPane = marginBox.querySelector(
        ":scope > .split-pane.horizontal-percent, :scope > .split-pane.vertical-percent",
    ) as HTMLElement | null;
    if (!splitPane) return false;
    if (splitPane.querySelector(".split-pane")) return false; // nested split: too complex, skip

    const splitConfig = getSplitConfig(splitPane);
    if (!splitConfig) return false;

    // First pane must be a plain background image with no overlays; second pane must be text-only.
    const firstCanvas = splitConfig.firstInner.querySelector(
        kBloomCanvasSelector,
    ) as HTMLElement | null;
    const firstHasText = splitConfig.firstInner.querySelector(
        ".bloom-translationGroup",
    );
    // Overlays (canvas elements other than the background image) make this page out of scope. Our
    // resizing math reasons only about the background image's aspect ratio; overlays don't scale with
    // it predictably — a text bubble keeps its font size (changing line breaks / revealing or clipping
    // content) and any bubble could end up extending past the resized image — so we leave such pages
    // exactly as authored. Text overlays are technically also caught by firstHasText (a text bubble
    // contains a .bloom-translationGroup), but this also excludes image/video/other overlays.
    const firstHasOverlay = firstCanvas?.querySelector(
        `${kCanvasElementSelector}:not(.${kBackgroundImageClass})`,
    );
    const secondTextGroup = splitConfig.secondInner.querySelector(
        ".bloom-translationGroup",
    ) as HTMLElement | null;
    const secondHasCanvas =
        splitConfig.secondInner.querySelector(kBloomCanvasSelector);
    if (!firstCanvas || firstHasText || firstHasOverlay) return false;
    if (!secondTextGroup || secondHasCanvas) return false;

    const splitPaneRelevantSize =
        splitConfig.orientation === "horizontal"
            ? splitPane.offsetHeight
            : splitPane.offsetWidth;
    if (splitPaneRelevantSize <= 0) return false;

    // The stored split value is the second (text) pane's size as a percent of the split pane; the
    // image pane gets the rest. Growing the image means shrinking this value.
    const originalTextPercent = readSecondPanePercent(splitConfig);

    const setTextPercent = (percent: number) => {
        const value = percent + "%";
        if (splitConfig.orientation === "horizontal") {
            splitConfig.firstComponent.style.bottom = value;
            splitConfig.divider.style.bottom = value;
            splitConfig.secondComponent.style.height = value;
        } else {
            splitConfig.firstComponent.style.right = value;
            splitConfig.divider.style.right = value;
            splitConfig.secondComponent.style.width = value;
        }
        // Force a synchronous reflow so the measurements below see the new layout.
        void splitPane.offsetHeight;
    };

    const textEditables = () =>
        Array.from(
            secondTextGroup.querySelectorAll(
                ".bloom-editable.bloom-visibility-code-on",
            ),
        ) as HTMLElement[];

    // True if any visible text box has more text than fits in the (current) text pane. We check both
    // kinds of overflow OverflowChecker knows about: the box overflowing itself (type 1) and the box
    // overflowing its pane/ancestor (type 2, the usual one for auto-height origami text).
    const textOverflows = (): boolean =>
        textEditables().some(
            (e) =>
                OverflowChecker.IsOverflowingSelf(e) ||
                OverflowChecker.overflowingAncestor(e) !== null,
        );

    // Upper bound for the search: a text pane this tall is assumed to fit any text we'd auto-fit.
    const hiBound = Math.max(originalTextPercent, 90);

    // If the text doesn't even fit when given most of the page, this isn't a page we can improve by
    // growing the image (it's just over-full). Leave it exactly as we found it.
    setTextPercent(hiBound);
    if (textOverflows()) {
        setTextPercent(originalTextPercent);
        return false;
    }

    // Binary-search the smallest text-pane percent at which the text still does not overflow.
    let lo = kFitMinTextPercent; // largest known-overflowing value as we narrow (starts as a guess)
    let hi = hiBound; // smallest known-fitting value
    setTextPercent(lo);
    if (textOverflows()) {
        for (let i = 0; i < 12; i++) {
            const mid = (lo + hi) / 2;
            setTextPercent(mid);
            if (textOverflows()) lo = mid;
            else hi = mid;
        }
    } else {
        hi = lo; // even a tiny text pane fits
    }
    const minTextPercent = hi;

    // Cap how far the image can grow: once it fills the page width, growing the image pane further
    // just adds whitespace around the image, so don't shrink the text below that point.
    const imageFitFirstPanePercent = computeImageFitFirstPanePercent(
        splitPane,
        firstCanvas,
        splitConfig.orientation,
    );

    let finalTextPercent = minTextPercent + kFitTextCushionPercent;
    if (imageFitFirstPanePercent !== undefined) {
        const textFloorForImageFit = 100 - imageFitFirstPanePercent;
        if (finalTextPercent < textFloorForImageFit)
            finalTextPercent = textFloorForImageFit;
    }
    finalTextPercent = Math.min(
        Math.max(finalTextPercent, kFitMinTextPercent),
        hiBound,
    );

    // Only ever grow the image (shrink the text). If our computed split wouldn't enlarge the image,
    // leave the page as authored.
    if (finalTextPercent >= originalTextPercent) {
        setTextPercent(originalTextPercent);
        return false;
    }
    setTextPercent(finalTextPercent);
    return true;
}

function getSplitConfig(splitPane: HTMLElement): SplitConfig | undefined {
    // A split pane is laid out either horizontally (panes stacked top/bottom) or vertically
    // (panes side-by-side left/right). The two cases differ only in the marker class and the
    // position classes of the two panes, so describe them in a table and share the lookup below.
    const layouts: Array<{
        orientation: SplitOrientation;
        percentClass: string;
        firstPosition: string;
        secondPosition: string;
    }> = [
        {
            orientation: "horizontal",
            percentClass: "horizontal-percent",
            firstPosition: "position-top",
            secondPosition: "position-bottom",
        },
        {
            orientation: "vertical",
            percentClass: "vertical-percent",
            firstPosition: "position-left",
            secondPosition: "position-right",
        },
    ];
    const layout = layouts.find((l) =>
        splitPane.classList.contains(l.percentClass),
    );
    if (!layout) {
        return undefined;
    }

    const firstComponent = splitPane.querySelector(
        `:scope > .split-pane-component.${layout.firstPosition}`,
    ) as HTMLElement | null;
    const secondComponent = splitPane.querySelector(
        `:scope > .split-pane-component.${layout.secondPosition}`,
    ) as HTMLElement | null;
    const divider = splitPane.querySelector(
        ":scope > .split-pane-divider",
    ) as HTMLElement | null;
    const firstInner = firstComponent?.querySelector(
        ":scope > .split-pane-component-inner",
    );
    const secondInner = secondComponent?.querySelector(
        ":scope > .split-pane-component-inner",
    );
    if (
        !firstComponent ||
        !secondComponent ||
        !divider ||
        !firstInner ||
        !secondInner
    ) {
        return undefined;
    }
    return {
        orientation: layout.orientation,
        firstComponent,
        secondComponent,
        divider,
        firstInner,
        secondInner,
    };
}

// Read the second (text) pane's size as a percent. The stylesheet defaults an unset split to 50%.
function readSecondPanePercent(splitConfig: SplitConfig): number {
    const match = (
        splitConfig.secondComponent.getAttribute("style") || ""
    ).match(
        splitConfig.orientation === "horizontal"
            ? /height:\s*([0-9.]+)%/
            : /width:\s*([0-9.]+)%/,
    );
    return match ? parseFloat(match[1]) : 50;
}

// The percent of the split-pane size the FIRST (image) pane needs so the image fills the limiting
// page dimension at its natural aspect ratio. Returns undefined if we can't determine it (e.g. image
// not yet loaded), in which case the caller simply skips the cap (the no-overflow guarantee still
// holds). Mirrors the aspect math in split-pane.ts getImagePercent().
function computeImageFitFirstPanePercent(
    splitPane: HTMLElement,
    firstCanvas: HTMLElement,
    orientation: SplitOrientation,
): number | undefined {
    const aspectRatio = getImageAspectRatio(firstCanvas);
    if (aspectRatio === undefined) return undefined;
    const scale = EditableDivUtils.getPageScale() || 1;
    const firstComponent = firstCanvas.closest(
        ".split-pane-component",
    ) as HTMLElement | null;

    if (orientation === "horizontal") {
        const splitPaneHeight = splitPane.offsetHeight;
        if (splitPaneHeight <= 0) return undefined;
        const width = splitPane.offsetWidth;
        const imageHeight = width / aspectRatio;
        const extraHeight = firstComponent
            ? (firstComponent.offsetHeight - firstCanvas.offsetHeight) / scale
            : 0;
        return ((imageHeight + extraHeight) * 100) / splitPaneHeight;
    }

    const splitPaneWidth = splitPane.offsetWidth;
    if (splitPaneWidth <= 0) return undefined;
    const height = splitPane.offsetHeight;
    const imageWidth = height * aspectRatio;
    const extraWidth = firstComponent
        ? (firstComponent.offsetWidth - firstCanvas.offsetWidth) / scale
        : 0;
    return ((imageWidth + extraWidth) * 100) / splitPaneWidth;
}

// The DISPLAYED aspect ratio (width/height) of the image in this bloom-canvas, or undefined if
// unknown.
// We measure the rendered .bloom-backgroundImage canvas element rather than the <img>'s natural
// dimensions, because that box reflects any cropping the user applied — and it is the same source
// adjustBackgroundImageSizeToFit() (split-pane.ts getImagePercent()) uses, so our width cap agrees
// with how the image will actually be re-fit afterward. The background canvas element keeps its
// load-time size while we resize panes, so this aspect is stable across the binary search.
function getImageAspectRatio(bloomCanvas: HTMLElement): number | undefined {
    const bg = bloomCanvas.getElementsByClassName(
        "bloom-backgroundImage",
    )[0] as HTMLElement | undefined;
    if (bg && bg.clientWidth > 0 && bg.clientHeight > 0) {
        return bg.clientWidth / bg.clientHeight;
    }
    // Fallback: the image's natural dimensions (ignores cropping, but better than nothing). These read
    // as 0 until the image has loaded, and a missing/placeholder/corrupt image may never acquire a
    // natural size; in those cases we return undefined and the caller simply skips the image-fit cap (the
    // no-overflow guarantee from the binary search still holds). In the off-screen book processor the
    // image-sizing delay (SetImageDisplaySizeIfCalledFor registers a requestPageContent delay) means
    // images are normally loaded before we get here, so the .bloom-backgroundImage branch above usually
    // wins anyway.
    const img = bloomCanvas.querySelector("img") as HTMLImageElement | null;
    if (img && img.naturalWidth > 0 && img.naturalHeight > 0) {
        return img.naturalWidth / img.naturalHeight;
    }
    return undefined;
}
