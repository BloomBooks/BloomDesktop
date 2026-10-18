// Helper functions extracted from CanvasElementManager.
//
// This module handles saving canvas element state as the current-language alternate
// (primarily for bubbles). Keeping it separate helps reduce the size and coupling
// of CanvasElementManager.

/// <reference path="../collectionSettings.d.ts" />

import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";
import { doOutsideHistory } from "../../pageHistory/pageHistoryAccess";

const kComicalGeneratedClass: string = "comical-generated";

// The canvas elements whose current-language alternate adjustCanvasElementsForCurrentLanguage has
// already applied since the page loaded, or which turning canvas editing off has already saved as
// that alternate (saveCurrentCanvasElementStateAsCurrentLangAlternate). From then on the element's
// style and tails are what the alternate for the current language is worked out from, never the
// other way round. The second case covers a canvas element added after editing was turned on,
// which no adjustment has seen: an undo while editing is off can put back its style, and the
// alternate saved when editing was turned off must not overwrite that when it is turned on again.
const canvasElementsAdjustedForCurrentLanguage = new WeakSet<Element>();

// This is a definition of the object we store as JSON in data-bubble-alternate.
// Tails has further structure but CanvasElementManager doesn't care about it.
export interface IAlternate {
    style: string; // What to put in the style attr of the canvas element; determines size and position
    tails: object[]; // The tails of the data-bubble; determines placing of tail.
    lang?: string;
}

interface ITailSpec {
    tipX: number;
    tipY: number;
    midpointX: number;
    midpointY: number;
}

