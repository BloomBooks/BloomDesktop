// Turning and mirroring the picture inside its container.
//
// This is separate from rotating a whole canvas element (canvasElementManager/
// canvasElementRotation.ts). There the box turns on the page and the picture rides round with
// it. Here the picture turns inside the page's picture area: the Rotate Right command uses this
// for a page background picture, whose box is the page's picture area itself and so cannot turn
// on the page, and the Flip commands use it for every picture, because mirroring a box would
// make no sense.
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
// The scale carries the mirrors and nothing else: each factor is 1 or -1. A quarter turn
// leaves the picture's layout box lying across its container, and we answer that by moving
// the box and the box's own canvas element, not by shrinking the picture. See
// computeTurnedBackgroundLayout.

import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
    kCanvasElementSelector,
} from "../toolbox/canvas/canvasElementConstants";

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

// Write the turn and mirror state back to the img's inline transform.
function setImageContentTransform(
    img: HTMLImageElement,
    state: ImageContentTransform,
): void {
    const sx = state.flipX ? -1 : 1;
    const sy = state.flipY ? -1 : 1;

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
        parts.push(`scale(${sx}, ${sy})`);
    }
    img.style.transform = parts.join(" ");
}

// Put the picture back the way it arrived: no turn and no mirror. The Reset Image command
// uses this together with removing the crop. It does not touch the rotation of a canvas
// element box, which is a property of the box rather than of the picture.
export function clearImageContentTransform(img: HTMLImageElement): void {
    img.style.transform = "";
}

// The size and place of a background picture's canvas element and of the picture inside it.
// All eight numbers are CSS pixels within the bloom-canvas (the page's picture area).
export interface BackgroundPictureLayout {
    elementWidth: number;
    elementHeight: number;
    elementLeft: number;
    elementTop: number;
    // The picture's layout box, which is what a transform then turns.
    imageWidth: number;
    imageHeight: number;
    imageLeft: number;
    imageTop: number;
}

// Where a background picture and its canvas element must go after one more quarter turn.
//
// Bloom shows a background picture by giving its canvas element the shape of the picture and
// centring that element in the page's picture area. A picture that is not the shape of the page
// therefore leaves blank bands above and below (or left and right), and the Expand Image
// command is there for an author who would rather fill the page. A quarter turn changes the
// shape of the picture, so it has to change the shape of the element in the same way: what you
// get by turning a picture is what you would have got if the picture had arrived already
// turned.
//
// So we turn the whole content of the element, which swaps the element's two dimensions, and
// then scale everything so that the swapped element fits the page again. Turning the content of
// a container of width Cw and height Ch a quarter turn clockwise gives a container of width Ch
// and height Cw, and carries the point (x, y) to (Ch - y, x). The picture's box turns about its
// own centre, because that is what a CSS transform does, so the only thing we have to place is
// that centre.
//
// The caller passes the picture's drawn rectangle, not its layout box. For a cropped picture the
// two are the same, but an uncropped picture is drawn inside its box by object-fit, and it is
// the drawn rectangle that has to land in the right place.
export function computeTurnedBackgroundLayout(
    // The bloom-canvas: the page's picture area.
    pageWidth: number,
    pageHeight: number,
    // The background canvas element, which is also the size of the image container.
    elementWidth: number,
    elementHeight: number,
    // The drawn picture, in the element's coordinates.
    imageWidth: number,
    imageHeight: number,
    imageLeft: number,
    imageTop: number,
    // True for a picture the author has told to fill the page (the Expand Image command, which
    // adds the class bloom-imageObjectFit-cover). Such a picture keeps the whole page and has
    // its ends clipped, instead of fitting inside the page with blank bands.
    fillsPage: boolean,
): BackgroundPictureLayout {
    // The turned element measures elementHeight by elementWidth. Scale it to the page.
    const widthRatio = pageWidth / elementHeight;
    const heightRatio = pageHeight / elementWidth;
    const scale = fillsPage
        ? Math.max(widthRatio, heightRatio)
        : Math.min(widthRatio, heightRatio);

    const newElementWidth = fillsPage ? pageWidth : scale * elementHeight;
    const newElementHeight = fillsPage ? pageHeight : scale * elementWidth;

    // Where the turned content sits inside the new element. When the element fits the page, the
    // two are the same size and this is zero. When the element fills the page, the content is
    // bigger than the element, and we centre it so that equal amounts are clipped from each end.
    const shiftLeft = (newElementWidth - scale * elementHeight) / 2;
    const shiftTop = (newElementHeight - scale * elementWidth) / 2;

    // The centre of the picture's box, carried by the turn and then scaled.
    const centreX =
        scale * (elementHeight - imageTop - imageHeight / 2) + shiftLeft;
    const centreY = scale * (imageLeft + imageWidth / 2) + shiftTop;

    const newImageWidth = scale * imageWidth;
    const newImageHeight = scale * imageHeight;
    return {
        elementWidth: newElementWidth,
        elementHeight: newElementHeight,
        elementLeft: (pageWidth - newElementWidth) / 2,
        elementTop: (pageHeight - newElementHeight) / 2,
        imageWidth: newImageWidth,
        imageHeight: newImageHeight,
        imageLeft: centreX - newImageWidth / 2,
        imageTop: centreY - newImageHeight / 2,
    };
}

