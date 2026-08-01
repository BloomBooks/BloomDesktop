import { describe, it, expect } from "vitest";
import {
    applyStackSizesPx,
    captureSplitStyles,
    collectSplitStack,
    readStackSizesPx,
    restoreSplitStyles,
    shareImagePanesPx,
} from "./autoFitImageOverTextSplits";

// These tests cover the two parts of the nested-stack support that don't need a real browser
// layout: recognizing which pages are a stack we can fit, and the percentage arithmetic that
// converts between per-split percentages and pane sizes. The measuring itself (binary-searching
// the overflow boundary) needs real geometry, which jsdom does not provide — offsetHeight and
// getBoundingClientRect are always 0 here — so it is verified end-to-end against a real book
// instead. Keep that in mind before "fixing" a test by asking it about pixel sizes.

const kTextPane = `<div class="bloom-translationGroup">
    <div class="bloom-editable bloom-visibility-code-on" lang="fuv"><p>text</p></div>
</div>`;

const kImagePane = `<div class="bloom-canvas bloom-has-canvas-element">
    <div class="bloom-canvas-element bloom-backgroundImage">
        <div class="bloom-imageContainer"><img src="pic.png" /></div>
    </div>
</div>`;

// An illustration with a text bubble floating on it — a canvas element that is not the background
// image, which puts the page out of scope.
const kImagePaneWithOverlay = `<div class="bloom-canvas bloom-has-canvas-element">
    <div class="bloom-canvas-element bloom-backgroundImage">
        <div class="bloom-imageContainer"><img src="pic.png" /></div>
    </div>
    <div class="bloom-canvas-element"><div class="bloom-translationGroup"></div></div>
</div>`;

/** One split pane: `first` in the leading pane, `second` in the trailing one. */
function split(
    axis: "horizontal" | "vertical",
    first: string,
    second: string,
): string {
    const leading = axis === "horizontal" ? "top" : "left";
    const trailing = axis === "horizontal" ? "bottom" : "right";
    return `<div class="split-pane ${axis}-percent">
        <div class="split-pane-component position-${leading}">
            <div class="split-pane-component-inner">${first}</div>
        </div>
        <div class="split-pane-divider ${axis}-divider"></div>
        <div class="split-pane-component position-${trailing}">
            <div class="split-pane-component-inner">${second}</div>
        </div>
    </div>`;
}

/** Parse a marginBox's worth of HTML and hand back its top-level split pane. */
function topSplitOf(inner: string): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = `<div class="marginBox">${inner}</div>`;
    document.body.appendChild(page);
    return page.querySelector(".split-pane") as HTMLElement;
}

/** The text/image/text page this whole feature exists for, as BloomBridge emits it. */
function textImageTextStack(): HTMLElement {
    return topSplitOf(
        split(
            "horizontal",
            kTextPane,
            split("horizontal", kImagePane, kTextPane),
        ),
    );
}

