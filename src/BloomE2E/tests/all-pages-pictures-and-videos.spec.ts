// Pictures and videos with the Edit tab showing all the pages of the book (BL-16929).
//
// The other pages are drawn from what Bloom serves for them (pageList/pageContent with full-size
// pictures), not from the page being edited, so a picture there could come out small (the page
// list's small copy) or lose its transparency. And putting a video on a page goes through the Sign
// Language tool, which must leave All Pages on a page that can be used.
//
// The tests are serial because each one starts from the book the one before it left behind.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
    waitForEditTabSettled,
    type IBookPage,
} from "../helpers/bookMaking";
import { setPageBackgroundColor } from "../helpers/bookSettings";
import { chooseImageFile } from "../helpers/images";
import { chooseSectionType, setChangeLayoutMode } from "../helpers/origami";
import {
    choosePageView,
    clickPageInGrid,
    clickTextOfPageInGrid,
    expectGridToMatchBook,
    getPagePlacement,
    getPictureOnPage,
    isPageChangeUnderway,
    waitForPageToBeShown,
} from "../helpers/pageView";
import {
    chooseVideoFile,
    getVideoSource,
    recordVideo,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

test.use({
    collectionSpec: { name: "all-pages-pictures", languages: ["en"] },
    // One test records a video with the Sign Language tool.
    fakeCamera: true,
});

test.describe.configure({ mode: "serial" });

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// The page with a picture, the text page facing it, and two pages with a video box: one to import
// a video into, one to record one into.
let picturePage: IBookPage;
let textPage: IBookPage;
let videoPage: IBookPage;
let recordingPage: IBookPage;

test.describe("pictures and videos in All Pages", () => {
    test("builds a book with a picture page, a text page and two video pages", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        await addPage(page, "Just Text");
        await addPage(page, "Custom", 2);
        [picturePage, textPage, videoPage, recordingPage] =
            await getContentPages(page);
        for (const p of [videoPage, recordingPage]) {
            await goToPage(page, p.id);
            await setChangeLayoutMode(page, true);
            await chooseSectionType(page, "video");
            await setChangeLayoutMode(page, false);
            await expect(videoBoxes(page)).toHaveCount(1);
        }
        await goToPage(page, textPage.id);
    });

    test("a picture with a transparent background, on a coloured page, shows on that page among the others at full size and still transparent", async ({
        page,
    }) => {
        await goToPage(page, picturePage.id);
        await choosePageView(page, "one");
        // bird.png is 274x300, with its corners transparent.
        await chooseImageFile(page, fixtureImage("bird.png"));
        await setPageBackgroundColor(page, "#fdf3c5");
        const onItsPage = await getPictureOnPage(page, picturePage.id);
        expect(
            onItsPage.topLeftAlpha,
            "test setup: the picture's corner should be transparent on its own page",
        ).toBe(0);

        // THE ACTION UNDER TEST: show the page among the others, while editing the page facing it.
        await goToPage(page, textPage.id);
        await choosePageView(page, "all");

        await expectGridToMatchBook(page);
        const amongOthers = await getPictureOnPage(page, picturePage.id);
        expect(
            amongOthers.src,
            "The other page asked for the page list's small copy of the picture.",
        ).not.toContain("thumbnail=1");
        expect(
            amongOthers.src,
            "The page's colour should have Bloom serve the picture for a coloured page.",
        ).toContain("transparent=");
        expect(amongOthers.naturalWidth).toBe(274);
        expect(amongOthers.naturalHeight).toBe(300);
        expect(
            amongOthers.topLeftAlpha,
            "The picture's transparent corner came out opaque on the other page.",
        ).toBe(0);
    });

    for (const how of ["imported", "recorded"] as const) {
        test(`a video ${how} on a page with All Pages showing leaves that page usable, with no wait cursor, and other pages open on a click`, async ({
            page,
        }) => {
            test.setTimeout(300000);
            const onPage = how === "imported" ? videoPage : recordingPage;
            await goToPage(page, onPage.id);
            await choosePageView(page, "all");
            await expectGridToMatchBook(page);

            // THE ACTION UNDER TEST: put a video in the box through the Sign Language tool, by its
            // Import button or by recording one with the (fake) camera.
            if (how === "imported")
                await chooseVideoFile(
                    page,
                    videoBoxes(page).first(),
                    SHORT_VIDEO,
                );
            else await recordVideo(page, videoBoxes(page).first());

            await expect
                .poll(() => isPageChangeUnderway(page), {
                    message:
                        "The wait cursor stayed over the pages after the video went in.",
                })
                .toBe(false);
            await waitForEditTabSettled(page);
            expect(await getShownPageId(page)).toBe(onPage.id);
            await expectGridToMatchBook(page);

            // Another page opens on a click, and so does the video page again.
            await clickTextOfPageInGrid(page, textPage.id);
            await waitForPageToBeShown(page, textPage.id);
            await waitForEditablePage(page);
            const placement = await getPagePlacement(page, onPage.id);
            await clickPageInGrid(
                page,
                onPage.id,
                Math.max(
                    (placement.page.top + placement.page.bottom) / 2 -
                        placement.visible.top,
                    20,
                ),
            );
            await waitForPageToBeShown(page, onPage.id);
            await waitForEditablePage(page);
            await waitForEditTabSettled(page);
            await expect(videoBoxes(page)).toHaveCount(1);
            expect(await getVideoSource(videoBoxes(page).first())).not.toBe("");
        });
    }
});
