import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { kGameToolId } from "../toolIds";
import { Fragment, useEffect, useMemo, useState } from "react";
import {
    kBloomBlue,
    kOptionPanelBackgroundColor,
    toolboxTheme
} from "../../../bloomMaterialUITheme";
import { Div } from "../../../react_components/l10nComponents";
import {
    CanvasElementGifItem,
    CanvasElementImageItem,
    CanvasElementItemRegion,
    CanvasElementItemRow,
    CanvasElementRectangleItem,
    CanvasElementSoundItem,
    CanvasElementTextItem,
    CanvasElementVideoItem,
    setGeneratedDraggableId
} from "../overlay/CanvasElementItem";
import { ToolBox } from "../toolbox";
import {
    adjustDraggablesForLanguage,
    classSetter,
    copyContentToTarget,
    doShowAnswersInTargets,
    getTarget,
    playInitialElements,
    prepareActivity,
    undoPrepareActivity
} from "bloom-player";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import { getWithPromise, postData, postJson } from "../../../utils/bloomApi";
import {
    getEditablePageBundleExports,
    getToolboxBundleExports
} from "../../editViewFrame";
import { MenuItem, Select } from "@mui/material";
import { useL10n } from "../../../react_components/l10nHooks";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { BubbleSpec } from "comicaljs";
import { setPlayerUrlPrefixFromWindowLocationHref } from "bloom-player";
import { renderGamePromptDialog } from "./GamePromptDialog";
import {
    CanvasElementManager,
    kBackgroundImageClass
} from "../../js/CanvasElementManager";
import {
    getCanvasElementManager,
    kCanvasElementSelector
} from "../overlay/canvasElementUtils";
import { ThemeChooser } from "./ThemeChooser";
import GameIntroText, { Instructions } from "./GameIntroText";
import { getGameType, isPageBloomGame } from "./GameInfo";
import {
    getImageFromCanvasElement,
    kImageContainerSelector
} from "../../js/bloomImages";
import { doesContainingPageHaveSameSizeMode } from "./gameUtilities";
import { CanvasSnapProvider } from "../../js/CanvasSnapProvider";
import { CanvasGuideProvider } from "../../js/CanvasGuideProvider";

// This is the main code that manages the Bloom Games, including Drag Activities.
// See especially DragActivityControls, which is the main React component for the tool,
// and DragActivityTool, which is the ToolboxToolReactAdaptor subclass that represents
// the tool to the toolbox code. See also the summary in Games.less of important classes
// and attributes which are used for these games in both template pages and the code here.

// Make the targets draggable (in Start mode).
export const enableDraggingTargets = (startingPoint: HTMLElement) => {
    const page = startingPoint.closest(".bloom-page") as HTMLElement;
    page.querySelectorAll("[data-target-of]").forEach((elt: HTMLElement) => {
        elt.addEventListener("mousedown", startDraggingTarget);
    });
    //Slider if (page.getAttribute("data-activity") === "drag-word-chooser-slider") {
    //     setupWordChooserSlider(page);
    //     const wrapper = page.getElementsByClassName(
    //         "bloom-activity-slider"
    //     )[0] as HTMLElement;
    //     wrapper.addEventListener("click", designTimeClickOnSlider);
    // }
};

// Must only be called in the right iframe, not imported elsewhere; otherwise,
// React rendering will be confused.
export const showGamePromptDialog = (onlyIfEmpty: boolean) => {
    const page = document.getElementsByClassName(
        "bloom-page"
    )[0] as HTMLElement;
    if (!page) return;
    const prompt = page.getElementsByClassName(
        "bloom-game-prompt"
    )[0] as HTMLElement;
    // So far only this one kind of game has a prompt
    if (
        !prompt ||
        page.getAttribute("data-activity") !== "drag-letter-to-target"
    ) {
        return;
    }

    if (onlyIfEmpty) {
        const editable = prompt.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0];
        if (editable && editable.textContent?.trim()) {
            return;
        }
    }
    let dialogRoot = page.ownerDocument.getElementsByClassName(
        "bloom-ui-dialog"
    )[0] as HTMLElement;
    if (!dialogRoot) {
        dialogRoot = page.ownerDocument.createElement("div");
        // The first class makes sure not permanently saved; second is used to find it.
        dialogRoot.classList.add("bloom-ui", "bloom-ui-dialog");
        page.appendChild(dialogRoot);
    }
    // Things are simpler if no canvas element is active. We don't have to worry if we delete the
    // active one, for example.
    getCanvasElementManager()?.setActiveElement(undefined);
    renderGamePromptDialog(dialogRoot, prompt, true);
};

export const hideGamePromptDialog = (page: HTMLElement) => {
    const dialogRoot = page.ownerDocument.getElementsByClassName(
        "bloom-ui-dialog"
    )[0];
    if (dialogRoot) {
        ReactDOM.unmountComponentAtNode(dialogRoot);
        dialogRoot.remove();
    }
};

// Make the targets not draggable (in Correct, Wrong, and Play modes).
const disableDraggingTargets = (startingPoint: HTMLElement) => {
    const page = startingPoint.closest(".bloom-page") as HTMLElement;
    page.querySelectorAll("[data-target-of]").forEach((elt: HTMLElement) => {
        elt.removeEventListener("mousedown", startDraggingTarget);
    });
    //Slider: if (page.getAttribute("data-activity") === "drag-word-chooser-slider") {
    //     const wrapper = page.getElementsByClassName(
    //         "bloom-activity-slider"
    //     )[0] as HTMLElement;
    //     wrapper.removeEventListener("click", designTimeClickOnSlider);
    // }
};

const overlap = (start: HTMLElement, end: HTMLElement): boolean => {
    return (
        start.offsetLeft + start.offsetWidth > end.offsetLeft &&
        start.offsetLeft < end.offsetLeft + end.offsetWidth &&
        start.offsetTop + start.offsetHeight > end.offsetTop &&
        start.offsetTop < end.offsetTop + end.offsetHeight
    );
};

// Make things right for start mode when something happened to an element (possibly draggable)
// that might affect the target and even other draggables, such as clicking an element on the page
// or moving or resizing a draggable.
// Draw the arrow from the draggable to its target if it has one (and remove any previous arrow).
// If the draggable is resized, adjust the target (if any) to match.
// If forceAdjustAll is true, or we're in auto-adjust mode, adjust all draggables and targets
// on the page to match the size of the draggable, except that image targets are not resized.
export const adjustTarget = (
    draggable: HTMLElement,
    target: HTMLElement | undefined,
    forceAdjustAll?: boolean
) => {
    let arrow = (draggable.ownerDocument.getElementById(
        "target-arrow"
    ) as unknown) as SVGSVGElement;
    if (!target) {
        // if there is a target, we'll adjust the existing arrow if any.
        // If not, get rid of any arrow now.
        if (arrow) {
            arrow.remove();
        }
    }
    // This may get called when we click something that isn't a draggable at all.
    // That gets rid of any arrow. It shouldn't do anything else.
    if (!draggable?.getAttribute("data-draggable-id")) {
        return;
    }
    const allSameSize = doesContainingPageHaveSameSizeMode(draggable);
    // get height and width of things this way, because sometimes some are not visible
    // (e.g., when creating letters in the drag-letter-to-target game)
    const getHeight = (elt: HTMLElement) => {
        return CanvasElementManager.pxToNumber(elt.style.height);
    };
    const getWidth = (elt: HTMLElement) => {
        return CanvasElementManager.pxToNumber(elt.style.width);
    };
    // if the target is not the same size, presumably the draggable size changed, in which case
    // we need to adjust the target, and possibly all other targets and draggables on the page.
    // If there's no target, just assume we need to adjust all if we're keeping sizes the same.
    // Note: an image might be a different size even though it has not been resized; not a great
    // problem if we adjust things anyway.
    let adjustAll = forceAdjustAll ?? false;
    if (!target) {
        adjustAll = true;
    } else {
        if (getHeight(target) !== getHeight(draggable)) {
            if (!allSameSize) {
                target.style.height = `${getHeight(draggable)}px`;
            }
            adjustAll = true;
        }
        if (getWidth(target) !== getWidth(draggable)) {
            if (!allSameSize) {
                target.style.width = `${getWidth(draggable)}px`;
            }
            adjustAll = true;
        }
    }

    // Resize everything, unless that behavior is turned off.
    // Enhance: possibly we should only resize the ones that are initially the same size as the
    // target used to be? That could be useful if we ever again allow letter and word draggables
    // on the same game.
    if (adjustAll && allSameSize) {
        // We need to adjust the position of all the targets and non-image draggables other than the one we started with.
        const page = draggable.closest(".bloom-page") as HTMLElement;
        const otherDraggables: HTMLElement[] = [];
        const draggableImages: HTMLElement[] = [];
        const draggables: HTMLElement[] = Array.from(
            page.querySelectorAll("[data-draggable-id]")
        );
        draggables.forEach(x => {
            const img = getImageFromCanvasElement(x);
            // I don't think we want to increase the minimum size of targets to account
            // for images that are still placeholders.
            if (img && img.getAttribute("src") !== "placeholder.png") {
                draggableImages.push(x as HTMLElement);
            } else if (x !== draggable) {
                otherDraggables.push(x as HTMLElement);
            }
        });
        const targets: HTMLElement[] = Array.from(
            page.querySelectorAll("[data-target-of]")
        );

        let targetHeight = getHeight(draggable);
        let targetWidth = getWidth(draggable);
        if (draggableImages.length > 0) {
            targetHeight = Math.max(...draggables.map(x => getHeight(x)));
            targetWidth = Math.max(...draggables.map(x => getWidth(x)));
        }

        // from BL-14646 we decided to limit this "same size" behavior to the targets only
        // on the idea that with the grid, it's easy to get all the draggables the same size if you want,
        // but the targets can't be manipulated directly so doing this automatically makes sense.
        //otherDraggables.concat(targets).forEach((elt: HTMLElement) => {
        targets.forEach((elt: HTMLElement) => {
            if (getHeight(elt) !== targetHeight) {
                elt.style.height = `${targetHeight}px`;
            }
            if (getWidth(elt) !== targetWidth) {
                elt.style.width = `${targetWidth}px`;
            }
        });
    }
    if (!target) {
        return;
    }
    // if start and end overlap, we don't want an arrow
    if (overlap(draggable, target)) {
        if (arrow) {
            arrow.remove();
        }
        return;
    }
    arrow = makeArrowShape(draggable, target, arrow);
};