// The class the Expand Image command puts on a background picture that must fill the page.
const kFillsPageClass = "bloom-imageObjectFit-cover";

// Read a length we wrote ourselves. Anything we did not write counts as zero.
function pxOrZero(value: string): number {
    const parsed = parseFloat(value);
    return isNaN(parsed) ? 0 : parsed;
}

// Round to a hundredth of a pixel. That is finer than anyone can see, and it keeps the saved
// HTML from filling up with digits.
function roundPx(value: number): number {
    return Math.round(value * 100) / 100;
}

// The picture's drawn rectangle inside its layout box, in the container's coordinates.
//
// A cropped picture has an explicit width, and then its box and its drawn rectangle are the
// same thing. An uncropped picture has a box the size of its container and is drawn inside that
// box by object-fit: contain, which fills the box only when the container is already the shape
// of the picture. The container usually is, because Bloom shapes it that way, but we work the
// drawn rectangle out rather than assume it.
function getDrawnPictureRectangle(
    img: HTMLImageElement,
): { width: number; height: number; left: number; top: number } | undefined {
    const boxWidth = img.clientWidth;
    const boxHeight = img.clientHeight;
    if (!boxWidth || !boxHeight) {
        return undefined;
    }
    const left = pxOrZero(img.style.left);
    const top = pxOrZero(img.style.top);
    if (img.style.width) {
        return { width: boxWidth, height: boxHeight, left, top };
    }
    const naturalWidth = img.naturalWidth;
    const naturalHeight = img.naturalHeight;
    if (!naturalWidth || !naturalHeight) {
        return undefined;
    }
    const containScale = Math.min(
        boxWidth / naturalWidth,
        boxHeight / naturalHeight,
    );
    const width = containScale * naturalWidth;
    const height = containScale * naturalHeight;
    return {
        width,
        height,
        left: left + (boxWidth - width) / 2,
        top: top + (boxHeight - height) / 2,
    };
}

