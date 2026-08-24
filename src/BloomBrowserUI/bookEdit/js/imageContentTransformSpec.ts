import { describe, it, expect } from "vitest";
import {
    clearImageContentTransform,
    computeTurnedBackgroundLayout,
    flipImageContent,
    getImageContentTransform,
    imageContentIsTransformed,
    rotateImageContentRight,
} from "./imageContentTransform";

// A picture inside a container, which is what the code expects. There is no page and no canvas
// element around it, so a turn of this picture changes only the transform. That keeps the
// transform easy to read in the tests that are about the turn and the mirror; the tests about
// where a background picture lands use makeBackgroundImage below.
function makeImage(): HTMLImageElement {
    const container = document.createElement("div");
    const img = document.createElement("img");
    container.appendChild(img);
    return img;
}

// Give the picture and its container a laid-out size.
function giveSizes(
    img: HTMLImageElement,
    imgWidth: number,
    imgHeight: number,
    containerWidth: number,
    containerHeight: number,
): void {
    Object.defineProperty(img, "clientWidth", { value: imgWidth });
    Object.defineProperty(img, "clientHeight", { value: imgHeight });
    Object.defineProperty(img.parentElement, "clientWidth", {
        value: containerWidth,
    });
    Object.defineProperty(img.parentElement, "clientHeight", {
        value: containerHeight,
    });
}

// The number that both axes of the scale are multiplied by, or undefined when there is no
// scale at all. The sign is dropped, because the sign is the mirror and this is the size.
function getScaleMagnitude(img: HTMLImageElement): number | undefined {
    const match = /scale\(\s*(-?[0-9.]+)/.exec(img.style.transform);
    return match ? Math.abs(parseFloat(match[1])) : undefined;
}

// A page background picture as Bloom builds it: the page's picture area (the bloom-canvas), the
// background canvas element inside it, the image container, and the picture. jsdom lays nothing
// out, so we answer the layout questions ourselves, from the styles the code writes. That is
// what lets a test turn a picture several times and see where it really ends up.
//
// The rules here are the browser's rules for these elements. A picture with an explicit width
// is that wide, and as tall as its own shape then makes it. A picture with no explicit width
// fills its container.
function makeBackgroundImage(
    pageWidth: number,
    pageHeight: number,
    elementWidth: number,
    elementHeight: number,
    naturalWidth: number,
    naturalHeight: number,
    // False for an ordinary picture element, which keeps its own size and place rather than
    // being fitted to the page's picture area.
    isBackground = true,
): HTMLImageElement {
    const page = document.createElement("div");
    page.classList.add("bloom-canvas");
    const element = document.createElement("div");
    element.classList.add("bloom-canvas-element");
    if (isBackground) {
        element.classList.add("bloom-backgroundImage");
    }
    const container = document.createElement("div");
    container.classList.add("bloom-imageContainer");
    const img = document.createElement("img");
    page.appendChild(element);
    element.appendChild(container);
    container.appendChild(img);

    Object.defineProperty(page, "clientWidth", { get: () => pageWidth });
    Object.defineProperty(page, "clientHeight", { get: () => pageHeight });
    Object.defineProperty(img, "naturalWidth", { get: () => naturalWidth });
    Object.defineProperty(img, "naturalHeight", { get: () => naturalHeight });

    const elementSize = (axis: "width" | "height") => {
        const written = parseFloat(element.style[axis]);
        if (!isNaN(written)) {
            return written;
        }
        return axis === "width" ? elementWidth : elementHeight;
    };
    for (const box of [element, container]) {
        Object.defineProperty(box, "clientWidth", {
            get: () => elementSize("width"),
        });
        Object.defineProperty(box, "clientHeight", {
            get: () => elementSize("height"),
        });
    }
    Object.defineProperty(img, "clientWidth", {
        get: () => {
            const written = parseFloat(img.style.width);
            return isNaN(written) ? elementSize("width") : written;
        },
    });
    Object.defineProperty(img, "clientHeight", {
        get: () => {
            const writtenHeight = parseFloat(img.style.height);
            if (!isNaN(writtenHeight)) {
                return writtenHeight;
            }
            const writtenWidth = parseFloat(img.style.width);
            if (!isNaN(writtenWidth)) {
                return (writtenWidth * naturalHeight) / naturalWidth;
            }
            return elementSize("height");
        },
    });
    return img;
}

// The canvas element a picture belongs to, so a test can read where the turn put it.
function elementOf(img: HTMLImageElement): HTMLElement {
    return img.closest(".bloom-canvas-element") as HTMLElement;
}

// A length the code wrote, as a number.
function px(value: string): number {
    return parseFloat(value);
}

describe("getImageContentTransform", () => {
    it("reports nothing for a picture with no transform", () => {
        expect(getImageContentTransform(makeImage())).toEqual({
            quarterTurns: 0,
            flipX: false,
            flipY: false,
        });
    });

    it("reads the number of quarter turns", () => {
        const img = makeImage();
        img.style.transform = "rotate(180deg)";
        expect(getImageContentTransform(img).quarterTurns).toBe(2);
    });

    it("brings a turn of a whole circle or more back into range", () => {
        const img = makeImage();
        img.style.transform = "rotate(450deg)";
        expect(getImageContentTransform(img).quarterTurns).toBe(1);
    });

    it("reads a mirror of one axis from the sign of the scale", () => {
        const img = makeImage();
        img.style.transform = "rotate(90deg) scale(-1, 1)";
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 1,
            flipX: true,
            flipY: false,
        });
    });

    it("takes a scale with one number as both axes", () => {
        const img = makeImage();
        img.style.transform = "scale(-0.5)";
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 0,
            flipX: true,
            flipY: true,
        });
    });
});