// Set the internals of the arrow SVG to make an arrow from the center of the draggable
// (but the part inside the draggable is omitted )
// to a corner or side mid-point on the the target that is closest to the center of the draggable.
const makeArrowShape = (
    draggable: HTMLElement,
    target: HTMLElement,
    arrow: SVGSVGElement
): SVGSVGElement => {
    // These values make a line from the center of the draggable to the center of the target.
    const startX = draggable.offsetLeft + draggable.offsetWidth / 2;
    const startY = draggable.offsetTop + draggable.offsetHeight / 2;
    const endXCenter = target.offsetLeft + target.offsetWidth / 2;
    const endYCenter = target.offsetTop + target.offsetHeight / 2;

    // Figure out where the tip of the arrow should be. At least one of these will be changed,
    // so that we end up at a corner or side midpoint of the target.
    // (This assumes the two don't overlap. If they do, we don't make an arrow at all.)
    let endX = endXCenter;
    let endY = endYCenter;
    if (target.offsetLeft > startX) {
        // The target is entirely to the right of the center of the draggable.
        // We will go for one of the points on the left of the target.
        endX = target.offsetLeft;
    } else if (target.offsetLeft + target.offsetWidth < startX) {
        // The target is entirely to the left of the center of the draggable.
        // We will go for one of the points on the right of the target.
        endX = target.offsetLeft + target.offsetWidth;
    }
    if (target.offsetTop > startY) {
        // The target is entirely below the center of the draggable.
        // We will go for one of the points on the top of the target.
        endY = target.offsetTop;
    } else if (target.offsetTop + target.offsetHeight < startY) {
        // The target is entirely above the center of the draggable.
        // We will go for one of points on the bottom of the target.
        endY = target.offsetTop + target.offsetHeight;
    }

    if (!arrow) {
        // Make an empty SVG that we will add lines to to make the arrow.
        arrow = draggable.ownerDocument.createElementNS(
            "http://www.w3.org/2000/svg",
            "svg"
        );
        arrow.setAttribute("id", "target-arrow");
        draggable.parentElement!.appendChild(arrow);
    }

    // But we actually want to start the arrow from the border of the draggable.
    // If the line runs through the top or bottom border:
    const yMultiplier = startY < endY ? 1 : -1;
    const deltaYTB = (draggable.offsetHeight / 2) * yMultiplier;
    const startYTB = startY + deltaYTB;
    // If it's a horizontal arrow, we won't be using this, but we need to avoid dividing by zero and get a really big number
    const deltaXTB =
        endY === startY
            ? 10000000
            : ((startYTB - startY) * (endX - startX)) / (endY - startY);
    const startXTB = startX + deltaXTB;

    // If the line runs through the left or right border:
    const xMultiplier = startX < endX ? 1 : -1;
    const deltaXLR = (draggable.offsetWidth / 2) * xMultiplier;
    const startXLR = startX + deltaXLR;
    // If it's a vertical arrow, we won't be using this, but we need to avoid dividing by zero and get a really big number
    const deltaYLR =
        endX === startX
            ? 10000000
            : ((startXLR - startX) * (endY - startY)) / (endX - startX);
    const startYLR = startY + deltaYLR;

    // We need to use the point that is closest to the center of the draggable.
    // (The real delta would require sqrt, but we can just compare delta-squared values.)
    const deltaTB = deltaXTB * deltaXTB + deltaYTB * deltaYTB;
    const deltaLR = deltaXLR * deltaXLR + deltaYLR * deltaYLR;

    // The point where we actually want the starting point of the arrow.
    const finalStartX = deltaTB < deltaLR ? startXTB : startXLR;
    const finalStartY = deltaTB < deltaLR ? startYTB : startYLR;

    const finalEndX = endX;
    const finalEndY = endY;

    const deltaX = finalEndX - finalStartX;
    const deltaY = finalEndY - finalStartY;

    let line = arrow.firstChild as SVGLineElement;
    let line2 = line?.nextSibling as SVGLineElement;
    let line3 = line2?.nextSibling as SVGLineElement;
    if (!line) {
        line = draggable.ownerDocument.createElementNS(
            "http://www.w3.org/2000/svg",
            "line"
        );
        arrow.appendChild(line);
        line2 = draggable.ownerDocument.createElementNS(
            "http://www.w3.org/2000/svg",
            "line"
        );
        arrow.appendChild(line2);
        line3 = draggable.ownerDocument.createElementNS(
            "http://www.w3.org/2000/svg",
            "line"
        );
        arrow.appendChild(line3);
    }

    const arrowheadLength = 14;
    const angle =
        deltaY === 0 ? Math.sign(deltaX) * Math.PI : Math.atan(deltaX / deltaY);
    const baseX = 0;
    const baseY = 0;
    const tipX = deltaX;
    const tipY = deltaY;

    const leftAngle = angle + Math.PI / 4;
    const rightAngle = angle - Math.PI / 4;
    const leftArrowX =
        tipX + Math.sin(leftAngle) * -arrowheadLength * Math.sign(deltaY);
    const leftArrowY =
        tipY + Math.cos(leftAngle) * -arrowheadLength * Math.sign(deltaY);
    const rightArrowX =
        tipX + Math.sin(rightAngle) * -arrowheadLength * Math.sign(deltaY);
    const rightArrowY =
        tipY + Math.cos(rightAngle) * -arrowheadLength * Math.sign(deltaY);

    line.setAttribute("x1", baseX.toString());
    line.setAttribute("y1", baseY.toString());
    line.setAttribute("x2", tipX.toString());
    line.setAttribute("y2", tipY.toString());
    line2.setAttribute("x2", tipX.toString());
    line2.setAttribute("y2", tipY.toString());
    line3.setAttribute("x2", tipX.toString());
    line3.setAttribute("y2", tipY.toString());
    line2.setAttribute("x1", leftArrowX.toString());
    line2.setAttribute("y1", leftArrowY.toString());
    line3.setAttribute("x1", rightArrowX.toString());
    line3.setAttribute("y1", rightArrowY.toString());

    // Now figure out how big the arrow is and where to put it.
    const minX = Math.min(baseX, tipX, leftArrowX, rightArrowX);
    const maxX = Math.max(baseX, tipX, leftArrowX, rightArrowX);
    const minY = Math.min(baseY, tipY, leftArrowY, rightArrowY);
    const maxY = Math.max(baseY, tipY, leftArrowY, rightArrowY);
    // Big enough to hold all the points that make up the arrow
    arrow.setAttribute("width", (maxX - minX).toString());
    arrow.setAttribute("height", (maxY - minY).toString());
    // This viewBox avoids the need to translate all the points in the lines
    arrow.setAttribute(
        "viewBox",
        `${minX} ${minY} ${maxX - minX} ${maxY - minY}`
    );
    arrow.style.left = finalStartX + minX + "px";
    arrow.style.top = finalStartY + minY + "px";
    // The arrow is "on top" of the targets, so if one of them happens to be inside the
    // rectangle that contains the arrow, without this it would not get mouse events.
    arrow.style.pointerEvents = "none";

    const color = "#80808080";
    const strokeWidth = "3";
    const lines = [line, line2, line3];
    lines.forEach(l => {
        l.setAttribute("stroke", color);
        l.setAttribute("stroke-width", strokeWidth);
    });
    arrow.style.zIndex = "1003";
    arrow.style.position = "absolute";
    return arrow;
};

