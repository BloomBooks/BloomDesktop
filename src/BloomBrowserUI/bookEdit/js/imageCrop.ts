// The arithmetic of cropping a picture, shared by a picture on a canvas
// (canvasElementManager/CanvasElementHandleDragInteractions.ts and imageContentTransform.ts) and
// an inline image (inlineImageInteractions.ts and inlineImageCrop.ts).
//
// Both describe a crop the same way here: an element, which is the part of the page that shows
// the picture, and the picture's box, which is where the whole picture lies, in the element's own
// coordinates. A cropped picture's box is bigger than the element and reaches past it on the
// cropped sides. The two store a crop differently: a canvas picture's box is the img's own pixel
// left, top and width, and an inline image's crop is four percentages on its wrapper. Each turns
// its own storage into an element and a box, calls these functions, and turns the answer back.

export type CropSide = "n" | "e" | "s" | "w";

// Where a picture lands inside the element that shows it, in the element's own
// coordinates.
//
// The picture's own box may be rotated inside the element, by the Rotate Right command on a page
// background. A CSS transform rotates the box about its own centre and leaves the layout alone, so
// what the element shows is a rectangle the size of the box with its two dimensions swapped for
// an odd number of 90-degree rotations, centred where the layout put the box's centre. Every crop
// measurement has to use this rectangle. The box itself, which the picture's `left`, `top` and
// `width` describe, is the right answer only for a picture that is not rotated.
export function getShownContentRectangle(
    boxLeft: number,
    boxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterRotations: number,
): { left: number; top: number; width: number; height: number } {
    const isQuarterRotation = quarterRotations % 2 === 1;
    const width = isQuarterRotation ? boxHeight : boxWidth;
    const height = isQuarterRotation ? boxWidth : boxHeight;
    return {
        left: boxLeft + boxWidth / 2 - width / 2,
        top: boxTop + boxHeight / 2 - height / 2,
        width,
        height,
    };
}

// True if the picture the element shows is bigger than the element in either direction, which is
// when the picture is cropped and the move-crop handle is wanted. The size compared is that of
// the rectangle the element shows, as in getCroppedSides, so a picture rotated 90 degrees whose
// box lies across its element is not mistaken for a cropped one.
export function shownPictureIsBiggerThanElement(
    elementWidth: number,
    elementHeight: number,
    boxWidth: number,
    boxHeight: number,
    quarterRotations: number,
    // Client values are whole pixels, and rounding easily produces a spurious difference of one.
    slop = 1,
): boolean {
    const shown = getShownContentRectangle(
        0,
        0,
        boxWidth,
        boxHeight,
        quarterRotations,
    );
    return (
        shown.width > elementWidth + slop || shown.height > elementHeight + slop
    );
}

// Which sides of a picture are cropped, so that the marks go on the right handles.
export function getCroppedSides(
    elementWidth: number,
    elementHeight: number,
    boxLeft: number,
    boxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterRotations: number,
    // Client values are whole pixels, and rounding easily produces a spurious difference of one.
    slop = 1,
): { n: boolean; e: boolean; s: boolean; w: boolean } {
    const shown = getShownContentRectangle(
        boxLeft,
        boxTop,
        boxWidth,
        boxHeight,
        quarterRotations,
    );
    return {
        n: shown.top < -slop,
        e: shown.left + shown.width > elementWidth + slop,
        s: shown.top + shown.height > elementHeight + slop,
        w: shown.left < -slop,
    };
}

