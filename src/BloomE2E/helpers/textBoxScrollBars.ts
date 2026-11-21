// The scroll bar Bloom gives a text box whose text does not fit it.
//
// A text box on a page has a fixed size, so text that outgrows it is marked red and, so that the
// rest can still be reached, the box is given a scroll bar. The scroll bar is not the browser's
// own: Bloom's page code asks the niceScroll library for one (addScrollbarsToPage, called from
// OverflowChecker.ts), and niceScroll draws it as an element of its own beside the box, the
// "rail", holding the handle a person drags. The rail is not inside the box and carries nothing
// that names the box, so this file finds a box's rail by where it is drawn: over the box's right
// edge. Nothing here returns a pixel size for a test to compare with a constant; a test compares
// a handle with the same handle earlier.
//
// Bloom checks a box for overflow when a key is released in it (OverflowChecker.AddOverflowHandlers),
// so the typing helpers here end with real key presses. (AUTOMATION-DEBT.md: "Undo and overflow
// checks in a table cell need real key presses".)
//
// niceScroll cannot scroll a box whose translation group is laid out to centre its text or put it
// at the bottom, so while a box has a scroll bar Bloom moves that alignment aside, and puts it
// back when the scroll bar goes. getVerticalAlignment reads which alignment is in force.

import { expect, type Page } from "@playwright/test";
import { editablePageFrame, groupTextBox } from "./bookMaking";
import { moveCaretToEnd, typeWithKeys } from "./keys";
import { emptyTextBox } from "./overflowMarks";

// How long to wait for a scroll bar to come or go: Bloom checks a box a second after a key is
// released, and rebuilds the page's scroll bars a fifth of a second after that.
const kScrollBarWaitMs = 10000;

/**
 * Add text at the end of what a text box holds, the way a person carries on typing there. The
 * text goes in at once, and then `finalWord` is typed with real key presses, because Bloom checks
 * a box for overflow when a key is released. Returns once the box ends with `finalWord`.
 */
export async function typeAtEndOfGroup(
    page: Page,
    groupSelector: string,
    languageTag: string,
    text: string,
    finalWord: string,
    groupIndex = 0,
): Promise<void> {
    const box = groupTextBox(page, groupSelector, languageTag, groupIndex);
    await moveCaretToEnd(box);
    await page.keyboard.insertText(text);
    await typeWithKeys(page, finalWord);
    await expect
        .poll(async () => (await box.innerText()).trimEnd(), {
            timeout: 15000,
            message: `Typing at the end of the "${languageTag}" box of "${groupSelector}" did not land.`,
        })
        .toMatch(new RegExp(`${finalWord}$`));
}

/**
 * Replace everything a text box holds with `text`, typed with real key presses, the way a person
 * selects all, deletes, and types something short. Returns once the box holds just `text`.
 */
export async function retypeGroupWithKeys(
    page: Page,
    groupSelector: string,
    languageTag: string,
    text: string,
    groupIndex = 0,
): Promise<void> {
    const box = groupTextBox(page, groupSelector, languageTag, groupIndex);
    await emptyTextBox(box);
    await typeWithKeys(page, text);
    await expect(box).toHaveText(text, { timeout: 15000, useInnerText: true });
}

/** A text box's scroll bar, as it is drawn. */
export interface ITextBoxScrollBar {
    /** Whether a scroll bar is showing beside the box. */
    showing: boolean;
    /** The height of the handle a person drags, in screen pixels; 0 when no scroll bar shows. */
    handleHeight: number;
}

/**
 * The scroll bar Bloom has given one text box, if any. It is found by where it is drawn: a
 * showing rail that covers the box's right edge and overlaps the box's height.
 */
export async function getTextBoxScrollBar(
    page: Page,
    groupSelector: string,
    languageTag: string,
    groupIndex = 0,
): Promise<ITextBoxScrollBar> {
    return groupTextBox(page, groupSelector, languageTag, groupIndex).evaluate(
        (box) => {
            const boxRect = box.getBoundingClientRect();
            for (const rail of Array.from(
                box.ownerDocument.querySelectorAll<HTMLElement>(
                    ".nicescroll-rails",
                ),
            )) {
                if (getComputedStyle(rail).display === "none") continue;
                const railRect = rail.getBoundingClientRect();
                // The vertical rail is a narrow strip; the horizontal one is not a scroll bar a
                // person sees on a text box.
                if (railRect.height <= railRect.width) continue;
                const besideTheBox =
                    railRect.right > boxRect.right - 2 &&
                    railRect.left < boxRect.right + 2;
                const overlapsTheBox =
                    railRect.top < boxRect.bottom &&
                    railRect.bottom > boxRect.top;
                if (!besideTheBox || !overlapsTheBox) continue;
                const handle = rail.querySelector(".nicescroll-cursors");
                return {
                    showing: true,
                    handleHeight: handle
                        ? handle.getBoundingClientRect().height
                        : 0,
                };
            }
            return { showing: false, handleHeight: 0 };
        },
    );
}

/**
 * Wait until a text box has a scroll bar (`showing` true) or has none (false), and return it as
 * it then is.
 */
