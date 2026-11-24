// Drive and read Bloom Player as the BloomPUB preview shows it: move between its pages, click the
// answers of a choice activity, and hear which sound it plays.
//
// The preview is Bloom Player itself, running in a frame of the Publish tab (showBloomPubPreview in
// helpers/publish.ts returns that frame), so what a test sees here is what a reader sees in Bloom
// Reader. Bloom Player keeps the pages beside the one showing in the document as well, so every
// reader here works on the active slide only.
//
// Bloom Player's code is in the bloom-player repository. The choice activities are
// src/activities/domActivities/SimpleDomChoice.ts there, and their sounds are played by
// playCorrectOrWrongSound in src/shared/dragActivityRuntime.ts.

import * as Path from "node:path";
import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { getPages } from "./bookMaking";
import type { IRect } from "./geometry";

/** The page Bloom Player is showing. */
export function activePreviewPage(player: Frame): Locator {
    return player.locator(".swiper-slide-active .bloom-page");
}

/** The id of the page Bloom Player is showing, which is the id the page has in the book. */
export async function getActivePreviewPageId(player: Frame): Promise<string> {
    const shown = activePreviewPage(player);
    await expect(shown, "Bloom Player is not showing a page.").toHaveCount(1, {
        timeout: 30000,
    });
    return (await shown.getAttribute("id")) ?? "";
}

/**
 * Move Bloom Player to the page with this id by clicking its Next and Previous arrows, the way a
 * reader does, and return once it is showing that page.
 *
 * Bloom Player puts only the page showing and its two neighbours in the document (the rest are
 * empty slides until the reader comes near them), so which way to go is worked out from the
 * order of the book's pages in Bloom (getPages), which is the order Bloom Player shows them in.
 *
 * Just after the book loads, Bloom Player ignores the arrows for a moment while it settles on its
 * first page (finishUp in bloom-player-core); there is nothing on the page that says when that is
 * over. So each click is checked by whether the page changed, and a click that changed nothing is
 * made again, rather than waiting out a fixed time first.
 */
export async function goToPreviewPage(
    page: Page,
    player: Frame,
    pageId: string,
): Promise<void> {
    const order = (await getPages(page)).map((p) => p.id);
    const target = order.indexOf(pageId);
    if (target < 0)
        throw new Error(
            `The book has no page "${pageId}". Its pages are: ${order.join(", ")}.`,
        );
    // Enough clicks to cross the whole book twice over, so a few ignored ones do not matter.
    for (let attempt = 0; attempt < order.length * 2 + 10; attempt++) {
        await dismissBigPlayButton(player);
        const shown = await getActivePreviewPageId(player);
        if (shown === pageId) return;
        const arrow =
            order.indexOf(shown) < target
                ? player.locator(".swiper-button-next")
                : player.locator(".swiper-button-prev");
        await arrow.click();
        await expect
            .poll(() => getActivePreviewPageId(player), { timeout: 3000 })
            .not.toBe(shown)
            .catch(() => {
                // The click was ignored; the loop clicks again.
            });
    }
    throw new Error(
        `Bloom Player never reached page "${pageId}"; it is showing "${await getActivePreviewPageId(player)}".`,
    );
}

/**
 * When the browser will not let Bloom Player start sound by itself, it covers the page with a big
 * Play button, and a reader's first act is to tap it. Tap it if it is there.
 */
async function dismissBigPlayButton(player: Frame): Promise<void> {
    const bigPlayButton = player.locator(".bigButtonOverlay .bigPlayButton");
    if (await bigPlayButton.isVisible()) {
        await bigPlayButton.click();
        await bigPlayButton.waitFor({ state: "hidden" });
    }
}

/** Which kind of answer a choice is, as the page marks it in data-activityRole. */
export type ChoiceRole = "correct-answer" | "wrong-answer";

/** One answer button of a choice activity on the page Bloom Player is showing. */
export interface IPreviewChoice {
    role: ChoiceRole;
    /** The text the button shows, trimmed; empty for a picture button. */
    text: string;
    /** Bloom Player marks a button chosen-correct when the reader picks the right answer. */
    chosenCorrect: boolean;
    /** Bloom Player marks a button chosen-wrong when the reader picks a wrong answer. */
    chosenWrong: boolean;
    /** Where the button is drawn. */
    rect: IRect;
    /** Where the button's text is drawn: the extent of its letters, descenders included. */
    textRect: IRect | undefined;
}

/**
 * The answer buttons of the choice activity on the page Bloom Player is showing, in the order the
 * reader sees them. Bloom Player shuffles them as it loads the book, so this order is the one to
 * compare with the order in the Edit tab.
 */
