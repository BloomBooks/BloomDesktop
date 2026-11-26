// An ePUB made with "Include image descriptions on page" unticked keeps the pictures' descriptions
// off its pages, yet still reads them aloud: a sighted reader is not shown them, while a listener
// still hears them (EpubMaker.HandleImageDescriptions leaves them in the page, hidden, BL-7237).
// BL-7839 once took the description's audio out of the read-aloud, and was reverted as wrong.
//
// Automates the manual test "ePUB Without Descriptions On Page" (Test Case ID 573).
//
// The ePUB is read from the ePUB screen's preview, which shows the very files that Save... zips.
// Save... itself opens the operating system's save dialog, which no test can answer, and the
// manual case's reader app (eKitabu) is not something a test can drive; what that app reads aloud
// is the page's media overlay, which this test reads instead.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Frame, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseEpubMode,
    epubPreviewHasFile,
    getEpubPreviewImageDescriptions,
    getIncludeImageDescriptionsBoxState,
    getEpubPreviewPageItems,
    getEpubPreviewPageReadAloud,
    openEpubPublishScreen,
    setIncludeImageDescriptionsOnPage,
    showEpubPreview,
    showEpubPreviewPage,
    type EpubMode,
} from "../helpers/epubPublish";
import {
    kImageDescriptionGroup,
    openImageDescriptionTool,
} from "../helpers/imageDescription";
import { chooseImageFile } from "../helpers/images";
import { addNarration } from "../helpers/talkingBook";
import { enableToolForBook, expandToolByHeader } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "epub-without-descriptions-on-page",
        languages: ["en"],
        // Publishing an ePUB needs a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const L1 = "en";
const PICTURE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

const TITLE = "The Golden Touch";
const COVER_DESCRIPTION = "A small bird sits on a branch.";
const PAGE_DESCRIPTION = "The bird looks at the sky.";
const PAGE_TEXT = "This is a text page.";
const PAGE_TEXT_GROUP = `.bloom-translationGroup:not(${kImageDescriptionGroup})`;

/** One described page of the book, and the ids Bloom gave the recordings on it. */
interface IDescribedPage {
    name: string;
    pageId: string;
    description: string;
    /** The page's own words that are not the description: the title, or the page's text. */
    otherText: string;
    descriptionIds: string[];
    otherTextIds: string[];
}

