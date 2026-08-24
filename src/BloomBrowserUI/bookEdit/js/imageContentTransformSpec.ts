import { describe, it, expect } from "vitest";
import {
    clearImageContentTransform,
    flipImageContent,
    getImageContentTransform,
    imageContentIsTransformed,
    rotateImageContentRight,
} from "./imageContentTransform";

// A picture inside a container, which is what the code expects. jsdom lays nothing out, so
// the sizes are zero unless a test gives them, and a zero size makes the fit scale 1. That
// keeps the transform easy to read in the tests that are about the turn and the mirror.
function makeImage(): HTMLImageElement {
    const container = document.createElement("div");
    const img = document.createElement("img");
    container.appendChild(img);
    return img;
}

// Give the picture and its container a laid-out size, so that the fit scale can be measured.
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

    it("is false for a picture that is only made smaller to fit", () => {
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

    it("removes the crop, because the crop described the picture before the turn", () => {
        const img = makeImage();
        img.style.width = "300px";
        img.style.height = "200px";
        img.style.left = "-20px";
        img.style.top = "-10px";
        // Sanity check: the crop is really there before the turn.
        expect(img.style.width).toBe("300px");

        rotateImageContentRight(img);

        expect(img.style.width).toBe("");
        expect(img.style.height).toBe("");
        expect(img.style.left).toBe("");
        expect(img.style.top).toBe("");
    });

    it("shrinks the picture so that it still fits after an odd quarter turn", () => {
        const img = makeImage();
        // A wide picture in a wide container: lying across the container, its 200 of width
        // has to fit into 100 of height, so it must come down to half its size.
        giveSizes(img, 200, 100, 200, 100);

        rotateImageContentRight(img);

        expect(getScaleMagnitude(img)).toBeCloseTo(0.5);
    });

    it("uses the full size again after a half turn", () => {
        const img = makeImage();
        giveSizes(img, 200, 100, 200, 100);
        rotateImageContentRight(img);
        // Sanity check: the picture is smaller after the first turn.
        expect(getScaleMagnitude(img)).toBeCloseTo(0.5);

        rotateImageContentRight(img);

        expect(getScaleMagnitude(img)).toBeUndefined();
        expect(img.style.transform).toBe("rotate(180deg)");
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
