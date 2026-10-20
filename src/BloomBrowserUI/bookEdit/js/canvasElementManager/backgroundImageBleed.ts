// The small overhang that keeps a background picture's edges from showing a white line. It lives
// on its own, with no imports, so that the code that rotates a picture (imageContentTransform.ts)
// can give the picture the same overhang that fitting it does, without depending on the rest of
// the background image manager.

// How far (in px) a background image extends past each edge of its bloom-canvas that it reaches.
// The bloom-canvas clips it (overflow: hidden), so the overhang is never seen. Without it, a picture
// meant to fill the bloom-canvas shows a thin white line along its edges: Chrome snaps an img to
// whole pixels in the page's own coordinates and then applies the editing zoom transform, so the
// drawn picture can stop up to half a pixel short of its box and let the white page show through.
export const kBackgroundImageBleedPx = 1;

// A background picture that comes within this many px (in total, across both sides) of filling the
// bloom-canvas in one dimension is treated as filling it, and gets the bleed on that dimension too.
// Origami split positions are percentages, so a picture the author lined up with the edges of its
// pane is rarely an exact fit, and the leftover sliver shows as a white line.
export const kBackgroundImageFillTolerancePx = 2;

// Given the size a background image would have if fitted inside a bloom-canvas of size
// canvasWidth x canvasHeight, return the size it should have so that, in each dimension where it
// fills (or very nearly fills) the bloom-canvas, it extends kBackgroundImageBleedPx past both edges.
// The image keeps its aspect ratio; where it overhangs more than the bleed in the other dimension,
// the bloom-canvas crops it equally on both sides.
export function getBackgroundImageSizeWithBleed(
    fittedWidth: number,
    fittedHeight: number,
    canvasWidth: number,
    canvasHeight: number,
): { width: number; height: number } {
    const fillsWidth =
        canvasWidth - fittedWidth < kBackgroundImageFillTolerancePx;
    const fillsHeight =
        canvasHeight - fittedHeight < kBackgroundImageFillTolerancePx;
    const scale = Math.max(
        1,
        fillsWidth
            ? (canvasWidth + 2 * kBackgroundImageBleedPx) / fittedWidth
            : 1,
        fillsHeight
            ? (canvasHeight + 2 * kBackgroundImageBleedPx) / fittedHeight
            : 1,
    );
    return { width: fittedWidth * scale, height: fittedHeight * scale };
}