describe("imageContentIsTransformed", () => {
    it("is false for a picture with no transform", () => {
        expect(imageContentIsTransformed(makeImage())).toBe(false);
    });

    it("is false for a scale that mirrors nothing", () => {
        const img = makeImage();
        img.style.transform = "scale(0.5, 0.5)";
        expect(imageContentIsTransformed(img)).toBe(false);
    });

    it("is true for a turned picture", () => {
        const img = makeImage();
        img.style.transform = "rotate(90deg)";
        expect(imageContentIsTransformed(img)).toBe(true);
    });

    it("is true for a mirrored picture", () => {
        const img = makeImage();
        img.style.transform = "scale(-1, 1)";
        expect(imageContentIsTransformed(img)).toBe(true);
    });
});

describe("rotateImageContentRight", () => {
    it("adds a quarter turn each time, and four turns leave nothing behind", () => {
        const img = makeImage();

        rotateImageContentRight(img);
        expect(getImageContentTransform(img).quarterTurns).toBe(1);
        rotateImageContentRight(img);
        expect(getImageContentTransform(img).quarterTurns).toBe(2);
        rotateImageContentRight(img);
        expect(getImageContentTransform(img).quarterTurns).toBe(3);
        rotateImageContentRight(img);

        expect(getImageContentTransform(img).quarterTurns).toBe(0);
        expect(img.style.transform).toBe("");
    });

    it("keeps a mirror while it turns", () => {
        const img = makeImage();
        img.style.transform = "scale(-1, 1)";
        rotateImageContentRight(img);
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 1,
            flipX: true,
            flipY: false,
        });
    });

    it("never shrinks the picture with the transform", () => {
        // The transform carries the turn and the mirrors and nothing else. Where the picture
        // goes is said by the picture's own size and place, and by its canvas element's.
        const img = makeImage();
        giveSizes(img, 200, 100, 200, 100);

        rotateImageContentRight(img);

        expect(img.style.transform).toBe("rotate(90deg)");
        expect(getScaleMagnitude(img)).toBeUndefined();
    });
});

