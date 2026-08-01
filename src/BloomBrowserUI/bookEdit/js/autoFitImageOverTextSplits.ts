import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
    kCanvasElementSelector,
} from "../toolbox/canvas/canvasElementConstants";
import { EditableDivUtils } from "./editableDivUtils";

// ── Auto-fit image/text origami splits (off-screen process-book only) ───────────────────────────
//
// A page that is a single illustration in one pane and a single text block in the other is usually
// saved at whatever ratio it was authored with (commonly 50/50) — a number that is right only by
// accident. This moves the divider to the size the content actually calls for, in EITHER
// direction:
//
//   - GROW the image pane (shrinking the text pane) when the text leaves room to spare, but no
//     further than the point where the image already fills the constraining page dimension (for
//     top/bottom splits, the page width; for left/right splits, the page height). Growing past that
//     just adds whitespace around the image.
//   - SHRINK the image pane when it is bigger than the image needs. Past the fill point the surplus
//     is dead space, so trimming it costs the illustration NOTHING — it renders at exactly the same
//     size — and hands the difference to the text. And when the text needs more than even that,
//     shrink the image for real: clipped text is a failure, a smaller picture is not.
//
// Governing rule: never leave the text overflowing, and never waste space to no one's benefit. We
// don't estimate font/text sizes — we measure the real browser layout with OverflowChecker, and bias
// toward a hair more text room than the strict minimum.
//
// The one thing we won't do is wreck a page we can't fix: if the text overflows even when given
// almost the whole page, the page is simply over-full, and shrinking the image to a sliver would
// leave it BOTH clipped and ugly. Those we leave exactly as authored.
//
// Two shapes reach that in two different ways. A two-pane page with the illustration FIRST is
// handled by fitImageOverTextSplitOnPage itself, which drives the divider directly. Everything else
// goes to the STACK fitter: panes in a single top-to-bottom row — text above / illustration / text
// below, picture over text, text over picture — holding illustrations and text and nothing else.
// Stacks of three or more panes are a very common layout in scanned story books, and they need us
// more than the two-pane case does, because nothing ever set their dividers on purpose: an importer
// emits one as a right-nested chain of horizontal splits, and with no explicit percentages Bloom's
// 50/50-per-split CSS default cascades into an effective 50/25/25. Those numbers describe the
// nesting, not the content, so the top text block gets half the page for its one line while the last
// (usually longest) one is clipped. A stack may hold SEVERAL illustrations (a figure above the text
// and another below it is a common way to lay out a page whose source wrapped the text around both),
// in which case the pictures divide up whatever the text turns out not to need. See
// fitImageTextStack() for the arithmetic.
//
// Called by captureContentForExternalProcessing() (when process-book asks for it) so the fitted
// split persists into the saved HTML. Mutates the live DOM; relies on the fresh disposable browser
// per page that the off-screen path uses.

// Give the text a little more room than the exact overflow boundary (percent of split-pane height).
const kFitTextCushionPercent = 1.5;
// Never shrink the text pane below this percent of the split-pane height.
const kFitMinTextPercent = 5;
// Don't touch the divider for a move smaller than this (percent of split-pane size). Keeps
// rounding noise from rewriting a split — and re-fitting every background image — for nothing.
const kFitMinMovePercent = 0.5;

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

