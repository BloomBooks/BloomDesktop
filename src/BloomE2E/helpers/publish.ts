// Drive and read the Publish tab: its destination chooser, and its two language check box lists.
//
// There are two lists, rendered by the same component (LanguageSelectionSettingsGroup) and told
// apart only by a test id: "Text Languages", which controls which languages of the text a
// publication carries, and "Talking Book Languages", which controls whose narration it carries.
// Every reader here takes which list it means as its first argument after `page`.
//
// Both lists appear on the Web screen and on the BloomPUB screen, and both screens write the same
// settings (BookInfo.PublishSettings.BloomLibrary.TextLangs and .AudioLangs). So "does the other
// screen agree" is a real question a test can ask, not a formality.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { switchTab } from "./workspace";

/** The publish destinations, by the label each one shows in the left-hand column. */
export type PublishDestination =
    | "PDF & Print"
    | "Web"
    | "BloomPUB"
    | "Apps"
    | "ePUB"
    | "Audio or Video";

/**
 * What publish/languagesInBook says about one language of the book. This is Bloom's own view of
 * the same state the check boxes show, so a test can assert on it instead of scraping the DOM.
 * Mirrors C#'s LanguagePublishInfo.
 */
export interface ILanguagePublishInfo {
    code: string;
    name: string;
    /** False when at least one content-page translation group lacks this language. */
    complete: boolean;
    includeText: boolean;
    containsAnyAudio: boolean;
    includeAudio: boolean;
    /** True when the book currently shows this language, which forces it into the publication. */
    required: boolean;
}

/**
 * Which of the Publish tab's two language lists a helper means. "text" is the Text Languages
 * list; "audio" is the Talking Book Languages list.
 */
export type LanguageGroup = "text" | "audio";

/** The test id each list's container carries; see LanguageSelectionSettingsGroup.tsx. */
const groupTestIds: Record<LanguageGroup, string> = {
    text: "text-languages-group",
    audio: "audio-languages-group",
};

/** The name each list shows itself under, for failure messages. */
const groupNames: Record<LanguageGroup, string> = {
    text: "Text Languages",
    audio: "Talking Book Languages",
};

/** One row of a language list, as a reader of the screen sees it. */
export interface ILanguageRow {
    /** The language's name, as the row shows it: the collection's name for it, or its autonym. */
    name: string;
    /**
     * True when the row carries the "(incomplete translation)" sub-label. Only the text list ever
     * shows it; an audio row's is always false, whatever the translation is like.
     */
    incomplete: boolean;
    checked: boolean;
    /**
     * True when the box cannot be clicked. In the text list that means the book shows the
     * language, so it is required; in the audio list it means the book has no narration in that
     * language, or its text is not being published.
     */
    disabled: boolean;
}

/**
 * Go to the Publish tab and click one of its destinations, without waiting for what that screen
 * shows. Each screen's own module waits for its own content; they do not all show the same things
 * (the Web screen, for one, shows no Text Languages list for a book that has no text).
 */
export async function selectPublishDestination(
    page: Page,
    destination: PublishDestination,
): Promise<void> {
    await switchTab(page, "publish");
    const target = page.getByRole("tab", { name: destination, exact: true });
    await target.waitFor({ state: "visible", timeout: 30000 });
    await target.click();
}

/**
 * Go to the Publish tab, open one of its destinations, and wait for its Text Languages list. A
 * book must already be selected. Use this for the screens whose subject is those lists; the Web
 * screen has its own opener in helpers/libraryPublish.ts.
 *
 * It waits for the TEXT list even when the caller is after the audio one, because the audio list
 * is shown only for a book that has narration -- waiting for it would hang on a book that has
 * none, which is a state several tests need to read.
 */
export async function openPublishDestination(
    page: Page,
    destination: PublishDestination,
): Promise<void> {
    await selectPublishDestination(page, destination);
    await languagesGroup(page, "text").waitFor({
        state: "visible",
        timeout: 60000,
    });
}

/** One of the two language lists, told apart from the other by its test id. */
export function languagesGroup(page: Page, group: LanguageGroup): Locator {
    return page.getByTestId(groupTestIds[group]);
}

