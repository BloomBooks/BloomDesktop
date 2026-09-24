import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

// A faithful stand-in for the requestPageContent delay bookkeeping in bloomEditing.ts, which we
// can't import here (it pulls in the whole editing world). What matters for this suite is the
// ordering the real code has: a save requested while delays are active is captured
// SYNCHRONOUSLY, the instant the last delay is released, before any promise continuation of
// the work that held the delay gets to run. vi.hoisted so the mock factory below (which vitest
// hoists above the imports) can see it.
const saveTracker = vi.hoisted(() => ({
    activeDelays: 0,
    savePending: false,
    capturedBodyHtml: undefined as string | undefined,
    // What C#'s RequestBrowserToSave ends up calling.
    requestPageContent() {
        if (this.activeDelays > 0) {
            this.savePending = true;
        } else {
            this.capturedBodyHtml = document.body.innerHTML;
        }
    },
    releaseDelay() {
        this.activeDelays--;
        if (this.activeDelays === 0 && this.savePending) {
            this.savePending = false;
            this.capturedBodyHtml = document.body.innerHTML;
        }
    },
    reset() {
        this.activeDelays = 0;
        this.savePending = false;
        this.capturedBodyHtml = undefined;
    },
}));

vi.mock("../bloomEditing", () => ({
    wrapWithRequestPageContentDelay: async <T>(
        fn: () => Promise<T>,
        _delayId: string,
    ): Promise<T> => {
        saveTracker.activeDelays++;
        try {
            return await fn();
        } finally {
            saveTracker.releaseDelay();
        }
    },
}));

// The real helpers in bloomImages.ts, minus the module's heavy imports.
vi.mock("../bloomImages", () => {
    const getImageFromContainer = (container: HTMLElement) =>
        Array.from(container.children).find((x) => x.nodeName === "IMG") as
            | HTMLImageElement
            | undefined;
    const getImageFromCanvasElement = (canvasElement: HTMLElement) => {
        const container = canvasElement.getElementsByClassName(
            "bloom-imageContainer",
        )[0];
        return container
            ? getImageFromContainer(container as HTMLElement)
            : null;
    };
    return {
        isPlaceHolderImage: (src: string | null) =>
            !!src && src.toLowerCase().includes("placeholder.png"),
        HandleImageError: vi.fn(),
        SetupMetadataButton: vi.fn(),
        getImageFromContainer,
        getImageFromCanvasElement,
        getBackgroundImageFromBloomCanvas: (bloomCanvas: HTMLElement) => {
            const bg = bloomCanvas.getElementsByClassName(
                "bloom-backgroundImage",
            )[0] as HTMLElement | undefined;
            return bg ? getImageFromCanvasElement(bg) : null;
        },
    };
});

// Enough of comicaljs for putBubbleBefore: specs live in data-bubble, as in the real thing.
vi.mock("comicaljs", () => {
    class Bubble {
        private spec: { level: number };
        constructor(private element: HTMLElement) {
            this.spec = Bubble.getBubbleSpec(element);
        }
        static getBubbleSpec(element: HTMLElement): { level: number } {
            const raw = element.getAttribute("data-bubble");
            return raw ? JSON.parse(raw) : { level: 1 };
        }
        getBubbleSpec() {
            return this.spec;
        }
        persistBubbleSpec() {
            this.element.setAttribute("data-bubble", JSON.stringify(this.spec));
        }
    }
    return { Bubble, Comical: { update: vi.fn() } };
});

vi.mock("./CanvasElementContextControls", () => ({
    renderCanvasElementContextControls: vi.fn(),
}));

// jsdom has no layout; give the bloom-canvas a size so the fit arithmetic is real numbers.
// A test that changes the size of the page's picture area sets bloomCanvasSize.
const bloomCanvasSize = vi.hoisted(() => ({ width: 400, height: 300 }));
vi.mock("../../../utils/elementUtils", () => ({
    getExactClientSize: () => ({ ...bloomCanvasSize }),
}));

import {
    adjustBackgroundImageSize,
    BackgroundImageManagerState,
    handleResizeAdjustments,
    repairInterruptedBackgroundConversion,
} from "./CanvasElementBackgroundImageManager";