// Move the picture's canvas element and the picture inside it for one more quarter turn, and say
// whether it worked. It does not touch the transform; the caller does that.
//
// This is where a crop survives a turn. The four numbers that hold a crop say how big to draw
// the whole picture and how far to move it up and to the left. They describe the picture as it
// lies before the turn, and they stay true of it afterwards: all that changes is where that box
// has to be for the turned picture to land on the page.
//
// There are two kinds of element to place the turned content in, and one formula covers both.
// A background picture has to fit the page's picture area, so its element is scaled to that
// area and centred in it. Any other element keeps the size it has, so the area is that element
// with its own two dimensions swapped, the scale comes out as one, and the element turns about
// its own centre and stays where the author put it. The Rotate Right command reaches an element
// of the second kind when its box cannot turn on the page for some other reason, which is the
// case for an element whose outline comicaljs draws.
function setTurnedBackgroundLayout(img: HTMLImageElement): boolean {
    const element = img.closest(kCanvasElementSelector) as HTMLElement | null;
    const page = img.closest(kBloomCanvasSelector) as HTMLElement | null;
    if (!element || !page) {
        return false;
    }
    const isBackground = element.classList.contains(kBackgroundImageClass);
    const elementWidth = element.clientWidth;
    const elementHeight = element.clientHeight;
    // The area the turned element has to land in.
    const areaWidth = isBackground ? page.clientWidth : elementHeight;
    const areaHeight = isBackground ? page.clientHeight : elementWidth;
    const picture = getDrawnPictureRectangle(img);
    if (
        !areaWidth ||
        !areaHeight ||
        !elementWidth ||
        !elementHeight ||
        !picture
    ) {
        // Something has not been laid out yet, so we have nothing to measure and would write
        // nonsense. The turn itself still happens, and the container clips any overhang.
        return false;
    }

    const layout = computeTurnedBackgroundLayout(
        areaWidth,
        areaHeight,
        elementWidth,
        elementHeight,
        picture.width,
        picture.height,
        picture.left,
        picture.top,
        img.classList.contains(kFillsPageClass),
    );

    element.style.width = `${roundPx(layout.elementWidth)}px`;
    element.style.height = `${roundPx(layout.elementHeight)}px`;
    if (isBackground) {
        // Centred in the page's picture area, which is where Bloom keeps a background picture.
        element.style.left = `${roundPx(layout.elementLeft)}px`;
        element.style.top = `${roundPx(layout.elementTop)}px`;
    } else {
        // Turned about its own centre, so the element stays where the author put it.
        element.style.left = `${roundPx(
            pxOrZero(element.style.left) +
                (elementWidth - layout.elementWidth) / 2,
        )}px`;
        element.style.top = `${roundPx(
            pxOrZero(element.style.top) +
                (elementHeight - layout.elementHeight) / 2,
        )}px`;
    }

    // If the picture fills its element again, it needs no numbers of its own. Leaving them off
    // matters: an explicit width is how the rest of Bloom recognizes a crop, so an uncropped
    // picture must not acquire one just by being turned. The other direction matters as well.
    // When the page changes size, adjustBackgroundImageSizeToFit keeps the element's own shape
    // for a picture that has a width and goes back to the picture's natural shape for one that
    // has none. A quarter turn always writes a width, so the turned shape survives a resize; a
    // half turn writes none, and the natural shape is then the right one.
    const fillsElementExactly =
        Math.abs(layout.imageWidth - layout.elementWidth) < 1 &&
        Math.abs(layout.imageHeight - layout.elementHeight) < 1 &&
        Math.abs(layout.imageLeft) < 1 &&
        Math.abs(layout.imageTop) < 1;
    if (fillsElementExactly) {
        img.style.width = "";
        img.style.height = "";
        img.style.left = "";
        img.style.top = "";
        return true;
    }
    img.style.width = `${roundPx(layout.imageWidth)}px`;
    img.style.left = `${roundPx(layout.imageLeft)}px`;
    img.style.top = `${roundPx(layout.imageTop)}px`;
    // The height follows the width and the picture's own shape, and we must not write it: when
    // the page changes size, Bloom scales a picture's width, left and top and leaves its height
    // alone, so a height written here would stay behind and stretch the picture.
    img.style.height = "";
    return true;
}

// Turn the picture a quarter turn clockwise inside the page's picture area, keeping any crop the
// author made. See setTurnedBackgroundLayout for how, and computeTurnedBackgroundLayout for why
// the picture's own canvas element changes shape as well.
export function rotateImageContentRight(img: HTMLImageElement): void {
    const state = getImageContentTransform(img);
    // Measure and move first: clientWidth reports the layout box, which a transform does not
    // change, so the old transform can stay in place while we measure.
    setTurnedBackgroundLayout(img);
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
