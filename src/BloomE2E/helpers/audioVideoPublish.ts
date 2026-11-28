// Drive and read the Publish tab's Audio or Video screen: its "Configure & Preview" step, where
// Bloom shows the book in Bloom Player (in its video preview mode) above Bloom's own Reset, Play
// and Pause buttons.
//
// Bloom Player's red buttons above the book (the language chooser among them) set the book up for
// recording. Bloom Player reports each choice to Bloom (publish/av/videoSettings), which keeps it in
// the book's publish settings and hands it to the window that records the video.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGet } from "./api";
import { waitForPreviewToSettle } from "./bloomPlayer";
import {
    refreshBloomPubPreview,
    selectPublishDestination,
    showBloomPubPreview,
} from "./publish";

/** The "Configure & Preview" step: the one that holds the Preview button. */
function configureAndPreviewStep(page: Page): Locator {
    return page
        .locator(".MuiStep-root")
        .filter({ has: page.locator('[aria-label="refresh preview"]') });
}

/**
 * Bloom's own Play button under the preview (not Bloom Player's, nor the Check Recording step's).
 * The preview's three buttons have no label; each is told apart by its Material UI icon, which
 * carries its name as a test id.
 */
export function audioVideoPlayButton(page: Page): Locator {
    return configureAndPreviewStep(page).locator(
        'button:has([data-testid="PlayCircleFilledWhiteIcon"])',
    );
}

/**
 * Open the Audio or Video screen on the selected book, press its Preview button, and return Bloom
 * Player's frame once it shows the book.
 */
export async function showAudioVideoPreview(page: Page): Promise<Frame> {
    await selectPublishDestination(page, "Audio or Video");
    const player = await showBloomPubPreview(page);
    await waitForAudioVideoPreviewReady(page, player);
    return player;
}

/**
 * Press the Preview button again, as a person does to rebuild the preview, and return Bloom
 * Player's frame once it shows the rebuilt book. `player` is the frame the preview showed before.
 */
export async function showAudioVideoPreviewAgain(
    page: Page,
    player: Frame,
): Promise<Frame> {
    const rebuilt = await refreshBloomPubPreview(page, player);
    await waitForAudioVideoPreviewReady(page, rebuilt);
    return rebuilt;
}

/**
 * Wait until Bloom has finished making the preview and Bloom Player is showing the book's pages.
 * Bloom enables its Play button only once its progress dialog for the preview is done (and the
 * book's license allows it); until then Bloom may still replace the document in the preview's
 * frame, so the book showing there earlier does not mean the preview is ready.
 */
async function waitForAudioVideoPreviewReady(
    page: Page,
    player: Frame,
): Promise<void> {
    await expect(
        audioVideoPlayButton(page),
        "Bloom's Play button under the Audio or Video preview never became enabled.",
    ).toBeEnabled({ timeout: 60000 });
    // A language chosen before Bloom Player has settled on the book's first page is laid out on
    // pages Bloom Player is still setting up, and then the preview never plays to its end.
    await waitForPreviewToSettle(player);
}

/**
 * Choose a language in Bloom Player's language chooser (its "Choose Language" button above the
 * book), by the name the chooser shows for it. Choosing a language closes the chooser.
 */
export async function choosePreviewLanguage(
    player: Frame,
    languageName: string,
): Promise<void> {
    await player.locator('[aria-label="Choose Language"]').click();
    const menu = player.locator('[aria-label="languages"]');
    await menu.waitFor({ state: "visible", timeout: 30000 });
    await menu.locator("label").filter({ hasText: languageName }).click();
    await menu.waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * Press Bloom's Play button under the preview and wait until the preview has played to its end.
 * While it plays, Bloom shows a spinner in the Play button's place; when Bloom Player tells Bloom
 * the playback is complete, Bloom shows the Play button again.
 */
export async function playAudioVideoPreviewToTheEnd(page: Page): Promise<void> {
    const play = audioVideoPlayButton(page);
    await play.click();
    await expect(
        play,
        "Bloom's Play button did not give way to its spinner when pressed.",
    ).toHaveCount(0, { timeout: 15000 });
    await expect(
        play,
        "The Audio or Video preview never finished playing (Bloom never showed its Play button again).",
    ).toBeVisible({ timeout: 180000 });
}

/**
 * The language that Bloom keeps for recording the book's video: the `lang` of the settings Bloom
 * Player last reported from the preview, as Bloom saved them in the book's publish settings, or
 * undefined when it has none.
 */
export async function getAudioVideoRecordingLanguage(
    page: Page,
): Promise<string | undefined> {
    // Bloom Player sends the settings URL-encoded, and Bloom keeps them as an opaque string.
    const saved = (await apiGet(page, "publish/av/videoSettings")).body.trim();
    if (!saved) return undefined;
    return (JSON.parse(decodeURIComponent(saved)) as { lang?: string }).lang;
}
