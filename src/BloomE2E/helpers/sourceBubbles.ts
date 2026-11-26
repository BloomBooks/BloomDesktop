// What a text box shows, and the source bubble beside it.
//
// A translation group holds one box per language. Which of them show depends on the book's
// content languages and on the group's own language setting (Text Box Properties). The text of the
// other languages goes into the group's source bubble, a qTip tooltip that Bloom puts inside the
// page's own document and ties to the group through aria-describedby (BloomSourceBubbles.tsx).
// Each language in the bubble is a copy of its box with the class "source-text".

import { expect, type Page } from "@playwright/test";
import { clickInGroup, editablePageFrame } from "./bookMaking";

/** One language's text, as a box or a source bubble shows it. */
export interface ILanguageText {
    lang: string;
    text: string;
}

/**
 * The languages of the boxes of one translation group that are showing on the page, with the text
 * in each, in the order they appear. `groupSelector` and `groupIndex` pick the group as for
 * typeInGroup.
 */
export async function getShownBoxes(
    page: Page,
    groupSelector: string,
    groupIndex = 0,
): Promise<ILanguageText[]> {
    return editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex)
        .locator(".bloom-editable")
        .evaluateAll((boxes) =>
            boxes
                .filter(
                    (box) => (box as HTMLElement).getClientRects().length > 0,
                )
                .map((box) => ({
                    lang: box.getAttribute("lang") ?? "",
                    text: (box as HTMLElement).innerText.trim(),
                })),
        );
}

/** A translation group's source bubble, as a person looking at the group sees it. */
export interface ISourceBubble {
    /** Whether the group has a source bubble and it is showing on the page. */
    shown: boolean;
    /** The languages the bubble offers, with the text of each, sorted by language. */
    texts: ILanguageText[];
}

/**
 * One translation group's source bubble: whether it shows, and the languages it offers with the
 * text of each. `texts` is empty when the group has no source bubble.
 *
 * The bubble is shown when its group has the focus, so this first clicks in the group's box in
 * `shownLanguage`, the way a person looks at the bubble. Then it waits until the bubble holds
 * still, because Bloom rebuilds a bubble after a change of focus.
 */
export async function getSourceBubble(
    page: Page,
    groupSelector: string,
    shownLanguage: string,
    groupIndex = 0,
): Promise<ISourceBubble> {
    await clickInGroup(page, groupSelector, shownLanguage, groupIndex);
    const group = editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex);
    const read = async (): Promise<ISourceBubble> => {
        const bubbleId = await group.getAttribute("aria-describedby");
        if (!bubbleId) return { shown: false, texts: [] };
        const bubble = editablePageFrame(page).locator(`[id="${bubbleId}"]`);
        const shown = await bubble.isVisible();
        const texts = await bubble
            .locator(".source-text")
            .evaluateAll((sources) =>
                sources.map((source) => ({
                    lang: source.getAttribute("lang") ?? "",
                    // The bubble adds a copy button to each language, whose label ("Copied") is in
                    // the same element; leave it out of the text.
                    text: (() => {
                        const copy = source.cloneNode(true) as HTMLElement;
                        copy.querySelectorAll(".source-copy-button").forEach(
                            (button) => button.remove(),
                        );
                        return (copy.textContent ?? "").trim();
                    })(),
                })),
            );
        return {
            shown,
            texts: texts.sort((a, b) => a.lang.localeCompare(b.lang)),
        };
    };
    let last = "";
    await expect
        .poll(
            async () => {
                const now = JSON.stringify(await read());
                const steady = now === last;
                last = now;
                return steady;
            },
            {
                timeout: 15000,
                intervals: [250],
                message: "The source bubble never held still.",
            },
        )
        .toBe(true);
    return JSON.parse(last) as ISourceBubble;
}

/**
 * Wait until Bloom has finished setting up the source bubbles of a page it has just loaded.
 *
 * Bloom makes a page's source bubbles in a timer after the page loads (bloomEditing.ts, where it
 * calls makeSourceBubblesIntoQtips), and the last thing that timer does is put the insertion point
 * in the page's first text box. So once a box of the group has the focus without the test having
 * clicked, the bubbles are there, and a group without one has none still to come. This holds for a
 * page Bloom rebuilt or opened from the Pages list, whose first text box is a plain text box (not a
 * canvas element), as on a "Just Text" page; Bloom gives a page opened by a click in the Pages grid
 * no focus.
 */
export async function waitForSourceBubbleSetup(
    page: Page,
    groupSelector: string,
    groupIndex = 0,
): Promise<void> {
    await expect(
        editablePageFrame(page)
            .locator(groupSelector)
            .nth(groupIndex)
            .locator(".bloom-editable:focus"),
        "Bloom never put the insertion point in the text box, which it does once the page's source bubbles are set up.",
    ).toHaveCount(1, { timeout: 30000 });
}

/**
 * Bring one language of the shown source bubble of a group forward by its tab, the way a person
 * reads it, and check that the bubble then shows that language's text and hides the others'.
 * The bubble must be showing (getSourceBubble reports that).
 */
export async function expectSourceBubbleLanguageReadable(
    page: Page,
    groupSelector: string,
    language: string,
    text: string,
    groupIndex = 0,
): Promise<void> {
    const bubbleId = await editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex)
        .getAttribute("aria-describedby");
    if (!bubbleId) throw new Error("The group has no source bubble.");
    const bubble = editablePageFrame(page).locator(`[id="${bubbleId}"]`);
    const shown = bubble.locator(`.source-text[lang="${language}"]`);
    if (!(await shown.isVisible()))
        await bubble.locator(`li[id="${language}"] a.sourceTextTab`).click();
    await expect(
        shown,
        `The source bubble's ${language} tab does not show the ${language} text.`,
    ).toBeVisible({ timeout: 15000 });
    await expect(shown).toContainText(text);
    await expect(
        bubble.locator(`.source-text:not([lang="${language}"]):visible`),
        `While the source bubble shows ${language}, it also shows another language.`,
    ).toHaveCount(0);
}

/**
 * How many pictures the shown source bubble of a group holds, counting every img element in it
 * apart from the bubble's own buttons. A source bubble is for reading another language's text, so
 * a picture that is part of that text, such as an inline image, must not come along into it. The
 * bubble must be showing (getSourceBubble reports that).
 */
export async function countPicturesInSourceBubble(
    page: Page,
    groupSelector: string,
    groupIndex = 0,
): Promise<number> {
    const bubbleId = await editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex)
        .getAttribute("aria-describedby");
    if (!bubbleId) throw new Error("The group has no source bubble.");
    return editablePageFrame(page)
        .locator(`[id="${bubbleId}"] .source-text img`)
        .count();
}