export async function getPreviewChoices(
    player: Frame,
): Promise<IPreviewChoice[]> {
    const buttons = activePreviewPage(player).locator(
        "button.player-button[data-activityrole]",
    );
    await expect(
        buttons.first(),
        "The page Bloom Player is showing has no answer buttons.",
    ).toBeVisible({ timeout: 30000 });
    return buttons.evaluateAll((elements) =>
        elements.map((button) => {
            const toRect = (r: DOMRect) => ({
                x: r.x,
                y: r.y,
                width: r.width,
                height: r.height,
            });
            // Only the language the reader sees is displayed; the others are hidden boxes.
            const shownText = Array.from(
                button.querySelectorAll<HTMLElement>(".bloom-editable"),
            ).find((box) => box.offsetParent !== null);
            let textRect: ReturnType<typeof toRect> | undefined;
            if (shownText && shownText.innerText.trim()) {
                const range = button.ownerDocument.createRange();
                range.selectNodeContents(shownText);
                textRect = toRect(range.getBoundingClientRect());
            }
            return {
                role: button.getAttribute("data-activityrole") as
                    | "correct-answer"
                    | "wrong-answer",
                text: shownText?.innerText.trim() ?? "",
                chosenCorrect: button.classList.contains("chosen-correct"),
                chosenWrong: button.classList.contains("chosen-wrong"),
                rect: toRect(button.getBoundingClientRect()),
                textRect,
            };
        }),
    );
}

/**
 * Click an answer button on the page Bloom Player is showing, the way a reader taps it: the
 * `index`th button of that kind, counted in the order the reader sees them. Returns once Bloom
 * Player has marked it chosen.
 */
export async function clickPreviewChoice(
    player: Frame,
    role: ChoiceRole,
    index = 0,
): Promise<void> {
    await dismissBigPlayButton(player);
    const button = activePreviewPage(player)
        .locator(`button.player-button[data-activityrole="${role}"]`)
        .nth(index);
    await button.click();
    await expect(
        button,
        `Bloom Player did not mark the ${role} it was clicked.`,
    ).toHaveClass(
        role === "correct-answer" ? /chosen-correct/ : /chosen-wrong/,
        { timeout: 15000 },
    );
}

/**
 * Start noting every sound Bloom Player plays from here on. Bloom Player plays an activity's sounds
 * through audio elements (playSound in its dragActivityRuntime.ts); this wraps their play() so that
 * each call records the file it was asked to play, then plays it as before. The record lives in the
 * player's document, so it ends when the preview is rebuilt; call this again after that.
 */
export async function startRecordingPreviewSounds(
    player: Frame,
): Promise<void> {
    await player.evaluate(() => {
        const w = window as unknown as { e2eSoundsPlayed?: string[] };
        if (w.e2eSoundsPlayed) {
            w.e2eSoundsPlayed.length = 0;
            return;
        }
        w.e2eSoundsPlayed = [];
        const originalPlay = HTMLMediaElement.prototype.play;
        HTMLMediaElement.prototype.play = function (this: HTMLMediaElement) {
            w.e2eSoundsPlayed!.push(this.currentSrc || this.src);
            return originalPlay.call(this);
        };
    });
}

/**
 * The file names of the sounds Bloom Player has played since startRecordingPreviewSounds, in order,
 * e.g. ["wrong_answer.mp3"]. Bloom Player's built-in sounds are served with a hash in their name
 * (right_answer-1a2b3c.mp3); this returns them without it.
 */
export async function getPreviewSoundsPlayed(player: Frame): Promise<string[]> {
    const played = await player.evaluate(
        () =>
            (window as unknown as { e2eSoundsPlayed?: string[] })
                .e2eSoundsPlayed,
    );
    if (!played)
        throw new Error(
            "Nothing is recording Bloom Player's sounds; call startRecordingPreviewSounds first.",
        );
    return played.map((url) => {
        const name = Path.posix.basename(new URL(url).pathname);
        return decodeURIComponent(name).replace(
            /^(right_answer|wrong_answer)-[^.]+\.mp3$/,
            "$1.mp3",
        );
    });
}

/**
 * Wait until Bloom Player has played exactly these sounds since startRecordingPreviewSounds, in
 * this order, by file name (see getPreviewSoundsPlayed).
 */
export async function expectPreviewSoundsPlayed(
    player: Frame,
    expected: string[],
    message: string,
): Promise<void> {
    await expect
        .poll(() => getPreviewSoundsPlayed(player), { timeout: 15000, message })
        .toEqual(expected);
}
