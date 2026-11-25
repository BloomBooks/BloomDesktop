// Text in Language 2 on the inside front cover, the inside back cover and the outside back cover
// reaches the BloomPUB, whether or not Language 2 is ticked in the Publish tab's Text Languages
// list. Automates the manual test "Cover Content In BloomPUB" (Test Case ID 197).
//
// A BloomPUB uses the Device front/back matter, which moves these three pages to the back as "End
// Extra" pages and drops any of them that has no text in a language being published
// (BloomPubMaker, Book.RemoveBlankPages). Their boxes are Language 2 boxes, so a page holding only
// Language 2 text survives only because Bloom keeps the front/back matter's languages even when
// the Text Languages list leaves Language 2 out. That is what this test checks, with Language 2
// ticked and then not ticked.
//
// The collection gets the Traditional pack, the one the Settings dialog calls the default, because
// it is the pack that has all three pages; the pack a test collection starts with puts the credits
// on the inside front cover instead.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { restartWithCollectionSettings } from "../helpers/collectionSettings";
import {
    clickLanguage,
    getLanguageRows,
    getLanguagesInBook,
    getPreviewTextBox,
    openPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
    stageBloomPub,
} from "../helpers/publish";
import type { Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";

const LANGUAGES = ["fr", "de"];
const [L1, L2] = LANGUAGES;

test.use({
    collectionSpec: { name: "bloompub-cover-content", languages: LANGUAGES },
});

/** A page the case puts Language 2 text on, and the text it gets. */
interface ICoverPage {
    /** The page list's caption for the page in the Traditional pack. */
    caption: string;
    /** The page's data-xmatter-page, which the BloomPUB's Device pack gives it too. */
    xmatterPage: string;
    text: string;
}

const COVER_PAGES: ICoverPage[] = [
    {
        caption: "Inside Front Cover",
        xmatterPage: "insideFrontCover",
        text: "Innenseite vorne",
    },
    {
        caption: "Inside Back Cover",
        xmatterPage: "insideBackCover",
        text: "Innenseite hinten",
    },
    {
        caption: "Outside Back Cover",
        xmatterPage: "outsideBackCover",
        text: "Rückseite",
    },
];

/** What the content page says in each language. */
const CONTENT_L1 = "Bonjour";
const CONTENT_L2 = "Guten Tag";

/** One page of a staged BloomPUB, as its .htm has it. */
interface IStagedPage {
    id: string;
    /** The page's data-xmatter-page; null on a content page. */
    xmatterPage: string | null;
    /** Every language box on the page that has text, by language tag. */
    texts: { lang: string; text: string }[];
    /** How many <img> elements the page has. */
    images: number;
}

/**
 * Stage the selected book as a BloomPUB, as PREVIEW does, and read the pages of the .htm it
 * carries, in book order. The Publish tab must be open on the book.
 *
 * bloom-player builds only the pages near the one it shows, so the preview cannot be asked which
 * pages and which languages' text the publication holds; the staged book can.
 */
async function readStagedPages(page: Page): Promise<IStagedPage[]> {
    const folder = await stageBloomPub(page);
    const htmFiles = fs
        .readdirSync(folder)
        .filter((name) => name.toLowerCase().endsWith(".htm"));
    if (htmFiles.length !== 1)
        throw new Error(
            `Expected one .htm in the staged BloomPUB at ${folder}, found ${htmFiles.length}: ${htmFiles.join(", ")}.`,
        );
    const html = fs.readFileSync(Path.join(folder, htmFiles[0]), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return [...dom.querySelectorAll("body > .bloom-page")].map(
            (bloomPage) => ({
                id: bloomPage.getAttribute("id") ?? "",
                xmatterPage: bloomPage.getAttribute("data-xmatter-page"),
                texts: [...bloomPage.querySelectorAll(".bloom-editable")]
                    .map((box) => ({
                        lang: box.getAttribute("lang") ?? "",
                        text: (box.textContent ?? "").trim(),
                    }))
                    .filter((box) => box.text !== ""),
                images: bloomPage.querySelectorAll("img").length,
            }),
        );
    }, html);
}

