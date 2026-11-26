// A book made from the Sign Language template can use every page layout the template offers: the
// test adds one page of each layout, puts a short video in each page's video box, and checks that
// the BloomPUB preview, which is Bloom Player as Bloom Reader runs it, plays each page's own video
// to its end when the reader turns to that page. Which layouts there are, and how many video boxes
// each has, is read from the template's own file.
//
// Test Case ID 581 ("Sign Language Template Videos"). Not checked as written: viewing the books on
// Bloom Reader itself, the Android app. That needs a phone; the test views the same book in the
// BloomPUB preview instead. The case's videos are under 30 seconds; the suite's is one second.

import { expect, test } from "../fixtures/bloomTest";
import {
    expectPreviewVideosPlayed,
    goToPreviewPage,
    IPreviewVideoPlayed,
    previewVideos,
    startRecordingPreviewVideos,
} from "../helpers/bloomPlayer";
import { readPagesOnDisk } from "../helpers/bookHtml";
import {
    addPage,
    getContentPages,
    getFactoryTemplateFolder,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { getPageSize } from "../helpers/pageSize";
import {
    expectSignLanguageFeature,
    getCollectionSignLanguage,
    getSignLanguageTooltipText,
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    chooseVideoFile,
    getVideoFileName,
    readVideoFile,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

const kSignLanguageTemplate = "Sign Language";
const kSignLanguage = "ase";

test.use({
    collectionSpec: {
        name: "sign-language-template-layouts",
        languages: ["en"],
        signLanguage: kSignLanguage,
    },
});

/** A page the test added from one of the template's layouts, and the video put in it. */
interface ILayoutPage {
    label: string;
    pageId: string;
    videoFile: string;
}

test("every page layout of the Sign Language template takes a video that the BloomPUB preview plays [Test Case ID 581]", async ({
    page,
    step,
}) => {
    test.setTimeout(900000);

    const bookFolder = await step(
        "Make a book from the Sign Language template",
        () => makeBookFromTemplate(page, kSignLanguageTemplate),
    );
    if (!bookFolder) throw new Error("No book was made.");

    const layouts = await step(
        "Read the template's page layouts from its file",
        async () => {
            const orientation = (await getPageSize(page)).endsWith("Landscape")
                ? "landscape"
                : "portrait";
            const templatePages = (
                await readPagesOnDisk(
                    page,
                    await getFactoryTemplateFolder(page, kSignLanguageTemplate),
                )
            ).filter(
                (p) =>
                    p.isTemplatePage &&
                    ["", orientation].includes(p.onlyForOrientation),
            );
            expect(
                templatePages.length,
                "sanity check: the Sign Language template should offer several page layouts",
            ).toBeGreaterThan(1);
            // The preview's record of played videos goes by page, so the test needs one video box
            // per layout; fail plainly if the template ever gets a layout with none or with two.
            for (const templatePage of templatePages)
                expect(
                    templatePage.videoBoxCount,
                    `sanity check: the template's "${templatePage.label}" page should have one video box`,
                ).toBe(1);
            expect(
                await getContentPages(page),
                "sanity check: a book made from a template starts with no content page",
            ).toEqual([]);
            return templatePages;
        },
    );
    if (!layouts) throw new Error("The template's layouts were not read.");

    const added: ILayoutPage[] = [];
    for (const layout of layouts) {
        await step(
            `Add a "${layout.label}" page and put a video in it`,
            async () => {
                const before = (await getContentPages(page)).map((p) => p.id);
                await addPage(page, layout.label, 1, kSignLanguageTemplate);
                const newPages = (await getContentPages(page)).filter(
                    (p) => !before.includes(p.id),
                );
                expect(
                    newPages.length,
                    `adding a "${layout.label}" page should give the book one new content page`,
                ).toBe(1);
                await goToPage(page, newPages[0].id);
                const boxes = videoBoxes(page);
                await expect(
                    boxes,
                    `the "${layout.label}" page should have as many video boxes as the template's`,
                ).toHaveCount(layout.videoBoxCount);
                await chooseVideoFile(page, boxes.first(), SHORT_VIDEO);
                const videoFile = await getVideoFileName(boxes.first());
                expect(
                    readVideoFile(bookFolder, videoFile).length,
                    `the video of the "${layout.label}" page should be in the book's video folder`,
                ).toBeGreaterThan(0);
                added.push({
                    label: layout.label,
                    pageId: newPages[0].id,
                    videoFile,
                });
            },
        );
    }

    expect(
        new Set(added.map((a) => a.videoFile)).size,
        "sanity check: each page should have a video file of its own, or the preview could play one page's video on another unseen",
    ).toBe(added.length);

    const player = await step(
        "Go to Publish, BloomPUB, with Sign Language on, and show the preview",
        async () => {
            await selectPublishDestination(page, "BloomPUB");
            const signLanguage = await getCollectionSignLanguage(page);
            expect(
                signLanguage.tag,
                "sanity check: the collection should have the sign language it was made with",
            ).toBe(kSignLanguage);
            await expectSignLanguageFeature(page, {
                enabled: true,
                checked: true,
                tooltip: await getSignLanguageTooltipText(page, {
                    signLanguageName: signLanguage.name,
                }),
            });
            const frame = await showBloomPubPreview(page);
            await startRecordingPreviewVideos(frame);
            return frame;
        },
    );
    if (!player) throw new Error("The preview was not shown.");

    const played: IPreviewVideoPlayed[] = [];
    for (const layoutPage of added) {
        await step(
            `Turn to the "${layoutPage.label}" page: its video shows and plays to its end`,
            async () => {
                await goToPreviewPage(page, player, layoutPage.pageId);
                await expect(
                    previewVideos(player),
                    `the preview's "${layoutPage.label}" page should show its one video`,
                ).toHaveCount(1);
                await expect(previewVideos(player)).toBeVisible();
                played.push({
                    pageId: layoutPage.pageId,
                    fileName: layoutPage.videoFile,
                });
                await expectPreviewVideosPlayed(
                    player,
                    played,
                    `The preview should play the "${layoutPage.label}" page's own video to its end, once, when the reader turns to it.`,
                );
            },
        );
    }
});
