import { beforeEach, describe, expect, test } from "vitest";

// Tests for the one place in Bloom that decides how big an image ought to be for the space it
// occupies. Two consumers depend on these numbers: the image tooltip's "would fill this
// container" line, and the size the AI image editor generates at. They differ by output
// medium — a printed page wants 300 DPI, a screen-sized page wants its share of the screen
// Bloom's digital books are published at.

import {
    getOpenPageMetrics,
    getSuggestedImageTargetForContainer,
    getSuggestedImageTargetForFraction,
    isDeviceLayoutPage,
    kDigitalScreenLongEdgePx,
    kFractionOfPageAttribute,
    parseFractionOfPage,
    recordFractionOfPageOnImageSlots,
} from "./imageTargetResolution";

// jsdom lays nothing out, so offsetWidth/offsetHeight are always 0; the sizes a real browser
// would measure have to be stated.
const setLayoutSize = (el: HTMLElement, width: number, height: number) => {
    Object.defineProperty(el, "offsetWidth", {
        value: width,
        configurable: true,
    });
    Object.defineProperty(el, "offsetHeight", {
        value: height,
        configurable: true,
    });
};

// A page of the given layout holding one image container, both at the given sizes.
const makePage = (
    pageClass: string,
    pageWidth: number,
    pageHeight: number,
    containerWidth: number,
    containerHeight: number,
): HTMLElement => {
    document.body.innerHTML = `<div class="bloom-page ${pageClass}"><div class="bloom-canvas"></div></div>`;
    const page = document.querySelector(".bloom-page") as HTMLElement;
    const container = document.querySelector(".bloom-canvas") as HTMLElement;
    setLayoutSize(page, pageWidth, pageHeight);
    setLayoutSize(container, containerWidth, containerHeight);
    return container;
};

beforeEach(() => {
    document.body.innerHTML = "";
});

describe("getSuggestedImageTargetForContainer on a paper page", () => {
    test("asks for whatever fills the container at 300 DPI", () => {
        const container = makePage("A5Portrait", 559, 794, 469, 546);
        // Sanity checks: without these a wrong answer below could just mean jsdom reported 0.
        expect(container.offsetWidth).toBe(469);
        expect(container.offsetHeight).toBe(546);

        const suggestion = getSuggestedImageTargetForContainer(container);

        if (!suggestion)
            throw new Error("a sized container should be measurable");
        expect(suggestion.isDigital).toBe(false);
        // ceil(300 * 469 / 96) and ceil(300 * 546 / 96)
        expect(suggestion.width).toBe(1466);
        expect(suggestion.height).toBe(1707);
    });

    test("the memo says 300 DPI and how big the container is in millimeters", () => {
        const container = makePage("A5Portrait", 559, 794, 469, 546);

        const suggestion = getSuggestedImageTargetForContainer(container);

        if (!suggestion)
            throw new Error("a sized container should be measurable");
        expect(suggestion.memo).toContain("1466 x 1707");
        expect(suggestion.memo).toContain("300 DPI");
        // 469 px is 124 mm and 546 px is 144 mm, at 96 px to the inch.
        expect(suggestion.memo).toContain("124 mm x 144 mm");
    });
});

describe("getSuggestedImageTargetForContainer on a device page", () => {
    test("asks for the container's share of a 1280-pixel screen", () => {
        const container = makePage("Device16x9Portrait", 378, 672, 378, 300);
        // Sanity check: this test is only meaningful if the layout counts as a device one.
        expect(
            isDeviceLayoutPage(
                document.querySelector(".bloom-page") as HTMLElement,
            ),
        ).toBe(true);

        const suggestion = getSuggestedImageTargetForContainer(container);

        if (!suggestion)
            throw new Error("a sized container should be measurable");
        expect(suggestion.isDigital).toBe(true);
        const scale = kDigitalScreenLongEdgePx / 672;
        expect(suggestion.width).toBe(Math.ceil(378 * scale));
        expect(suggestion.height).toBe(Math.ceil(300 * scale));
    });

    test("a page that is not 16x9 is fitted to the screen's short edge too", () => {
        // A 2x3 portrait page: 500 across, 750 tall. Taking its long edge to 1280 would make
        // it 853 across, and the publish step caps the short edge at 720, so those pixels
        // would be thrown away. Fitting both edges gives 720 x 1080 for a full-page slot.
        const container = makePage("Ebook2x3Portrait", 500, 750, 500, 750);
        // Sanity check: this test is only meaningful if the layout counts as a device one.
        expect(
            isDeviceLayoutPage(
                document.querySelector(".bloom-page") as HTMLElement,
            ),
        ).toBe(true);

        const suggestion = getSuggestedImageTargetForContainer(container);

        if (!suggestion)
            throw new Error("a sized container should be measurable");
        expect(suggestion.isDigital).toBe(true);
        expect(suggestion.width).toBe(720);
        expect(suggestion.height).toBe(1080);
        // Neither edge exceeds what the publish step keeps.
        expect(
            Math.max(suggestion.width, suggestion.height),
        ).toBeLessThanOrEqual(kDigitalScreenLongEdgePx);
        expect(
            Math.min(suggestion.width, suggestion.height),
        ).toBeLessThanOrEqual(720);
    });

    test("the memo talks about a screen rather than about printing", () => {
        const container = makePage("Device16x9Portrait", 378, 672, 378, 300);

        const suggestion = getSuggestedImageTargetForContainer(container);

        if (!suggestion)
            throw new Error("a sized container should be measurable");
        expect(suggestion.memo).toContain("1280 x 720 screen");
        expect(suggestion.memo).not.toContain("DPI");
    });
});