// Old-style page markup (Bloom 6.2 and earlier): the img sits directly in the bloom-canvas, with
// no bloom-backgroundImage canvas element yet. handleResizeAdjustments converts it on page load.
const kOldStylePage = `<div class="bloom-page"><div class="marginBox">
    <div class="bloom-canvas"><img src="image3.png" data-copyright="Copyright © 2011, Pam Gregory" /></div>
</div></div>`;

const directImgChildren = (bloomCanvas: Element) =>
    Array.from(bloomCanvas.children).filter((c) => c.nodeName === "IMG");

describe("switchBackgroundToCanvasElement vs. a save requested mid-conversion (BL-16870)", () => {
    let state: BackgroundImageManagerState;
    let bloomCanvas: HTMLElement;

    beforeEach(() => {
        vi.useFakeTimers();
        saveTracker.reset();
        document.body.innerHTML = kOldStylePage;
        bloomCanvas = document.querySelector(".bloom-canvas") as HTMLElement;
        state = { bgImageLoadListeners: new WeakMap() };
    });

    afterEach(() => {
        vi.useRealTimers();
        document.body.innerHTML = "";
    });

    // Drive the conversion to completion. The new img never loads in jsdom, so mark it failed;
    // the sizing code then stops waiting on its next (100ms timer) attempt and finishes.
    const letTheConversionSettle = async () => {
        const newImg = bloomCanvas.querySelector(
            ".bloom-backgroundImage img",
        ) as HTMLImageElement;
        expect(newImg).not.toBeNull();
        newImg.classList.add("bloom-imageLoadError");
        await vi.advanceTimersByTimeAsync(200);
    };

    test("a save requested while the image is still loading waits for the conversion, and captures a clean page", async () => {
        // Sanity: the page starts old-style.
        expect(directImgChildren(bloomCanvas)).toHaveLength(1);
        expect(
            bloomCanvas.getElementsByClassName("bloom-backgroundImage"),
        ).toHaveLength(0);

        handleResizeAdjustments(
            state,
            [bloomCanvas],
            () => {},
            () => undefined,
            () => {},
        );

        // Mid-conversion: the new element exists but is hidden, and the old img is still there.
        // This is the state that must never reach the saved book.
        const bgElement = bloomCanvas.getElementsByClassName(
            "bloom-backgroundImage",
        )[0] as HTMLElement;
        expect(bgElement).toBeDefined();
        expect(bgElement.style.visibility).toBe("hidden");
        expect(directImgChildren(bloomCanvas)).toHaveLength(1);

        // C# asks for the page content now, as the Update Book page walk did the instant the
        // page's DOM had loaded. The conversion is in flight, so the save must wait...
        saveTracker.requestPageContent();
        expect(saveTracker.activeDelays).toBeGreaterThan(0);
        expect(saveTracker.capturedBodyHtml).toBeUndefined();

        await letTheConversionSettle();

        // ...and be captured only once the conversion, cleanup included, is done.
        expect(saveTracker.capturedBodyHtml).toBeDefined();
        const saved = document.createElement("div");
        saved.innerHTML = saveTracker.capturedBodyHtml!;
        const savedCanvas = saved.querySelector(".bloom-canvas") as HTMLElement;
        expect(directImgChildren(savedCanvas)).toHaveLength(0);
        const savedBg = savedCanvas.querySelector(
            ".bloom-backgroundImage",
        ) as HTMLElement;
        expect(savedBg).not.toBeNull();
        expect(savedBg.style.visibility).toBe("");
        const savedImg = savedBg.querySelector("img") as HTMLImageElement;
        expect(savedImg.getAttribute("src")).toBe("image3.png");
        expect(savedImg.getAttribute("data-copyright")).toBe(
            "Copyright © 2011, Pam Gregory",
        );

        // And the live page ends in the same clean state, with nothing left in flight.
        expect(directImgChildren(bloomCanvas)).toHaveLength(0);
        expect(bgElement.style.visibility).toBe("");
        expect(saveTracker.activeDelays).toBe(0);
    });

    test("with no save pending, the conversion still cleans up and releases all its delays", async () => {
        handleResizeAdjustments(
            state,
            [bloomCanvas],
            () => {},
            () => undefined,
            () => {},
        );
        await letTheConversionSettle();

        expect(directImgChildren(bloomCanvas)).toHaveLength(0);
        const bgElement = bloomCanvas.getElementsByClassName(
            "bloom-backgroundImage",
        )[0] as HTMLElement;
        expect(bgElement.style.visibility).toBe("");
        // A leaked delay would make every later save in the session wait out the 4s timeout.
        expect(saveTracker.activeDelays).toBe(0);
        expect(saveTracker.capturedBodyHtml).toBeUndefined();
    });
});