let targetBeingDragged: HTMLElement;
let targetClickOffsetLeft = 0;
let targetClickOffsetTop = 0;
let targetInitialPositions: { x: number; y: number; elt: HTMLElement }[] = [];
let snappedToExisting = false;

// Handle mousedown that begins dragging a target (one of the fixed destinations when playing,
// but in the Start tab they can be moved). Saves some initial state so we can do snapping,
// and sets up the mousemove and mouseup handlers that do the actual dragging and snapping.
const startDraggingTarget = (e: MouseEvent) => {
    const canvasElementManager = getCanvasElementManager()!;
    // get the mouse cursor position at startup:
    const target = e.currentTarget as HTMLElement;
    targetBeingDragged = target;
    const page = target.closest(".bloom-page") as HTMLElement;
    const targets = Array.from(
        page.querySelectorAll("[data-target-of]")
    ) as HTMLElement[];
    const canvasElements = Array.from(
        page.querySelectorAll(kCanvasElementSelector)
    ) as HTMLElement[];
    guideProvider.startDrag("move", [...targets, ...canvasElements]);

    const scale = page.getBoundingClientRect().width / page.offsetWidth;
    targetInitialPositions = [];
    targets.forEach((elt: HTMLElement) => {
        const x = elt.offsetLeft;
        const y = elt.offsetTop;
        targetInitialPositions.push({ x, y, elt });
    });
    // Sort first by y so we can compare two adjacent ones to see if they are in a row.
    targetInitialPositions.sort((a, b) => {
        const yDelta = a.y - b.y;
        return yDelta === 0 ? a.x - b.x : yDelta;
    });

    targetClickOffsetLeft = e.clientX / scale - target.offsetLeft;
    targetClickOffsetTop = e.clientY / scale - target.offsetTop;
    page.addEventListener("mouseup", stopDraggingTarget);
    page.addEventListener("mousemove", dragTarget);
    canvasElementManager.setActiveElement(draggableOfTarget(target));
    dragTarget(e); // some side effects like drawing the arrow we want even if no movement happens.
};

const snapProvider = new CanvasSnapProvider();
const guideProvider = new CanvasGuideProvider();

const snapDelta = 30; // review: how close do we want?
const defaultGapBetweenTargets = 15;
const dragTarget = (e: MouseEvent) => {
    const page = targetBeingDragged.closest(".bloom-page") as HTMLElement;
    const scale = page.getBoundingClientRect().width / page.offsetWidth;
    e.preventDefault();
    // Where will we move the target to? If no snaps, we move it to stay in the same place
    // relative to the mouse.
    let x = e.clientX / scale - targetClickOffsetLeft;
    let y = e.clientY / scale - targetClickOffsetTop;
    let deltaMin = Number.MAX_VALUE;
    let deltaRowMin = Number.MAX_VALUE;
    const width = targetBeingDragged.offsetWidth;
    snappedToExisting = false;
    // This code tries to snap a dragged target so as to form a row with other targets at a uniform spacing,
    // including moving others over if a new one gets dragged on top of an existing one.
    // It conflicts with the new goal of making the targets snap to the grid, so we are not using it for now.
    // There's probably other code we could delete or comment out that is only needed to support this, but for now,
    // the main thing is not to have two conflicting snap behaviors.
    // for (let i = 0; i < targetInitialPositions.length; i++) {
    //     const slot = targetInitialPositions[i];
    //     const deltaX = slot.x - x;
    //     const deltaY = slot.y - y;
    //     const delta = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
    //     // It's interesting if it is dropped on top of another target(or where it started),
    //     // but only if that target is in a row.
    //     let inRow = false;
    //     if (i > 0 && targetInitialPositions[i - 1].y === slot.y) {
    //         inRow = true;
    //     }
    //     if (
    //         i < targetInitialPositions.length - 1 &&
    //         targetInitialPositions[i + 1].y === slot.y
    //     ) {
    //         inRow = true;
    //     }
    //     if (inRow && delta < deltaMin) {
    //         deltaMin = delta;
    //         if (delta < snapDelta) {
    //             x = slot.x;
    //             y = slot.y;
    //             snappedToExisting = true;
    //         }
    //     }
    //     // It's also interesting if it is dropped to the right of another target
    //     // Todo: possibly also if it is below another one?
    //     // Right of it's own start position, which is likely in range initially, is not interesting.
    //     if (slot.elt === targetBeingDragged) {
    //         continue;
    //     }
    //     // By default look at a position a bit to the right of the current target.
    //     let spacing = width + defaultGapBetweenTargets;
    //     if (inRow) {
    //         // if the current target is in a row, look at a position the same distance to the right as the row spacing.
    //         const row = targetInitialPositions.filter(s => s.y === slot.y);
    //         spacing = row[row.length - 1].x - row[row.length - 2].x;
    //     }
    //     const deltaXRow = slot.x + spacing - x;
    //     const deltaRow = Math.sqrt(deltaXRow * deltaXRow + deltaY * deltaY);
    //     if (deltaRow < deltaRowMin) {
    //         deltaRowMin = deltaRow;
    //         // For a "to the right of" position to be interesting, it must be closer to that
    //         // position than to any other target
    //         if (deltaRow < snapDelta && deltaRow < deltaMin) {
    //             if (inRow) {
    //                 // If there isn't already a row, we'd only be guessing at spacing
    //                 // so don't snap in that direction.
    //                 x = slot.x + spacing;
    //             }
    //             y = slot.y;
    //             snappedToExisting = false;
    //         }
    //     }
    // }

    const snappedPoint = snapProvider.getPosition(e, x, y);
    x = snappedPoint.x;
    y = snappedPoint.y;

    targetBeingDragged.style.top = y + "px";
    targetBeingDragged.style.left = x + "px";

    const draggable = draggableOfTarget(targetBeingDragged);
    if (draggable) {
        adjustTarget(draggable, targetBeingDragged);
    }
    guideProvider.duringDrag(targetBeingDragged);
};

const draggableOfTarget = (
    target: HTMLElement | undefined
): HTMLElement | undefined => {
    if (!target) {
        return undefined;
    }
    const targetId = target.getAttribute("data-target-of");
    if (!targetId) {
        return undefined;
    }
    return target.ownerDocument.querySelector(
        `[data-draggable-id="${targetId}"]`
    ) as HTMLElement;
};

const stopDraggingTarget = (_: MouseEvent) => {
    snapProvider.endDrag();
    guideProvider.endDrag();
    const page = targetBeingDragged.closest(".bloom-page") as HTMLElement;
    if (snappedToExisting) {
        // Move things around so we end up with an evenly spaced row again.
        const row = targetInitialPositions.filter(
            s => s.y === targetBeingDragged.offsetTop
        );
        const indexDroppedOn = row.findIndex(
            s => s.x === targetBeingDragged.offsetLeft
        );
        const indexDragged = row.findIndex(s => s.elt === targetBeingDragged);
        if (indexDragged !== indexDroppedOn) {
            // if equal, we didn't really move it at all.
            const spacing =
                row.length >= 2
                    ? row[row.length - 1].x - row[row.length - 2].x
                    : // We dropped on another target that's not in a row. Create a row.
                      targetBeingDragged.offsetWidth + defaultGapBetweenTargets;
            if (indexDragged < 0) {
                // Not in the row previously, move others over.
                for (let i = row.length - 1; i >= indexDroppedOn; i--) {
                    row[i].elt.style.left = `${row[i].x + spacing}px`;
                }
            } else if (indexDroppedOn < indexDragged) {
                // Move others right.
                for (let i = indexDragged - 1; i >= indexDroppedOn; i--) {
                    row[i].elt.style.left = `${row[i].x + spacing}px`;
                }
            } else {
                // Move others left.
                for (let i = indexDragged + 1; i <= indexDroppedOn; i++) {
                    row[i].elt.style.left = `${row[i].x - spacing}px`;
                }
            }
        }
    }

    page.removeEventListener("mouseup", stopDraggingTarget);
    page.removeEventListener("mousemove", dragTarget);
};

const startTabIndex = 0;
const correctTabIndex = 1;
const wrongTabIndex = 2;
const playTabIndex = 3;

// Set a class that CSS rules can use to modify the appearance of elements based on which
// tab we are in.
// The class is put on the parent element of the page. This is better than putting it on
// the page itself, because we don't want it saved. It's better than putting it on the body,
// because that doesn't work in Bloom Player due to the way we polyfill scoped styles.
const updateTabClass = (tabIndex: number) => {
    const pageBody = ToolBox.getPage();
    const page = pageBody?.getElementsByClassName(
        "bloom-page"
    )[0] as HTMLElement;
    if (!page) {
        // try again in a bit (this might happen if the toolbox iframe loads faster than the page iframe)
        setTimeout(() => {
            updateTabClass(tabIndex);
        }, 100);
        return;
    }
    const classes = [
        "drag-activity-start",
        "drag-activity-correct", // in correct tab, or in Play after verifying answer is correct
        "drag-activity-wrong", // in wrong tab, or in Play after finding answer is wrong
        "drag-activity-play", // when in Play mode, either Play tab or Bloom Player.
        // doesn't have a tab, but used in Play when showing the correct answer.
        "drag-activity-solution"
    ];
    for (let i = 0; i < classes.length; i++) {
        const className = classes[i];
        classSetter(page, className, i === tabIndex);
    }
};