describe("getSuggestedImageTargetForContainer when the size is unknown", () => {
    test("an unlaid-out container gets no suggestion", () => {
        const container = makePage("A5Portrait", 559, 794, 0, 0);
        // Sanity check: the paper case above proves the same call does return something when
        // the container has a size, so a null here is about the size and nothing else.
        expect(container.offsetWidth).toBe(0);

        expect(getSuggestedImageTargetForContainer(container)).toBeNull();
    });

    test("a container with width but no height gets no suggestion", () => {
        const container = makePage("A5Portrait", 559, 794, 469, 0);

        expect(getSuggestedImageTargetForContainer(container)).toBeNull();
    });

    test("a container outside any page is treated as a whole paper page, not measured against some other page", () => {
        // A device page exists in the document, but this container is not in it. If the
        // container were measured against that page it would be scaled to a screen instead.
        makePage("Device16x9Landscape", 1280, 720, 640, 360);
        const loose = document.createElement("div");
        loose.className = "bloom-canvas";
        document.body.appendChild(loose);
        setLayoutSize(loose, 469, 546);
        // Sanity check: the device page really is there to be wrongly picked up.
        expect(document.querySelector(".bloom-page")).not.toBeNull();
        expect(loose.closest(".bloom-page")).toBeNull();

        const suggestion = getSuggestedImageTargetForContainer(loose);

        expect(suggestion).not.toBeNull();
        expect(suggestion!.isDigital).toBe(false);
        // The whole container at 300 DPI: ceil(300 * 469 / 96) x ceil(300 * 546 / 96).
        expect(suggestion!.width).toBe(1466);
        expect(suggestion!.height).toBe(1707);
    });
});

describe("isDeviceLayoutPage", () => {
    test("a paper layout is not a device layout", () => {
        document.body.innerHTML = `<div class="bloom-page A5Portrait"></div>`;
        const page = document.querySelector(".bloom-page") as HTMLElement;

        expect(isDeviceLayoutPage(page)).toBe(false);
    });
});

describe("getSuggestedImageTargetForFraction", () => {
    test("a share of a paper page asks for what fills that space at 300 DPI", () => {
        // A5 portrait is 559 x 794 at 96 px to the inch.
        const suggestion = getSuggestedImageTargetForFraction(
            { width: 0.42, height: 0.31 },
            { widthPx: 559, heightPx: 794, isDigital: false },
        );

        if (!suggestion)
            throw new Error("a real fraction should be answerable");
        expect(suggestion.isDigital).toBe(false);
        expect(suggestion.width).toBe(Math.ceil((300 * 0.42 * 559) / 96));
        expect(suggestion.height).toBe(Math.ceil((300 * 0.31 * 794) / 96));
        expect(suggestion.memo).toContain("300 DPI");
    });

    test("a share of a screen-sized page asks for its share of a 1280-pixel screen", () => {
        const suggestion = getSuggestedImageTargetForFraction(
            { width: 1, height: 0.45 },
            { widthPx: 378, heightPx: 672, isDigital: true },
        );

        if (!suggestion)
            throw new Error("a real fraction should be answerable");
        expect(suggestion.isDigital).toBe(true);
        const scale = kDigitalScreenLongEdgePx / 672;
        expect(suggestion.width).toBe(Math.ceil(378 * scale));
        expect(suggestion.height).toBe(Math.ceil(0.45 * 672 * scale));
        expect(suggestion.memo).toContain("1280 x 720 screen");
    });

    test("the element path and the fraction path agree", () => {
        // The tooltip measures the container; the AI image editor works from the fraction
        // recorded in the HTML. They must not be able to disagree, which is why the element
        // path goes through this same function.
        const container = makePage("A5Portrait", 559, 794, 469, 546);

        const fromElement = getSuggestedImageTargetForContainer(container);
        const fromFraction = getSuggestedImageTargetForFraction(
            { width: 469 / 559, height: 546 / 794 },
            { widthPx: 559, heightPx: 794, isDigital: false },
        );

        expect(fromElement).toEqual(fromFraction);
        // Sanity check: and it is still the number the tooltip has always shown.
        expect(fromElement?.width).toBe(1466);
        expect(fromElement?.height).toBe(1707);
    });

    test("a fraction of nothing is not answerable", () => {
        expect(
            getSuggestedImageTargetForFraction(
                { width: 0.42, height: 0.31 },
                { widthPx: 0, heightPx: 0, isDigital: false },
            ),
        ).toBeNull();
    });
});