describe("rotateImageContentRight, on a page background picture", () => {
    // A photograph three units wide and two high, which the camera saved on its side, on a page
    // whose picture area is 480 by 720. Bloom gives the picture's canvas element the shape of
    // the picture, so before any turn that element is 480 by 320, centred in the page.
    function makeSidewaysPhotograph(): HTMLImageElement {
        return makeBackgroundImage(480, 720, 480, 320, 300, 200);
    }

    it("turns an uncropped picture to exactly where it would be if it had arrived turned", () => {
        const img = makeSidewaysPhotograph();
        // Sanity check: the picture has no numbers of its own before the turn.
        expect(img.style.width).toBe("");

        rotateImageContentRight(img);

        // The element now has the shape of the turned picture, which on this page is the shape
        // of the page itself, so the picture fills the page. That is what an author would have
        // had if they had turned the file before they chose it.
        expect(px(elementOf(img).style.width)).toBeCloseTo(480);
        expect(px(elementOf(img).style.height)).toBeCloseTo(720);
        expect(px(elementOf(img).style.left)).toBeCloseTo(0);
        expect(px(elementOf(img).style.top)).toBeCloseTo(0);
        // The picture's box still lies the way the picture does, and the turn of the box about
        // its own centre brings it upright over the element.
        expect(px(img.style.width)).toBeCloseTo(720);
        expect(px(img.style.left)).toBeCloseTo(-120);
        expect(px(img.style.top)).toBeCloseTo(120);
        expect(img.style.transform).toBe("rotate(90deg)");
    });

    it("leaves no height behind, which a change of page size would stretch", () => {
        const img = makeSidewaysPhotograph();
        rotateImageContentRight(img);
        // Bloom scales a picture's width, left and top when the page changes size, and leaves
        // its height alone, so a height written here would stay behind and stretch the picture.
        expect(img.style.height).toBe("");
    });

    it("brings an uncropped picture back to where it began after four turns", () => {
        const img = makeSidewaysPhotograph();

        for (let turn = 0; turn < 4; turn++) {
            rotateImageContentRight(img);
        }

        expect(px(elementOf(img).style.width)).toBeCloseTo(480);
        expect(px(elementOf(img).style.height)).toBeCloseTo(320);
        expect(img.style.width).toBe("");
        expect(img.style.left).toBe("");
        expect(img.style.top).toBe("");
        expect(img.style.transform).toBe("");
    });

    it("gives an uncropped picture no numbers of its own after a half turn", () => {
        const img = makeSidewaysPhotograph();
        rotateImageContentRight(img);
        // Sanity check: the quarter turn does give it numbers.
        expect(img.style.width).not.toBe("");

        rotateImageContentRight(img);

        // An explicit width is how the rest of Bloom recognizes a crop, so a picture nobody
        // cropped must not keep one.
        expect(img.style.width).toBe("");
        expect(px(elementOf(img).style.height)).toBeCloseTo(320);
        expect(img.style.transform).toBe("rotate(180deg)");
    });

    // The same photograph, cropped: the author magnified it to 1080 wide, which is three and
    // three fifths of its own size, and centred it, so the crop fills a canvas element that
    // covers the whole page.
    function makeCroppedPhotograph(): HTMLImageElement {
        const img = makeBackgroundImage(480, 720, 480, 720, 300, 200);
        img.style.width = "1080px";
        img.style.left = "-300px";
        img.style.top = "0px";
        return img;
    }

    it("keeps the crop", () => {
        const img = makeCroppedPhotograph();
        // Sanity check: the crop is really there before the turn.
        expect(px(img.style.width)).toBeCloseTo(1080);

        rotateImageContentRight(img);

        expect(img.style.width).not.toBe("");
        expect(px(img.style.width)).toBeGreaterThan(0);
    });

    it("shows the whole of the crop, and shortens the element rather than cut it", () => {
        const img = makeCroppedPhotograph();

        rotateImageContentRight(img);

        // The crop had the shape of the page, so turned it has the page's shape lying down, and
        // it cannot fill a page that has not changed. The element takes the turned crop's shape
        // and keeps the page's width, which leaves 200 blank above it and 200 below.
        expect(px(elementOf(img).style.width)).toBeCloseTo(480);
        expect(px(elementOf(img).style.height)).toBeCloseTo(320);
        expect(px(elementOf(img).style.top)).toBeCloseTo(200);
        // The whole crop is there: its box is 720 by 480, its centre is the centre of the
        // element, and turning it about that centre covers the element exactly.
        expect(px(img.style.width)).toBeCloseTo(720);
        expect(px(img.style.left)).toBeCloseTo(-120);
        expect(px(img.style.top)).toBeCloseTo(-80);
    });

    it("keeps filling the page for a picture the author told to fill it", () => {
        const img = makeCroppedPhotograph();
        // The Expand Image command marks a picture this way.
        img.classList.add("bloom-imageObjectFit-cover");

        rotateImageContentRight(img);

        // The element still covers the page, and the ends of the picture are clipped instead.
        expect(px(elementOf(img).style.width)).toBeCloseTo(480);
        expect(px(elementOf(img).style.height)).toBeCloseTo(720);
        expect(px(img.style.width)).toBeCloseTo(1620);
        expect(px(img.style.left)).toBeCloseTo(-570);
        expect(px(img.style.top)).toBeCloseTo(-180);
    });

    it("turns the picture even when nothing has been laid out yet", () => {
        // A page that has not been laid out gives no sizes to measure. We must still turn the
        // picture, and we must write no numbers we cannot work out.
        const img = makeBackgroundImage(0, 0, 0, 0, 300, 200);

        rotateImageContentRight(img);

        expect(img.style.transform).toBe("rotate(90deg)");
        expect(img.style.width).toBe("");
        expect(elementOf(img).style.width).toBe("");
    });
});