const getPage = () => {
    const pageBody = ToolBox.getPage();
    return pageBody?.getElementsByClassName("bloom-page")[0] as HTMLElement;
};

const getSoundFilesAsync = async (prefix: string): Promise<string[]> => {
    const result = await getWithPromise(
        `fileIO/listFiles?subPath=sounds&match=${prefix}_*`
    );
    if (!result || !result.data) {
        return []; // huh?
    }
    return result.data.files as string[];
};

const getSoundOptions = (
    prefix: string,
    files: string[],
    currentId: string,
    noneLabel: string,
    chooseLabel: string
): { label: string; id: string; divider: boolean }[] => {
    const soundOptions = [{ label: noneLabel, id: "none", divider: false }];
    const idToLabel = label =>
        label
            .replace(new RegExp(`^${prefix}_`), "") // don't use substring, for own sounds prefix might not be found
            .replace(/\.mp3$/i, "")
            .replace(/\.webm/i, "")
            .replace("_", " ")
            .replace("-", " ")
            .replace(/ pixabay/i, "");

    files.forEach(file => {
        soundOptions.push({ label: idToLabel(file), id: file, divider: false });
    });
    soundOptions[soundOptions.length - 1].divider = true;

    soundOptions.push({ label: chooseLabel, id: "choose", divider: false });

    if (
        currentId !== "none" &&
        !soundOptions.find(opt => opt.id === currentId)
    ) {
        soundOptions.splice(0, 0, {
            label: idToLabel(currentId),
            id: currentId,
            divider: false
        });
    }
    return soundOptions;
};

export let soundFolder: string;
export const setSoundFolder = (folder: string) => {
    soundFolder = folder;
};

export const copyAndPlaySoundAsync = async (
    newSoundId: string,
    page: HTMLElement,
    copyBuiltIn: boolean
) => {
    if (copyBuiltIn) {
        await copyBuiltInSoundAsync(newSoundId);
    }
    playSound(newSoundId, page);
};

const copyBuiltInSoundAsync = async (newSoundId: string) => {
    const resultAudioDir = await postJson(
        "fileIO/getSpecialLocation",
        "CurrentBookAudioDirectory"
    );

    if (!resultAudioDir) {
        return; // huh??
    }

    const targetPath = resultAudioDir.data + "/" + newSoundId;
    await postData("fileIO/copyFile", {
        from: encodeURIComponent(newSoundId),
        to: encodeURIComponent(targetPath)
    });
};

export const showDialogToChooseSoundFileAsync = async () => {
    const title = await theOneLocalizationManager.asyncGetText(
        "EditTab.Toolbox.DragActivity.ChooseSoundFile",
        "Choose Sound File",
        ""
    );
    const result = await postJson("fileIO/chooseFile", {
        // Enhance: use something with a callback that can't timeout
        title,
        fileTypes: [
            {
                name: "MP3",
                extensions: ["mp3"]
            }
        ],
        defaultPath: soundFolder,
        destFolder: "audio"
    });
    if (result?.data) {
        setSoundFolder(result?.data);
    }
    return result?.data;
};