// A page saved by an early 6.5 "Update Book" part-way through the conversion: the old-style img is
// still directly in the bloom-canvas, and the new background canvas element was saved hidden.
const kDamagedPage = `<div class="bloom-page"><div class="marginBox">
    <div class="bloom-canvas bloom-has-canvas-element">
        <img src="image3.png" data-copyright="Copyright © 2011, Pam Gregory" />
        <div class="bloom-canvas-element bloom-backgroundImage" style="visibility: hidden;" data-bubble="{&quot;level&quot;:1}">
            <div class="bloom-imageContainer"><img src="image3.png" data-copyright="Copyright © 2011, Pam Gregory" /></div>
        </div>
    </div>
</div></div>`;

// The normal state of a converted bloom-canvas from an older book: a visible background canvas
// element, plus the obsolete placeholder img that older Bloom left as a direct child.
const kConvertedPageWithPlaceholder = `<div class="bloom-page"><div class="marginBox">
    <div class="bloom-canvas bloom-has-canvas-element">
        <img src="placeHolder.png" />
        <div class="bloom-canvas-element bloom-backgroundImage" style="width: 300px;" data-bubble="{&quot;level&quot;:1}">
            <div class="bloom-imageContainer"><img src="image3.png" /></div>
        </div>
    </div>
</div></div>`;

describe("repairInterruptedBackgroundConversion (BL-16870)", () => {
    let state: BackgroundImageManagerState;

    beforeEach(() => {
        saveTracker.reset();
        state = { bgImageLoadListeners: new WeakMap() };
    });

    afterEach(() => {
        document.body.innerHTML = "";
    });

    test("a page saved mid-conversion is repaired when it loads: stray picture removed, element shown", () => {
        document.body.innerHTML = kDamagedPage;
        const bloomCanvas = document.querySelector(
            ".bloom-canvas",
        ) as HTMLElement;
        const bgElement = bloomCanvas.querySelector(
            ".bloom-backgroundImage",
        ) as HTMLElement;
        // Sanity: the damage is present.
        expect(directImgChildren(bloomCanvas)).toHaveLength(1);
        expect(bgElement.style.visibility).toBe("hidden");

        handleResizeAdjustments(
            state,
            [bloomCanvas],
            () => {},
            () => undefined,
            () => {},
        );

        expect(directImgChildren(bloomCanvas)).toHaveLength(0);
        expect(bgElement.style.visibility).toBe("");
        // Repaired in place: no second background element, and the real picture survives inside it.
        expect(
            bloomCanvas.querySelectorAll(".bloom-backgroundImage"),
        ).toHaveLength(1);
        expect(bgElement.querySelector("img")!.getAttribute("src")).toBe(
            "image3.png",
        );
        // The repair is synchronous and holds no save delay.
        expect(saveTracker.activeDelays).toBe(0);
    });

    test("a normally converted page, with the obsolete placeholder img, is left alone", () => {
        document.body.innerHTML = kConvertedPageWithPlaceholder;
        const bloomCanvas = document.querySelector(
            ".bloom-canvas",
        ) as HTMLElement;
        const bgElement = bloomCanvas.querySelector(
            ".bloom-backgroundImage",
        ) as HTMLElement;
        const before = bloomCanvas.innerHTML;

        handleResizeAdjustments(
            state,
            [bloomCanvas],
            () => {},
            () => undefined,
            () => {},
        );

        expect(
            repairInterruptedBackgroundConversion(bloomCanvas, bgElement),
        ).toBe(false);
        expect(bloomCanvas.innerHTML).toBe(before);
        expect(directImgChildren(bloomCanvas)).toHaveLength(1);
    });
});