describe("rotateImageContentRight, on an ordinary picture element", () => {
    // The Rotate Right command turns the picture inside the box, rather than the box on the
    // page, whenever the box itself cannot turn. That is true of a background picture, and also
    // of an element whose outline comicaljs draws. Such an element is not the page's background,
    // so it must keep the size and the place the author gave it.
    function makeOrdinaryPicture(): HTMLImageElement {
        return makeBackgroundImage(480, 720, 300, 200, 300, 200, false);
    }

    it("swaps the element's two dimensions about the element's own centre", () => {
        const img = makeOrdinaryPicture();
        elementOf(img).style.left = "100px";
        elementOf(img).style.top = "50px";

        rotateImageContentRight(img);

        // The box is now as tall as it was wide, and its centre has not moved: it was at
        // (250, 150) and it still is.
        expect(px(elementOf(img).style.width)).toBeCloseTo(200);
        expect(px(elementOf(img).style.height)).toBeCloseTo(300);
        expect(px(elementOf(img).style.left)).toBeCloseTo(150);
        expect(px(elementOf(img).style.top)).toBeCloseTo(0);
    });

    it("does not stretch the element to the page's picture area", () => {
        const img = makeOrdinaryPicture();

        rotateImageContentRight(img);

        // The page's picture area is 480 by 720. A background picture would have been fitted to
        // it; this one must not be.
        expect(px(elementOf(img).style.width)).toBeLessThan(480);
        expect(px(elementOf(img).style.height)).toBeLessThan(720);
    });

    it("keeps the picture the size it was, turned to fill the swapped element", () => {
        const img = makeOrdinaryPicture();

        rotateImageContentRight(img);

        // The picture's box still lies the way the picture does, at its old size, and the turn
        // about its own centre brings it upright over the element.
        expect(px(img.style.width)).toBeCloseTo(300);
        expect(px(img.style.left)).toBeCloseTo(-50);
        expect(px(img.style.top)).toBeCloseTo(50);
        expect(img.style.transform).toBe("rotate(90deg)");
    });
});

describe("computeTurnedBackgroundLayout", () => {
    it("swaps the two dimensions of the element and fits it to the page", () => {
        // A landscape element, 480 by 320, on a page 480 by 720.
        const layout = computeTurnedBackgroundLayout(
            480,
            720,
            480,
            320,
            480,
            320,
            0,
            0,
            false,
        );

        expect(layout.elementWidth).toBeCloseTo(480);
        expect(layout.elementHeight).toBeCloseTo(720);
    });

    it("centres the element in the page", () => {
        // A square element on a tall page: it fills the width and leaves equal bands.
        const layout = computeTurnedBackgroundLayout(
            480,
            720,
            480,
            480,
            480,
            480,
            0,
            0,
            false,
        );

        expect(layout.elementWidth).toBeCloseTo(480);
        expect(layout.elementHeight).toBeCloseTo(480);
        expect(layout.elementLeft).toBeCloseTo(0);
        expect(layout.elementTop).toBeCloseTo(120);
    });

    it("puts the centre of the picture at the centre of the element", () => {
        const layout = computeTurnedBackgroundLayout(
            480,
            720,
            480,
            320,
            480,
            320,
            0,
            0,
            false,
        );

        expect(layout.imageLeft + layout.imageWidth / 2).toBeCloseTo(
            layout.elementWidth / 2,
        );
        expect(layout.imageTop + layout.imageHeight / 2).toBeCloseTo(
            layout.elementHeight / 2,
        );
    });

    it("magnifies instead of shrinking for a picture that must fill the page", () => {
        const fitted = computeTurnedBackgroundLayout(
            480,
            720,
            480,
            720,
            1080,
            720,
            -300,
            0,
            false,
        );
        const filling = computeTurnedBackgroundLayout(
            480,
            720,
            480,
            720,
            1080,
            720,
            -300,
            0,
            true,
        );

        // Sanity check: the fitted one really is smaller than the page.
        expect(fitted.elementHeight).toBeLessThan(720);
        expect(filling.elementWidth).toBeCloseTo(480);
        expect(filling.elementHeight).toBeCloseTo(720);
        expect(filling.imageWidth).toBeGreaterThan(fitted.imageWidth);
    });
});

