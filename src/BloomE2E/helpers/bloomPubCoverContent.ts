// Checks that Language 2 text on a book's inside and back cover pages reaches the BloomPUB, whether
// or not Language 2 is ticked in the Publish tab's Text Languages list. Shared by the tests of the
// manual cases "Cover Content In BloomPUB" (a Basic Book) and "Digital Comic Cover Content" (a
// Digital Comic Book, whose Device front/back matter calls these pages End Extra 1, 2 and 3).
//
// A BloomPUB uses the Device front/back matter, which puts these three pages at the back as "End
// Extra" pages and drops any of them that has no text in a language being published
// (BloomPubMaker, Book.RemoveBlankPages). Their boxes are Language 2 boxes, so a page holding only
// Language 2 text survives only because Bloom keeps the front/back matter's languages even when
// the Text Languages list leaves Language 2 out.

import { expect } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    setContentLanguages,
    typeInGroup,
} from "./bookMaking";
import {
    clickLanguage,
    getLanguageRows,
    getLanguagesInBook,
    getPreviewTextBox,
    openPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
    stageBloomPub,
} from "./publish";
import { switchTab } from "./workspace";
import type { Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";

/** A page the case puts Language 2 text on, and the text it gets. */
export interface ICoverPage {
    /** The page list's caption for the page in the book being edited. */
    caption: string;
    /** The page's data-xmatter-page, which the BloomPUB's Device pack gives it too. */
    xmatterPage: string;
    text: string;
}

/** What one test of cover content works with. */
export interface ICoverContentCase {
    /** Language 1 and Language 2 of the collection, by tag. */
    l1: string;
    l2: string;
    /** The pages that get Language 2 text, in the order the BloomPUB has them. */
    coverPages: ICoverPage[];
    /** The xmatterPage of the cover page that is given nothing but its Language 2 text. */
    textOnlyPage: string;
}

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
 * Add a "Just Text" page from Basic Book to the book in the Edit tab and type text in both
 * languages on it, then leave the book showing Language 1 alone.
 *
 * Which of the two the BloomPUB carries shows whether it was made with Language 2 or without it,
 * so the checks of the cover pages are known to be about the publication the test asked for.
 */
export async function addBilingualContentPage(
    page: Page,
    coverCase: ICoverContentCase,
): Promise<void> {
    await addPage(page, "Just Text", 1, "Basic Book");
    const contentPages = await getContentPages(page);
    await goToPage(page, contentPages[contentPages.length - 1].id);
    // Showing both languages for a moment is how a person types the Language 2 text, and the
    // Language 2 text is what gives Language 2 a row in the Text Languages list.
    await setContentLanguages(page, [coverCase.l1, coverCase.l2]);
    await typeInGroup(
        page,
        ".bloom-translationGroup",
        coverCase.l1,
        CONTENT_L1,
    );
    await typeInGroup(
        page,
        ".bloom-translationGroup",
        coverCase.l2,
        CONTENT_L2,
    );
    // Back to Language 1 alone, so the book does not require Language 2 and the Text Languages
    // list lets it be left out.
    await setContentLanguages(page, [coverCase.l1]);
}

/**
 * Check that a BloomPUB of the book in the Edit tab leaves out the text-only cover page while it is
 * still blank, then go back to the Edit tab.
 *
 * Bloom keeps a page in a BloomPUB for its pictures, video and the like as well as for its text.
 * This is the control that shows the page has nothing else to keep it, so that, once it has text,
 * finding it in the BloomPUB shows that its Language 2 text is what kept it.
 */
export async function expectBlankTextOnlyPageLeftOut(
    page: Page,
    coverCase: ICoverContentCase,
): Promise<void> {
    await openPublishDestination(page, "BloomPUB");
    const stagedPages = await readStagedPages(page);
    const describePages = () =>
        JSON.stringify(stagedPages.map((p) => [p.xmatterPage, p.texts]));
    expect(
        stagedPages.some((p) =>
            p.texts.some(
                (t) => t.lang === coverCase.l1 && t.text === CONTENT_L1,
            ),
        ),
        `test setup: the content page's Language 1 text should be in the BloomPUB; it has ${describePages()}`,
    ).toBe(true);
    const textOnlyCoverPage = coverCase.coverPages.find(
        (p) => p.xmatterPage === coverCase.textOnlyPage,
    )!;
    expect(
        stagedPages.some((p) => p.xmatterPage === coverCase.textOnlyPage),
        `test setup: a BloomPUB should leave out the ${textOnlyCoverPage.caption} page while it is blank; it has ${describePages()}`,
    ).toBe(false);
    await switchTab(page, "edit");
}

/** Type each cover page's Language 2 text on it, in the book in the Edit tab. */
export async function typeLanguage2OnCoverPages(
    page: Page,
    coverCase: ICoverContentCase,
): Promise<void> {
    const pages = await getPages(page);
    for (const coverPage of coverCase.coverPages) {
        const bookPage = pages.find((p) => p.caption === coverPage.caption);
        if (!bookPage)
            throw new Error(
                `The book has no "${coverPage.caption}" page; its pages are ${pages.map((p) => p.caption).join(", ")}.`,
            );
        await goToPage(page, bookPage.id);
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            coverCase.l2,
            coverPage.text,
        );
    }
    // Leave the last cover page, which is what makes Bloom save it.
    await goToPage(page, pages[0].id);
}

/**
 * Open the BloomPUB screen on the selected book, check that Bloom lists Language 2 among the
 * book's languages without requiring it, and return the name the Text Languages list gives it.
 */