// The core of the Game tool. (a good many classes and function names reflect its original
// name, Drag Activity Tool))
const DragActivityControls: React.FunctionComponent<{
    activeTab: number;
    pageGeneration: number; // incremented when the page changes
}> = props => {
    // The sound files for correct and wrong answers, determined by attributes of the page.
    const [correctSoundId, setCorrectSoundId] = useState("");
    const [wrongSoundId, setWrongSoundId] = useState("");

    // The core type of activity of the current page, from the data-activity attribute.
    const [activityType, setActivityType] = useState<string>("");

    // Option values, stored in page attributes
    const [allItemsSameSize, setAllItemsSameSize] = useState(true);
    const [showTargetsDuringPlay, setShowTargetsDuringPlay] = useState(true);
    const [showAnswersInTargets, setShowAnswersInTargets] = useState(false);

    // Observer to copy changes to the current canvas element to its target when showAnswersInTargets is true.
    const draggableToTargetObserver = React.useRef<MutationObserver | null>(
        null
    );

    // Menu item names for 'none' and "Choose...", options in both the correct and wrong sound menus.
    const noneLabel = useL10n("None", "EditTab.Toolbox.DragActivity.None", "");
    const chooseLabel = useL10n(
        "Choose...",
        "EditTab.Toolbox.DragActivity.ChooseSound",
        ""
    );

    const canvasElementManager = getCanvasElementManager()!;
    let currentCanvasElement = canvasElementManager?.getActiveElement();
    // Currently we mainly use this to decide whether to show the delete and duplicate buttons.
    // Maybe those are obsolete now we have the new toolbox?
    // In any case, we don't want to duplicate or delete a background image, so it doesn't count.
    if (currentCanvasElement?.classList.contains(kBackgroundImageClass)) {
        currentCanvasElement = undefined;
    }
    const currentCanvasElementTargetId = currentCanvasElement?.getAttribute(
        "data-draggable-id"
    );
    const [currentDraggableTarget, setCurrentDraggableTarget] = useState<
        HTMLElement | undefined
    >();
    const [_, setBubble] = useState<BubbleSpec | undefined>(undefined);
    useEffect(() => {
        // The only use of this is to force a re-render when the canvas element changes.
        // We will get a different result from getActiveElement, and various things may need
        // to update as a result.
        // For better or worse, I'm currently not bothering to detach from these
        // requests. I'm not entirely sure we we don't need the notifications, even if the tool is
        // hidden, at least to make sure things are right if it is shown again.
        // At worst, CanvasElementManager is a singleton, and we only allow one
        // requester with the key dragActivityTool, so it won't be a large leak.
        canvasElementManager?.requestCanvasElementChangeNotification(
            "dragActivityTool",
            b => setBubble(b)
        );
    }, [props.pageGeneration, canvasElementManager]);
    useEffect(() => {
        if (!currentCanvasElementTargetId) {
            setCurrentDraggableTarget(undefined);
            return;
        }
        const page = getPage();
        setCurrentDraggableTarget(
            page?.querySelector(
                `[data-target-of="${currentCanvasElementTargetId}"]`
            ) as HTMLElement
        );
        // We need to re-evaluate when changing pages, it's possible the initially selected item
        // on a new page has the same currentDraggableTargetId.
    }, [props.pageGeneration, currentCanvasElementTargetId]);
    // The main point of this is to make the visibility of the arrow consistent with whether
    // a draggable is actually selected when changing pages. As far as I know, we don't need to do it when the
    // draggable or target change otherwise...other code handles target adjustment for those changes...
    // but it's fairly harmless to do it an extra time, and makes lint happy, and maybe will
    // catch some inconsistency that would otherwise be missed.
    useEffect(() => {
        // careful here. After a change to currentDraggableElement, there will unfortunately be a render
        // before the useEffect above runs and sets currentDraggableTarget. We don't want to
        // adjust the old target to conform to the new canvas element.
        // (It's harmless to adjust it passing undefined as the target and then again with
        // the correct target, and preventing it would require searching the whole page for
        // a matching target, so we don't try to prevent that.)
        if (currentCanvasElement) {
            if (
                !currentDraggableTarget ||
                currentDraggableTarget.getAttribute("data-target-of") ===
                    currentCanvasElement.getAttribute("data-draggable-id")
            ) {
                adjustTarget(currentCanvasElement, currentDraggableTarget);
            }
        } else {
            const page = getPage();
            const arrow = page?.querySelector("#target-arrow") as HTMLElement;
            if (arrow) {
                arrow.remove();
            }
        }
    }, [currentCanvasElement, currentDraggableTarget, props.pageGeneration]);
    // If applicable, set up an observer to copy changes to the current canvas element to its target,
    // whenever the target (or other relevant factors) changes. I don't think there's any
    // way an unselected canvas element can change in a way that requires the target to do so.
    // (For example, a style change might affect it, but would have the same effect on the target.)
    // We don't need this when the active tab is the play tab, because the content of the
    // draggable can't change, and also, we don't want to copy the content to the target
    // if in that mode unless showAnswersInTargets is true.
    useEffect(() => {
        if (draggableToTargetObserver.current) {
            draggableToTargetObserver.current.disconnect();
            draggableToTargetObserver.current = null;
        }
        if (
            props.activeTab !== playTabIndex &&
            currentCanvasElement &&
            currentDraggableTarget
        ) {
            draggableToTargetObserver.current = new MutationObserver(_ => {
                // if it's no longer current, we just haven't removed the observer yet,
                // don't do it.
                if (
                    currentCanvasElement ===
                    getCanvasElementManager()?.getActiveElement()
                ) {
                    copyContentToTarget(currentCanvasElement);
                }
            });
            draggableToTargetObserver.current.observe(currentCanvasElement, {
                childList: true,
                subtree: true,
                attributes: true // e.g., cropping of image
            });
        }
    }, [currentCanvasElement, currentDraggableTarget, props.activeTab]);
    // Get various state values from the current page, initially and whenever it changes.
    useEffect(() => {
        const getStateFromPage = () => {
            const pageBody = ToolBox.getPage();
            const page = pageBody?.getElementsByClassName(
                "bloom-page"
            )[0] as HTMLElement;
            if (!page) {
                // Hopefully the only way this happens is if the toolbox iframe loads faster than the page iframe.
                setTimeout(() => {
                    getStateFromPage();
                }, 100);
                return;
            }

            setAllItemsSameSize(doesContainingPageHaveSameSizeMode(page));
            setShowTargetsDuringPlay(
                page.getAttribute("data-show-targets-during-play") !== "false"
            );
            setShowAnswersInTargets(
                page.getAttribute("data-show-answers-in-targets") === "true"
            );
            setCorrectSoundId(
                page.getAttribute("data-correct-sound") || "none"
            );
            setWrongSoundId(page.getAttribute("data-wrong-sound") || "none");
            setActivityType(page.getAttribute("data-activity") ?? "");
        };
        getStateFromPage();
    }, [props.pageGeneration]);

    const updateSoundShowingDialog = async (soundType: SoundType) => {
        const newSoundId = await showDialogToChooseSoundFileAsync();
        if (!newSoundId) {
            return;
        }

        const page = getPage();
        const copyBuiltIn = false; // already copied, and not in our sounds folder
        setSound(soundType, newSoundId, copyBuiltIn);
        playSound(newSoundId, page);
    };

    // There's a lot of async stuff here, and a lot of arguments passed. Some of the argument-passing
    // could be avoided by making getSoundFilesAsync and/or getSoundOptionsAsync local functions.
    // My goal in breaking things up like this was that we clearly only need to do the server queries
    // (to localize None and Choose and get the lists of files) once each, while making it very clear
    // when the list of options must be recomputed.
    const [correctFiles, setCorrectFiles] = useState<string[]>([]);
    const [wrongFiles, setWrongFiles] = useState<string[]>([]);
    useEffect(() => {
        getSoundFilesAsync("correct").then(setCorrectFiles);
        getSoundFilesAsync("wrong").then(setWrongFiles);
    }, []);

    const correctSoundOptions = useMemo(
        () =>
            getSoundOptions(
                "correct",
                correctFiles,
                correctSoundId,
                noneLabel,
                chooseLabel
            ),
        [correctFiles, correctSoundId, noneLabel, chooseLabel]
    );
    const wrongSoundOptions = useMemo(
        () =>
            getSoundOptions(
                "wrong",
                wrongFiles,
                wrongSoundId,
                noneLabel,
                chooseLabel
            ),
        [wrongFiles, wrongSoundId, noneLabel, chooseLabel]
    );

    // const [dragObjectType, setDragObjectType] = useState("text");
    // Todo: something has to call setDragObjectType when a draggable is selected.
    // Not needed until we implement the order circle game.
    // let titleId = "EditTab.Toolbox.DragActivity.Draggable";
    // if (dragObjectType === "dragTarget") {
    //     titleId = "EditTab.Toolbox.DragActivity.DraggableTarget";
    // } else if (dragObjectType === "orderCircle") {
    //     titleId = "EditTab.Toolbox.DragActivity.OrderCircle";
    // }

    const onSoundItemChosen = (soundType: SoundType, newSoundId: string) => {
        if (newSoundId === "choose") {
            updateSoundShowingDialog(soundType);
            return;
        }
        if (
            (newSoundId === correctSoundId && soundType === "correct") ||
            (newSoundId === wrongSoundId && soundType === "wrong")
        ) {
            // Nothing is changing; also, we don't want to try to copy the sound file again, especially if it
            // is a user-chosen one that we won't find in our sounds folder.
            return;
        }
        const copyBuiltIn = true; // built-in sound needs to be copied to the book's audio folder
        setSound(soundType, newSoundId, copyBuiltIn);
    };
    const setSound = (
        soundType: SoundType,
        newSoundId: string,
        copyBuiltIn: boolean
    ) => {
        const page = getPage();
        switch (soundType) {
            case "correct":
                setCorrectSoundId(newSoundId);
                if (newSoundId === "none") {
                    page.removeAttribute("data-correct-sound");
                } else {
                    page.setAttribute("data-correct-sound", newSoundId);
                }
                break;
            case "wrong":
                setWrongSoundId(newSoundId);
                if (newSoundId === "none") {
                    page.removeAttribute("data-wrong-sound");
                } else {
                    page.setAttribute("data-wrong-sound", newSoundId);
                }
                break;
        }
        if (newSoundId !== "none") {
            // I think this can be fire-and-forget. But if you add something else that
            // needs the file to be there,you should await this, or add it to copyAndPlaySound.
            copyAndPlaySoundAsync(newSoundId, page, copyBuiltIn);
        }
    };

    const toggleAllSameSize = () => {
        const newAllSameSize = !allItemsSameSize;
        setAllItemsSameSize(newAllSameSize);
        const page = getPage();
        page.setAttribute("data-same-size", newAllSameSize ? "true" : "false");
        if (newAllSameSize) {
            let someDraggable = getCanvasElementManager()!.getActiveElement(); // prefer the selected one
            if (
                !someDraggable ||
                !someDraggable.getAttribute("data-draggable-id")
            ) {
                // find something
                someDraggable = page.querySelector(
                    "[data-draggable-id]"
                ) as HTMLElement;
            }
            if (!someDraggable) {
                return;
            }
            adjustTarget(someDraggable, getTarget(someDraggable), true);
        } else {
            // No longer all same size, so each target should match the size of its own draggable.
            // When images are involved, because of matching canvas element aspect ratio to image, it
            // isn't workable to make all the draggables the same size, so turning this off can
            // actually make a difference to image targets that previously matched the largest
            // draggable in each dimension.
            page.querySelectorAll(
                "[data-target-of] " + kImageContainerSelector
            ).forEach((ic: HTMLElement) => {
                const target = ic.closest("[data-target-of]") as HTMLElement;
                target.style.width = ic.style.width;
                target.style.height = ic.style.height;
            });
        }
    };

    const toggleShowAnswersInTargets = () => {
        const newShowAnswersInTargets = !showAnswersInTargets;
        // This will trigger a new render that sets up the observer if needed.
        setShowAnswersInTargets(newShowAnswersInTargets);
        const page = getPage();
        page.setAttribute(
            "data-show-answers-in-targets",
            newShowAnswersInTargets ? "true" : "false"
        );
        // Don't actually change it. Answers always show in Start mode.
    };

    const toggleShowTargetsDuringPlay = () => {
        const newShowTargetsDuringPlay = !showTargetsDuringPlay;
        setShowTargetsDuringPlay(newShowTargetsDuringPlay);
        const page = getPage();
        page.setAttribute(
            "data-show-targets-during-play",
            newShowTargetsDuringPlay ? "true" : "false"
        );
    };

    // Does this activity type have items that should be dragged to a specific location during play?
    // Sentence sorting actually does involve dragging things, but not items that the author can add manually;
    // they are created at play time. So it is not listed here.
    // It ought to be possible to make this a const and assign the result of the or, but somehow
    // doing so confuses typescript about the type of activityType, and we get spurious errors.
    let anyDraggables = false;
    if (
        activityType === "drag-letter-to-target" ||
        activityType === "drag-image-to-target" ||
        // anticipating
        activityType === "drag-to-destination"
    ) {
        anyDraggables = true;
    }
    let anyFixedInPlace = anyDraggables;
    if (activityType === "drag-sort-sentence") {
        anyFixedInPlace = true;
    }

    // Does this activity type have a row of options buttons in Start mode?
    const anyOptions = anyDraggables; // but they might diverge as we do more?
    // which controls to show?
    const showLetterDraggable =
        activityType !== "drag-word-chooser-slider" &&
        activityType !== "drag-image-to-target";
    const showWordDraggable =
        activityType !== "drag-word-chooser-slider" &&
        activityType !== "drag-letter-to-target"; //&&
    // For now we'll show for this game also. We probably want to add a generic game
    // that shows all the options and go back to hiding it for this.
    //activityType !== "drag-image-to-target";
    const showImageDraggable = activityType !== "drag-letter-to-target";
    const showVideoDraggable = true;
    const showSoundDraggable = activityType !== "drag-letter-to-target";
    return (
        <ThemeProvider theme={toolboxTheme}>
            {props.activeTab === startTabIndex && (
                <div>
                    {anyDraggables && (
                        <CanvasElementItemRegion
                            l10nKey="EditTab.Toolbox.DragActivity.Draggable"
                            theme="blueOnTan"
                        >
                            <CanvasElementItemRow>
                                {showLetterDraggable && (
                                    <CanvasElementTextItem
                                        css={draggableWordCss}
                                        l10nKey="EditTab.Toolbox.DragActivity.Letter"
                                        makeTarget={true}
                                        addClasses="draggable-text bloom-noAutoHeight"
                                        userDefinedStyleName="GameDragMediumCenter"
                                    />
                                )}
                                {showImageDraggable && (
                                    <Fragment>
                                        <CanvasElementImageItem
                                            makeTarget={
                                                activityType !==
                                                "drag-word-chooser-slider"
                                            }
                                            makeMatchingTextBox={
                                                activityType ===
                                                "drag-word-chooser-slider"
                                            }
                                            color={kBloomBlue}
                                            strokeColor={kBloomBlue}
                                            showOuterRectangle={true}
                                        />
                                        {showVideoDraggable && (
                                            <CanvasElementVideoItem
                                                makeTarget={
                                                    activityType !==
                                                    "drag-word-chooser-slider"
                                                }
                                                showOuterRectangle={true}
                                            />
                                        )}
                                    </Fragment>
                                )}
                            </CanvasElementItemRow>
                            <CanvasElementItemRow>
                                {showSoundDraggable && (
                                    <CanvasElementSoundItem />
                                )}
                                <CanvasElementTextItem
                                    css={draggableWordCss}
                                    l10nKey="EditTab.Toolbox.DragActivity.Word"
                                    makeTarget={true}
                                    addClasses="draggable-text bloom-noAutoHeight"
                                    hide={!showWordDraggable}
                                    userDefinedStyleName="GameDragMediumCenter"
                                />

                                {/* Slider: rather than reinstating this item, make the "selected item is part of answer" control work.
                                // Keeping this just as a reminder of what it might take to make that work.
                                 {activityType === "drag-word-chooser-slider" && (
                                    <CanvasElementWrongImageItem
                                        style="image"
                                        makeTarget={false}
                                        makeMatchingTextBox={false}
                                        color={kBloomBlue}
                                        strokeColor={kBloomBlue}
                                        // without this it won't be initially visible
                                        addClasses="bloom-activePicture"
                                        extraAction={bubble =>
                                            bubble.setAttribute(
                                                "data-img-txt",
                                                "wrong"
                                            )
                                        }
                                    />
                                )} */}
                            </CanvasElementItemRow>
                            {/* If we want this at all, it would only be in the drag-sort-sentence activity
                            <CanvasElementTextItem
                                css={textItemProps}
                                l10nKey="EditTab.Toolbox.DragActivity.OrderSentence"
                                style="none"
                                draggable={false}
                                addClasses="drag-item-order-sentence"
                            />
                        </CanvasElementItemRow> */}
                            {anyFixedInPlace && (
                                <div
                                    css={css`
                                        border-top: 3px solid ${kBloomBlue};
                                        opacity: 0.15;
                                        width: 100%;
                                        margin-top: 10px;
                                        //background-color: white;
                                        //margin: 0 10px 0 10px;
                                    `}
                                ></div>
                            )}
                        </CanvasElementItemRegion>
                    )}

                    {anyFixedInPlace && (
                        <CanvasElementItemRegion
                            // Items in this region are draggable in Start mode, but not in Play mode.
                            l10nKey="EditTab.Toolbox.DragActivity.FixedInPlace"
                            theme="blueOnTan"
                        >
                            <CanvasElementItemRow>
                                <CanvasElementImageItem
                                    makeTarget={false}
                                    color={kBloomBlue}
                                    strokeColor={kBloomBlue}
                                />
                                <CanvasElementRectangleItem />
                                <CanvasElementVideoItem />
                            </CanvasElementItemRow>
                            <CanvasElementItemRow>
                                <CanvasElementGifItem />
                                <GameTextItem />
                            </CanvasElementItemRow>
                        </CanvasElementItemRegion>
                    )}
                </div>
            )}
            {props.activeTab === startTabIndex && (
                <div
                    css={css`
                        margin-left: 10px;
                    `}
                >
                    <GameIntroText
                        gameType={getGameType(activityType, getPage())}
                    />
                    <Div
                        css={css`
                            margin-top: 10px;
                            margin-bottom: 5px;
                            margin-right: 10px;
                        `}
                        l10nKey="EditTab.Toolbox.Games.Theme"
                    ></Div>
                    <ThemeChooser pageGeneration={props.pageGeneration} />
                    {anyOptions && (
                        <Div
                            css={css`
                                margin-top: 10px;
                            `}
                            l10nKey="EditTab.Toolbox.DragActivity.Options"
                        ></Div>
                    )}
                    {anyOptions && (
                        <div
                            css={css`
                                display: flex;
                                margin-top: 5px;
                            `}
                        >
                            <BloomTooltip
                                id="sameSize"
                                placement="top-end"
                                tip={
                                    <Div l10nKey="EditTab.Toolbox.DragActivity.TargetsSameSize"></Div>
                                }
                            >
                                <div
                                    css={css`
                                        ${optionCss(allItemsSameSize)}
                                    `}
                                    onClick={toggleAllSameSize}
                                >
                                    <img src="images/uniform sized targets.svg"></img>
                                </div>
                            </BloomTooltip>
                            <BloomTooltip
                                id="sameSize"
                                placement="top-end"
                                tip={
                                    <Div l10nKey="EditTab.Toolbox.DragActivity.ShowTargetsPlay"></Div>
                                }
                            >
                                <div
                                    css={css`
                                        ${optionCss(showTargetsDuringPlay)}
                                    `}
                                    onClick={toggleShowTargetsDuringPlay}
                                >
                                    <img src="images/Show Targets During Play.svg"></img>
                                </div>
                            </BloomTooltip>

                            <BloomTooltip
                                id="showAnswersInTargets"
                                placement="top"
                                tip={
                                    <Div l10nKey="EditTab.Toolbox.DragActivity.ShowAnswersInTargets"></Div>
                                }
                            >
                                <div
                                    css={css`
                                        ${optionCss(showAnswersInTargets)}
                                    `}
                                    onClick={toggleShowAnswersInTargets}
                                >
                                    <img src="images/Show answers on targets.svg"></img>
                                </div>
                            </BloomTooltip>
                        </div>
                    )}
                </div>
            )}

            {props.activeTab === correctTabIndex && (
                <CorrectWrongControls
                    soundType="correct"
                    instructionsL10nKey="EditTab.Toolbox.DragActivity.CorrectInstructions"
                    whenTheAnswerIsSubKey="WhenCorrect"
                    classToAddToItems="drag-item-correct"
                    soundOptions={correctSoundOptions}
                    currentSound={correctSoundId}
                    onSoundItemChosen={onSoundItemChosen}
                />
            )}

            {// At one point, we had extra controls in the Wrong tab to add Try Again and Show Answer buttons,
            // but decided to build those in.
            props.activeTab === wrongTabIndex && (
                <CorrectWrongControls
                    soundType="wrong"
                    instructionsL10nKey="EditTab.Toolbox.DragActivity.WrongInstructions"
                    whenTheAnswerIsSubKey="WhenWrong"
                    classToAddToItems="drag-item-wrong"
                    soundOptions={wrongSoundOptions}
                    currentSound={wrongSoundId}
                    onSoundItemChosen={onSoundItemChosen}
                />
            )}
            {props.activeTab === playTabIndex && (
                <div>
                    <Div
                        css={css`
                            margin-top: 5px;
                            margin-left: 5px;
                        `}
                        l10nKey="EditTab.Toolbox.DragActivity.TestInstructions"
                    />
                </div>
            )}
        </ThemeProvider>
    );
};