/** Every row of one language list, in the order the screen shows them. */
export async function getLanguageRows(
    page: Page,
    group: LanguageGroup,
): Promise<ILanguageRow[]> {
    await languagesGroup(page, group).waitFor({
        state: "visible",
        timeout: 60000,
    });
    return page.evaluate((testId) => {
        const group = document.querySelector(`[data-testid="${testId}"]`);
        if (!group) return [];
        return [...group.querySelectorAll('input[type="checkbox"]')].map(
            (input) => {
                const box = input as HTMLInputElement;
                const label = box.closest("label");
                const lines = (label?.innerText ?? "")
                    .split("\n")
                    .map((line) => line.trim())
                    .filter(Boolean);
                return {
                    name: lines[0] ?? "",
                    incomplete: lines.some((line) =>
                        line.includes("incomplete translation"),
                    ),
                    checked: box.checked,
                    disabled: box.disabled,
                };
            },
        );
    }, groupTestIds[group]);
}

/**
 * Wait until one language list settles into `expected`. The lists are filled from
 * publish/languagesInBook after the screen mounts, so reading one once races that fetch.
 */
export async function expectLanguageRows(
    page: Page,
    group: LanguageGroup,
    expected: ILanguageRow[],
    message: string,
): Promise<void> {
    await expect
        .poll(async () => JSON.stringify(await getLanguageRows(page, group)), {
            timeout: 30000,
            message: `${message} (the ${groupNames[group]} list)`,
        })
        .toBe(JSON.stringify(expected));
}

/**
 * Wait until one language list holds exactly `expected`, in any order.
 *
 * Use this where the position of a row is not part of the behavior being tested. Where it is,
 * such as an incomplete language sorting last, use expectLanguageRows instead.
 */
export async function expectLanguageRowsInAnyOrder(
    page: Page,
    group: LanguageGroup,
    expected: ILanguageRow[],
    message: string,
): Promise<void> {
    const byName = (rows: ILanguageRow[]) =>
        JSON.stringify([...rows].sort((a, b) => a.name.localeCompare(b.name)));
    await expect
        .poll(async () => byName(await getLanguageRows(page, group)), {
            timeout: 30000,
            message: `${message} (the ${groupNames[group]} list)`,
        })
        .toBe(byName(expected));
}

/**
 * Click one language's check box in one list, by the name its row shows. This is the action under
 * test wherever a test is about what a person's choice does.
 */
export async function clickLanguage(
    page: Page,
    group: LanguageGroup,
    name: string,
): Promise<void> {
    const row = languageRow(page, group, name);
    await row.waitFor({ state: "visible", timeout: 30000 });
    await row.locator('input[type="checkbox"]').click();
}

/** One list's row for one language, found the way a reader finds it: by the name it shows. */
function languageRow(page: Page, group: LanguageGroup, name: string): Locator {
    return languagesGroup(page, group)
        .locator("label")
        .filter({ hasText: name })
        .first();
}

/**
 * Whether the check mark beside Features: Talking Book is showing. Bloom turns it on exactly when
 * some language's narration is going into the publication.
 *
 * It reads an attribute rather than the check mark itself: the mark is always in the DOM and is
 * merely CSS-hidden when off (see PublishFeaturesGroup.tsx, which carries the attribute for us).
 */
export async function isTalkingBookFeatureOn(page: Page): Promise<boolean> {
    const feature = page.getByTestId("feature-talking-book");
    await feature.waitFor({ state: "visible", timeout: 30000 });
    return (await feature.getAttribute("data-feature-on")) === "true";
}

/**
 * Wait until the Features: Talking Book check mark is `expected`. The Features group is filled
 * from the same fetch as the language lists, so reading it right after a click races that fetch.
 */
export async function expectTalkingBookFeature(
    page: Page,
    expected: boolean,
    message: string,
): Promise<void> {
    await expect
        .poll(async () => isTalkingBookFeatureOn(page), {
            timeout: 30000,
            message,
        })
        .toBe(expected);
}

/**
 * Click PREVIEW on the BloomPUB screen and wait for bloom-player to show the book.
 *
 * The preview is the publication itself, so it is where a test can see whether clearing a
 * language's check box really kept that language out.
 */