// Returns true if it changed this page's split (in either direction), false if it left it alone.
function fitImageOverTextSplitOnPage(page: HTMLElement): boolean {
    const marginBox = page.querySelector(".marginBox");
    if (!marginBox) return false;

    // The marginBox's content has to be a single top-level split.
    const splitPane = marginBox.querySelector(
        ":scope > .split-pane.horizontal-percent, :scope > .split-pane.vertical-percent",
    ) as HTMLElement | null;
    if (!splitPane) return false;

    // Nested. The one nested shape we understand is a STACK: panes in a single row along one axis,
    // each of them either an illustration or text (see tryFitAsStack). Anything else nested is too
    // complex — leave it alone.
    if (splitPane.querySelector(".split-pane")) return tryFitAsStack(splitPane);

    const splitConfig = getSplitConfig(splitPane);
    if (!splitConfig) return false;

    // Everything below assumes the illustration is in the FIRST pane. A top/bottom page with the
    // text above and the picture below is the same problem mirrored, and it is also just a two-slot
    // stack — the stack fitter doesn't care which slot holds the picture — so hand it over rather
    // than growing a second copy of the measurement here. A page the stack fitter turns down would
    // have been turned down by the tests below anyway (they require a canvas in the first pane).
    if (
        splitConfig.orientation === "horizontal" &&
        !splitConfig.firstInner.querySelector(kBloomCanvasSelector) &&
        splitConfig.secondInner.querySelector(kBloomCanvasSelector)
    ) {
        return tryFitAsStack(splitPane);
    }

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
        setSecondPanePercent(splitConfig, percent);
        // Force a synchronous reflow so the measurements below see the new layout.
        void splitPane.offsetHeight;
    };

    // Undo the probing exactly, for the paths that end up declining the page — see
    // captureSplitStyles. Writing back `originalTextPercent` instead would look equivalent but isn't:
    // on a split with no explicit percentage (the common case, defaulting to 50%) it would leave an
    // explicit "50%" behind on a page we decided not to touch.
    const savedStyles = captureSplitStyles([splitConfig]);
    const restore = () => restoreSplitStyles(savedStyles, splitPane);

    const textOverflows = (): boolean => textGroupOverflows(secondTextGroup);

    // Upper bound for the search: a text pane this tall is assumed to fit any text we'd auto-fit.
    const hiBound = Math.max(originalTextPercent, 90);

    // If the text doesn't even fit when given most of the page, this isn't a page we can improve by
    // growing the image (it's just over-full). Leave it exactly as we found it.
    setTextPercent(hiBound);
    if (textOverflows()) {
        restore();
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

    // The point past which the image gains nothing: once it fills the page width (or height, for a
    // left/right split) a bigger pane only adds whitespace around it. So this is the text pane's
    // FLOOR — the text is welcome to every percent above it, in either direction from where the page
    // was authored, because those percents are dead space as far as the illustration is concerned.
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
    // Note there is no cap at the authored split: when the text needs more than the image's fill
    // point, finalTextPercent stays at what the text needs and the image really does get smaller.
    // That is deliberate — text the reader can't see is a failure; a smaller picture is a trade.
    finalTextPercent = Math.min(
        Math.max(finalTextPercent, kFitMinTextPercent),
        hiBound,
    );

    // Nothing worth doing (the authored split is already about right): put it back exactly as we
    // found it rather than rewriting the style with a rounding-noise difference.
    if (Math.abs(finalTextPercent - originalTextPercent) < kFitMinMovePercent) {
        restore();
        return false;
    }
    setTextPercent(finalTextPercent);
    return true;
}

// Fit `splitPane` as a stack of panes along one axis, if that is what it is. Returns false for any
// shape the stack fitter doesn't understand, so a caller can treat it as "not my kind of page".
function tryFitAsStack(splitPane: HTMLElement): boolean {
    const stack = collectSplitStack(splitPane);
    if (!stack) return false;
    // Top/bottom stacks only. In a left/right stack the panes have different WIDTHS, so a text
    // block's required height depends on how wide its own pane is; the panes stop being independent
    // and the one-at-a-time measurement fitImageTextStack relies on doesn't hold.
    if (stack.orientation !== "horizontal") return false;
    // It takes both kinds of content to have anything to weigh. An all-TEXT stack has no picture to
    // absorb the slack, so every percent the text doesn't claim would have to go somewhere
    // arbitrary; an all-PICTURE stack has no text that could be clipped, which is the only thing
    // this is here to prevent. Either way, leave the page as authored.
    if (!stack.slots.some((slot) => slot.kind === "image")) return false;
    if (!stack.slots.some((slot) => slot.kind === "text")) return false;
    return fitImageTextStack(stack);
}

// Write a split's position: the SECOND pane takes `percent` of the split's own box, and the first
// pane and the divider are inset from the far edge by the same amount. Callers force the reflow.
function setSecondPanePercent(config: SplitConfig, percent: number): void {
    const value = percent + "%";
    if (config.orientation === "horizontal") {
        config.firstComponent.style.bottom = value;
        config.divider.style.bottom = value;
        config.secondComponent.style.height = value;
    } else {
        config.firstComponent.style.right = value;
        config.divider.style.right = value;
        config.secondComponent.style.width = value;
    }
}

