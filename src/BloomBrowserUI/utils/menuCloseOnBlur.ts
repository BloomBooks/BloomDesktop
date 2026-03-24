// This file contains utilities for closing popup menus (including in Selects) when the user
// clicks outside the window that holds the menu, or when Bloom is deactivated, or when the
// panel that contains the menu closes.
// Only one such menu can be opened, so at this point we only need to register one function.
// Most activity outside the toolbox, even outside Bloom altogether, causes its window
// to lose focus, so we listen for that event.
// However, some clicks in the document iframe...at least clicks on images...do not have
// that effect, so we have an explict mousedown listener there that calls
// handleClickOutsideToolbox().
// The function is typically a React useState setter that is fairly harmless to call
// multiple times, but to reduce renders we try to only call it once, though it is
// possible that handleClickOutsideToolbox will be called both by the blur listener
// and the iframe mousedown listener as a result of the same click.

// let losingFocusFunction: (() => void) | undefined;

// export function handleClickOutsideToolbox(): void {
//     losingFocusFunction?.();
//     losingFocusFunction = undefined;
// }

// export function callWhenFocusLost(fn: () => void): void {
//     losingFocusFunction = fn;
//     registerMenuCloseOnBlur(fn);
//     window.addEventListener(
//         "blur",
//         () => {
//             handleClickOutsideToolbox();
//         },
//         { once: true },
//     );
// }

// This function, which typically closes a popup menu, should be called when the containing
// window blurs or the toolbox is closed.
let onBlurFunction: (() => void) | undefined;
// If something else also needs to know about blur functions, this can be registered to be told about them.
let extraFunctionToHandleBlurTasks: ((task: () => void) => void) | undefined;

// Register a function that will also receive functions passed to callOnBlur.
// This is currently used so that blur functions are are passed to the toolbox
// function addWhenClosingToolTask, which will call them when the toolbox is closed,
// in case the menu is still open at that point.
export function setExtraFunctionToHandleBlurTasks(
    addTask: (task: () => void) => void,
): void {
    extraFunctionToHandleBlurTasks = addTask;
}

// There are cases where a mouse down in a bloom document doesn't actually
// cause focus to move, so other panes don't get a blur event. We want to simulate
// a blur, at least for the purposes of callOnBlur().
export function simulateBlurOnPageFrameMouseDown(): void {
    onBlurFunction?.();
    onBlurFunction = undefined;
}

// call the specified function, which typically closes a popup menu, when the containing window
// blurs.
export function callOnBlur(fn: () => void): void {
    onBlurFunction = fn;
    extraFunctionToHandleBlurTasks?.(fn);
    window.addEventListener(
        "blur",
        () => {
            simulateBlurOnPageFrameMouseDown();
        },
        { once: true },
    );
}