// Where to put a picture's box so that the picture keeps covering its element while the
// author drags it about inside the crop. The wanted position is what the pointer asks for; the
// result is that position pulled back far enough that no blank band appears at an edge.
//
// The box is what we write, but the element shows the rotated rectangle, so the limits belong to
// that rectangle. On a picture whose two dimensions differ, and which is rotated, the two frames
// disagree by half the difference, which is enough to hold the picture still through a whole
// drag.
export function clampCropPosition(
    elementWidth: number,
    elementHeight: number,
    wantedBoxLeft: number,
    wantedBoxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterRotations: number,
): { left: number; top: number } {
    const shown = getShownContentRectangle(
        wantedBoxLeft,
        wantedBoxTop,
        boxWidth,
        boxHeight,
        quarterRotations,
    );
    const clampedShownLeft = Math.max(
        Math.min(shown.left, 0),
        elementWidth - shown.width,
    );
    const clampedShownTop = Math.max(
        Math.min(shown.top, 0),
        elementHeight - shown.height,
    );
    // The rotation moves the box and the rectangle it shows by the same amount, so one difference
    // carries the clamp back into the box's own frame.
    return {
        left: wantedBoxLeft + clampedShownLeft - shown.left,
        top: wantedBoxTop + clampedShownTop - shown.top,
    };
}

/**
 * Where dragging one side handle puts the element's edges. delta is how far the pointer has moved
 * since the drag began, in the element's coordinates, along the axis that handle moves on:
 * positive is to the right for the e and w handles, and down for the n and s handles. shown is
 * the rectangle of the picture that the element shows (see getShownContentRectangle). An edge
 * cannot be dragged past the picture's edge, so no blank band appears, and the element stays
 * between minSize and maxSize across that axis.
 *
 * The picture stays where it is on the page while its side is cropped. So the n and w handles,
 * which move the element's top or left edge, must move the picture's box the same distance the
 * other way inside the element. The result's left and top say how far those two edges moved.
 */
export function cropOneSide(
    side: CropSide,
    delta: number,
    elementWidth: number,
    elementHeight: number,
    shown: { left: number; top: number; width: number; height: number },
    minSize: number,
    maxSize = Number.POSITIVE_INFINITY,
): { width: number; height: number; left: number; top: number } {
    const clampSize = (size: number) =>
        Math.min(Math.max(size, minSize), maxSize);
    switch (side) {
        case "n": {
            const height = clampSize(
                elementHeight - Math.max(delta, shown.top),
            );
            return {
                width: elementWidth,
                height,
                left: 0,
                top: elementHeight - height,
            };
        }
        case "s": {
            const height = clampSize(
                elementHeight +
                    Math.min(delta, shown.top + shown.height - elementHeight),
            );
            return { width: elementWidth, height, left: 0, top: 0 };
        }
        case "e": {
            const width = clampSize(
                elementWidth +
                    Math.min(delta, shown.left + shown.width - elementWidth),
            );
            return { width, height: elementHeight, left: 0, top: 0 };
        }
        case "w": {
            const width = clampSize(elementWidth - Math.max(delta, shown.left));
            return {
                width,
                height: elementHeight,
                left: elementWidth - width,
                top: 0,
            };
        }
    }
}

// Where to put a cropped picture's box so that mirroring the picture shows the same part of it,
// mirrored, instead of another part.
//
// The mirror turns the picture about the centre of its box, so unless the box is centred on the
// element, the mirrored picture puts a different part of itself in view. Mirroring the rectangle
// the element shows about the element's own centre line keeps the same part in view, so we move
// the box by as much as that moves the rectangle. mirrorLeftRight is true to mirror left and right
// in the element's own frame, false for up and down.
export function mirrorCropPosition(
    elementWidth: number,
    elementHeight: number,
    boxLeft: number,
    boxTop: number,
    boxWidth: number,
    boxHeight: number,
    quarterRotations: number,
    mirrorLeftRight: boolean,
): { left: number; top: number } {
    const shown = getShownContentRectangle(
        boxLeft,
        boxTop,
        boxWidth,
        boxHeight,
        quarterRotations,
    );
    if (mirrorLeftRight) {
        const mirroredShownLeft = elementWidth - (shown.left + shown.width);
        return { left: boxLeft + mirroredShownLeft - shown.left, top: boxTop };
    }
    const mirroredShownTop = elementHeight - (shown.top + shown.height);
    return { left: boxLeft, top: boxTop + mirroredShownTop - shown.top };
}