// True if any visible text box in this group has more text than fits. We check both kinds of overflow
// OverflowChecker knows about: the box overflowing itself (type 1) and the box overflowing its
// pane/ancestor (type 2, the usual one for auto-height origami text).
function textGroupOverflows(textGroup: HTMLElement): boolean {
    const editables = Array.from(
        textGroup.querySelectorAll(".bloom-editable.bloom-visibility-code-on"),
    ) as HTMLElement[];
    return editables.some(
        (e) =>
            OverflowChecker.IsOverflowingSelf(e) ||
            OverflowChecker.overflowingAncestor(e) !== null,
    );
}

// ── Stacks of three or more panes ───────────────────────────────────────────────────────────────

// One pane of a stack, and which of the two kinds of content it holds.
interface StackSlot {
    kind: "image" | "text";
    component: HTMLElement; // the .split-pane-component whose size we control
    canvas?: HTMLElement; // image slots only
    textGroup?: HTMLElement; // text slots only
}

export interface SplitStack {
    orientation: SplitOrientation;
    splitPane: HTMLElement; // the OUTERMOST split pane; its box is the whole stack
    splits: SplitConfig[]; // outermost → innermost; one fewer than slots
    slots: StackSlot[]; // in visual order (top → bottom, or left → right)
}

// Decide what a single pane holds. A pane we can't confidently call "just an illustration" or "just
// text" makes the whole page out of scope, so this returns undefined rather than guessing.
function classifySlot(
    inner: Element,
    component: HTMLElement,
): StackSlot | undefined {
    const canvas = inner.querySelector(
        kBloomCanvasSelector,
    ) as HTMLElement | null;
    const textGroup = inner.querySelector(
        ".bloom-translationGroup",
    ) as HTMLElement | null;
    if (canvas && !textGroup) {
        // Overlays (canvas elements other than the background image) put the page out of scope for
        // the same reason as in the two-pane case: our math reasons only about the background
        // image's aspect ratio, and overlays don't scale with it predictably. Not redundant with the
        // `!textGroup` test above, though it looks it: that one already excludes TEXT bubbles (a
        // bubble contains a translationGroup), so this is what excludes image/video/other overlays.
        if (
            canvas.querySelector(
                `${kCanvasElementSelector}:not(.${kBackgroundImageClass})`,
            )
        )
            return undefined;
        return { kind: "image", component, canvas };
    }
    if (textGroup && !canvas) return { kind: "text", component, textGroup };
    return undefined;
}

// Deepest sane nesting we'll walk. Real stacks are three or four panes; anything deeper is a layout
// we don't understand and shouldn't be rearranging.
const kMaxStackDepth = 8;

// Recognize a stack: splits of ONE axis chained down their second panes, each contributing its first
// pane as a slot and the innermost contributing its second pane as the last slot. That right-leaning
// chain is exactly what origami (and BloomBridge) produce for a row of panes along one axis.
// Returns undefined for any other arrangement — a split nested in a FIRST pane, mixed axes, a second
// pane holding a split alongside other content, or a pane whose content we can't classify.
export function collectSplitStack(
    splitPane: HTMLElement,
): SplitStack | undefined {
    const splits: SplitConfig[] = [];
    const slots: StackSlot[] = [];
    let current = splitPane;
    let orientation: SplitOrientation | undefined;
    for (;;) {
        const config = getSplitConfig(current);
        if (!config) return undefined;
        if (orientation === undefined) orientation = config.orientation;
        else if (config.orientation !== orientation) return undefined;

        // A split inside the first pane means this is not a simple one-axis chain.
        if (config.firstInner.querySelector(".split-pane")) return undefined;
        const firstSlot = classifySlot(
            config.firstInner,
            config.firstComponent,
        );
        if (!firstSlot) return undefined;
        splits.push(config);
        slots.push(firstSlot);

        const nested = config.secondInner.querySelector(
            ":scope > .split-pane",
        ) as HTMLElement | null;
        if (!nested) {
            const lastSlot = classifySlot(
                config.secondInner,
                config.secondComponent,
            );
            if (!lastSlot) return undefined;
            slots.push(lastSlot);
            return { orientation, splitPane, splits, slots };
        }
        // The second pane continues the chain, so it must hold the nested split and nothing else.
        if (config.secondInner.children.length !== 1) return undefined;
        if (splits.length >= kMaxStackDepth) return undefined;
        current = nested;
    }
}