describe("collectSplitStack", () => {
    it("recognizes a text/image/text stack, in visual order", () => {
        const stack = collectSplitStack(textImageTextStack());
        expect(stack).toBeDefined();
        expect(stack!.orientation).toBe("horizontal");
        expect(stack!.slots.map((s) => s.kind)).toEqual([
            "text",
            "image",
            "text",
        ]);
        // One split per divider: two dividers for three panes.
        expect(stack!.splits.length).toBe(2);
    });

    it("recognizes deeper stacks and other positions for the illustration", () => {
        const imageLast = collectSplitStack(
            topSplitOf(
                split(
                    "horizontal",
                    kTextPane,
                    split("horizontal", kTextPane, kImagePane),
                ),
            ),
        );
        expect(imageLast!.slots.map((s) => s.kind)).toEqual([
            "text",
            "text",
            "image",
        ]);

        const fourPanes = collectSplitStack(
            topSplitOf(
                split(
                    "horizontal",
                    kTextPane,
                    split(
                        "horizontal",
                        kImagePane,
                        split("horizontal", kTextPane, kTextPane),
                    ),
                ),
            ),
        );
        expect(fourPanes!.slots.map((s) => s.kind)).toEqual([
            "text",
            "image",
            "text",
            "text",
        ]);
    });

    it("recognizes a stack holding more than one illustration", () => {
        // A figure above the text and another below it: what an importer produces for a source page
        // that wrapped its text around two small pictures. The pictures then divide up whatever the
        // text doesn't need (see shareImagePanesPx).
        const stack = collectSplitStack(
            topSplitOf(
                split(
                    "horizontal",
                    kImagePane,
                    split("horizontal", kTextPane, kImagePane),
                ),
            ),
        );
        expect(stack!.slots.map((s) => s.kind)).toEqual([
            "image",
            "text",
            "image",
        ]);
    });

    it("reads a plain two-pane text-over-picture page as a two-slot stack", () => {
        // Not nested at all, but the stack fitter is what handles it: the two-pane code in
        // fitImageOverTextSplitOnPage drives the divider from the illustration's own pane and so
        // assumes the picture is FIRST. Text above, picture below is the mirror image of that.
        const stack = collectSplitStack(
            topSplitOf(split("horizontal", kTextPane, kImagePane)),
        );
        expect(stack!.slots.map((s) => s.kind)).toEqual(["text", "image"]);
        // Two panes, so one divider and one split to write.
        expect(stack!.splits.length).toBe(1);
    });

    it("reads a side-by-side stack but reports it as vertical, so the caller can decline it", () => {
        const stack = collectSplitStack(
            topSplitOf(
                split(
                    "vertical",
                    kTextPane,
                    split("vertical", kImagePane, kTextPane),
                ),
            ),
        );
        expect(stack!.orientation).toBe("vertical");
    });

    it("declines a stack whose axes are mixed", () => {
        expect(
            collectSplitStack(
                topSplitOf(
                    split(
                        "horizontal",
                        kTextPane,
                        split("vertical", kImagePane, kTextPane),
                    ),
                ),
            ),
        ).toBeUndefined();
    });

    it("declines a split nested in the FIRST pane (not a one-axis chain)", () => {
        expect(
            collectSplitStack(
                topSplitOf(
                    split(
                        "horizontal",
                        split("horizontal", kTextPane, kImagePane),
                        kTextPane,
                    ),
                ),
            ),
        ).toBeUndefined();
    });

    it("declines a second pane holding a nested split alongside other content", () => {
        expect(
            collectSplitStack(
                topSplitOf(
                    split(
                        "horizontal",
                        kTextPane,
                        kTextPane + split("horizontal", kImagePane, kTextPane),
                    ),
                ),
            ),
        ).toBeUndefined();
    });

    it("declines a pane that holds both an illustration and text", () => {
        expect(
            collectSplitStack(
                topSplitOf(
                    split(
                        "horizontal",
                        kTextPane,
                        split("horizontal", kImagePane + kTextPane, kTextPane),
                    ),
                ),
            ),
        ).toBeUndefined();
    });

    it("declines an illustration carrying an overlay", () => {
        expect(
            collectSplitStack(
                topSplitOf(
                    split(
                        "horizontal",
                        kTextPane,
                        split("horizontal", kImagePaneWithOverlay, kTextPane),
                    ),
                ),
            ),
        ).toBeUndefined();
    });

    it("declines an empty pane", () => {
        expect(
            collectSplitStack(
                topSplitOf(
                    split(
                        "horizontal",
                        kTextPane,
                        split("horizontal", "", kTextPane),
                    ),
                ),
            ),
        ).toBeUndefined();
    });
});

describe("shareImagePanesPx", () => {
    // What each picture "wants" is the pane height at which it already fills the stack's width,
    // i.e. width / aspect. A tall narrow figure wants far more height than any page has; a wide one
    // wants little.
    const kFloor = 40;

    it("gives every picture what it wants when there is room to spare", () => {
        const sizes = shareImagePanesPx([100, 150], 400, kFloor);
        expect(sizes).toEqual([100, 150]);
        // The 150 left over is the caller's to hand back to the text, so it must NOT be padded in
        // here: that is the difference between a picture at its natural size and one in a pane with
        // 150px of whitespace around it.
        expect(sizes.reduce((a, b) => a + b, 0)).toBe(250);
    });

    it("shares in proportion when the pictures want more than there is", () => {
        const sizes = shareImagePanesPx([1400, 800], 330, kFloor);
        expect(sizes[0]).toBeCloseTo((330 * 1400) / 2200, 6);
        expect(sizes[1]).toBeCloseTo((330 * 800) / 2200, 6);
        expect(sizes[0] + sizes[1]).toBeCloseTo(330, 6);
    });

    it("lands two height-limited pictures at the same rendered width", () => {
        // This is the point of sharing by what each wants rather than evenly. Both figures are taller
        // than any pane we could give them, so each renders at (pane height x its aspect); heights
        // proportional to 1/aspect are exactly the heights at which those widths come out equal.
        const stackWidth = 469;
        const aspects = [0.33, 0.58];
        const wants = aspects.map((a) => stackWidth / a);
        expect(wants[0]).toBeGreaterThan(350); // sanity: both really are height-limited here
        expect(wants[1]).toBeGreaterThan(350);
        const sizes = shareImagePanesPx(wants, 350, kFloor);
        const renderedWidths = sizes.map((h, i) => h * aspects[i]);
        expect(renderedWidths[0]).toBeCloseTo(renderedWidths[1], 6);
    });

    it("never drops a picture below the floor, and never hands out more than there is", () => {
        // The second picture's proportional share would be ~10px. It gets the floor instead, and the
        // first one absorbs the difference rather than the stack overflowing.
        const sizes = shareImagePanesPx([1000, 50], 200, 50);
        expect(sizes[1]).toBe(50);
        expect(sizes[0]).toBeCloseTo(150, 6);
    });

    it("caps a picture at what it wants even while another is squeezed", () => {
        // A wide picture (wants only 60) and a tall one (wants 1000) sharing 300. The wide one is
        // held to the floor rather than its even share, and everything left goes to the tall one.
        const sizes = shareImagePanesPx([60, 1000], 300, kFloor);
        expect(sizes[0]).toBe(kFloor);
        expect(sizes[1]).toBeCloseTo(260, 6);
        expect(sizes[0] + sizes[1]).toBeCloseTo(300, 6);
    });
});