const GameTextItem: React.FunctionComponent<{
    addClasses?: string;
}> = props => {
    // We don't want game text items to autosize, so we add this class to them. (BL-14779)
    let classesToAdd = props.addClasses ?? "";
    if (!classesToAdd.includes("bloom-noAutoHeight")) {
        classesToAdd = classesToAdd + " bloom-noAutoHeight";
    }
    return (
        <CanvasElementTextItem
            css={textItemCss("14pt")}
            l10nKey="EditTab.Toolbox.DragActivity.Text"
            makeTarget={false}
            addClasses={classesToAdd.trim()}
            userDefinedStyleName="GameTextMediumCenter"
        />
    );
};

function textItemCss(
    fontSize: string = "larger",
    darkBackground: boolean = false,
    radius: string = "0"
) {
    return css`
        margin-left: 5px;
        text-align: center; // Center the text horizontally
        padding: 5px 0.5em;
        vertical-align: middle;
        color: ${darkBackground ? "white" : kBloomBlue};
        background-color: ${darkBackground ? kBloomBlue : "white"};
        border-radius: ${radius};
        font-size: ${fontSize};
    `;
}

const draggableWordCss = textItemCss("20px", true, "5px");

const playAudioCss = css`
    margin-left: 10px;
    margin-top: 10px;
`;