describe("adjustBackgroundImageSize on a background that fills the page", () => {
    afterEach(() => {
        bloomCanvasSize.width = 400;
        bloomCanvasSize.height = 300;
        document.body.innerHTML = "";
    });

    // A background element that fills a picture area 400 by 300, holding a picture with the
    // given natural size, transform, and img box. jsdom does no layout, so the element reports
    // the size written on it.
    function makeFilledBackground(
        naturalWidth: number,
        naturalHeight: number,
        transform: string,
        boxWidth: number,
        boxLeft: number,
        boxTop: number,
    ): {
        bloomCanvas: HTMLElement;
        element: HTMLElement;
        img: HTMLImageElement;
    } {
        document.body.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-canvas-element bloom-backgroundImage" style="width: 400px; height: 300px; left: 0px; top: 0px">
                    <div class="bloom-imageContainer">
                        <img class="bloom-imageObjectFit-cover" src="picture.png">
                    </div>
                </div>
            </div>`;
        const bloomCanvas = document.querySelector(
            ".bloom-canvas",
        ) as HTMLElement;
        const element = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        const img = document.querySelector("img") as HTMLImageElement;
        img.style.width = `${boxWidth}px`;
        img.style.left = `${boxLeft}px`;
        img.style.top = `${boxTop}px`;
        img.style.transform = transform;
        Object.defineProperty(element, "clientWidth", {
            get: () => parseFloat(element.style.width),
        });
        Object.defineProperty(element, "clientHeight", {
            get: () => parseFloat(element.style.height),
        });
        Object.defineProperty(img, "naturalWidth", { value: naturalWidth });
        Object.defineProperty(img, "naturalHeight", { value: naturalHeight });
        return { bloomCanvas, element, img };
    }

    // The rectangle the element shows: the img box, with its two dimensions swapped when the
    // picture is rotated 90 degrees, about the same centre.
    function shownRectangle(
        img: HTMLImageElement,
        rotated: boolean,
    ): { left: number; top: number; width: number } {
        const boxWidth = parseFloat(img.style.width);
        const boxHeight = (boxWidth * img.naturalHeight) / img.naturalWidth;
        const width = rotated ? boxHeight : boxWidth;
        const height = rotated ? boxWidth : boxHeight;
        return {
            left: parseFloat(img.style.left) + boxWidth / 2 - width / 2,
            top: parseFloat(img.style.top) + boxHeight / 2 - height / 2,
            width,
        };
    }

    test("a picture rotated 90 degrees keeps the framing it would have had if it had arrived rotated", async () => {
        // Both show a landscape picture 600 by 300 whose left edge is at the page's left edge,
        // with 200 hidden at the right. The first arrived that shape; the second is a portrait
        // picture 100 by 200 that Rotate Right has rotated, whose box is 300 by 600.
        const state: BackgroundImageManagerState = {
            bgImageLoadListeners: new WeakMap(),
        };
        const upright = makeFilledBackground(200, 100, "", 600, 0, 0);
        // Sanity check: the shown rectangle is what the comment says.
        expect(shownRectangle(upright.img, false)).toEqual({
            left: 0,
            top: 0,
            width: 600,
        });
        // Origami makes the picture area twice as tall.
        bloomCanvasSize.height = 600;
        await adjustBackgroundImageSize(
            state,
            upright.bloomCanvas,
            upright.element,
            false,
            () => undefined,
            () => {},
        );
        const expected = shownRectangle(upright.img, false);

        bloomCanvasSize.height = 300;
        const rotated = makeFilledBackground(
            100,
            200,
            "rotate(90deg)",
            300,
            150,
            -150,
        );
        expect(shownRectangle(rotated.img, true)).toEqual({
            left: 0,
            top: 0,
            width: 600,
        });
        bloomCanvasSize.height = 600;
        await adjustBackgroundImageSize(
            state,
            rotated.bloomCanvas,
            rotated.element,
            false,
            () => undefined,
            () => {},
        );

        expect(shownRectangle(rotated.img, true)).toEqual(expected);
    });
});
