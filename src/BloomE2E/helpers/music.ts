// The Music tool, and the background music Bloom Player plays from what it saves.
//
// The tool offers three choices for the page being edited (musicToolControls.tsx): "No Music",
// "Continue music from previous page" and "Start new music". Each is kept as an attribute of the
// page div, data-backgroundaudio: "none" for No Music, no attribute at all for Continue, and the
// file's name in the book's audio folder for new music. Bloom Player reads those attributes
// (music.ts in the bloom-player repository) to decide what plays on each page, and plays it
// through one audio element of its own, #music-player.
//
// "Choose..." opens a native file picker, which no automation can drive (AUTOMATION-DEBT.md,
// "Native OS dialogs hang automation"); e2e/nextFileToChoose arms Bloom with the file the picker
// would have returned, as for videos (see videos.ts), and everything after the picker runs as it
// does for a person.

import { expect, type Frame, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiPost } from "./api";
import { bookHtmlPath } from "./bookHtml";
import { expandTool } from "./toolbox";

/** The three choices of the Music tool, by the value its radio buttons carry. */
export type MusicChoice = "noMusic" | "continueMusic" | "newMusic";

/** Expand the Music tool, and return the toolbox frame once its choices are showing. */
export async function openMusicTool(page: Page): Promise<Frame> {
    return expandTool(page, "music", ".musicBody [role='radiogroup']");
}

/**
 * Pick one of the Music tool's choices for the page being edited, by clicking its radio button.
 * Picking "Start new music" alone changes nothing until a file is chosen; see chooseMusicFile.
 */
export async function chooseMusicOption(
    page: Page,
    choice: MusicChoice,
): Promise<void> {
    const frame = await openMusicTool(page);
    await frame.locator(`input[type="radio"][value="${choice}"]`).check();
}

/**
 * Start new music on the page being edited with the sound file at `filePath`, through the Music
 * tool's "Choose..." link, and wait until the tool shows the file. Bloom copies the file into the
 * book's audio folder under the same name.
 */
export async function chooseMusicFile(
    page: Page,
    filePath: string,
): Promise<void> {
    if (!fs.existsSync(filePath))
        throw new Error(`There is no sound file at ${filePath}.`);
    const frame = await openMusicTool(page);
    await apiPost(
        page,
        "e2e/nextFileToChoose",
        Path.resolve(filePath),
        "text/plain",
    );
    await frame.locator(".musicChooseFile").click();
    await expect(
        frame.locator("#musicFilename"),
        `The Music tool never showed ${Path.basename(filePath)} as the page's music.`,
    ).toHaveText(Path.parse(filePath).name, { timeout: 30000 });
}

/** What the Music tool shows for the page being edited. */
export interface IMusicToolState {
    choice: string;
    /** The music file's name without its extension, as the tool shows it; "" when none. */
    fileName: string;
}

/** Read which choice the Music tool has selected for the page being edited, and the file it shows. */
export async function getMusicToolState(page: Page): Promise<IMusicToolState> {
    const frame = await openMusicTool(page);
    return {
        choice:
            (await frame
                .locator('input[type="radio"]:checked')
                .getAttribute("value")) ?? "",
        fileName: (await frame.locator("#musicFilename").innerText()).trim(),
    };
}

/**
 * The data-backgroundaudio attribute of each page in the book's HTML file on disk, by page id:
 * null when the page has none. `page` is used only as a DOM parser.
 */
export async function readSavedPageMusic(
    page: Page,
    bookFolder: string,
): Promise<Record<string, string | null>> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        const result: Record<string, string | null> = {};
        for (const p of Array.from(dom.querySelectorAll(".bloom-page[id]")))
            result[p.id] = p.getAttribute("data-backgroundaudio");
        return result;
    }, html);
}

/** What Bloom Player's background music is doing. */
export interface IPreviewMusic {
    /** The music file's name, or "" when Bloom Player has not been given any music. */
    file: string;
    /**
     * The whole address Bloom Player gave its music player. Bloom Player adds a fresh query
     * string each time it starts a piece of music, so the same address on two pages means the
     * music went on rather than starting over.
     */
    src: string;
    playing: boolean;
}

/** Read what Bloom Player's background music player is playing, if anything. */
export async function getPreviewMusic(player: Frame): Promise<IPreviewMusic> {
    const { src, paused } = await player.evaluate(() => {
        const audio = document.getElementById(
            "music-player",
        ) as HTMLAudioElement | null;
        return {
            src: audio?.getAttribute("src") ?? "",
            paused: audio ? audio.paused : true,
        };
    });
    return {
        file: src
            ? decodeURIComponent(Path.posix.basename(new URL(src).pathname))
            : "",
        src,
        playing: !paused,
    };
}