// The stored percentages, turned into pane sizes in px along the stack axis. Each split's percentage
// is relative to ITS OWN box, not the page: the outermost split sees the whole stack, the next only
// what is left after the first pane, and so on — which is precisely why the CSS default of 50% per
// split reads as 50/25/25 rather than thirds.
export function readStackSizesPx(stack: SplitStack, totalPx: number): number[] {
    const sizes: number[] = [];
    let remaining = totalPx;
    for (const config of stack.splits) {
        const secondPx = (remaining * readSecondPanePercent(config)) / 100;
        sizes.push(remaining - secondPx);
        remaining = secondPx;
    }
    sizes.push(remaining);
    return sizes;
}

// Remember every style attribute the fitting is about to overwrite, so a page we end up declining can
// be put back byte-for-byte. Measuring means laying the splits out over and over, so by the time we
// decide a page is out of reach its styles have all been rewritten; re-deriving the percentages to
// undo that would stamp an explicit "50%" onto panes that had no style at all, putting a page we
// chose NOT to touch into the saved HTML's diff for nothing.
export function captureSplitStyles(
    configs: SplitConfig[],
): Array<[HTMLElement, string | null]> {
    const saved: Array<[HTMLElement, string | null]> = [];
    for (const config of configs)
        for (const el of [
            config.firstComponent,
            config.divider,
            config.secondComponent,
        ])
            saved.push([el, el.getAttribute("style")]);
    return saved;
}

export function restoreSplitStyles(
    saved: Array<[HTMLElement, string | null]>,
    reflowFrom: HTMLElement,
): void {
    for (const [el, style] of saved) {
        if (style === null) el.removeAttribute("style");
        else el.setAttribute("style", style);
    }
    // Force a synchronous reflow so anything measured afterward sees the restored layout.
    void reflowFrom.offsetHeight;
}

// The inverse of readStackSizesPx: lay the stack out with these pane sizes (px, visual order).
export function applyStackSizesPx(
    stack: SplitStack,
    sizesPx: number[],
    totalPx: number,
): void {
    let remaining = totalPx;
    for (let i = 0; i < stack.splits.length; i++) {
        const secondPx = remaining - sizesPx[i];
        setSecondPanePercent(stack.splits[i], (secondPx / remaining) * 100);
        remaining = secondPx;
    }
    // One synchronous reflow at the end, so measurements afterward see the new layout.
    void stack.splitPane.offsetHeight;
}

// The pane size at which the illustration already fills the stack's width, so a taller pane would
// only add whitespace around it. The px counterpart of computeImageFitFirstPanePercent, simpler
// because every pane in a top/bottom stack is the full width of the stack.
function computeImageFitPaneSizePx(
    stack: SplitStack,
    slot: StackSlot,
): number | undefined {
    const aspectRatio = getImageAspectRatio(slot.canvas!);
    if (aspectRatio === undefined) return undefined;
    const width = stack.splitPane.offsetWidth;
    if (width <= 0) return undefined;
    const scale = EditableDivUtils.getPageScale() || 1;
    // Whatever the pane spends on padding/chrome rather than on the picture itself. Only this term is
    // scale-divided, matching computeImageFitFirstPanePercent and the reference getImagePercent() in
    // lib/split-pane/split-pane.ts. Don't "fix" the asymmetry here alone: it would put this out of
    // step with the re-fit that runs afterward. It costs nothing in the off-screen processor (scale
    // is 1), and even at another scale it can only skew this whitespace cap — never the no-overflow
    // guarantee, which comes from measuring the real text.
    const extra =
        (slot.component.offsetHeight - slot.canvas!.offsetHeight) / scale;
    return width / aspectRatio + extra;
}

