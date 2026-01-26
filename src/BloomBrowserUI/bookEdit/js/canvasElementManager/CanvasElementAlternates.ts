// Helper functions extracted from CanvasElementManager.
//
// This module handles saving canvas element state as the current-language alternate
// (primarily for bubbles). Keeping it separate helps reduce the size and coupling
// of CanvasElementManager.

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