export async function waitForTextBoxScrollBar(
    page: Page,
    groupSelector: string,
    languageTag: string,
    showing: boolean,
    groupIndex = 0,
): Promise<ITextBoxScrollBar> {
    let scrollBar: ITextBoxScrollBar = { showing: !showing, handleHeight: 0 };
    // A scroll bar that has just appeared can be read before niceScroll has sized its handle (a
    // handle about one pixel high has been seen), so when waiting for one to show, also wait until
    // two reads in a row give the same handle height.
    let previousHandleHeight = -1;
    await expect
        .poll(
            async () => {
                scrollBar = await getTextBoxScrollBar(
                    page,
                    groupSelector,
                    languageTag,
                    groupIndex,
                );
                const settled = scrollBar.handleHeight === previousHandleHeight;
                previousHandleHeight = scrollBar.handleHeight;
                return showing
                    ? scrollBar.showing && settled
                    : scrollBar.showing;
            },
            {
                timeout: kScrollBarWaitMs,
                message: showing
                    ? `The "${languageTag}" box of "${groupSelector}" never got a scroll bar.`
                    : `The "${languageTag}" box of "${groupSelector}" kept its scroll bar.`,
            },
        )
        .toBe(showing);
    return scrollBar;
}

/**
 * Wait until a text box's scroll bar handle is shorter than `than`, which is what the handle does
 * when the box is given more text to scroll through, and return the scroll bar as it then is.
 */
export async function waitForShorterScrollBarHandle(
    page: Page,
    groupSelector: string,
    languageTag: string,
    than: ITextBoxScrollBar,
    groupIndex = 0,
): Promise<ITextBoxScrollBar> {
    let scrollBar = than;
    await expect
        .poll(
            async () => {
                scrollBar = await getTextBoxScrollBar(
                    page,
                    groupSelector,
                    languageTag,
                    groupIndex,
                );
                return scrollBar.showing
                    ? scrollBar.handleHeight
                    : Number.POSITIVE_INFINITY;
            },
            {
                timeout: kScrollBarWaitMs,
                message:
                    `The scroll bar of the "${languageTag}" box of "${groupSelector}" should have ` +
                    `a shorter handle now that the box holds more text.`,
            },
        )
        .toBeLessThan(than.handleHeight);
    return scrollBar;
}

/** How much of a text box's text is out of sight above and below what the box shows. */
export interface ITextBoxScrollPosition {
    /** Pixels of text scrolled out of sight above; 0 when the start of the text shows. */
    hiddenAbove: number;
    /** Pixels of text out of sight below; 0, give or take a pixel, when the end shows. */
    hiddenBelow: number;
}

/** Where one text box is scrolled to. */
export async function getTextBoxScrollPosition(
    page: Page,
    groupSelector: string,
    languageTag: string,
    groupIndex = 0,
): Promise<ITextBoxScrollPosition> {
    return groupTextBox(page, groupSelector, languageTag, groupIndex).evaluate(
        (box) => ({
            hiddenAbove: box.scrollTop,
            hiddenBelow: box.scrollHeight - box.clientHeight - box.scrollTop,
        }),
    );
}

/**
 * Turn the mouse wheel over a text box, far enough to reach the start of its text ("start") or
 * the end ("end"), and wait until the box has stopped scrolling. Says nothing about where the
 * box should end up; read getTextBoxScrollPosition for that.
 */
export async function wheelTextBoxTo(
    page: Page,
    groupSelector: string,
    languageTag: string,
    where: "start" | "end",
    groupIndex = 0,
): Promise<void> {
    const box = groupTextBox(page, groupSelector, languageTag, groupIndex);
    const scrollTop = () => box.evaluate((element) => element.scrollTop);
    const before = await scrollTop();
    await box.hover();
    // Much further than any box in a test holds: the scroll bar stops at the end of the text.
    await page.mouse.wheel(0, where === "end" ? 20000 : -20000);
    // The scroll bar starts a moment after the wheel turns and then glides, so first give the
    // box time to leave where it was (it will not, if it was already there), and then wait for
    // two readings in a row that agree.
    await expect
        .poll(async () => (await scrollTop()) !== before, { timeout: 3000 })
        .toBe(true)
        .catch(() => undefined);
    let last = Number.NaN;
    await expect
        .poll(
            async () => {
                const now = await scrollTop();
                const settled = now === last;
                last = now;
                return settled;
            },
            {
                timeout: kScrollBarWaitMs,
                intervals: [300],
                message: `The "${languageTag}" box of "${groupSelector}" never stopped scrolling.`,
            },
        )
        .toBe(true);
}

/** How a translation group places its text in the height it has. */
export type VerticalAlignment = "top" | "center" | "bottom";

/**
 * How one translation group places its text right now: at the top (the default), centred, or at
 * the bottom. While a box of the group has a scroll bar, Bloom takes centring and bottom
 * placement off, so this then says "top"; it says what the author chose again once the scroll
 * bar has gone.
 */
export async function getVerticalAlignment(
    page: Page,
    groupSelector: string,
    groupIndex = 0,
): Promise<VerticalAlignment> {
    return editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex)
        .evaluate((group): VerticalAlignment => {
            if (group.classList.contains("bloom-vertical-align-center"))
                return "center";
            if (group.classList.contains("bloom-vertical-align-bottom"))
                return "bottom";
            return "top";
        });
}

/**
 * Wait until a translation group places its text as `alignment` says. Bloom puts centring or
 * bottom placement back a moment after it takes a scroll bar away, so the answer has to be waited
 * for. `why` is the failure message.
 */
export async function waitForVerticalAlignment(
    page: Page,
    groupSelector: string,
    alignment: VerticalAlignment,
    why: string,
    groupIndex = 0,
): Promise<void> {
    await expect
        .poll(
            async () => getVerticalAlignment(page, groupSelector, groupIndex),
            { timeout: kScrollBarWaitMs, message: why },
        )
        .toBe(alignment);
}