// Divide `availablePx` among the illustrations of a stack, given what each one WANTS (the pane size
// at which it already fills the stack's width; see computeImageFitPaneSizePx). Nobody is given more
// than it wants, because past that point a taller pane is pure whitespace, and nobody is dropped
// below `floorPx`. When the pictures want more than there is, they share what there is in proportion
// to what they wanted, which lands them all at the same rendered WIDTH: a pane height proportional
// to 1/aspect is exactly the height at which two contained pictures come out equally wide. When they
// want less, the surplus is left for the caller to hand back to the text.
//
// Exported for unit testing: this is the part of the multi-illustration case that is pure arithmetic
// and so can be checked without a real browser layout.
export function shareImagePanesPx(
    wantsPx: number[],
    availablePx: number,
    floorPx: number,
): number[] {
    // A picture that wants less than the floor still gets the floor, so treating the floor as its
    // want keeps the two rules below from contradicting each other.
    const wants = wantsPx.map((px) => Math.max(px, floorPx));
    const sizes = new Array<number>(wants.length).fill(0);
    const open = wants.map(() => true);
    let remaining = availablePx;
    for (;;) {
        const openIndexes = wants.map((_, i) => i).filter((i) => open[i]);
        if (openIndexes.length === 0) return sizes;
        const wantsTotal = openIndexes.reduce((sum, i) => sum + wants[i], 0);
        // Shares are proportional to what each picture wants. wantsTotal is only ever 0 if a caller
        // passed all-zero wants with a zero floor; then there is nothing to be proportional to, so
        // split evenly.
        const shareOf = (i: number) =>
            wantsTotal > 0
                ? (remaining * wants[i]) / wantsTotal
                : remaining / openIndexes.length;
        // Pin the first pane whose proportional share overshoots what it wants, else the first that
        // undershoots the floor, then re-share what is left among the rest. Each pass pins one pane,
        // so this ends.
        const overshoot = openIndexes.find((i) => shareOf(i) > wants[i]);
        const pinIndex =
            overshoot ?? openIndexes.find((i) => shareOf(i) < floorPx);
        if (pinIndex === undefined) {
            for (const i of openIndexes) sizes[i] = shareOf(i);
            return sizes;
        }
        sizes[pinIndex] = overshoot === undefined ? floorPx : wants[pinIndex];
        open[pinIndex] = false;
        remaining -= sizes[pinIndex];
    }
}