export async function showBloomPubPreview(page: Page): Promise<Frame> {
    await page.locator('[aria-label="refresh preview"]').click();
    let player: Frame | undefined;
    await expect
        .poll(
            async () => {
                player = page
                    .frames()
                    .find((f) => f.url().includes("bloomplayer.htm"));
                if (!player) return 0;
                return player
                    .locator('[aria-label="Choose Language"]')
                    .count()
                    .catch(() => 0);
            },
            {
                timeout: 120000,
                message: "The BloomPUB preview never showed the book.",
            },
        )
        .toBeGreaterThan(0);
    return player!;
}

/**
 * The languages bloom-player offers in its "Languages in this book:" menu, in the order shown.
 * Each is the name the language calls itself, which is not the name the check box list shows.
 *
 * The menu is closed again, so the preview is left as it was found.
 */
export async function getPreviewLanguages(player: Frame): Promise<string[]> {
    await player.locator('[aria-label="Choose Language"]').click();
    const options = player.locator('[aria-label="languages"] label');
    await options.first().waitFor({ state: "visible", timeout: 30000 });
    const names = (await options.allInnerTexts()).map((t) => t.trim());
    await player.getByRole("button", { name: "Close" }).click();
    return names;
}

/**
 * Stage the selected book as a BloomPUB, exactly as clicking PREVIEW on the BloomPUB screen does,
 * and return the folder the staged files landed in. That folder holds the same set of files an
 * unzipped .bloompub does, so it is where a test can see what a publication really carries --
 * which audio files, in particular, since a language left out of the Talking Book list has its
 * mp3s removed from the staged copy rather than merely unreferenced.
 *
 * Saving a real .bloompub instead would need the native save dialog, which no test can dismiss
 * (see AUTOMATION-DEBT.md). The Publish tab must already be open on this book.
 */
export async function stageBloomPub(page: Page): Promise<string> {
    const response = await apiPost(page, "e2e/makeBloomPubPreview");
    // Bloom answers with the localhost URL of the staged .htm; the file's real path is the URL
    // path, escaped for HTTP (Extensions.ToLocalhost). The folder is what a caller wants.
    const url = response.body.trim();
    const prefix = url.match(/^https?:\/\/[^/]+\/bloom\//);
    if (!prefix)
        throw new Error(
            `Staging a BloomPUB answered "${url}", which is not a localhost /bloom/ URL.`,
        );
    const path = decodeURIComponent(url.substring(prefix[0].length));
    return Path.dirname(path);
}

/**
 * The base names of the narration files a staged BloomPUB carries, sorted. Each name is the id of
 * the sentence it narrates, so a caller compares these against the ids it seeded (see
 * helpers/talkingBook.ts).
 */
export function getStagedNarrationIds(stagedFolder: string): string[] {
    const audioFolder = Path.join(stagedFolder, "audio");
    if (!fs.existsSync(audioFolder)) return [];
    return fs
        .readdirSync(audioFolder)
        .filter((name) => name.toLowerCase().endsWith(".mp3"))
        .map((name) => Path.basename(name, Path.extname(name)))
        .sort();
}

/** What Bloom itself says about the languages in the selected book. */
export async function getLanguagesInBook(
    page: Page,
): Promise<ILanguagePublishInfo[]> {
    return apiGetJson<ILanguagePublishInfo[]>(page, "publish/languagesInBook");
}

/**
 * The tooltip text shown for one row, after hovering it the way a reader would. Bloom's tooltip
 * needs a real pointer over the row; it is not in the DOM until then.
 *
 * The pointer moves off every row first, and the previous tooltip has to go, because reading the
 * tooltip while the last one is still leaving returns the last one's words.
 */
export async function getTooltipForLanguage(
    page: Page,
    group: LanguageGroup,
    name: string,
): Promise<string> {
    const tooltip = page.locator('[role="tooltip"]');
    await page.mouse.move(0, 0);
    await expect
        .poll(async () => tooltip.count(), {
            timeout: 30000,
            message: "The tooltip from the row hovered before never went away.",
        })
        .toBe(0);

    const row = languageRow(page, group, name);
    await row.hover();
    await tooltip.first().waitFor({ state: "visible", timeout: 30000 });
    return (await tooltip.first().innerText()).trim();
}