/**
 * Check that the BloomPUB carries the front cover and every cover page with its Language 2 text,
 * that the inside front cover holds that text and nothing else, and that the preview shows each
 * cover page's text to a reader. `l2Ticked` says whether Language 2 is being published, which
 * the content page shows.
 */
async function expectCoverPagesInBloomPub(
    page: Page,
    l2Ticked: boolean,
): Promise<void> {
    const when = l2Ticked
        ? "with Language 2 ticked"
        : "with Language 2 not ticked";
    const stagedPages = await readStagedPages(page);
    const describePages = () =>
        JSON.stringify(stagedPages.map((p) => [p.xmatterPage, p.texts]));

    // The content page shows whether the BloomPUB was made with Language 2 or without it, so the
    // checks below are known to be about the publication the test asked for.
    const contentTexts = stagedPages
        .filter((p) => p.xmatterPage === null)
        .flatMap((p) => p.texts);
    expect(
        contentTexts,
        `test setup: the content page's Language 1 text should be in the BloomPUB ${when}; it has ${describePages()}`,
    ).toContainEqual({ lang: L1, text: CONTENT_L1 });
    expect(
        contentTexts.some((t) => t.lang === L2 && t.text === CONTENT_L2),
        `test setup: the content page's Language 2 text should be in the BloomPUB only when Language 2 is ticked (${when}); it has ${describePages()}`,
    ).toBe(l2Ticked);

    expect(
        stagedPages.some((p) => p.xmatterPage === "frontCover"),
        `The BloomPUB ${when} should have the front cover; it has ${describePages()}`,
    ).toBe(true);
    const coverPageIds: string[] = [];
    for (const coverPage of COVER_PAGES) {
        const staged = stagedPages.find(
            (p) => p.xmatterPage === coverPage.xmatterPage,
        );
        if (!staged)
            throw new Error(
                `The BloomPUB ${when} has no ${coverPage.caption} page; it has ${describePages()}`,
            );
        expect(
            staged.texts,
            `The ${coverPage.caption} page of the BloomPUB ${when} should have its Language 2 text.`,
        ).toContainEqual({ lang: L2, text: coverPage.text });
        coverPageIds.push(staged.id);
    }

    // The inside front cover was given nothing but its Language 2 text, so it is a page kept for
    // that text alone.
    const insideFrontCover = stagedPages.find(
        (p) => p.xmatterPage === "insideFrontCover",
    )!;
    expect(
        { texts: insideFrontCover.texts, images: insideFrontCover.images },
        `The inside front cover of the BloomPUB ${when} should hold only its Language 2 text.`,
    ).toEqual({ texts: [{ lang: L2, text: COVER_PAGES[0].text }], images: 0 });

    // And the preview is the same publication: it opens on the front cover, and the content page
    // beside it carries its Language 2 text only when Language 2 is ticked. bloom-player builds
    // only the pages near the one it shows, which on opening are the cover and the content page.
    const player = await showBloomPubPreview(page);
    await expect(
        player.locator(
            '.swiper-slide-active .bloom-page[data-xmatter-page="frontCover"]',
        ),
        `The BloomPUB preview ${when} should open on the front cover.`,
    ).toHaveCount(1);
    const contentPageId = stagedPages.find((p) => p.xmatterPage === null)!.id;
    const contentBox = (lang: string, text: string) =>
        player
            .locator(
                `.bloom-page[id="${contentPageId}"] .bloom-editable[lang="${lang}"]`,
            )
            .filter({ hasText: text });
    await expect(
        contentBox(L1, CONTENT_L1),
        `test setup: the preview's content page should carry its Language 1 text ${when}`,
    ).toHaveCount(1);
    await expect(
        contentBox(L2, CONTENT_L2),
        `The preview's content page should carry its Language 2 text only when Language 2 is ticked (${when}).`,
    ).toHaveCount(l2Ticked ? 1 : 0);

    // A reader sees each cover page with its text. They are at the back, in this order, as the
    // Device front/back matter puts them.
    for (const [index, coverPage] of COVER_PAGES.entries()) {
        const shown = await showPreviewPage(player, coverPageIds[index]);
        await expect(
            getPreviewTextBox(shown, coverPage.text),
            `The BloomPUB preview ${when} should show the ${coverPage.caption} page's Language 2 text.`,
        ).toBeVisible();
    }
}

