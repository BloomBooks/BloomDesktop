// The Music tool puts background music on a page, plays it in the toolbox, and the BloomPUB preview
// plays it on that page and the pages after it until another page starts different music or no
// music. The choice survives selecting the book again and restarting Bloom.
//
// Automates "Music pages" (Test Case ID 557). "The music plays" is checked as the audio element
// playing the expected file with its time moving on; nobody listens.

import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Frame, Locator, Page } from "@playwright/test";
import { expect, test, type IBloomApp } from "../fixtures/bloomTest";
import {
    addPage,
    editBook,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { armFileChooser } from "../helpers/talkingBook";
import { enableToolForBook, expandTool } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

// The Music tool is behind a subscription tier (RequiresSubscriptionOverlayWrapper).
test.use({
    collectionSpec: {
        name: "music-tool",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const sample = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "audio",
    "sample.mp3",
);

/**
 * Two copies of the suite's sample, under different names, so the two pieces of music differ.
 * The caller deletes `folder`.
 */
function makeMusicFiles(): { folder: string; first: string; second: string } {
    const folder = fs.mkdtempSync(Path.join(os.tmpdir(), "bloom-e2e-music-"));
    const first = Path.join(folder, "first-tune.mp3");
    const second = Path.join(folder, "second-tune.mp3");
    fs.copyFileSync(sample, first);
    fs.copyFileSync(sample, second);
    return { folder, first, second };
}

/** Open the Music tool for the page being shown and return the toolbox frame. */
async function openMusicTool(page: Page): Promise<Frame> {
    return expandTool(page, "music", ".musicBody");
}

function player(frame: Frame): Locator {
    return frame.locator("#musicPlayer");
}

interface IAudioState {
    paused: boolean;
    time: number;
    volume: number;
    src: string;
}

async function audioState(audio: Locator): Promise<IAudioState> {
    return audio.evaluate((el) => {
        const a = el as HTMLAudioElement;
        return {
            paused: a.paused,
            time: a.currentTime,
            volume: a.volume,
            src: decodeURIComponent(a.currentSrc || a.src || ""),
        };
    });
}

/** Wait until `audio` is playing a file whose name ends with `fileName`, its time moving on. */
async function expectPlaying(audio: Locator, fileName: string, what: string) {
    await expect
        .poll(async () => audioState(audio), {
            timeout: 30000,
            message: `${what}: the music should be playing ${fileName}.`,
        })
        .toMatchObject({
            paused: false,
            src: expect.stringContaining(fileName),
        });
    // bloom-player loops the music back to the start when it ends, so a time read just before the
    // end may never be passed. Any other time counts as moving on, as long as it is past the
    // start and the player still plays: music that went back to the start and stalled there
    // does not count.
    const start = (await audioState(audio)).time;
    await expect
        .poll(
            async () => {
                // The whole state goes in, so a failure shows what the player was doing.
                const now = await audioState(audio);
                return {
                    ...now,
                    start,
                    movedOn: !now.paused && now.time > 0 && now.time !== start,
                };
            },
            {
                timeout: 15000,
                message: `${what}: the music's time should move on while it plays.`,
            },
        )
        .toMatchObject({ movedOn: true });
}

/** The page attributes the Music tool writes. */
async function pageMusic(page: Page) {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .evaluate((p) => ({
            audio: p.getAttribute("data-backgroundaudio"),
            volume: p.getAttribute("data-backgroundaudiovolume"),
        }));
}

/** Choose a music file for the page being shown, through the tool's Choose... link. */
async function chooseMusic(page: Page, frame: Frame, file: string) {
    await armFileChooser(page, file);
    await frame.locator(".musicChooseFile").click();
    await expect
        .poll(async () => (await pageMusic(page)).audio, {
            timeout: 30000,
            message: `Choosing ${Path.basename(file)} did not set the page's music.`,
        })
        .toBe(Path.basename(file));
}

async function sliderPosition(frame: Frame): Promise<number> {
    return Number(
        await frame
            .locator(
                '.musicVolumeSlider [role="slider"], .musicVolumeSlider input[type="range"]',
            )
            .first()
            .getAttribute("aria-valuenow"),
    );
}

test("music set on pages plays in the tool and in the BloomPUB preview, and survives a restart [Test Case ID 557]", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(420000);
    const { folder, first, second } = makeMusicFiles();
    try {
        await runMusicTest(page, bloomApp, first, second);
    } finally {
        fs.rmSync(folder, { recursive: true, force: true, maxRetries: 3 });
    }
});

async function runMusicTest(
    page: Page,
    bloomApp: IBloomApp,
    first: string,
    second: string,
) {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", 4);
    await enableToolForBook(page, bookFolder, "music");
    const pages: IBookPage[] = await getContentPages(page);
    expect(pages.length).toBeGreaterThanOrEqual(4);
    // A BloomPUB leaves out pages with nothing on them, so give each page some text.
    for (const [n, p] of pages.entries()) {
        await goToPage(page, p.id);
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            `Page ${n + 1} of the music book.`,
        );
    }

    // Page 1: choose music, play, pause, play, change the volume.
    await goToPage(page, pages[0].id);
    let frame = await openMusicTool(page);
    await chooseMusic(page, frame, first);
    await frame.locator("#musicPreview").click();
    await expectPlaying(
        player(frame),
        "first-tune.mp3",
        "After Play on page 1",
    );
    await frame.locator("#musicPreview").click();
    // A paused audio element's time does not move on, so `paused` is the whole check.
    await expect
        .poll(async () => (await audioState(player(frame))).paused, {
            message: "Pause should stop the music.",
        })
        .toBe(true);
    await frame.locator("#musicPreview").click();
    await expectPlaying(
        player(frame),
        "first-tune.mp3",
        "After Play again on page 1",
    );
    const volumeBefore = (await audioState(player(frame))).volume;
    const thumb = frame
        .locator(
            '.musicVolumeSlider [role="slider"], .musicVolumeSlider input[type="range"]',
        )
        .first();
    await thumb.focus();
    for (let i = 0; i < 20; i++) await thumb.press("ArrowRight");
    await expect
        .poll(async () => (await audioState(player(frame))).volume, {
            message:
                "Moving the volume slider should change the player's volume.",
        })
        .toBeGreaterThan(volumeBefore);
    const page1Volume = (await audioState(player(frame))).volume;
    const page1Slider = await sliderPosition(frame);
    await expect
        .poll(async () => Number((await pageMusic(page)).volume))
        .toBeCloseTo(page1Volume, 2);
    await frame.locator("#musicPreview").click(); // stop it before leaving the page

    // Page 3: different music; the slider starts where it was left on page 1.
    await goToPage(page, pages[2].id);
    frame = await openMusicTool(page);
    await chooseMusic(page, frame, second);
    expect(
        await sliderPosition(frame),
        "The volume slider on page 3 should start at the level set on page 1.",
    ).toBe(page1Slider);

    // Page 4: No Music disables Play and the volume.
    await goToPage(page, pages[3].id);
    frame = await openMusicTool(page);
    await frame.locator('input[type="radio"][value="noMusic"]').check();
    await expect(
        frame.locator("#musicOuterWrapper"),
        "No Music should disable Play and the volume control.",
    ).toHaveClass(/\bdisabled\b/);
    expect(
        await frame
            .locator(".musicVolumeSlider")
            .evaluate(
                (el) =>
                    el.classList.contains("Mui-disabled") ||
                    !!el.querySelector(
                        ".Mui-disabled, [aria-disabled='true'], input:disabled",
                    ),
            ),
        "The volume slider should be disabled with No Music.",
    ).toBe(true);
    // Leave page 4 so it is saved before publishing.
    await goToPage(page, pages[0].id);

    // The BloomPUB preview plays each page's music.
    // A book in one language shows no language list, so wait for the preview rather than for it.
    await selectPublishDestination(page, "BloomPUB");
    const preview = await showBloomPubPreview(page);
    const previewMusic = preview.locator("#music-player");
    // bloom-player renders only the pages beside the one it shows, so walk the whole preview with
    // its Next Page button and check, on each page of the book, what its music does. bloom-player
    // pauses `#music-player` on a page with no music.
    const shownPageId = () =>
        preview.evaluate(
            () =>
                document.querySelector(".swiper-slide-active .bloom-page")
                    ?.id ?? "",
        );
    const musicOnPage: { id: string; fileName: string; what: string }[] = [
        {
            id: pages[0].id,
            fileName: "first-tune.mp3",
            what: "Music should start on page 1 of the preview",
        },
        {
            id: pages[1].id,
            fileName: "first-tune.mp3",
            what: "The music should continue on page 2 of the preview",
        },
        {
            id: pages[2].id,
            fileName: "second-tune.mp3",
            what: "Different music should start on page 3 of the preview",
        },
    ];
    // For a moment after it shows a book, bloom-player lets its page swiper settle and starts no
    // page's music, even on a page turned to meanwhile (startingUpSwiper in
    // bloom-player-core.tsx). That moment ends when it gives the Next Page button its tab order.
    await expect(
        preview.locator('.swiper-button-next[tabindex="5"]'),
        "bloom-player never finished starting up",
    ).toHaveCount(1, { timeout: 30000 });
    const seen: string[] = [];
    let reachedPage4 = false;
    for (let step = 0; step < 30; step++) {
        await expect.poll(shownPageId, { timeout: 15000 }).not.toBe("");
        const id = await shownPageId();
        seen.push(id);
        reachedPage4 ||= id === pages[3].id;
        const music = musicOnPage.find((m) => m.id === id);
        if (music)
            await expectPlaying(previewMusic, music.fileName, music.what);
        else if (reachedPage4)
            await expect
                .poll(async () => (await audioState(previewMusic)).paused, {
                    timeout: 15000,
                    message: `No music should play on page 4 or after (page ${id}).`,
                })
                .toBe(true);
        const next = preview.getByRole("button", {
            name: "Next Page",
            exact: true,
        });
        if ((await next.count()) === 0) break;
        await next.click();
        await expect.poll(shownPageId, { timeout: 15000 }).not.toBe(id);
    }
    for (const [n, p] of pages.slice(0, 4).entries())
        expect(
            seen,
            `The preview should show page ${n + 1} of the book.`,
        ).toContain(p.id);

    // Select another book and this one again: page 1 plays its music.
    await switchTab(page, "collection");
    const otherBook = await makeBookFromTemplate(page, "Basic Book");
    await switchTab(page, "collection");
    await selectBook(page, otherBook);
    await editBook(page, bookFolder);
    await goToPage(page, pages[0].id);
    frame = await openMusicTool(page);
    await frame.locator("#musicPreview").click();
    await expectPlaying(
        player(frame),
        "first-tune.mp3",
        "Page 1 after selecting the book again",
    );
    await frame.locator("#musicPreview").click();

    // Restart Bloom: page 3 plays its music.
    const restarted = await bloomApp.quitAndRestart();
    await editBook(restarted, bookFolder);
    await goToPage(restarted, pages[2].id);
    frame = await openMusicTool(restarted);
    await frame.locator("#musicPreview").click();
    await expectPlaying(
        player(frame),
        "second-tune.mp3",
        "Page 3 after restarting Bloom",
    );
}
