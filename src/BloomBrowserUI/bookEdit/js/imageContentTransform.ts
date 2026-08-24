// Turning and mirroring the picture inside its container.
//
// This is separate from rotating a whole canvas element (canvasElementManager/
// canvasElementRotation.ts). Here the container keeps its shape and only the picture inside
// it changes: the Rotate Right command uses this for a background image, which fills its
// bloom-canvas and so cannot itself turn, and the Flip commands use it for every image,
// because mirroring the box would make no sense.
//
// State lives in the img's own inline `transform`, in the canonical form
// `rotate(<angle>deg) scale(<sx>, <sy>)`. As with canvas element rotation, an inline style
// is what makes the change appear in the reader and in PDF output, where no Bloom
// JavaScript runs, so it is the single source of truth and this module owns the parsing.
//
// The three logical values we keep are:
// - a number of quarter turns clockwise, 0 to 3, from the `rotate` part;
// - whether the picture is mirrored along its own x axis, from the sign of sx;
// - whether it is mirrored along its own y axis, from the sign of sy.
//
// The magnitude of the scale is not state: it is recomputed on every write, because a
// quarter turn leaves the picture's layout box lying across its container and it has to be
// shrunk to fit again.

export type FlipAxis = "horizontal" | "vertical";

interface ImageContentTransform {
    // Quarter turns clockwise: 0, 1, 2 or 3.
    quarterTurns: number;
    // Mirrored along the picture's own x axis (before the turn is applied).
    flipX: boolean;
    // Mirrored along the picture's own y axis (before the turn is applied).
    flipY: boolean;
}

const kRotatePattern = /rotate\(\s*(-?[0-9]*\.?[0-9]+)deg\s*\)/;
const kScalePattern =
    /scale\(\s*(-?[0-9]*\.?[0-9]+)\s*(?:,\s*(-?[0-9]*\.?[0-9]+)\s*)?\)/;

// Read the current turn and mirror state of the picture.
export function getImageContentTransform(
    img: HTMLImageElement,
): ImageContentTransform {
    const transform = img.style.transform ?? "";

    let quarterTurns = 0;
    const rotateMatch = kRotatePattern.exec(transform);
    if (rotateMatch) {
        const degrees = parseFloat(rotateMatch[1]);
        // Round to the nearest quarter turn. Only quarter turns can get here, because this
        // is the only code that writes the value, but rounding keeps a hand-edited or
        // future value from producing a fractional turn count.
        quarterTurns = ((Math.round(degrees / 90) % 4) + 4) % 4;
    }

    let flipX = false;
    let flipY = false;
    const scaleMatch = kScalePattern.exec(transform);
    if (scaleMatch) {
        const sx = parseFloat(scaleMatch[1]);
        // A one-argument scale() scales both axes by the same amount.
        const sy = scaleMatch[2] === undefined ? sx : parseFloat(scaleMatch[2]);
        flipX = sx < 0;
        flipY = sy < 0;
    }

    return { quarterTurns, flipX, flipY };
}

// True if the picture has been turned or mirrored at all.
export function imageContentIsTransformed(img: HTMLImageElement): boolean {
    const state = getImageContentTransform(img);
    return state.quarterTurns !== 0 || state.flipX || state.flipY;
}

// How much the picture must shrink so that, lying across its container after an odd number
// of quarter turns, it still fits. Its layout box keeps the shape it had before the turn, so
// the box's width has to fit the container's height and the box's height the container's
// width.
function getFitScaleForQuarterTurn(img: HTMLImageElement): number {
    const width = img.clientWidth;
    const height = img.clientHeight;
    if (!width || !height) {
        // We cannot measure the picture yet (it has not been laid out). Leaving the scale at
        // 1 turns the picture without shrinking it; the container clips the overhang, which
        // is better than dividing by zero.
        return 1;
    }
    const container = img.parentElement;
    const containerWidth = container?.clientWidth ?? width;
    const containerHeight = container?.clientHeight ?? height;
    return Math.min(containerHeight / width, containerWidth / height);
}