export const saveStateOfCanvasElementAsCurrentLangAlternate = (
    canvasElement: HTMLElement,
    canvasElementLangIn?: string,
): void => {
    const canvasElementLang =
        canvasElementLangIn ??
        (GetSettings() as ICollectionSettings).languageForNewTextBoxes;

    const editable = Array.from(
        canvasElement.getElementsByClassName("bloom-editable"),
    ).find((e) => e.getAttribute("lang") === canvasElementLang);
    if (editable) {
        const bubbleData = canvasElement.getAttribute("data-bubble") ?? "";
        const bubbleDataObj = JSON.parse(bubbleData.replace(/`/g, '"'));
        const alternate = {
            lang: canvasElementLang,
            style: canvasElement.getAttribute("style") ?? "",
            tails: bubbleDataObj.tails as object[],
        };
        const alternateString = JSON.stringify(alternate).replace(/"/g, "`");
        if (editable.getAttribute("data-bubble-alternate") === alternateString)
            return;
        // The current language's alternate is worked out again from the element's style and
        // tails each time canvas editing is turned off, which opening a tool or saving the page
        // does, so it is not recorded in the page history: undo leaves it in place, and a later
        // turn-off brings it up to date. Joining the latest undo step instead would clear redo
        // when a tool opens after an undo, and inside the click that opens a tool it would end
        // the history.
        doOutsideHistory(() =>
            editable.setAttribute("data-bubble-alternate", alternateString),
        );
    }
};

// If there is a bloom-editable in the canvas element that has a data-bubble-alternate,
// use it to set the data-bubble of the canvas element.
// This happens once for each canvas element after the page loads, when canvas editing is first
// turned on, and never for one that turning editing off has already saved from (one added since
// editing was turned on). After that the alternate for the current language only ever holds what
// saveStateOfCanvasElementAsCurrentLangAlternate worked out from the element, or what an undo put
// back, and the element itself is right: an undo or redo while canvas editing is off (with a tool
// open) puts back the element's style, which the alternate written when editing was turned off
// would otherwise overwrite when it is turned on again.
export const adjustCanvasElementsForCurrentLanguage = (
    container: HTMLElement,
): void => {
    const canvasElementLang = (GetSettings() as ICollectionSettings)
        .languageForNewTextBoxes;
    Array.from(container.getElementsByClassName(kCanvasElementClass)).forEach(
        (canvasElement) => {
            if (canvasElementsAdjustedForCurrentLanguage.has(canvasElement))
                return;
            canvasElementsAdjustedForCurrentLanguage.add(canvasElement);
            const editable = Array.from(
                canvasElement.getElementsByClassName("bloom-editable"),
            ).find((e) => e.getAttribute("lang") === canvasElementLang);
            if (editable) {
                const alternatesString = editable.getAttribute(
                    "data-bubble-alternate",
                );
                if (alternatesString) {
                    const alternate = JSON.parse(
                        alternatesString.replace(/`/g, '"'),
                    ) as IAlternate;
                    canvasElement.setAttribute("style", alternate.style);
                    const bubbleData =
                        canvasElement.getAttribute("data-bubble");
                    if (bubbleData) {
                        const bubbleDataObj = JSON.parse(
                            bubbleData.replace(/`/g, '"'),
                        );
                        bubbleDataObj.tails = alternate.tails;
                        const newBubbleData = JSON.stringify(
                            bubbleDataObj,
                        ).replace(/"/g, "`");
                        canvasElement.setAttribute(
                            "data-bubble",
                            newBubbleData,
                        );
                    }
                }
            }
        },
    );

    const altSvg = Array.from(
        container.getElementsByClassName("comical-alternate"),
    ).find((svg) => svg.getAttribute("data-lang") === canvasElementLang);
    if (altSvg) {
        container.removeChild(altSvg);
    }

    const currentSvg = container.getElementsByClassName(
        kComicalGeneratedClass,
    )[0];
    if (currentSvg) {
        const currentSvgLang = currentSvg.getAttribute("data-lang");
        if (currentSvgLang && currentSvgLang !== canvasElementLang) {
            currentSvg.classList.remove(kComicalGeneratedClass);
            currentSvg.classList.add("comical-alternate");
            (currentSvg as HTMLElement).style.display = "none";
        }
    }
};

// Save the current state of things so that we can later position everything correctly
// for this language, even if in the meantime we change canvas element positions for
// other languages.
export const saveCurrentCanvasElementStateAsCurrentLangAlternate = (
    container: HTMLElement,
): void => {
    const canvasElementLang = (GetSettings() as ICollectionSettings)
        .languageForNewTextBoxes;
    Array.from(container.getElementsByClassName(kCanvasElementClass)).forEach(
        (top) => {
            saveStateOfCanvasElementAsCurrentLangAlternate(
                top as HTMLElement,
                canvasElementLang,
            );
            canvasElementsAdjustedForCurrentLanguage.add(top);
        },
    );

    const currentSvg = container.getElementsByClassName(
        kComicalGeneratedClass,
    )[0];
    currentSvg?.setAttribute("data-lang", canvasElementLang);
};

const numberPxRegex = ": ?(-?\\d+.?\\d*)px";

// Typical source is something like "left: 224px; top: 79.6px; width: 66px; height: 30px;"
// We want to pass "top" and get 79.6.
export const getLabeledNumberInPx = (label: string, source: string): number => {
    const match = source.match(new RegExp(label + numberPxRegex));
    if (match) {
        return parseFloat(match[1]);
    }
    return 9;
};

// Find in 'style' the label followed by a number (e.g., left).
// Let oldRange be the size of the object in that direction, e.g., width.
// We want to move the center of the object based on scaling and translation.
export const adjustCenterOfTextBox = (
    label: string,
    style: string,
    scale: number,
    oldC: number,
    newC: number,
    oldRange: number,
): string => {
    const old = getLabeledNumberInPx(label, style);
    const center = old + oldRange / 2;
    const newCenter = newC + (center - oldC) * scale;
    const newVal = newCenter - oldRange / 2;
    return style.replace(
        new RegExp(label + numberPxRegex),
        label + ": " + newVal + "px",
    );
};

export const adjustCanvasElementAlternates = (
    canvasElement: HTMLElement,
    scale: number,
    oldLeft: number,
    oldTop: number,
    newLeft: number,
    newTop: number,
): void => {
    const canvasElementLang = (GetSettings() as ICollectionSettings)
        .languageForNewTextBoxes;
    Array.from(canvasElement.getElementsByClassName("bloom-editable")).forEach(
        (editable) => {
            const lang = editable.getAttribute("lang");
            if (lang === canvasElementLang) {
                // Written as turning canvas editing off writes it, outside the page history. If
                // the resize's undo step held it, the next turn-off would rewrite it outside the
                // history, and that undo would be refused.
                saveStateOfCanvasElementAsCurrentLangAlternate(
                    canvasElement,
                    canvasElementLang,
                );
            } else {
                const alternatesString = editable.getAttribute(
                    "data-bubble-alternate",
                );
                if (alternatesString) {
                    const alternate = JSON.parse(
                        alternatesString.replace(/`/g, '"'),
                    ) as IAlternate;
                    const style = alternate.style;
                    const width = getLabeledNumberInPx("width", style);
                    const height = getLabeledNumberInPx("height", style);
                    let newStyle = adjustCenterOfTextBox(
                        "left",
                        style,
                        scale,
                        oldLeft,
                        newLeft,
                        width,
                    );
                    newStyle = adjustCenterOfTextBox(
                        "top",
                        newStyle,
                        scale,
                        oldTop,
                        newTop,
                        height,
                    );

                    const tails = alternate.tails as ITailSpec[];
                    tails.forEach((tail) => {
                        tail.tipX = newLeft + (tail.tipX - oldLeft) * scale;
                        tail.tipY = newTop + (tail.tipY - oldTop) * scale;
                        tail.midpointX =
                            newLeft + (tail.midpointX - oldLeft) * scale;
                        tail.midpointY =
                            newTop + (tail.midpointY - oldTop) * scale;
                    });
                    alternate.style = newStyle;
                    alternate.tails = tails;
                    editable.setAttribute(
                        "data-bubble-alternate",
                        JSON.stringify(alternate).replace(/"/g, "`"),
                    );
                }
            }
        },
    );
};
