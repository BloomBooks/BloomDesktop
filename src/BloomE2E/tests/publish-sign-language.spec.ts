// Choosing the collection's sign language from a Publish screen. In a book with a video, the
// Features list's Sign Language row has a tooltip with a link: "no sign language selected" when the
// collection has none, or the sign language's name. The link opens the language chooser, and the
// choice becomes the collection's sign language, which Collection Settings then shows.
//
// Test Case IDs 173 ("Pick Sign Language From Video") and 172 ("Sign Language With Video"), their
// automated portions. Uploading to Bloom Library and checking the Sign Language tag there stays
// manual (their manual portions in Notion).

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    cancelCollectionSettings,
    getCollectionSettingsLanguage,
    openCollectionSettings,
    readSavedCollectionSetting,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    cancelLanguageChooser,
    confirmLanguageChooser,
    pickLanguageInChooser,
} from "../helpers/languageChooser";
import {
    getCollectionSignLanguage,
    expectSignLanguageFeature,
    openSignLanguageChooserFromPublish,
    selectPublishDestination,
} from "../helpers/publish";
import { chooseVideoFile, SHORT_VIDEO, videoBoxes } from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

test.use({
    // A collection with no sign language, said so in its settings file.
    collectionSpec: {
        name: "publish-sign-language",
        languages: ["en"],
        signLanguage: "",
    },
});

test.describe.configure({ mode: "serial" });

const kSignLanguageHeading = "Sign Language (Optional)";

test("builds a book with a video, and shows it on the BloomPUB screen", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Video Over Text");
    const videoPage = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    )!;
    await goToPage(page, videoPage.id);
    await chooseVideoFile(page, videoBoxes(page).first(), SHORT_VIDEO);
    await selectPublishDestination(page, "BloomPUB");
});

test("with no sign language, the feature is off, and its tooltip's link sets one [Test Case ID 173]", async ({
    page,
    bloomApp,
}) => {
    expect(
        await getCollectionSignLanguage(page),
        "sanity check: the collection starts with no sign language",
    ).toEqual({ tag: "", name: "" });
    await expectSignLanguageFeature(page, {
        enabled: false,
        checked: false,
        tooltip:
            "This is disabled because this collection has no sign language selected.",
    });

    await openSignLanguageChooserFromPublish(page);
    await pickLanguageInChooser(page, "American Sign Language", "ase");
    await confirmLanguageChooser(page);

    await expect
        .poll(() => getCollectionSignLanguage(page))
        .toEqual({ tag: "ase", name: "American Sign Language" });
    await expectSignLanguageFeature(page, {
        enabled: true,
        checked: true,
        tooltip: "The videos in this book contain American Sign Language",
    });
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "SignLanguageIso639Code",
        ),
    ).toBe("ase");
});

test("the tooltip's link changes the sign language, Cancel there changes nothing, and Settings shows it [Test Case ID 172]", async ({
    page,
    bloomApp,
}) => {
    expect(
        (await getCollectionSignLanguage(page)).tag,
        "sanity check: the previous test set American Sign Language",
    ).toBe("ase");

    await openSignLanguageChooserFromPublish(page);
    await pickLanguageInChooser(page, "British Sign Language", "bfi");
    await confirmLanguageChooser(page);
    await expect
        .poll(() => getCollectionSignLanguage(page))
        .toEqual({ tag: "bfi", name: "British Sign Language" });
    await expectSignLanguageFeature(page, {
        enabled: true,
        checked: true,
        tooltip: "The videos in this book contain British Sign Language",
    });

    // Pick another, then Cancel: the collection keeps British Sign Language.
    await openSignLanguageChooserFromPublish(page);
    await pickLanguageInChooser(page, "American Sign Language", "ase");
    await cancelLanguageChooser(page);
    expect(await getCollectionSignLanguage(page)).toEqual({
        tag: "bfi",
        name: "British Sign Language",
    });
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "SignLanguageIso639Code",
        ),
    ).toBe("bfi");

    await switchTab(page, "collection");
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Languages");
    expect(
        await getCollectionSettingsLanguage(page, kSignLanguageHeading),
    ).toBe("British Sign Language (bfi)");
    await cancelCollectionSettings(page);
});
