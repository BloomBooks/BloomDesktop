// Helper functions extracted from CanvasElementManager.
//
// This module handles saving canvas element state as the current-language alternate
// (primarily for bubbles). Keeping it separate helps reduce the size and coupling
// of CanvasElementManager.

/// <reference path="../collectionSettings.d.ts" />

import { Bubble } from "comicaljs";
import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";

const kComicalGeneratedClass: string = "comical-generated";

// This is a definition of the object we store as JSON in data-bubble-alternate.
// Tails has further structure but CanvasElementManager doesn't care about it.
export interface IAlternate {
    style: string; // What to put in the style attr of the canvas element; determines size and position
    tails: object[]; // The tails of the data-bubble; determines placing of tail.
    lang?: string;
}

export const saveStateOfCanvasElementAsCurrentLangAlternate = (
    canvasElement: HTMLElement,
    canvasElementLangIn?: string,
): void => {
    const canvasElementLang =
        canvasElementLangIn ?? GetSettings().languageForNewTextBoxes;

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
        editable.setAttribute(
            "data-bubble-alternate",
            JSON.stringify(alternate).replace(/"/g, "`"),
        );
    }
};

// If there is a bloom-editable in the canvas element that has a data-bubble-alternate,
// use it to set the data-bubble of the canvas element.
export const adjustCanvasElementsForCurrentLanguage = (
    container: HTMLElement,
): void => {
    const canvasElementLang = GetSettings().languageForNewTextBoxes;
    Array.from(container.getElementsByClassName(kCanvasElementClass)).forEach(
        (canvasElement) => {
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
    const canvasElementLang = GetSettings().languageForNewTextBoxes;
    Array.from(container.getElementsByClassName(kCanvasElementClass)).forEach(
        (top: HTMLElement) =>
            saveStateOfCanvasElementAsCurrentLangAlternate(
                top,
                canvasElementLang,
            ),
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
    const canvasElementLang = GetSettings().languageForNewTextBoxes;
    Array.from(canvasElement.getElementsByClassName("bloom-editable")).forEach(
        (editable) => {
            const lang = editable.getAttribute("lang");
            if (lang === canvasElementLang) {
                const alternate = {
                    style: canvasElement.getAttribute("style"),
                    tails: Bubble.getBubbleSpec(canvasElement).tails,
                };
                editable.setAttribute(
                    "data-bubble-alternate",
                    JSON.stringify(alternate).replace(/"/g, "`"),
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

                    const tails = alternate.tails;
                    tails.forEach(
                        (tail: {
                            tipX: number;
                            tipY: number;
                            midpointX: number;
                            midpointY: number;
                        }) => {
                            tail.tipX = newLeft + (tail.tipX - oldLeft) * scale;
                            tail.tipY = newTop + (tail.tipY - oldTop) * scale;
                            tail.midpointX =
                                newLeft + (tail.midpointX - oldLeft) * scale;
                            tail.midpointY =
                                newTop + (tail.midpointY - oldTop) * scale;
                        },
                    );
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