// Fit a top/bottom stack of three or more panes: illustrations in some of them, text in the rest.
//
// What makes this tractable is that every pane in such a stack is the same WIDTH. A text block's
// required height therefore doesn't depend on where any divider sits, so we can measure each text
// pane on its own (give it a size, ask whether it overflows, binary-search the boundary) and then
// simply add the answers up. Same measured-not-estimated rule as the two-pane case. The pictures get
// what the text leaves, shared out by shareImagePanesPx.
//
// Returns true if it changed the page.
function fitImageTextStack(stack: SplitStack): boolean {
    const totalPx = stack.splitPane.offsetHeight;
    if (totalPx <= 0) return false;

    const imageIndexes = stack.slots
        .map((_, i) => i)
        .filter((i) => stack.slots[i].kind === "image");
    const originalSizesPx = readStackSizesPx(stack, totalPx);
    const minPanePx = (totalPx * kFitMinTextPercent) / 100;
    const cushionPx = (totalPx * kFitTextCushionPercent) / 100;
    const savedStyles = captureSplitStyles(stack.splits);
    const restore = () => restoreSplitStyles(savedStyles, stack.splitPane);

    // Measure each illustration's cap NOW, while the stack is still laid out as it was saved. Both
    // inputs are read off the rendered boxes, and the probing below deliberately squeezes whichever
    // panes it isn't testing — including these — so measuring afterward would read a picture's
    // chrome out of a pane collapsed to its minimum.
    const imageFitPx = imageIndexes.map((i) =>
        computeImageFitPaneSizePx(stack, stack.slots[i]),
    );

    // Lay the stack out with one pane at `sizePx` and the rest sharing what's left equally. Only the
    // pane under test is measured, so whatever this does to the others doesn't matter.
    const probe = (index: number, sizePx: number) => {
        const each = (totalPx - sizePx) / (stack.slots.length - 1);
        applyStackSizesPx(
            stack,
            stack.slots.map((_, i) => (i === index ? sizePx : each)),
            totalPx,
        );
    };

    // The most any one pane can have while the others still keep their floor.
    const hiPx = totalPx - minPanePx * (stack.slots.length - 1);

    // Smallest height at which each text pane's text still fits.
    const requiredPx: number[] = [];
    for (let i = 0; i < stack.slots.length; i++) {
        const slot = stack.slots[i];
        if (slot.kind !== "text") {
            requiredPx.push(0);
            continue;
        }
        probe(i, hiPx);
        if (textGroupOverflows(slot.textGroup!)) {
            // This block doesn't fit even with nearly the whole page to itself, so the page is
            // simply over-full. Same call as the two-pane case: leave it exactly as authored rather
            // than shuffling dividers to produce something both clipped AND ugly.
            restore();
            return false;
        }
        let lo = minPanePx; // largest known-overflowing size as we narrow (starts as a guess)
        let hi = hiPx; // smallest known-fitting size
        probe(i, lo);
        if (textGroupOverflows(slot.textGroup!)) {
            for (let n = 0; n < 12; n++) {
                const mid = (lo + hi) / 2;
                probe(i, mid);
                if (textGroupOverflows(slot.textGroup!)) lo = mid;
                else hi = mid;
            }
        } else {
            hi = lo; // even the smallest pane we'd use fits
        }
        requiredPx.push(hi);
    }

    // Each text pane gets what it needs plus the same hair of extra room the two-pane case allows.
    const textTargetsPx = requiredPx.map((px, i) =>
        stack.slots[i].kind === "text" ? px + cushionPx : 0,
    );
    const textTotalPx = textTargetsPx.reduce((a, b) => a + b, 0);

    // Individually they all fit, but together they may still not — and then there is no arrangement
    // of these dividers that works, so don't rearrange them. Every picture needs its floor too.
    if (textTotalPx > totalPx - minPanePx * imageIndexes.length) {
        restore();
        return false;
    }

    // The illustrations take everything the text doesn't need, each up to the point where it already
    // fills the width; past that a taller pane is pure whitespace. An illustration whose aspect we
    // couldn't read has no opinion about its own size, so let it ask for an equal share.
    const availableForImagesPx = totalPx - textTotalPx;
    const fairSharePx = availableForImagesPx / imageIndexes.length;
    const imageSizesPx = shareImagePanesPx(
        imageFitPx.map((px) => px ?? fairSharePx),
        availableForImagesPx,
        minPanePx,
    );

    // Whatever the illustrations decline goes back to the text panes rather than sitting dead,
    // shared out in proportion to what each of them needed.
    const targetsPx = textTargetsPx.slice();
    imageIndexes.forEach((slotIndex, n) => {
        targetsPx[slotIndex] = imageSizesPx[n];
    });
    const leftoverPx =
        availableForImagesPx - imageSizesPx.reduce((a, b) => a + b, 0);
    if (leftoverPx > 0 && textTotalPx > 0) {
        for (let i = 0; i < targetsPx.length; i++)
            if (stack.slots[i].kind === "text")
                targetsPx[i] += (leftoverPx * textTargetsPx[i]) / textTotalPx;
    }

    // Nothing worth doing (the stack is already about right): put it back exactly as we found it
    // rather than rewriting every split — and re-fitting the illustration — for rounding noise.
    const minMovePx = (totalPx * kFitMinMovePercent) / 100;
    if (
        targetsPx.every(
            (px, i) => Math.abs(px - originalSizesPx[i]) < minMovePx,
        )
    ) {
        restore();
        return false;
    }
    applyStackSizesPx(stack, targetsPx, totalPx);
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
//
// CAVEAT for freshly imported books: this box is whatever the importer wrote, which is not
// guaranteed to be the picture's shape. If an importer sizes the background element to the whole
// PANE, we read the PANE's aspect and conclude the image already fills it — i.e. the image-fit cap
// says "no room to reclaim" even when the pane is half empty. That only costs us whitespace: the
// binary search still keeps text from overflowing, because it measures real text, so the guarantee
// holds and the dead space gets reclaimed on a later pass once the box reflects the picture.
// (Measured on BloomBridge output, 2026-07: it writes an aspect-CORRECT box — the pane's height by
// the picture's proportional width — so this doesn't currently bite there. Don't rely on that for
// other importers.) Don't "fix" any of this by preferring naturalWidth/naturalHeight: that would
// ignore cropping and disagree with the re-fit that follows.
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