test("Language 2 text on the inside and back covers is in the BloomPUB whether or not Language 2 is published [Test Case ID 197]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(420000);

    const page = await step(
        "Give the collection the Traditional front/back matter",
        async () =>
            restartWithCollectionSettings(bloomApp, {
                languages: LANGUAGES,
                xmatterPack: "Traditional",
            }),
    );
    if (!page) throw new Error("Bloom did not come back after the restart.");

    await step(
        "Make a Basic Book with a content page in both languages",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            // Showing both languages for a moment is how a person types the Language 2 text, and the
            // Language 2 text is what gives Language 2 a row in the Text Languages list.
            await setContentLanguages(page, [L1, L2]);
            await typeInGroup(page, ".bloom-translationGroup", L1, CONTENT_L1);
            await typeInGroup(page, ".bloom-translationGroup", L2, CONTENT_L2);
            // Back to Language 1 alone, so the book does not require Language 2 and the Text Languages
            // list lets it be left out.
            await setContentLanguages(page, [L1]);
        },
    );

    await step(
        "Type Language 2 text on the inside front, inside back and outside back covers",
        async () => {
            const pages = await getPages(page);
            for (const coverPage of COVER_PAGES) {
                const bookPage = pages.find(
                    (p) => p.caption === coverPage.caption,
                );
                if (!bookPage)
                    throw new Error(
                        `The book has no "${coverPage.caption}" page; its pages are ${pages.map((p) => p.caption).join(", ")}.`,
                    );
                await goToPage(page, bookPage.id);
                await typeInGroup(
                    page,
                    ".bloom-translationGroup",
                    L2,
                    coverPage.text,
                );
            }
            // Leave the last cover page, which is what makes Bloom save it.
            await goToPage(page, pages[0].id);
        },
    );

    const l2Name = await step("Open the BloomPUB screen", async () => {
        await openPublishDestination(page, "BloomPUB");
        const l2Info = (await getLanguagesInBook(page)).find(
            (l) => l.code === L2,
        );
        if (!l2Info)
            throw new Error(
                "test setup: Bloom does not list Language 2 among the book's languages.",
            );
        expect(
            l2Info.required,
            "test setup: the book should not require Language 2",
        ).toBe(false);
        return l2Info.name;
    });
    if (l2Name === undefined) throw new Error("No Language 2 name was read.");

    const isL2Ticked = async () =>
        (await getLanguageRows(page, "text")).find((r) => r.name === l2Name)
            ?.checked;
    // The check box shows a click at once and tells Bloom afterwards, and staging reads Bloom's
    // setting. So wait for both to agree before making a BloomPUB.
    const expectL2Ticked = async (ticked: boolean) => {
        await expect.poll(isL2Ticked, { timeout: 30000 }).toBe(ticked);
        await expect
            .poll(
                async () =>
                    (await getLanguagesInBook(page)).find((l) => l.code === L2)
                        ?.includeText,
                {
                    timeout: 30000,
                    message: `Bloom never recorded Language 2 as ${ticked ? "" : "not "}published.`,
                },
            )
            .toBe(ticked);
    };

    await step("Tick Language 2 and preview the BloomPUB", async () => {
        if (!(await isL2Ticked())) await clickLanguage(page, "text", l2Name);
        await expectL2Ticked(true);
        await expectCoverPagesInBloomPub(page, true);
    });

    await step("Untick Language 2 and preview the BloomPUB again", async () => {
        await clickLanguage(page, "text", l2Name);
        await expectL2Ticked(false);
        await expectCoverPagesInBloomPub(page, false);
    });
});
