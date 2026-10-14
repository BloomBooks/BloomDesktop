// How an inline image's crop is stored. The handles that change it are in
// inlineImageInteractions.ts, and the CSS that shows it is in content/bookLayout/inlineImages.less.
//
// A crop is four numbers on the wrapper's style: how much of the picture is cut away on each side,
// as a percentage of the picture's width (left and right) or height (top and bottom). They are
// percentages of the picture rather than pixels so that the crop stays the same when the text
// block changes width: at another page size, in Bloom Player, in an ePUB. Like the rest of an
// inline image's state, they are on the wrapper, so the code that copies a wrapper to the other
// languages and the undo entries carry them without knowing about them.
//
// The numbers describe the picture as the reader sees it, after Flip. So flipping a cropped
// picture swaps two of them (mirrorInlineImageCrop), which keeps the same part of the picture in
// view, mirrored, as Flip does for a picture on a canvas. It also means the crop stays right when
// publishing writes the flip into the image file and removes the img's transform.
//
// The wrapper's --inline-image-aspect-ratio is the shape of the part that shows, so that the
// text wraps around that and not around the whole picture.
import { FlipAxis } from "./imageContentTransform";

export const kInlineImageCropTopVar = "--inline-image-crop-top";
export const kInlineImageCropRightVar = "--inline-image-crop-right";
export const kInlineImageCropBottomVar = "--inline-image-crop-bottom";
export const kInlineImageCropLeftVar = "--inline-image-crop-left";

/** The percentage of the picture cut away on each side. See the comment at the top of the file. */
export interface IInlineImageCrop {
    top: number;
    right: number;
    bottom: number;
    left: number;
}

export const kNoInlineImageCrop: IInlineImageCrop = {
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
};

const kCropVars: { side: keyof IInlineImageCrop; variable: string }[] = [
    { side: "top", variable: kInlineImageCropTopVar },
    { side: "right", variable: kInlineImageCropRightVar },
    { side: "bottom", variable: kInlineImageCropBottomVar },
    { side: "left", variable: kInlineImageCropLeftVar },
];

/** The crop recorded on this wrapper; all zeros for a picture that is not cropped. */
export function getInlineImageCrop(wrapper: HTMLElement): IInlineImageCrop {
    const crop = { ...kNoInlineImageCrop };
    kCropVars.forEach(({ side, variable }) => {
        const value = parseFloat(wrapper.style.getPropertyValue(variable));
        crop[side] = Number.isNaN(value) ? 0 : value;
    });
    return crop;
}

/**
 * Records a crop on the wrapper. A side with nothing cut away gets no property at all, so a
 * picture that is not cropped has none of them, and the CSS rules for a crop leave it alone.
 * Values are rounded to a hundredth of a percent, which is finer than a pixel of any picture Bloom
 * shows, and keeps the style attribute short; it is saved and copied to every language's copy.
 * The caller updates the aspect ratio (see refreshInlineImageAspectRatio in inlineImages.ts).
 */
export function setInlineImageCrop(
    wrapper: HTMLElement,
    crop: IInlineImageCrop,
): void {
    kCropVars.forEach(({ side, variable }) => {
        const value = Math.round(crop[side] * 100) / 100;
        if (value > 0) wrapper.style.setProperty(variable, `${value}`);
        else wrapper.style.removeProperty(variable);
    });
}

/** Whether any part of this wrapper's picture is cut away. */
export function inlineImageIsCropped(wrapper: HTMLElement): boolean {
    const crop = getInlineImageCrop(wrapper);
    return crop.top > 0 || crop.right > 0 || crop.bottom > 0 || crop.left > 0;
}

/** Removes the crop, so the whole picture shows. */
export function clearInlineImageCrop(wrapper: HTMLElement): void {
    setInlineImageCrop(wrapper, kNoInlineImageCrop);
}

/**
 * Swaps the crop's left and right (for a horizontal flip) or top and bottom (for a vertical one),
 * so that after the flip the same part of the picture shows, mirrored. Call it with every Flip of
 * the picture; the numbers describe the picture as it is seen.
 */
export function mirrorInlineImageCrop(
    wrapper: HTMLElement,
    axis: FlipAxis,
): void {
    const crop = getInlineImageCrop(wrapper);
    setInlineImageCrop(
        wrapper,
        axis === "horizontal"
            ? { ...crop, left: crop.right, right: crop.left }
            : { ...crop, top: crop.bottom, bottom: crop.top },
    );
}

/**
 * The value for --inline-image-aspect-ratio: the shape of the part of a picture of this natural
 * size that the crop leaves showing, as "width / height".
 */
export function computeCroppedInlineImageAspectRatio(
    naturalWidth: number,
    naturalHeight: number,
    crop: IInlineImageCrop,
): string {
    const round = (n: number) => Math.round(n * 100) / 100;
    const width = (naturalWidth * (100 - crop.left - crop.right)) / 100;
    const height = (naturalHeight * (100 - crop.top - crop.bottom)) / 100;
    return `${round(width)} / ${round(height)}`;
}