test("an ePUB without image descriptions on the page hides them but still reads them aloud [Test Case ID 573]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);

    const pages = await step(
        "Make a book whose cover and one content page each have a picture with a description, and the title and page text",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image");
            await enableToolForBook(page, bookFolder, "imageDescription");
            const cover = (await getPages(page))[0];
            const [contentPage] = await getContentPages(page);
            expect(
                cover.isContentPage,
                "test setup: the first page is the cover",
            ).toBe(false);

            await goToPage(page, cover.id);
            await chooseImageFile(page, PICTURE);
            await typeInGroup(page, ".bookTitle", L1, TITLE);
            await openImageDescriptionTool(page);
            await typeInGroup(
                page,
                kImageDescriptionGroup,
                L1,
                COVER_DESCRIPTION,
            );

            await goToPage(page, contentPage.id);
            await chooseImageFile(page, PICTURE);
            await typeInGroup(page, PAGE_TEXT_GROUP, L1, PAGE_TEXT);
            await openImageDescriptionTool(page);
            await typeInGroup(
                page,
                kImageDescriptionGroup,
                L1,
                PAGE_DESCRIPTION,
            );
            // Leaving the cover saved the title, and Bloom renamed the book's folder to match it.
            return {
                bookFolder: await findBookFolder(page, TITLE),
                cover,
                contentPage,
            };
        },
    );
    if (!pages) throw new Error("The book was not made.");

    const described = await step(
        "Record the title, the page text and both descriptions with the Talking Book tool",
        async () => {
            // Recording needs a microphone, which a test has not got: addNarration puts a recording
            // under each id the tool gave a sentence, where Bloom's own recording would go.
            await expandToolByHeader(page, "talkingBook");
            const recorded: IDescribedPage[] = [];
            for (const { name, pageId, description, otherGroup, otherText } of [
                {
                    name: "the cover",
                    pageId: pages.cover.id,
                    description: COVER_DESCRIPTION,
                    otherGroup: ".bookTitle",
                    otherText: TITLE,
                },
                {
                    name: "the content page",
                    pageId: pages.contentPage.id,
                    description: PAGE_DESCRIPTION,
                    otherGroup: PAGE_TEXT_GROUP,
                    otherText: PAGE_TEXT,
                },
            ]) {
                await goToPage(page, pageId);
                const descriptionIds = await waitForSentencesMarked(
                    page,
                    kImageDescriptionGroup,
                    description,
                );
                const otherTextIds = await waitForSentencesMarked(
                    page,
                    otherGroup,
                    otherText,
                );
                const narrated = await addNarration(page, pages.bookFolder, L1);
                expect(
                    [...narrated].sort(),
                    `test setup: every recording on ${name} is the description's or the ${name === "the cover" ? "title's" : "page text's"}`,
                ).toEqual([...descriptionIds, ...otherTextIds].sort());
                recorded.push({
                    name,
                    pageId,
                    description,
                    otherText,
                    descriptionIds,
                    otherTextIds,
                });
            }
            return recorded;
        },
    );
    if (!described) throw new Error("The recordings were not made.");

    await step(
        'Publish as an ePUB in its first mode, fixed, where "Include image descriptions on page" is not ticked',
        async () => {
            await openEpubPublishScreen(page);
            await chooseEpubMode(page, "fixed");
            expect(
                await getIncludeImageDescriptionsBoxState(page),
                'in fixed mode the "Include image descriptions on page" box should be unticked, and cannot be ticked',
            ).toEqual({ ticked: false, enabled: false });
            await showEpubPreview(page);
        },
    );
    await step(
        "Check that the fixed ePUB shows no description but reads each one aloud with the page's text",
        async () => {
            for (const describedPage of described)
                await expectDescriptionHiddenButReadAloud(
                    page,
                    "fixed",
                    describedPage,
                );
        },
    );

    await step(
        'Publish as a flowable ePUB with "Include image descriptions on page" unticked',
        async () => {
            await chooseEpubMode(page, "flowable");
            await setIncludeImageDescriptionsOnPage(page, false);
            await showEpubPreview(page);
        },
    );
    await step(
        "Check that the flowable ePUB shows no description but reads each one aloud with the page's text",
        async () => {
            for (const describedPage of described)
                await expectDescriptionHiddenButReadAloud(
                    page,
                    "flowable",
                    describedPage,
                );
        },
    );

    await step(
        'Control: tick "Include image descriptions on page", and check that the same ePUB now shows each description on its page',
        async () => {
            await setIncludeImageDescriptionsOnPage(page, true);
            await showEpubPreview(page);
            for (const { name, pageId, description } of described) {
                const { frame } = await showEpubPreviewPage(page, pageId);
                const descriptions = getEpubPreviewImageDescriptions(frame);
                await expect(
                    descriptions,
                    `with the box ticked, ${name} of the ePUB should show its picture's description`,
                ).toHaveCount(1);
                await expect(descriptions).toBeVisible();
                await expect(descriptions).toHaveText(description, {
                    useInnerText: true,
                });
            }
        },
    );
});

/**
 * Wait until the Talking Book tool has marked `text` as one sentence to record in the L1 box of the
 * first group `groupSelector` picks, and return the sentence's id.
 */
async function waitForSentencesMarked(
    page: Page,
    groupSelector: string,
    text: string,
): Promise<string[]> {
    const marked = editablePageFrame(page)
        .locator(groupSelector)
        .first()
        .locator(`.bloom-editable[lang="${L1}"] .audio-sentence[id]`);
    await expect
        .poll(
            async () =>
                (await marked.allTextContents()).map((words) => words.trim()),
            {
                timeout: 30000,
                message: `The Talking Book tool did not mark "${text}" in "${groupSelector}" for recording.`,
            },
        )
        .toEqual([text]);
    return marked.evaluateAll((elements) => elements.map((e) => e.id));
}