const CorrectWrongControls: React.FunctionComponent<{
    soundType: SoundType;
    instructionsL10nKey: string;
    whenTheAnswerIsSubKey: string;
    classToAddToItems: string;
    soundOptions: { label: string; id: string; divider: boolean }[];
    currentSound: string;
    onSoundItemChosen: (soundType: SoundType, value: string) => void;
}> = props => {
    return (
        <div>
            <CanvasElementItemRegion theme="blueOnTan" l10nKey="">
                <CanvasElementItemRow>
                    <CanvasElementImageItem
                        makeTarget={false}
                        addClasses={props.classToAddToItems}
                        color={kBloomBlue}
                        strokeColor={kBloomBlue}
                    />
                    <CanvasElementVideoItem
                        addClasses={props.classToAddToItems}
                    />
                </CanvasElementItemRow>
                <CanvasElementItemRow>
                    <CanvasElementGifItem
                        addClasses={props.classToAddToItems}
                    />
                    <CanvasElementRectangleItem
                        addClasses={props.classToAddToItems}
                    />
                </CanvasElementItemRow>
                <CanvasElementItemRow>
                    <GameTextItem addClasses={props.classToAddToItems} />
                </CanvasElementItemRow>
            </CanvasElementItemRegion>
            <div
                css={css`
                    margin: 0px 10px;
                `}
            >
                <Instructions l10nKey={props.instructionsL10nKey} />
            </div>
            <div css={playAudioCss}>
                <Div
                    l10nKey={
                        "EditTab.Toolbox.DragActivity." +
                        props.whenTheAnswerIsSubKey
                    }
                />
                <Div
                    css={css`
                        margin-top: 10px;
                    `}
                    l10nKey="EditTab.Toolbox.DragActivity.PlayAudio"
                />

                {soundSelect(
                    props.soundType,
                    props.soundOptions,
                    props.currentSound,
                    props.onSoundItemChosen
                )}
            </div>
        </div>
    );
};

export const makeDuplicateOfDragBubble = () => {
    const canvasElementManager = getCanvasElementManager();
    const old = canvasElementManager?.getActiveElement();
    const duplicate = canvasElementManager?.duplicateCanvasElement();
    if (!duplicate || !old) {
        // can't be duplicate without an old, but make TS happy
        return;
    }
    const oldTarget = getTarget(old);
    if (old.getAttribute("data-draggable-id")) {
        const oldAttributes = old.attributes;
        for (let i = 0; i < oldAttributes.length; i++) {
            const attr = oldAttributes[i];
            if (attr.name !== "style") {
                duplicate.setAttribute(attr.name, attr.value);
            }
        }
        setGeneratedDraggableId(duplicate);

        // Duplicate had better not be locked to contain the same text!
        Array.from(duplicate.querySelectorAll("[data-book]")).forEach(e =>
            e.removeAttribute("data-book")
        );

        if (oldTarget) {
            // Review: can we do anything smart about where to put it?
            // The same offset from duplicate as oldTarget has from old MIGHT be somewhat helpful,
            // though probably not right (e.g., if targets are in a row), and possibly off-the page.
            // We could try to detect that targets are in a row...but they may not be, and where in
            // the row does the new one belong? I think just leaving it in a position we've already
            // tried to make at least safe is the best we can do.
            makeTargetForDraggable(duplicate);
        }
    }
};

// Three kinds of sound we can set with a dropdown.
// Temporarily we also allowed "image", so it was not a boolean
export type SoundType = "correct" | "wrong";

// Make a <Select> for choosing a sound file. The arguments allow reusing this for various sounds;
// the "which" argument is only used to pass to the setValue function.
export const soundSelect = (
    soundType: SoundType,
    options: { label: string; id: string; divider: boolean }[],
    value: string,
    setValue: (soundType: SoundType, value: string) => void
) => {
    return (
        <Select
            css={css`
                svg.MuiSvgIcon-root {
                    color: white !important;
                }
                ul {
                    background-color: ${kOptionPanelBackgroundColor} !important;
                }
                fieldset {
                    border-color: rgba(255, 255, 255, 0.5) !important;
                }
            `}
            size="small"
            value={value}
            sx={{
                width: 170
            }}
            MenuProps={{ className: "sound-select-dropdown-menu" }}
            // Something like this ought to work but doesn't; the rules don't take effect.
            // so there are some rules in toolbox.less activated by the class above
            // to do it.
            // If reinstating this, note that I've used extreme colors here for testing;
            // once it works, switch to the right ones from toolbox.less.
            // Note that unless you get the zIndex rule to take effect, nothing else matters:
            // the pop-up menu won't be visible at all.
            // MenuProps={{
            //     sx: {
            //         "& .MuiPopover-root": {
            //             zIndex: "18001 !important"
            //         },
            //         "& .MuiMenu-paper": {
            //             zIndex: "18001 !important",
            //             backgroundColor: "red",
            //             color: "white"
            //         },
            //         "& .MuiMenu-root": {
            //             zIndex: "18001 !important"
            //         },
            //         "& .MuiMenuItem-root:hover": {
            //             backgroundColor: "blue",
            //             color: "text.white"
            //         },
            //         "& .Mui-selected": {
            //             backgroundColor: "yellow",
            //             color: "text.white"
            //         }
            //     }
            // }}
            onChange={event => {
                const newSoundId = event.target.value as string;
                setValue(soundType, newSoundId);
            }}
            disabled={false}
        >
            {options.map(option => (
                <MenuItem
                    value={option.id}
                    key={option.id}
                    disabled={false}
                    divider={option.divider}
                >
                    {option.label}
                </MenuItem>
            ))}
        </Select>
    );
};

// Function to produce the CSS for an option button.
const optionCss = turnedOn => `background-color: ${
    turnedOn ? kBloomBlue : "transparent"
};
padding: 6px;
border-radius: 3px;
height: 30px;
width: 30px;
margin-right: 6px;
img {
    height: 100%;
    width: 100%;
}
}`;

export class GameTool extends ToolboxToolReactAdaptor {
    public static theOneDragActivityTool: GameTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        GameTool.theOneDragActivityTool = this;
    }

    public setActiveTab(tab: number) {
        this.tab = tab;
        this.renderRoot();
    }

    // Activating the tool calls this right before newPageReady().
    // Currently the latter does this.renderRoot(), so we don't need to do it here.
    // public showTool() {
    //     this.renderRoot();
    // }

    // This tool should only be offered if the page has a matching data-tool-id attribute.
    public requiresToolId(): boolean {
        return true;
    }

    private root: HTMLDivElement | undefined;
    private tab = 0;

    public makeRootElement(): HTMLDivElement {
        this.root = document.createElement("div") as HTMLDivElement;
        //root.setAttribute("class", "DragActivityBody");

        this.renderRoot();
        return this.root;
    }

    private pageGeneration = 0;

    private renderRoot(): void {
        if (!this.root) return;
        this.pageGeneration++;
        ReactDOM.render(
            <DragActivityControls
                activeTab={this.tab}
                pageGeneration={this.pageGeneration}
            />,
            this.root
        );
    }

    public id(): string {
        return kGameToolId;
    }

    public isExperimental(): boolean {
        return false; // Todo: probably soon true, but first we need to make a control to turn it on
    }

    public toolRequiresEnterprise(): boolean {
        return true; // Todo: implement this more fully, probably using RequiresBloomEnterprise
    }

    public beginRestoreSettings(_settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    private lastPageId = "";

    public newPageReady() {
        const page = GameTool.getBloomPage();
        const pageFrameExports = getEditablePageBundleExports();
        if (!getCanvasElementManager() || !page || !pageFrameExports) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(() => this.newPageReady(), 100);
            return;
        }
        adjustDraggablesForLanguage(page);

        setPlayerUrlPrefixFromWindowLocationHref(
            page.ownerDocument.defaultView!.location.href
        );

        const pageId = page.getAttribute("id") ?? "";
        if (pageId === this.lastPageId) {
            // reinitialize for the current tab. This is especially important in Play mode,
            // because detachFromPage() undoes some of the initialization for that tab.
            const currentTab = getActiveDragActivityTab();
            getToolboxBundleExports()?.setActiveDragActivityTab(currentTab);
        } else {
            this.lastPageId = pageId;
            // useful during development, MAY not need in production.
            const canvasElementManager = getCanvasElementManager()!;
            canvasElementManager.removeDetachedTargets();
            canvasElementManager.adjustCanvasElementOrdering();

            // Force things to Start tab as we change page.
            // If we decide not to do this, we should probably at least find a way to do it
            // when it's a brand newly-created page.
            getToolboxBundleExports()?.setActiveDragActivityTab(0);
        }
    }

    public detachFromPage() {
        const page = GameTool.getBloomPage();
        if (page) {
            undoPrepareActivity(page);
        }
    }
}
export function playSound(newSoundId: string, page: HTMLElement) {
    const audio = new Audio("audio/" + newSoundId);
    audio.style.visibility = "hidden";
    audio.classList.add("bloom-ui"); // so it won't be saved, even if we fail to remove it otherwise

    // To my surprise, in BP storybook it works without adding the audio to any document.
    // But in Bloom proper, it does not. I think it is because this code is part of the toolbox,
    // so the audio element doesn't have the right context to interpret the relative URL.
    page.append(audio);
    // It feels cleaner if we remove it when done. This could fail, e.g., if the user
    // switches tabs or pages before we get done playing. Removing it immediately
    // prevents the sound being played. It's not a big deal if it doesn't get removed.
    audio.onended = () => {
        page.removeChild(audio);
    };
    audio.play();
}

