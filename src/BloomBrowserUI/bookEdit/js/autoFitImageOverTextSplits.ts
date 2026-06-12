import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { kBloomCanvasSelector } from "../toolbox/canvas/canvasElementConstants";
import { EditableDivUtils } from "./editableDivUtils";

// ── Auto-fit image-over-text origami splits (off-screen process-book only) ──────────────────────
//
// A page that is a single illustration above a single text block — a two-pane vertical origami split
// (a .split-pane.horizontal-percent with a bloom-canvas image in the top pane and a
// bloom-translationGroup in the bottom pane) — is usually saved at whatever ratio it was authored
// with (commonly 50/50). That often leaves a lot of empty space below the text while the image is
// much smaller than it could be. This grows the image pane (shrinking the text pane) as far as it
// can WITHOUT making the text overflow, but no further than the point where the image already fills
// the page width (growing past that just adds whitespace around the image).
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

// Auto-fit every qualifying image-over-text page in the document. Returns true if any page's split
// was changed (so the caller knows to re-fit the background images afterward).
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

// Returns true if it changed this page's split (grew the image), false if it left the page alone.
function fitImageOverTextSplitOnPage(page: HTMLElement): boolean {
    const marginBox = page.querySelector(".marginBox");
    if (!marginBox) return false;

    // We only handle the simple case: the marginBox's content is a single top-level
    // horizontal-percent split with exactly two panes and no nested splits.
    const splitPane = marginBox.querySelector(
        ":scope > .split-pane.horizontal-percent",
    ) as HTMLElement | null;
    if (!splitPane) return false;
    if (splitPane.querySelector(".split-pane")) return false; // nested split: too complex, skip

    const topComponent = splitPane.querySelector(
        ":scope > .split-pane-component.position-top",
    ) as HTMLElement | null;
    const bottomComponent = splitPane.querySelector(
        ":scope > .split-pane-component.position-bottom",
    ) as HTMLElement | null;
    const divider = splitPane.querySelector(
        ":scope > .split-pane-divider",
    ) as HTMLElement | null;
    if (!topComponent || !bottomComponent || !divider) return false;

    const topInner = topComponent.querySelector(
        ":scope > .split-pane-component-inner",
    );
    const bottomInner = bottomComponent.querySelector(
        ":scope > .split-pane-component-inner",
    );
    if (!topInner || !bottomInner) return false;

    // Top pane must be image-only; bottom pane must be text-only. (Matches "an illustration above a
    // single text block.")
    const topCanvas = topInner.querySelector(
        kBloomCanvasSelector,
    ) as HTMLElement | null;
    const topHasText = topInner.querySelector(".bloom-translationGroup");
    const bottomTextGroup = bottomInner.querySelector(
        ".bloom-translationGroup",
    ) as HTMLElement | null;
    const bottomHasCanvas = bottomInner.querySelector(kBloomCanvasSelector);
    if (!topCanvas || topHasText) return false;
    if (!bottomTextGroup || bottomHasCanvas) return false;

    const splitPaneHeight = splitPane.offsetHeight;
    if (splitPaneHeight <= 0) return false;

    // The stored split value is the bottom (text) pane's height as a percent of the split pane; the
    // image (top) pane gets the rest. Growing the image means shrinking this value.
    const originalTextPercent = readBottomPanePercent(bottomComponent);

    const setTextPercent = (percent: number) => {
        const value = percent + "%";
        topComponent.style.bottom = value;
        divider.style.bottom = value;
        bottomComponent.style.height = value;
        // Force a synchronous reflow so the measurements below see the new layout.
        void splitPane.offsetHeight;
    };

    const textEditables = () =>
        Array.from(
            bottomTextGroup.querySelectorAll(
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
    const imageFitTopPercent = computeImageFitTopPercent(splitPane, topCanvas);

    let finalTextPercent = minTextPercent + kFitTextCushionPercent;
    if (imageFitTopPercent !== null) {
        const textFloorForImageWidth = 100 - imageFitTopPercent;
        if (finalTextPercent < textFloorForImageWidth)
            finalTextPercent = textFloorForImageWidth;
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

// Read the bottom (text) pane's height as a percent. The stylesheet defaults an unset split to 50%.
function readBottomPanePercent(bottomComponent: HTMLElement): number {
    const match = (bottomComponent.getAttribute("style") || "").match(
        /height:\s*([0-9.]+)%/,
    );
    return match ? parseFloat(match[1]) : 50;
}

// The percent of the split-pane height the TOP (image) pane needs so the image fills the page width
// at its natural aspect ratio. Returns null if we can't determine it (e.g. image not yet loaded), in
// which case the caller simply skips the width cap (the no-overflow guarantee still holds). Mirrors
// the horizontal-split aspect math in split-pane.ts getImagePercent().
function computeImageFitTopPercent(
    splitPane: HTMLElement,
    topCanvas: HTMLElement,
): number | null {
    const aspectRatio = getImageAspectRatio(topCanvas);
    if (aspectRatio === null) return null;
    const splitPaneHeight = splitPane.offsetHeight;
    if (splitPaneHeight <= 0) return null;
    const scale = EditableDivUtils.getPageScale() || 1;
    const width = splitPane.offsetWidth;
    const imageHeight = width / aspectRatio;
    // Compensate for any padding between the component (which carries the percent) and the canvas.
    const topComponent = topCanvas.closest(
        ".split-pane-component",
    ) as HTMLElement | null;
    const extraHeight = topComponent
        ? (topComponent.offsetHeight - topCanvas.offsetHeight) / scale
        : 0;
    return ((imageHeight + extraHeight) * 100) / splitPaneHeight;
}

// The DISPLAYED aspect ratio (width/height) of the image in this bloom-canvas, or null if unknown.
// We measure the rendered .bloom-backgroundImage canvas element rather than the <img>'s natural
// dimensions, because that box reflects any cropping the user applied — and it is the same source
// adjustBackgroundImageSizeToFit() (split-pane.ts getImagePercent()) uses, so our width cap agrees
// with how the image will actually be re-fit afterward. The background canvas element keeps its
// load-time size while we resize panes, so this aspect is stable across the binary search.
function getImageAspectRatio(bloomCanvas: HTMLElement): number | null {
    const bg = bloomCanvas.getElementsByClassName(
        "bloom-backgroundImage",
    )[0] as HTMLElement | undefined;
    if (bg && bg.clientWidth > 0 && bg.clientHeight > 0) {
        return bg.clientWidth / bg.clientHeight;
    }
    // Fallback: the image's natural dimensions (ignores cropping, but better than nothing).
    const img = bloomCanvas.querySelector("img") as HTMLImageElement | null;
    if (img && img.naturalWidth > 0 && img.naturalHeight > 0) {
        return img.naturalWidth / img.naturalHeight;
    }
    return null;
}