describe("getOpenPageMetrics", () => {
    test("reports the size and kind of a laid-out paper page", () => {
        makePage("A5Portrait", 559, 794, 469, 546);

        const metrics = getOpenPageMetrics(
            document.querySelector(".bloom-page"),
        );

        expect(metrics).toEqual({
            widthPx: 559,
            heightPx: 794,
            isDigital: false,
        });
    });

    test("a screen-sized layout is reported as digital", () => {
        makePage("Device16x9Portrait", 378, 672, 378, 300);

        expect(
            getOpenPageMetrics(document.querySelector(".bloom-page"))
                ?.isDigital,
        ).toBe(true);
    });

    test("an unlaid-out page has no metrics", () => {
        makePage("A5Portrait", 0, 0, 469, 546);

        expect(
            getOpenPageMetrics(document.querySelector(".bloom-page")),
        ).toBeNull();
    });
});

describe("recordFractionOfPageOnImageSlots", () => {
    // A 500 x 800 page holding one image slot, plus whatever extra markup the test wants.
    const makePageWithSlots = (extraMarkup = "") => {
        document.body.innerHTML =
            `<div class="bloom-page A5Portrait">` +
            `<div class="bloom-imageContainer"><img src="a.png" /></div>` +
            extraMarkup +
            `</div>`;
        const page = document.querySelector(".bloom-page") as HTMLElement;
        setLayoutSize(page, 500, 800);
        return page;
    };

    const slots = () =>
        Array.from(
            document.querySelectorAll(".bloom-imageContainer"),
        ) as HTMLElement[];

    test("writes each slot's share of the page, to two decimals", () => {
        makePageWithSlots();
        setLayoutSize(slots()[0], 210, 248);
        // Sanity check: nothing is recorded yet, so anything found below was written now.
        expect(slots()[0].hasAttribute(kFractionOfPageAttribute)).toBe(false);

        recordFractionOfPageOnImageSlots(document.body);

        // 210/500 is 0.42 exactly; 248/800 is 0.31 exactly.
        expect(slots()[0].getAttribute(kFractionOfPageAttribute)).toBe(
            "0.42,0.31",
        );
    });

    test("rounds a share that is not a round number", () => {
        makePageWithSlots();
        setLayoutSize(slots()[0], 211, 249);

        recordFractionOfPageOnImageSlots(document.body);

        // 211/500 is 0.422 and 249/800 is 0.31125.
        expect(slots()[0].getAttribute(kFractionOfPageAttribute)).toBe(
            "0.42,0.31",
        );
    });

    test("a control Bloom injects into the live page is not a slot", () => {
        makePageWithSlots(
            `<div class="bloom-ui"><div class="bloom-imageContainer"><img src="icon.png" /></div></div>`,
        );
        // Sanity check: the injected container IS present and IS measurable, so its being
        // skipped below is the .bloom-ui filter and nothing else.
        expect(slots().length).toBe(2);
        slots().forEach((slot) => setLayoutSize(slot, 210, 248));

        recordFractionOfPageOnImageSlots(document.body);

        expect(slots()[0].hasAttribute(kFractionOfPageAttribute)).toBe(true);
        expect(slots()[1].hasAttribute(kFractionOfPageAttribute)).toBe(false);
    });

    test("leaves a previous value alone when the slot cannot be measured", () => {
        // A stale share from the last save is better evidence than none, and better than a
        // guess, which would have the AI image editor generate at the wrong resolution.
        makePageWithSlots();
        slots()[0].setAttribute(kFractionOfPageAttribute, "0.5,0.5");

        recordFractionOfPageOnImageSlots(document.body);

        expect(slots()[0].getAttribute(kFractionOfPageAttribute)).toBe(
            "0.5,0.5",
        );
    });

    test("writes nothing when the page itself is not laid out", () => {
        const page = makePageWithSlots();
        setLayoutSize(page, 0, 0);
        setLayoutSize(slots()[0], 210, 248);

        recordFractionOfPageOnImageSlots(document.body);

        expect(slots()[0].hasAttribute(kFractionOfPageAttribute)).toBe(false);
    });
});

describe("parseFractionOfPage", () => {
    test("reads back what the recorder writes", () => {
        expect(parseFractionOfPage("0.42,0.31")).toEqual({
            width: 0.42,
            height: 0.31,
        });
    });

    test.each(["", "abc", "1", "0.4,x", "0,0.5", "0.4,0.3,0.2"])(
        "refuses %p",
        (value) => {
            expect(parseFractionOfPage(value)).toBeNull();
        },
    );

    test("a missing attribute is not a fraction", () => {
        expect(parseFractionOfPage(null)).toBeNull();
    });
});