//Slider: function designTimeClickOnSlider(this: HTMLElement, ev: MouseEvent) {
//     if (draggingSlider) {
//         return;
//     }
//     const target = ev.target as HTMLElement;
//     // If they click on the wrong one in play mode, don't select it.
//     if (target.closest(".drag-activity-play")) {
//         return;
//     }
//     const src = target.getAttribute("src");
//     const id = target.getAttribute("data-img");
//     if (!id) {
//         return;
//     }
//     const possibleBubbles = Array.from(
//         target.ownerDocument.querySelectorAll("[data-img-txt='" + id + "']")
//     );
//     // usually there will only be one possibleBubble, but all the 'wrong' ones have the same data-img-txt.
//     const bubbleToSelect = possibleBubbles.find(
//         b => b.getElementsByTagName("img")[0].getAttribute("src") === src
//     );
//     if (bubbleToSelect) {
//         theOneCanvasElementManager?.setActiveElement(
//             bubbleToSelect as HTMLElement
//         );
//     }
// }

// After careful thought, I think the right source of truth for which tab is active is a variable on the
// top level window object.
// For a long time it was an attribute of the parent element of the bloom-page. This makes it difficult to
// stay on the same tab when reloading the current page (typically after a Save), since the whole document
// is reloaded.
// I don't want it anywhere in the toolbox, because it is applicable even when the Game Toolbox is not active.
// In addition to the reason above for not wanting it anywhere in the page iframe,
// I don't want it part of the page, because then I have to take steps to prevent persisting it.
// I don't want it in the element we add to hold the tab control, because it's possible for the page
// to exist before that gets created, and then we have another complication for the toolbox to worry about
// when trying to get it.
// With this new strategy, I think it would be possible to stay on the same tab while changing pages.
// I'm not sure this is desirable. Currently newPageReady() explicitly resets to the Start tab
// if it is loading a different page.
export function getActiveDragActivityTab(): number {
    return window.top!["dragActivityPage"] ?? 0;
}

// The top-level function to get everything into the right state for the specified tab
// (Start, Correct, Wrong, Play).
// Note: the games code is currently pulled into the page bundle, but this function
// MUST be called using the toolbox bundle copy, because it adds and removes event
// handlers, and if we mix bundles, removing the handlers will fail since a different
// copy of the function is being 'removed'. Safest to always use
// getToolboxBundleExports()?.setActiveDragActivityTab(), which works in any bundle.
// Even in this file, a calling function could be running in the page bundle.
export function setActiveDragActivityTab(tab: number) {
    window.top!["dragActivityPage"] = tab;
    const page = GameTool.getBloomPage();
    const pageFrameExports = getEditablePageBundleExports();
    if (!page || !pageFrameExports) {
        // just loading page??
        setTimeout(() => {
            console.log(
                "had to postpone setting tab to ",
                tab,
                " because page not ready yet."
            );
            setActiveDragActivityTab(tab);
        }, 100);
        return;
    }
    const parent = page.parentElement;
    if (!parent) {
        console.error("No parent for page");
        return;
    }
    updateTabClass(tab);
    pageFrameExports.renderDragActivityTabControl(tab);
    // Update the toolbox.
    /// Review: might it not exist yet? Do we need a timeout if so?
    // I think we're OK, if for no other reason, because both the dragActivityTool code and the
    // code here agree that we start in the Start tab after switching pages.
    const toolbox = getToolboxBundleExports()?.getTheOneToolbox();
    toolbox?.getTheOneDragActivityTool()?.setActiveTab(tab);

    //Slider: const wrapper = page.getElementsByClassName(
    //     "bloom-activity-slider"
    // )[0] as HTMLElement;

    const canvasElementManager = getCanvasElementManager();
    if (tab === playTabIndex) {
        canvasElementManager!.suspendComicEditing("forGamePlayMode");
        // Enhance: perhaps the next/prev page buttons could do something even here?
        // If so, would we want them to work only in TryIt mode, or always?
        prepareActivity(page, _next => {
            /* nothing to do */
        });
        playInitialElements(page, true);
        //Slider: wrapper?.removeEventListener("click", designTimeClickOnSlider);
    } else {
        undoPrepareActivity(page);
        canvasElementManager?.resumeComicEditing();
        canvasElementManager?.checkActiveElementIsVisible();
        //Slider: wrapper?.addEventListener("click", designTimeClickOnSlider);
    }
    if (tab === correctTabIndex || tab === wrongTabIndex) {
        // We can't currently do this for hidden canvas elements, and selecting one of these tabs
        // may cause some previously hidden canvas elements to become visible.
        canvasElementManager?.ensureCanvasElementsIntersectParent(page);
    }
    if (tab === startTabIndex) {
        enableDraggingTargets(page);
        pageFrameExports.showGamePromptDialog(true);
    } else {
        disableDraggingTargets(page);
        hideGamePromptDialog(page);
    }
}

// Replace the origami control with the Game tab control if the page is a game.
export function setupDragActivityTabControl() {
    const page = GameTool.getBloomPage();
    if (!page) {
        return;
    }

    if (!isPageBloomGame(page)) {
        return;
    }
    const tabControl = page.ownerDocument.createElement("div");
    tabControl.setAttribute("id", "drag-activity-tab-control");
    const abovePageControlContainer = page.ownerDocument.getElementsByClassName(
        "above-page-control-container"
    )[0];
    if (!abovePageControlContainer) {
        // if it's not already created, keep trying until it is.
        setTimeout(setupDragActivityTabControl, 200);
        return;
    }
    // We want the Game controls exactly when we don't
    // want origami, so we use the control container we usually use for origami,
    // a nice wrapper inside the page (so we can
    // get the correct page alignment) and have already arranged to delete before saving the page.
    abovePageControlContainer.appendChild(tabControl);
    // Seems strange that we need to do this to call a function in the same file,
    // but currently this code is also pulled into the page bundle, and called from
    // its initialization code, and it's vital to be consistent about the bundle
    // from which event handler functions are taken, so they can later be removed.
    getToolboxBundleExports()?.setActiveDragActivityTab(
        getActiveDragActivityTab()
    );
}

// dimension is assumed to end with "px" (as we use for positioning and dimensioning canvas elements).
// Technically it would get a result for other two-character units, but the result might not be
// what we want, since we use the resulting number assuming it means px.
function pxToNumber(dimension: string): number {
    const num = dimension.substring(0, dimension.length - 2); // strip off "px"
    return parseFloat(num);
}

export const makeTargetForDraggable = (
    canvasElement: HTMLElement
): HTMLElement => {
    const id = canvasElement.getAttribute("data-draggable-id");
    if (!id) {
        throw new Error("Bubble does not have a data-draggable-id attribute");
    }
    // don't simplify to 'document.createElement'; may be in a different iframe
    const target = canvasElement.ownerDocument.createElement("div");
    target.setAttribute("data-target-of", id);
    const left = pxToNumber(canvasElement.style.left);
    const top = pxToNumber(canvasElement.style.top);
    const width = pxToNumber(canvasElement.style.width);
    const height = pxToNumber(canvasElement.style.height);
    let newLeft = left + 20;
    let newTop = top + height + 30;
    if (newTop + height > canvasElement.parentElement!.clientHeight) {
        newTop = Math.max(0, top - height - 30);
    }
    if (newLeft + width > canvasElement.parentElement!.clientWidth) {
        newLeft = Math.max(0, left - width - 30);
    }
    const { x: snapX, y: snapY } = snapProvider.getPosition(
        undefined,
        newLeft,
        newTop
    );
    // Review: can we do any more to make sure it's visible and not overlapping canvas element?
    // Should we try to avoid overlapping other canvas elements and/or targets?
    target.style.left = `${snapX}px`;
    target.style.top = `${snapY}px`;
    target.style.width = `${width}px`;
    target.style.height = `${height}px`;
    // This allows it to get focus, which allows it to get the shadow effect we want when
    // clicked. But is that really right? We can't actually type there.
    target.setAttribute("tabindex", "0");
    canvasElement.parentElement!.appendChild(target);
    enableDraggingTargets(target);
    adjustTarget(canvasElement, target);
    return target;
};