describe("stack size arithmetic", () => {
    it("reads unset splits as 50/25/25 — the nesting artifact this feature fixes", () => {
        // Neither split carries a percentage, so each falls back to the CSS default of 50%. Because
        // the inner split only divides what the outer one left over, that reads as 50/25/25 and NOT
        // as equal thirds. This is exactly the layout that gives a one-line text block half the page.
        const stack = collectSplitStack(textImageTextStack())!;
        expect(readStackSizesPx(stack, 800)).toEqual([400, 200, 200]);
    });

    it("round-trips pane sizes through the nested percentages", () => {
        const stack = collectSplitStack(textImageTextStack())!;
        const target = [120, 460, 220];
        applyStackSizesPx(stack, target, 800);
        const readBack = readStackSizesPx(stack, 800);
        readBack.forEach((px, i) => expect(px).toBeCloseTo(target[i], 6));
    });

    it("writes each split's percentage relative to that split's own box", () => {
        const stack = collectSplitStack(textImageTextStack())!;
        applyStackSizesPx(stack, [200, 400, 200], 800);
        // The outer split keeps 200 of 800 for the top text, so its second pane is 600/800 = 75%.
        expect(stack.splits[0].secondComponent.style.height).toBe("75%");
        // The inner split divides that 600: the last text's 200 is a third of it, not a quarter of
        // the page. Getting this wrong is the easiest way to reintroduce the bug.
        expect(
            parseFloat(stack.splits[1].secondComponent.style.height),
        ).toBeCloseTo(100 / 3, 6);
    });

    it("moves the first pane's inset and the divider along with the second pane", () => {
        const stack = collectSplitStack(textImageTextStack())!;
        applyStackSizesPx(stack, [200, 400, 200], 800);
        // All three of these have to agree, or the divider ends up somewhere other than the seam.
        expect(stack.splits[0].firstComponent.style.bottom).toBe("75%");
        expect(stack.splits[0].divider.style.bottom).toBe("75%");
        expect(stack.splits[0].secondComponent.style.height).toBe("75%");
    });

    it("restores a declined page byte-for-byte, adding no style attributes", () => {
        // Measuring means laying the stack out repeatedly, so by the time we decide a page is out of
        // reach its styles have all been rewritten. Putting back the ORIGINAL attributes (rather than
        // re-deriving 50%) is what keeps a page we chose not to touch out of the saved HTML's diff.
        const stack = collectSplitStack(textImageTextStack())!;
        const saved = captureSplitStyles(stack.splits);
        applyStackSizesPx(stack, [120, 460, 220], 800);
        expect(stack.splits[0].secondComponent.getAttribute("style")).not.toBe(
            null,
        );
        restoreSplitStyles(saved, stack.splitPane);
        for (const config of stack.splits)
            for (const el of [
                config.firstComponent,
                config.divider,
                config.secondComponent,
            ])
                expect(el.getAttribute("style")).toBe(null);
    });

    it("restores an authored split to its exact original text", () => {
        const stack = collectSplitStack(textImageTextStack())!;
        stack.splits[0].secondComponent.setAttribute("style", "height: 62%");
        const saved = captureSplitStyles(stack.splits);
        applyStackSizesPx(stack, [120, 460, 220], 800);
        restoreSplitStyles(saved, stack.splitPane);
        expect(stack.splits[0].secondComponent.getAttribute("style")).toBe(
            "height: 62%",
        );
    });

    it("reads an illustration/text/illustration stack as 50/25/25 as well", () => {
        // The shape from a real import (FUV_Taale_Taale_Fulbe page 16: a figure, the story text, a
        // second figure) whose source wrapped the text around both pictures. Unsized, the text block
        // gets a quarter of the page for what needs about half of it, which is what the fitting fixes.
        const stack = collectSplitStack(
            topSplitOf(
                split(
                    "horizontal",
                    kImagePane,
                    split("horizontal", kTextPane, kImagePane),
                ),
            ),
        )!;
        expect(readStackSizesPx(stack, 800)).toEqual([400, 200, 200]);
    });

    it("round-trips a four-pane stack too", () => {
        const stack = collectSplitStack(
            topSplitOf(
                split(
                    "horizontal",
                    kTextPane,
                    split(
                        "horizontal",
                        kImagePane,
                        split("horizontal", kTextPane, kTextPane),
                    ),
                ),
            ),
        )!;
        const target = [100, 350, 150, 200];
        applyStackSizesPx(stack, target, 800);
        readStackSizesPx(stack, 800).forEach((px, i) =>
            expect(px).toBeCloseTo(target[i], 6),
        );
    });
});
