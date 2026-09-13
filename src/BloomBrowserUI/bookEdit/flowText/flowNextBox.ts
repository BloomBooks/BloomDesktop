// Whether a chain has a box on a page after the page being edited, and what that box holds.
//
// Only C# can say: the boxes of the other pages are not in the edit iframe, so the answer is a
// round trip (flowBoundaryClient.peekNext). Two things drawn on the page being edited turn on
// that one answer — the label saying the text flows out to a later page (flowToLabel) and the
// button offering to make the pages the text still needs (flowCreatePagesButton) — so the answer
// is asked for once, kept here, and handed to every caller that was waiting for it.
//
// The answer can only change when the chain itself changes, and the pass that changes it clears
// this.

import { INextBox, peekNext } from "./flowBoundaryClient";

const answerByPageChainAndLang = new Map<string, INextBox | undefined>();
// Which callers are waiting for a round trip that is already running, per key.
const waiting = new Map<string, Array<(page: HTMLElement) => void>>();

/**
 * Forget what C# said about the box after this page. Call this when the page being edited
 * changes, and after anything that changes which boxes are in which chain.
 */
export function resetNextBoxCache(): void {
    answerByPageChainAndLang.clear();
    waiting.clear();
}

/**
 * Has C# answered yet for this page, chain and language? A caller that draws something only when
 * the chain has NO box on a later page has to know the difference between "there is none" and
 * "nobody has said yet", because both read as undefined below.
 */
export function isNextBoxAnswerKnown(
    page: HTMLElement,
    chainId: string,
    language: string,
): boolean {
    return answerByPageChainAndLang.has(`${page.id}|${chainId}|${language}`);
}

/**
 * The box of this chain on a page after this one, or undefined when the chain ends on this page.
 *
 * The first call for a page, chain and language starts the round trip and reports nothing;
 * whenAnswered is called with the page once the answer is in, so the caller can draw itself
 * again. A caller that asks while a round trip is running is added to the ones it will tell.
 */
export function getNextBoxOnLaterPage(
    page: HTMLElement,
    chainId: string,
    language: string,
    whenAnswered: (page: HTMLElement) => void,
): INextBox | undefined {
    const key = `${page.id}|${chainId}|${language}`;
    if (answerByPageChainAndLang.has(key)) {
        return answerByPageChainAndLang.get(key);
    }

    const alreadyWaiting = waiting.get(key);
    if (alreadyWaiting) {
        if (!alreadyWaiting.includes(whenAnswered)) {
            alreadyWaiting.push(whenAnswered);
        }
        return undefined;
    }

    waiting.set(key, [whenAnswered]);
    peekNext(chainId, page.id, language)
        .then((found) => {
            const toTell = waiting.get(key) ?? [];
            waiting.delete(key);
            answerByPageChainAndLang.set(key, found);
            if (page.isConnected) {
                toTell.forEach((tell) => tell(page));
            }
        })
        .catch(() => {
            // Without an answer there is nothing to draw. The next pass asks again.
            waiting.delete(key);
        });
    return undefined;
}
