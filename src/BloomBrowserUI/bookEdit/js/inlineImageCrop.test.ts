import { describe, it, expect } from "vitest";
import {
    clearInlineImageCrop,
    computeCroppedInlineImageAspectRatio,
    getInlineImageCrop,
    inlineImageIsCropped,
    kInlineImageCropLeftVar,
    kInlineImageCropRightVar,
    kInlineImageCropTopVar,
    kNoInlineImageCrop,
    mirrorInlineImageCrop,
    setInlineImageCrop,
} from "./inlineImageCrop";

function makeWrapper(style = ""): HTMLElement {
    const wrapper = document.createElement("div");
    wrapper.setAttribute("style", style);
    return wrapper;
}

describe("inline image crop storage", () => {
    it("reads no crop from a wrapper that has none", () => {
        const wrapper = makeWrapper("--inline-image-width: 40%");
        expect(getInlineImageCrop(wrapper)).toEqual(kNoInlineImageCrop);
        expect(inlineImageIsCropped(wrapper)).toBe(false);
    });

    it("writes the crop to the wrapper's style and reads it back", () => {
        const wrapper = makeWrapper("--inline-image-width: 40%");

        setInlineImageCrop(wrapper, {
            top: 20,
            right: 10,
            bottom: 15,
            left: 30,
        });

        expect(getInlineImageCrop(wrapper)).toEqual({
            top: 20,
            right: 10,
            bottom: 15,
            left: 30,
        });
        expect(inlineImageIsCropped(wrapper)).toBe(true);
        // The rest of the wrapper's state is left alone.
        expect(wrapper.style.getPropertyValue("--inline-image-width")).toBe(
            "40%",
        );
    });

    it("writes nothing for a side that is not cropped, and rounds to a hundredth", () => {
        const wrapper = makeWrapper();

        setInlineImageCrop(wrapper, {
            top: 12.3456,
            right: 0,
            bottom: 0,
            left: 0,
        });

        expect(wrapper.style.getPropertyValue(kInlineImageCropTopVar)).toBe(
            "12.35",
        );
        // The CSS crop rules apply only to a wrapper with a crop property, so an uncropped side
        // must not leave a property behind.
        expect(wrapper.getAttribute("style")).not.toContain(
            kInlineImageCropLeftVar,
        );
        expect(wrapper.getAttribute("style")).not.toContain(
            kInlineImageCropRightVar,
        );
    });

    it("clearing the crop leaves no crop property at all", () => {
        const wrapper = makeWrapper("--inline-image-width: 40%");
        setInlineImageCrop(wrapper, { top: 5, right: 6, bottom: 7, left: 8 });
        // Sanity check.
        expect(wrapper.getAttribute("style")).toContain("--inline-image-crop");

        clearInlineImageCrop(wrapper);

        expect(wrapper.getAttribute("style")).not.toContain(
            "--inline-image-crop",
        );
        expect(inlineImageIsCropped(wrapper)).toBe(false);
    });

    it("a horizontal mirror swaps left and right, and a vertical one top and bottom", () => {
        const wrapper = makeWrapper();
        setInlineImageCrop(wrapper, { top: 1, right: 2, bottom: 3, left: 4 });

        mirrorInlineImageCrop(wrapper, "horizontal");
        expect(getInlineImageCrop(wrapper)).toEqual({
            top: 1,
            right: 4,
            bottom: 3,
            left: 2,
        });

        mirrorInlineImageCrop(wrapper, "vertical");
        expect(getInlineImageCrop(wrapper)).toEqual({
            top: 3,
            right: 4,
            bottom: 1,
            left: 2,
        });
    });

    it("gives the aspect ratio of the part of the picture that shows", () => {
        expect(
            computeCroppedInlineImageAspectRatio(1200, 800, kNoInlineImageCrop),
        ).toBe("1200 / 800");
        // Half the width cut away, a quarter of the height.
        expect(
            computeCroppedInlineImageAspectRatio(1200, 800, {
                top: 25,
                right: 20,
                bottom: 0,
                left: 30,
            }),
        ).toBe("600 / 600");
    });
});