// Write the turn and mirror state back to the img's inline transform.
function setImageContentTransform(
    img: HTMLImageElement,
    state: ImageContentTransform,
): void {
    const isQuarterTurn = state.quarterTurns % 2 === 1;
    const fitScale = isQuarterTurn ? getFitScaleForQuarterTurn(img) : 1;
    const sx = (state.flipX ? -1 : 1) * fitScale;
    const sy = (state.flipY ? -1 : 1) * fitScale;

    if (state.quarterTurns === 0 && sx === 1 && sy === 1) {
        // Back to the original picture, so leave no transform behind at all.
        img.style.transform = "";
        return;
    }

    const parts: string[] = [];
    if (state.quarterTurns !== 0) {
        parts.push(`rotate(${state.quarterTurns * 90}deg)`);
    }
    if (sx !== 1 || sy !== 1) {
        // Three decimal places keeps the fit scale accurate to well under a pixel at any
        // page size we support, without filling the saved HTML with digits.
        const round = (value: number) => Math.round(value * 1000) / 1000;
        parts.push(`scale(${round(sx)}, ${round(sy)})`);
    }
    img.style.transform = parts.join(" ");
}

// Put the picture back the way it arrived: no turn and no mirror. The Reset Image command
// uses this together with removing the crop. It does not touch the rotation of a canvas
// element box, which is a property of the box rather than of the picture.
export function clearImageContentTransform(img: HTMLImageElement): void {
    img.style.transform = "";
}

// Cropping is stored as an explicit size and offset that makes the img bigger than its
// container. Those numbers describe the picture as it was, so a quarter turn would leave the
// crop showing a part of the picture the user never chose. We therefore drop the crop when
// we turn the picture. Mirroring keeps the box the same shape, so it leaves the crop alone.
function removeCropping(img: HTMLImageElement): void {
    img.style.width = "";
    img.style.height = "";
    img.style.left = "";
    img.style.top = "";
}

// Turn the picture a quarter turn clockwise inside its container, and shrink it so that it
// still fits. Any cropping is removed; see removeCropping.
export function rotateImageContentRight(img: HTMLImageElement): void {
    const state = getImageContentTransform(img);
    // Drop the crop first, so that the fit scale is measured against the layout box the
    // picture will actually have. (clientWidth reports the layout box, which a transform
    // does not change, so the old transform can stay in place while we measure.)
    removeCropping(img);
    setImageContentTransform(img, {
        ...state,
        quarterTurns: (state.quarterTurns + 1) % 4,
    });
}

// Mirror the picture about the axis the user sees on screen. "Horizontal" means left and
// right change places on screen, whichever way the picture has been turned. After an odd
// number of quarter turns the picture's own x axis runs up and down the screen, so a
// horizontal flip on screen is a flip of the picture's y axis.
//
// The canvas element box can be turned as well, by the Rotate Right command or by the
// rotation handle, and that turn moves the picture on screen just as a turn of the picture
// itself does. The caller passes that angle as boxRotationDegrees. The handle turns to any
// angle, and no mirror of the picture's own axes equals a mirror about the screen axis at,
// say, 37 degrees, so we take the box angle to the nearest quarter turn and mirror about the
// axis of the picture that lies nearest the one the user asked for.
export function flipImageContent(
    img: HTMLImageElement,
    axis: FlipAxis,
    boxRotationDegrees = 0,
): void {
    const state = getImageContentTransform(img);
    const boxQuarterTurns = Math.round(boxRotationDegrees / 90);
    const isQuarterTurn = (state.quarterTurns + boxQuarterTurns) % 2 !== 0;
    const flipLocalX = axis === "horizontal" ? !isQuarterTurn : isQuarterTurn;
    setImageContentTransform(img, {
        ...state,
        flipX: flipLocalX ? !state.flipX : state.flipX,
        flipY: flipLocalX ? state.flipY : !state.flipY,
    });
}
