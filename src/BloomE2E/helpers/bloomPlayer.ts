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
 * Every click must turn the page: one that does not fails the test.
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
    await waitForPreviewToSettle(player);
    // One turn for each page of the book is enough to reach any page from any other.
    for (let turns = 0; turns <= order.length; turns++) {
        await dismissBigPlayButton(player);
        const shown = await getActivePreviewPageId(player);
        if (shown === pageId) return;
        const forward = order.indexOf(shown) < target;
        await player
            .locator(forward ? ".swiper-button-next" : ".swiper-button-prev")
            .click();
        await expect
            .poll(() => getActivePreviewPageId(player), {
                timeout: 30000,
                message: `Bloom Player's ${forward ? "Next" : "Previous"} arrow did not turn the page.`,
            })
            .not.toBe(shown);
    }
    throw new Error(
        `Bloom Player never reached page "${pageId}"; it is showing "${await getActivePreviewPageId(player)}".`,
    );
}

/**
 * Whether Bloom Player is showing its Previous and Next arrows, as a reader sees them: an arrow
 * counts as shown when the browser draws it (checkVisibility), not merely when it is in the
 * document. A page whose player options say hideNavigation, such as a Captive Widget Page, has
 * Bloom Player hide both.
 */
export async function getPreviewArrowsShown(
    player: Frame,
): Promise<{ previous: boolean; next: boolean }> {
    const shown = async (selector: string) => {
        const arrows = player.locator(selector);
        await expect(
            arrows.first(),
            `Bloom Player has no "${selector}" arrow in its document at all.`,
        ).toBeAttached({ timeout: 30000 });
        return arrows.evaluateAll((elements) =>
            elements.some((e) => e.checkVisibility()),
        );
    };
    return {
        previous: await shown(".swiper-button-prev"),
        next: await shown(".swiper-button-next"),
    };
}

/**
 * Wait until Bloom Player has settled on the first page of a book it has just loaded. Until then
 * it holds the book on that page, so a click on an arrow is undone (startingUpSwiper in
 * bloom-player-core). When it lets go, it makes the arrows reachable with the Tab key by giving
 * them a tabindex, in the same step; that is the only thing in the document that says so.
 */