/**
 * The id an element of the book has in the ePUB: an ePUB validator objects to an id that starts
 * with a digit, so Bloom puts an "i" before one (EpubMaker.FixIllegalIds).
 */
function idInEpub(id: string): string {
    return /^\d/.test(id) ? `i${id}` : id;
}

/**
 * Check one page of the ePUB the preview is showing, made without the descriptions on its pages:
 * the picture is there, with its description as its alternative text; the description is in the
 * page's document but not shown; and the page's read-aloud plays a recording for each sentence of
 * the description and of the page's other text, and for nothing else.
 */
async function expectDescriptionHiddenButReadAloud(
    page: Page,
    mode: EpubMode,
    described: IDescribedPage,
): Promise<void> {
    const { name, pageId, description, otherText } = described;
    const where = `${name} of the ${mode} ePUB`;
    const { frame } = await showEpubPreviewPage(page, pageId);

    await expect(
        getEpubPreviewImageDescriptions(frame),
        `${where} should have no description put on the page`,
    ).toHaveCount(0);
    const items = await getEpubPreviewPageItems(frame);
    expect(
        items.filter((item) => item.kind === "picture"),
        `${where} should show its picture`,
    ).toContainEqual({
        kind: "picture",
        content: expect.stringMatching(/^bird\d*\.png$/),
    });
    expect(
        items
            .filter((item) => item.kind === "text")
            .map((item) => item.content),
        `${where} should show its own text, and not the description`,
    ).toEqual([otherText]);
    await expect(
        frame.locator(`img[alt="${description}"]`),
        `the picture on ${where} should have its description as its alternative text`,
    ).toHaveCount(1);

    const hidden = await readHiddenDescription(frame, described.descriptionIds);
    expect(
        hidden,
        `${where} should keep the description in the page, with its recording, but not show it`,
    ).toEqual({ text: description, showing: false, recordingsFound: true });

    const clips = await getEpubPreviewPageReadAloud(page, frame);
    expect(
        clips.map((clip) => clip.textId).sort(),
        `${where} should read aloud the description and the page's text, each once`,
    ).toEqual(
        [...described.descriptionIds, ...described.otherTextIds]
            .map(idInEpub)
            .sort(),
    );
    for (const clip of clips) {
        expect(
            clip.clipEnd,
            `the clip of ${where} that reads ${clip.textId} should have some audio`,
        ).toBeGreaterThan(clip.clipBegin);
        expect(
            await epubPreviewHasFile(page, clip.audioUrl),
            `the ePUB should have the audio file ${clip.audioUrl} that ${where} plays`,
        ).toBe(true);
    }
}

/**
 * The description box in a page of the ePUB: its words, whether it is drawn, and whether each
 * recording of it (by id in the book) is in it.
 */
async function readHiddenDescription(
    frame: Frame,
    recordingIds: string[],
): Promise<{ text: string; showing: boolean; recordingsFound: boolean }> {
    return frame.evaluate(
        ({ selector, ids }) => {
            const groups = [...document.querySelectorAll(selector)];
            if (groups.length !== 1)
                throw new Error(
                    `The page has ${groups.length} description groups, not one.`,
                );
            const group = groups[0] as HTMLElement;
            const showing = (element: Element) =>
                (element as HTMLElement).getClientRects().length > 0 &&
                getComputedStyle(element).visibility !== "hidden";
            return {
                text: (group.textContent ?? "").trim(),
                showing:
                    showing(group) ||
                    [...group.querySelectorAll("*")].some(showing),
                recordingsFound: ids.every(
                    (id) => !!group.querySelector(`[id="${id}"]`),
                ),
            };
        },
        {
            selector: kImageDescriptionGroup,
            ids: recordingIds.map(idInEpub),
        },
    );
}
