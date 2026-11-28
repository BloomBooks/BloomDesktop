// The language chooser in the Audio or Video screen's "Configure & Preview" step: with a Talking
// Book narrated in two languages, the language a person chooses in the preview is the one whose
// narration plays when they press Play, and the one Bloom keeps for recording the video.
//
// Automates the manual test "Configure Preview Language Selection" (Test Case ID 233).
//
// The manual case uses a book from Bloom Library with English and Tok Pisin narration. The test
// makes its own: one content page with an English and a French sentence, each with a narration
// file of its own (see addNarration), so the file that plays names the language it belongs to.
// The language chooser is Bloom Player's, shown above the book in the preview; Play is Bloom's own
// button under it.

import { expect, test } from "../fixtures/bloomTest";
import {
    choosePreviewLanguage,
    getAudioVideoRecordingLanguage,
    playAudioVideoPreviewToTheEnd,
    showAudioVideoPreview,
    showAudioVideoPreviewAgain,
} from "../helpers/audioVideoPublish";
import {
    expectPreviewSoundsPlayed,
    startRecordingPreviewSounds,
} from "../helpers/bloomPlayer";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { getLanguagesInBook, getPreviewLanguages } from "../helpers/publish";
import {
    addNarration,
    getNarrationSentences,
    openToolboxWithTalkingBook,
} from "../helpers/talkingBook";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";

test.use({
    collectionSpec: {
        name: "audio-video-preview-language",
        languages: ["en", "fr"],
        // Publishing as Audio or Video needs a Pro subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const BOOK_TITLE = "Preview Language Test";

test("the language chosen in the Audio or Video preview is the one whose narration plays [Test Case ID 233]", async ({
    bloomApp,
}) => {
    test.setTimeout(600000);
    // The Publish tab's destinations are found by their English names, and a fresh Bloom takes
    // the operating system's UI language.
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    await addPage(page, "Just Text", 1);
    await setContentLanguages(page, ["en", "fr"]);
    const contentPages = await getContentPages(page);
    expect(contentPages.length).toBe(1);
    await goToPage(page, contentPages[0].id);
    await typeInGroup(page, ".bloom-translationGroup", "en", "Hello.");
    await typeInGroup(page, ".bloom-translationGroup", "fr", "Bonjour.");

    // Opening the toolbox puts the Talking Book tool to work, which marks each sentence with the
    // id its narration file is named after.
    await openToolboxWithTalkingBook(page);
    const sentences = await getNarrationSentences(page);
    expect(
        sentences.map((s) => s.languageTag).sort(),
        "The Talking Book tool did not mark one sentence in each language.",
    ).toEqual(["en", "fr"]);
    const bookFolder = await findBookFolder(page, BOOK_TITLE);
    const narrationFile = {
        en: `${(await addNarration(page, bookFolder, "en"))[0]}.mp3`,
        fr: `${(await addNarration(page, bookFolder, "fr"))[0]}.mp3`,
    };

    // Leave the page, which makes Bloom write it to the file, and show only English again, as a
    // book whose other language lives only in its embedded text and narration.
    const cover = (await getPages(page)).find((p) => !p.isContentPage);
    await goToPage(page, cover!.id);
    await setContentLanguages(page, ["en"]);

    let player = await showAudioVideoPreview(page);
    // Sanity check the book: Bloom sees narration in both languages, and offers both.
    expect(
        (await getLanguagesInBook(page))
            .filter((l) => l.containsAnyAudio)
            .map((l) => l.code),
        "Bloom did not see narration in both English and French.",
    ).toEqual(["en", "fr"]);
    const offered = await getPreviewLanguages(player);
    const english = offered.find((name) => /english/i.test(name));
    const french = offered.find((name) => /fran[cç]ais|french/i.test(name));
    expect(
        [english, french],
        `The preview's language chooser should offer English and French; it offers ${JSON.stringify(offered)}.`,
    ).toEqual([expect.any(String), expect.any(String)]);

    // Choose French and play: the French narration plays, and the English does not.
    await choosePreviewLanguage(player, french!);
    await expect
        .poll(() => getAudioVideoRecordingLanguage(page), {
            timeout: 15000,
            message:
                "Bloom did not keep French as the language to record the video in.",
        })
        .toBe("fr");
    await startRecordingPreviewSounds(player);
    await playAudioVideoPreviewToTheEnd(page);
    await expectPreviewSoundsPlayed(
        player,
        [narrationFile.fr],
        "With French chosen, Play should have played the French narration and only that",
    );

    // Press Preview again, as the manual case does, choose English and play: now the English
    // narration plays. (Pressing Reset instead of Preview leaves Bloom Player playing the French
    // recording under the English text, BL-17227.)
    player = await showAudioVideoPreviewAgain(page, player);
    await choosePreviewLanguage(player, english!);
    await expect
        .poll(() => getAudioVideoRecordingLanguage(page), {
            timeout: 15000,
            message:
                "Bloom did not keep English as the language to record the video in.",
        })
        .toBe("en");
    await startRecordingPreviewSounds(player);
    await playAudioVideoPreviewToTheEnd(page);
    await expectPreviewSoundsPlayed(
        player,
        [narrationFile.en],
        "With English chosen, Play should have played the English narration and only that",
    );
});