export async function openBloomPubScreenWithOptionalL2(
    page: Page,
    coverCase: ICoverContentCase,
): Promise<string> {
    await openPublishDestination(page, "BloomPUB");
    const l2Info = (await getLanguagesInBook(page)).find(
        (l) => l.code === coverCase.l2,
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
}

/**
 * Tick or untick Language 2 in the BloomPUB screen's Text Languages list, and wait until Bloom has
 * recorded it. `l2Name` is the name the list gives Language 2.
 */
export async function setL2Published(
    page: Page,
    coverCase: ICoverContentCase,
    l2Name: string,
    ticked: boolean,
): Promise<void> {
    const isL2Ticked = async () =>
        (await getLanguageRows(page, "text")).find((r) => r.name === l2Name)
            ?.checked;
    if ((await isL2Ticked()) !== ticked)
        await clickLanguage(page, "text", l2Name);
    // The check box shows a click at once and tells Bloom afterwards, and staging reads Bloom's
    // setting. So wait for both to agree before making a BloomPUB.
    await expect.poll(isL2Ticked, { timeout: 30000 }).toBe(ticked);
    await expect
        .poll(
            async () =>
                (await getLanguagesInBook(page)).find(
                    (l) => l.code === coverCase.l2,
                )?.includeText,
            {
                timeout: 30000,
                message: `Bloom never recorded Language 2 as ${ticked ? "" : "not "}published.`,
            },
        )
        .toBe(ticked);
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
 * that the text-only cover page holds that text and nothing else, and that the preview shows each
 * cover page's text to a reader. `l2Ticked` says whether Language 2 is being published, which the
 * content page added by addBilingualContentPage shows.
 */
export async function expectCoverPagesInBloomPub(
    page: Page,
    coverCase: ICoverContentCase,
    l2Ticked: boolean,
): Promise<void> {
    const { l1, l2, coverPages } = coverCase;
    const when = l2Ticked
        ? "with Language 2 ticked"
        : "with Language 2 not ticked";
    const stagedPages = await readStagedPages(page);
    const describePages = () =>
        JSON.stringify(stagedPages.map((p) => [p.xmatterPage, p.texts]));

    const contentPages = stagedPages.filter((p) => p.xmatterPage === null);
    const contentPage = contentPages.find((p) =>
        p.texts.some((t) => t.lang === l1 && t.text === CONTENT_L1),
    );
    if (!contentPage)
        throw new Error(
            `test setup: the content page's Language 1 text should be in the BloomPUB ${when}; it has ${describePages()}`,
        );
    expect(
        contentPage.texts.some((t) => t.lang === l2 && t.text === CONTENT_L2),
        `test setup: the content page's Language 2 text should be in the BloomPUB only when Language 2 is ticked (${when}); it has ${describePages()}`,
    ).toBe(l2Ticked);

    expect(
        stagedPages.some((p) => p.xmatterPage === "frontCover"),
        `The BloomPUB ${when} should have the front cover; it has ${describePages()}`,
    ).toBe(true);
    const coverPageIds: string[] = [];
    for (const coverPage of coverPages) {
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
        ).toContainEqual({ lang: l2, text: coverPage.text });
        coverPageIds.push(staged.id);
    }

    // This cover page was given nothing but its Language 2 text, so it is a page kept for that
    // text alone.
    const textOnlyCoverPage = coverPages.find(
        (p) => p.xmatterPage === coverCase.textOnlyPage,
    )!;
    const textOnlyStaged = stagedPages.find(
        (p) => p.xmatterPage === coverCase.textOnlyPage,
    )!;
    expect(
        { texts: textOnlyStaged.texts, images: textOnlyStaged.images },
        `The ${textOnlyCoverPage.caption} page of the BloomPUB ${when} should hold only its Language 2 text.`,
    ).toEqual({
        texts: [{ lang: l2, text: textOnlyCoverPage.text }],
        images: 0,
    });

    // And the preview is the same publication: it opens on the front cover. bloom-player builds
    // only the pages near the one it shows, so the content page is checked in the preview only
    // when it is the page beside the cover.
    const player = await showBloomPubPreview(page);
    await expect(
        player.locator(
            '.swiper-slide-active .bloom-page[data-xmatter-page="frontCover"]',
        ),
        `The BloomPUB preview ${when} should open on the front cover.`,
    ).toHaveCount(1);
    const shownContentPage = await showPreviewPage(player, contentPage.id);
    const contentBox = (lang: string, text: string) =>
        shownContentPage
            .locator(`.bloom-editable[lang="${lang}"]`)
            .filter({ hasText: text });
    await expect(
        contentBox(l1, CONTENT_L1),
        `test setup: the preview's content page should carry its Language 1 text ${when}`,
    ).toHaveCount(1);
    await expect(
        contentBox(l2, CONTENT_L2),
        `The preview's content page should carry its Language 2 text only when Language 2 is ticked (${when}).`,
    ).toHaveCount(l2Ticked ? 1 : 0);

    // A reader sees each cover page with its text. They are at the back, in this order, as the
    // Device front/back matter puts them.
    for (const [index, coverPage] of coverPages.entries()) {
        const shown = await showPreviewPage(player, coverPageIds[index]);
        await expect(
            getPreviewTextBox(shown, coverPage.text),
            `The BloomPUB preview ${when} should show the ${coverPage.caption} page's Language 2 text.`,
        ).toBeVisible();
    }
}
