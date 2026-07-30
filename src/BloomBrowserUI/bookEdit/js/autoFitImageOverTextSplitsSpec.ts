import { describe, it, expect } from "vitest";
import {
    applyStackSizesPx,
    captureSplitStyles,
    collectSplitStack,
    readStackSizesPx,
    restoreSplitStyles,
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
