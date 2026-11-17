import * as React from "react";
import { useLayoutEffect, useRef } from "react";

// A block that reduces its font size when its text is too wide for the width it is given,
// typically because a translation is much longer than the English. The text still wraps
// normally at spaces; this only matters when a single word won't fit even on a line of its
// own. The block's own (stylesheet) font size is the largest it uses, and it never goes
// below minFontSizePx. What is still too wide at that size overflows, unless
// breakWordsAtMinimum is set, in which case the too-wide words are broken wherever they
// need to be. Children should size their text in em (or inherit), so that they follow the
// font size set here.
//
// The block must have a width that does not grow with its text (a grid cell or flex item
// with min-width: 0, or a fixed width), or there is never anything to shrink.
export const ShrinkTextToFit: React.FunctionComponent<{
    minFontSizePx: number;
    breakWordsAtMinimum?: boolean;
    className?: string;
    children: React.ReactNode;
}> = (props) => {
    const ref = useRef<HTMLDivElement>(null);

    // The right font size depends on layout React knows nothing about: the width the
    // block is given (which changes when, for example, a vertical scrollbar appears beside
    // it) and its text (localized components fill theirs in after a server round trip).
    // So this effect fits the text once before the first paint, then watches the DOM for
    // both and fits again.
    useLayoutEffect(() => {
        const element = ref.current!;
        let fittedWidth = -1;
        const fit = () => {
            fitTextToWidth(
                element,
                props.minFontSizePx,
                props.breakWordsAtMinimum,
            );
            fittedWidth = element.clientWidth;
        };
        // Fitting changes the element's height, so doing it inside the ResizeObserver
        // callback would make the browser report "ResizeObserver loop completed with
        // undelivered notifications". Waiting a frame avoids that, and lets a text change
        // and the resize it causes share one fit.
        let frame = 0;
        const fitNextFrame = () => {
            cancelAnimationFrame(frame);
            frame = requestAnimationFrame(fit);
        };
        fit();
        const resizeObserver = new ResizeObserver(() => {
            // A height change alone (such as the one each fit makes) needs no new fit.
            if (element.clientWidth !== fittedWidth) fitNextFrame();
        });
        resizeObserver.observe(element);
        const mutationObserver = new MutationObserver(fitNextFrame);
        mutationObserver.observe(element, {
            childList: true,
            characterData: true,
            subtree: true,
        });
        return () => {
            cancelAnimationFrame(frame);
            resizeObserver.disconnect();
            mutationObserver.disconnect();
        };
    }, [props.minFontSizePx, props.breakWordsAtMinimum]);

    return (
        <div ref={ref} className={props.className}>
            {props.children}
        </div>
    );
};

// Fit element's text to its width as ShrinkTextToFit describes. Exported for testing.
export function fitTextToWidth(
    element: HTMLElement,
    minFontSizePx: number,
    breakWordsAtMinimum = false,
): void {
    element.style.fontSize = "";
    element.style.overflowWrap = "";
    let size = parseFloat(getComputedStyle(element).fontSize);
    // Shrinking the text can change the width the element is given (e.g. a grid column
    // sized from its content), so one proportional step is not always enough; a few
    // more converge in practice.
    for (let tries = 0; tries < 8; tries++) {
        if (
            element.scrollWidth <= element.clientWidth ||
            size <= minFontSizePx
        ) {
            break;
        }
        const proportional = (size * element.clientWidth) / element.scrollWidth;
        // At least half a pixel per step, so each one makes progress.
        size = Math.max(minFontSizePx, Math.min(proportional, size - 0.5));
        element.style.fontSize = `${size}px`;
    }
    if (breakWordsAtMinimum && element.scrollWidth > element.clientWidth) {
        element.style.overflowWrap = "anywhere";
    }
}