async function waitForPreviewToSettle(player: Frame): Promise<void> {
    await expect(
        player.locator(".swiper-button-next[tabindex]").first(),
        "Bloom Player never finished settling on the first page of the book.",
    ).toBeAttached({ timeout: 30000 });
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
    /**
     * The file name of the picture the button shows, without its folder or extension; empty for
     * a word button, for a picture Bloom Player has not loaded yet, and for one whose file
     * the browser cannot draw.
     */
    picture: string;
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
    return buttons.evaluateAll(async (elements) => {
        // Bloom Player draws a button's picture as the background of its canvas, once the page
        // is near enough to showing to be worth loading. A background that names a file the
        // browser cannot draw shows the reader nothing, so the name counts only when the
        // file decodes as a picture.
        const pictureName = async (within: Element) => {
            const canvas = within.querySelector<HTMLElement>(".bloom-canvas");
            const url = /url\("(.*)"\)/.exec(
                canvas?.style.backgroundImage ?? "",
            )?.[1];
            if (!url) return "";
            const picture = new Image();
            picture.src = url;
            const drawable = await picture.decode().then(
                () => true,
                () => false,
            );
            if (!drawable) return "";
            return decodeURIComponent(
                new URL(url).pathname.split("/").pop()!,
            ).replace(/\.[^.]*$/, "");
        };
        const pictures = await Promise.all(elements.map(pictureName));
        return elements.map((button, index) => {
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
            const doc = button.ownerDocument;
            const letters =
                shownText &&
                doc
                    .createTreeWalker(shownText, NodeFilter.SHOW_TEXT, {
                        acceptNode: (node) =>
                            node.nodeValue!.trim()
                                ? NodeFilter.FILTER_ACCEPT
                                : NodeFilter.FILTER_SKIP,
                    })
                    .nextNode();
            if (letters) {
                // The box the browser gives a run of text is as tall as the font allows any
                // letter to be, which for Andika is well above and below the letters of a
                // word. What the reader sees is the ink, so work that out from the font's
                // measurements of this word: how far its letters rise above the baseline
                // and how far they fall below it. Those are in the font's own pixels, and
                // Bloom Player draws the page at a scale, so they are scaled by how tall the
                // browser drew the run. An answer is one word, so the run is one line.
                const range = doc.createRange();
                range.selectNodeContents(letters);
                const run = range.getBoundingClientRect();
                const style = getComputedStyle(letters.parentElement!);
                const canvas = doc.createElement("canvas").getContext("2d")!;
                canvas.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                const ink = canvas.measureText(letters.nodeValue!.trim());
                const scale =
                    run.height /
                    (ink.fontBoundingBoxAscent + ink.fontBoundingBoxDescent);
                const baseline = run.y + ink.fontBoundingBoxAscent * scale;
                textRect = {
                    x: run.x,
                    y: baseline - ink.actualBoundingBoxAscent * scale,
                    width: run.width,
                    height:
                        (ink.actualBoundingBoxAscent +
                            ink.actualBoundingBoxDescent) *
                        scale,
                };
            }
            return {
                role: button.getAttribute("data-activityrole") as
                    | "correct-answer"
                    | "wrong-answer",
                text: shownText?.innerText.trim() ?? "",
                picture: pictures[index],
                chosenCorrect: button.classList.contains("chosen-correct"),
                chosenWrong: button.classList.contains("chosen-wrong"),
                rect: toRect(button.getBoundingClientRect()),
                textRect,
            };
        });
    });
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

/** Every video on the page Bloom Player is showing. */
export function previewVideos(player: Frame): Locator {
    return activePreviewPage(player).locator(".bloom-videoContainer video");
}

/** A video that Bloom Player played to its end. */
export interface IPreviewVideoPlayed {
    /** The id of the book page the video is on. */
    pageId: string;
    /** The video's file name, without its folder. */
    fileName: string;
}

/**
 * Start recording which videos Bloom Player plays to their end, from now on (see
 * getPreviewVideosPlayed). Call it before the page with the video is shown: Bloom Player plays a
 * page's video as soon as the reader turns to it. The record lives in the player's document, so it
 * ends when the preview is rebuilt; call this again after that.
 *
 * Only reaching the end is recorded, not each start: on the way to playing a video through, Bloom
 * Player starts it, stops it at once and starts it again a second later, so the starts say nothing
 * a reader would notice.
 */
export async function startRecordingPreviewVideos(
    player: Frame,
): Promise<void> {
    await player.evaluate(() => {
        const w = window as unknown as {
            e2eVideosPlayed?: { pageId: string; src: string }[];
        };
        if (w.e2eVideosPlayed) {
            w.e2eVideosPlayed.length = 0;
            return;
        }
        const played: { pageId: string; src: string }[] = [];
        w.e2eVideosPlayed = played;
        // A media element's events do not bubble, so this one is caught on its way down.
        document.addEventListener(
            "ended",
            (e) => {
                if (!(e.target instanceof HTMLVideoElement)) return;
                played.push({
                    pageId: e.target.closest(".bloom-page")?.id ?? "",
                    src: e.target.currentSrc,
                });
            },
            true,
        );
    });
}

/**
 * The videos Bloom Player has played to their end since startRecordingPreviewVideos, in order. A
 * video played through twice is there twice.
 */
export async function getPreviewVideosPlayed(
    player: Frame,
): Promise<IPreviewVideoPlayed[]> {
    const played = await player.evaluate(
        () =>
            (
                window as unknown as {
                    e2eVideosPlayed?: { pageId: string; src: string }[];
                }
            ).e2eVideosPlayed,
    );
    if (!played)
        throw new Error(
            "Nothing is recording Bloom Player's videos; call startRecordingPreviewVideos first.",
        );
    return played.map((video) => ({
        pageId: video.pageId,
        fileName: decodeURIComponent(
            Path.posix.basename(new URL(video.src).pathname),
        ),
    }));
}

/**
 * Wait until Bloom Player has played exactly these videos to their end since
 * startRecordingPreviewVideos, in this order (see getPreviewVideosPlayed).
 */
export async function expectPreviewVideosPlayed(
    player: Frame,
    expected: IPreviewVideoPlayed[],
    message: string,
): Promise<void> {
    await expect
        .poll(() => getPreviewVideosPlayed(player), { timeout: 30000, message })
        .toEqual(expected);
}

/**
 * Tap the only video on the page Bloom Player is showing, as a reader does to play it again once
 * it has stopped.
 */
export async function tapPreviewVideo(player: Frame): Promise<void> {
    const videos = previewVideos(player);
    await expect(
        videos,
        "The page Bloom Player is showing should have one video.",
    ).toHaveCount(1);
    await videos.click();
}