describe("flipImageContent", () => {
    it("mirrors the x axis of an upright picture from side to side", () => {
        const img = makeImage();
        flipImageContent(img, "horizontal");
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 0,
            flipX: true,
            flipY: false,
        });
    });

    it("mirrors the y axis of an upright picture from top to bottom", () => {
        const img = makeImage();
        flipImageContent(img, "vertical");
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 0,
            flipX: false,
            flipY: true,
        });
    });

    it("leaves nothing behind when the same mirror is used twice", () => {
        const img = makeImage();
        flipImageContent(img, "horizontal");
        // Sanity check: the first mirror really was written.
        expect(img.style.transform).not.toBe("");

        flipImageContent(img, "horizontal");

        expect(img.style.transform).toBe("");
    });

    it("mirrors the other axis of the picture after a quarter turn", () => {
        const img = makeImage();
        rotateImageContentRight(img);
        // After a quarter turn the x axis of the picture runs up and down the screen, so a
        // mirror from side to side on screen is a mirror of the y axis of the picture.
        flipImageContent(img, "horizontal");
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 1,
            flipX: false,
            flipY: true,
        });
    });

    it("mirrors the other axis when the box is turned a quarter turn", () => {
        const img = makeImage();
        // The picture itself is not turned; the box around it is, which moves the picture on
        // screen in the same way, so a mirror from side to side on screen is again a mirror
        // of the y axis of the picture.
        flipImageContent(img, "horizontal", 90);
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 0,
            flipX: false,
            flipY: true,
        });
    });

    it("mirrors the requested axis when the box is turned a half turn", () => {
        const img = makeImage();
        flipImageContent(img, "horizontal", 180);
        expect(getImageContentTransform(img).flipX).toBe(true);
    });

    it("takes the two turns together", () => {
        const img = makeImage();
        rotateImageContentRight(img);
        // A quarter turn of the picture and a quarter turn of the box make a half turn, which
        // leaves the axes of the picture the way they started on screen.
        flipImageContent(img, "horizontal", 90);
        expect(getImageContentTransform(img)).toEqual({
            quarterTurns: 1,
            flipX: true,
            flipY: false,
        });
    });

    it("takes the angle of the box to the nearest quarter turn", () => {
        const img = makeImage();
        flipImageContent(img, "horizontal", 80);
        expect(getImageContentTransform(img).flipY).toBe(true);
        const other = makeImage();
        flipImageContent(other, "horizontal", 10);
        expect(getImageContentTransform(other).flipX).toBe(true);
    });

    it("keeps the crop, because a mirror does not change the shape of the box", () => {
        const img = makeImage();
        img.style.width = "300px";
        img.style.left = "-20px";

        flipImageContent(img, "horizontal");

        expect(img.style.width).toBe("300px");
        expect(img.style.left).toBe("-20px");
    });
});

describe("clearImageContentTransform", () => {
    it("removes the turn and the mirror", () => {
        const img = makeImage();
        img.style.transform = "rotate(90deg) scale(-1, 1)";
        // Sanity check: there is something to clear.
        expect(imageContentIsTransformed(img)).toBe(true);

        clearImageContentTransform(img);

        expect(img.style.transform).toBe("");
        expect(imageContentIsTransformed(img)).toBe(false);
    });
});
