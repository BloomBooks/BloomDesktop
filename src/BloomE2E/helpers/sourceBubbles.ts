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
