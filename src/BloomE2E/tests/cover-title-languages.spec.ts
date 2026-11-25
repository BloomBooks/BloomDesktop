// The Cover page of Book and Page Settings: which languages' titles the front cover shows, and
// whether it shows the topic. Automates the manual test "Cover Tab Language Combinations" (Test
// Case ID 404).
//
// The card asks for a derivative of an Open Bible book in a collection of English, French and
// German. The test makes a derivative of "The Moon and the Cap" from the Sample Shells that Bloom
// installs instead, in a collection of English, French and Tibetan, because that shell has a title
// and pages of text in each of those three languages and a topic ("Story Book"), and no download is
// needed. Tibetan plays the part the card gives German: Language 3, whose title is off until
// someone turns it on. (The shell's Spanish title is empty, and a language with no text on the
// pages is left out of a BloomPUB, so Spanish could not stand in.)
//
// For each combination of titles the card lists, the test checks the cover in the Edit tab and in
// the BloomPUB preview, which is the publication itself.

import { expect, test } from "../fixtures/bloomTest";
import {
    editablePageFrame,
    makeBookFromSourceBook,
} from "../helpers/bookMaking";
import {
    bookSettingsCheckbox,
    okBookSettings,
    openBookSettings,
    setBookSettingsCheckbox,
} from "../helpers/bookSettings";
import {
    openPublishDestination,
    openPublishTab,
    showBloomPubPreview,
} from "../helpers/publish";
import { switchTab } from "../helpers/workspace";
import type { Frame, FrameLocator, Locator } from "@playwright/test";

test.use({
    collectionSpec: {
        name: "cover-title-languages",
        languages: ["en", "fr", "bo"],
    },
});

const SHELL_COLLECTION = "Sample Shells";
const SHELL_TITLE = "The Moon and the Cap";

// The setting behind each check box, by the language whose title it shows.
const TITLE_SETTING = {
    en: "appearance.cover-title-L1-show",
    fr: "appearance.cover-title-L2-show",
    bo: "appearance.cover-title-L3-show",
} as const;
type Lang = keyof typeof TITLE_SETTING;
const LANGS: Lang[] = ["en", "fr", "bo"];
const TOPIC_SETTING = "appearance.cover-topic-show";
const LANGUAGE_NAME_SETTING = "appearance.cover-languageName-show";

function frontCover(frame: FrameLocator | Frame): Locator {
    return frame.locator(".bloom-page.outsideFrontCover").first();
}

/** Assert which titles, and whether the topic, the front cover shows. */
async function expectCover(
    cover: Locator,
    where: string,
    titles: Lang[],
    topic: boolean,
): Promise<void> {
    for (const lang of LANGS) {
        const title = cover
            .locator(`.bookTitle .bloom-editable[lang="${lang}"]`)
            .first();
        if (titles.includes(lang)) {
            await expect(
                title,
                `${where}: the ${lang} title should show`,
            ).toBeVisible();
            // An empty title box can still be visible in the Edit tab.
            await expect(
                title,
                `${where}: the ${lang} title should have its words`,
            ).not.toHaveText("");
        } else
            await expect(
                title,
                `${where}: the ${lang} title should not show`,
            ).toBeHidden();
    }
    const topicBox = cover.locator(".coverBottomBookTopic").first();
    if (topic) {
        await expect(topicBox, `${where}: the topic should show`).toBeVisible();
        await expect(
            topicBox,
            `${where}: the topic should have its words`,
        ).not.toHaveText("");
    } else
        await expect(
            topicBox,
            `${where}: the topic should not show`,
        ).toBeHidden();
}

/** Turn on exactly these titles on the Cover page of Book and Page Settings, and press OK. */
async function showTitles(
    page: import("@playwright/test").Page,
    titles: Lang[],
): Promise<void> {
    await openBookSettings(page, "Cover");
    // Turn the wanted ones on first, so that Bloom never sees all of them off.
    for (const lang of titles)
        if (lang !== "en")
            await setBookSettingsCheckbox(page, TITLE_SETTING[lang], true);
    for (const lang of LANGS)
        if (!titles.includes(lang))
            await setBookSettingsCheckbox(page, TITLE_SETTING[lang], false);
    await okBookSettings(page);
}

/** Check the cover in the Edit tab, then in the BloomPUB preview, and come back to the Edit tab. */
async function expectCoverEverywhere(
    page: import("@playwright/test").Page,
    titles: Lang[],
    topic: boolean,
): Promise<void> {
    await expectCover(
        frontCover(editablePageFrame(page)),
        "Edit tab",
        titles,
        topic,
    );
    await openPublishTab(page);
    await openPublishDestination(page, "BloomPUB");
    const player = await showBloomPubPreview(page);
    await expectCover(frontCover(player), "BloomPUB preview", titles, topic);
    await switchTab(page, "edit");
}

test("the Cover page of Book and Page Settings decides which titles and whether the topic show [Test Case ID 404]", async ({
    page,
}) => {
    test.setTimeout(600000);

    await makeBookFromSourceBook(page, SHELL_COLLECTION, SHELL_TITLE);

    // What the Cover page shows for a new derivative.
    await openBookSettings(page, "Cover");
    const english = bookSettingsCheckbox(page, TITLE_SETTING.en);
    await expect(english, "English title should start on").toBeChecked();
    await expect(
        english,
        "English, Language 1, should not be possible to turn off",
    ).toBeDisabled();
    const french = bookSettingsCheckbox(page, TITLE_SETTING.fr);
    await expect(french, "French title should start on").toBeChecked();
    await expect(french).toBeEnabled();
    const tibetan = bookSettingsCheckbox(page, TITLE_SETTING.bo);
    await expect(tibetan, "Tibetan title should start off").not.toBeChecked();
    await expect(tibetan).toBeEnabled();
    const languageName = bookSettingsCheckbox(page, LANGUAGE_NAME_SETTING);
    await expect(
        languageName,
        "Show Language Name should start on",
    ).toBeChecked();
    await expect(languageName).toBeEnabled();
    const topic = bookSettingsCheckbox(page, TOPIC_SETTING);
    await expect(topic, "Show Topic should start on").toBeChecked();
    await expect(topic).toBeEnabled();

    // a) English only, and no topic.
    await setBookSettingsCheckbox(page, TITLE_SETTING.fr, false);
    await expect(
        english,
        "English should still not be possible to turn off",
    ).toBeDisabled();
    await okBookSettings(page);
    // With the topic still on, so that the publication is seen showing it before it goes off.
    await expectCoverEverywhere(page, ["en"], true);
    await openBookSettings(page, "Cover");
    await setBookSettingsCheckbox(page, TOPIC_SETTING, false);
    await okBookSettings(page);
    await expectCoverEverywhere(page, ["en"], false);

    // b) English and Tibetan (the card's English and German).
    await showTitles(page, ["en", "bo"]);
    await expectCoverEverywhere(page, ["en", "bo"], false);

    // c) English and French.
    await showTitles(page, ["en", "fr"]);
    await expectCoverEverywhere(page, ["en", "fr"], false);

    // d) All three.
    await showTitles(page, ["en", "fr", "bo"]);
    await expectCoverEverywhere(page, ["en", "fr", "bo"], false);
});
