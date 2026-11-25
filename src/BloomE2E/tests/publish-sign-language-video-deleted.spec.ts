// The Sign Language feature of Publish: Web follows the book's videos. Once the only video is
// deleted in Edit, the feature's checkbox on the Web screen is unchecked and disabled, even though
// it was checked before.
//
// Test Case ID 591 ("SL Box Unchecks After Delete"). The card also has the tester give the book a
// title, a copyright and some text, which an upload needs (BL-10004); the Features list shows
// without them, so this test leaves them out.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    confirmLanguageChooser,
    pickLanguageInChooser,
} from "../helpers/languageChooser";
import { openPublishToWeb } from "../helpers/libraryPublish";
import {
    expectSignLanguageFeature,
    getCollectionSignLanguage,
    openSignLanguageChooserFromPublish,
} from "../helpers/publish";
import {
    chooseVideoFile,
    deleteVideo,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";
import { switchTab } from "../helpers/workspace";

test.use({
    // A collection with no sign language yet: the tester sets it from the Web screen.
    collectionSpec: {
        name: "publish-sign-language-video-deleted",
        languages: ["en"],
        signLanguage: "",
    },
});

test("deleting the book's only video unchecks and disables Sign Language on the Web screen [Test Case ID 591]", async ({
    page,
    bloomApp,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Video Over Text");
    const videoPage = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    )!;
    await goToPage(page, videoPage.id);
    await chooseVideoFile(page, videoBoxes(page).first(), SHORT_VIDEO);

    // On Publish: Web, set the sign language, which checks Sign Language.
    await openPublishToWeb(page);
    await openSignLanguageChooserFromPublish(page);
    await pickLanguageInChooser(page, {
        search: "American Sign Language",
        code: "ase",
    });
    await confirmLanguageChooser(page);
    await expect
        .poll(() => getCollectionSignLanguage(page))
        .toEqual({ tag: "ase", name: "American Sign Language" });
    await expectSignLanguageFeature(page, {
        enabled: true,
        checked: true,
        tooltip: "The videos in this book contain American Sign Language",
    });

    // Back in Edit, delete the video.
    await switchTab(page, "edit");
    await goToPage(page, videoPage.id);
    await deleteVideo(page, bloomApp.bloomPid, videoBoxes(page).first());

    await openPublishToWeb(page);
    await expectSignLanguageFeature(page, {
        enabled: false,
        checked: false,
        tooltip: "This is disabled because this book does not have any videos.",
    });
});
