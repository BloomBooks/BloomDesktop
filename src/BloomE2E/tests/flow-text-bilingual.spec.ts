// A book whose pages show two languages, each with a run of text that flows on its own.
//
// A chain is a claim about a translation GROUP, not about a box: the group on each page of the run
// belongs to the chain, and inside it every content language has a box of its own
// (E2eTestingApi's chain report gives each group's text by language tag). So the two languages
// share the pages and share nothing else — a word of one must never arrive in a box of the other,
// and where one language needs more pages than the other, the pages it gains are pages the first
// language simply has nothing on.
//
// The journey this follows is the one an author takes: write the book in one language and let the
// flow make the pages that language needs; then put the second language in, which is longer, and
// let it flow through the same pages and make the few more it needs.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addJustTextPage,
    assertRunIsIntact,
    clickCreatePagesAndContinue,
    enableFlowTextFeature,
    getBookChains,
    getBoxTexts,
    type IFlowChain,
    kFlowTextCollection,
    kFlowTextFeatures,
    kParagraphsForSeveralPages,
    kTextForSeveralPages,
    pasteText,
    runPendingReflow,
    setAutoPages,
    typeParagraphs,
    waitForReflowIdle,
} from "../helpers/flowText";
import {
    getPageLanguages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
} from "../helpers/bookMaking";

// The same collection object as every other flow-text spec, which is what lets all of them
// run on one Bloom rather than one each. Its second language is what this file needs.
test.use({
    collectionSpec: kFlowTextCollection,
    experimentalFeatures: kFlowTextFeatures,
});

// Flow text needs a paid subscription as well as the feature token above. Without it Bloom does
// not offer the feature at all and every test here fails at once; see kFlowTextCollection.
test.beforeAll(async ({ bloomApp }) => {
    await enableFlowTextFeature(bloomApp.page);
});

test.describe.configure({ mode: "serial" });

/** The collection's second language, as kFlowTextCollection names it. */
const kSecondLanguage = "fr";

/**
 * The second language's run of text: with words of its own (`f0042` rather than `w0042`) and half
 * as much again of it. The distinct words are what makes "no text leaked from one language into the
 * other" a question a test can answer; the extra length is what makes the second language need
 * pages the first one did not, which is the half of the author's journey this file is for. It is
 * the one test in this suite whose run passes three pages, and it has to: what it shows is a run
 * outgrowing the pages the other language gave it.
 *
 * One paragraph, not several, because of where the later ones would go. Typing paragraph by
 * paragraph into a box that is ALREADY part of a chain moves text off the box between one paragraph
 * and the next, and Bloom follows the moved words to the next page with the caret — so everything
 * typed after that goes into whatever box the caret has landed in, which is not the box the test
 * aimed at. Putting the whole run in at once leaves one settling pass at the end, after all of it
 * is in.
 *
 * Where the caret ends up was measured twice, on the same build, with different answers. On a book
 * built fresh for the measurement, it was in this language's box half a second after the page
 * turn and stayed there: right destination, and the group on that page carried the chain attribute
 * and a visible box of this language. Run against the book the tests above leave behind, it was
 * still not in this language's box thirty seconds later. A new page holds the focus it takes by
 * default until Bloom places the caret, and that default is the FIRST language's box, so words
 * typed before the caret arrives go into the wrong language or nowhere.
 *
 * So: do not type into a chained box and expect the next keystroke to reach the same language.
 * Anyone working on this should measure it themselves rather than trust either reading here.
 *
 * Paragraph shape is covered by flow-text-paragraphs.spec.ts; what this file is about is the two
 * languages.
 */
const kSecondLanguageText = Array.from(
    { length: Math.round((kTextForSeveralPages.length * 1.5) / 6) },
    (_unused, index) => "f" + String(index + 1).padStart(4, "0"),
).join(" ");

/** The pages the first language's run of text was given, in flow order. */
let firstLanguagePageIds: string[] = [];

/** The one chain of the book, which both languages flow through. */
async function readTheChain(page: Page): Promise<IFlowChain> {
    const chains = await getBookChains(page);
    expect(
        chains,
        "This book holds one run of text in each language, through one chain of groups.",
    ).toHaveLength(1);
    return chains[0];
}

test.describe("a run of text in each of a book's two languages", () => {
    test("the first language's run makes the pages it needs [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addJustTextPage(page);
        await typeParagraphs(page, 0, kParagraphsForSeveralPages);

        // THE ACTION UNDER TEST: let the flow make the pages this language's text needs, which is
        // where the author starts from before there is a second language at all.
        await clickCreatePagesAndContinue(page, 0);

        const chain = await readTheChain(page);
        firstLanguagePageIds = chain.groups.map((group) => group.pageId);
        expect(
            firstLanguagePageIds.length,
            "The run is sized to need three pages.",
        ).toBe(3);
        assertRunIsIntact(
            chain.groups.map((group) => group.textByLang["en"]),
            kTextForSeveralPages,
        );
    });

    test("turning the second language on gives every page of the run a box for it [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);

        // THE ACTION UNDER TEST: show two languages, the way the Edit tab's Two Languages choice
        // does.
        await setContentLanguages(page, ["en", kSecondLanguage]);

        // Ask the page which languages reached it rather than assuming the collection's order:
        // the boxes a test addresses are the page's, and their order is the page's answer.
        for (const pageId of firstLanguagePageIds) {
            await goToPage(page, pageId);
            await waitForReflowIdle(page);
            expect(
                await getPageLanguages(page),
                `Page ${pageId} of the run has to show both languages, in this order.`,
            ).toEqual(["en", kSecondLanguage]);
            expect(
                (await getBoxTexts(page, kSecondLanguage))[0],
                "The second language's box starts empty: turning a language on writes no text.",
            ).toBe("");
        }
    });

    test("the second language flows through the same pages and makes the few more it needs [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, firstLanguagePageIds[0]);
        await waitForReflowIdle(page);
        // The second language is longer than the first, so the run has to be able to grow. This is
        // the setting the reflow panel calls "Automatically add & remove pages".
        await setAutoPages(page, true);

        // THE ACTION UNDER TEST: put the second language's text in, in its own box on the first
        // page of the run, and let it flow.
        await pasteText(page, 0, kSecondLanguageText, kSecondLanguage);
        await runPendingReflow(page);

        const chain = await readTheChain(page);
        const pageIds = chain.groups.map((group) => group.pageId);
        expect(
            pageIds.slice(0, firstLanguagePageIds.length),
            "The second language flows through the pages that are already there, in order, " +
                "rather than starting a run of its own somewhere else.",
        ).toEqual(firstLanguagePageIds);
        expect(
            pageIds.length,
            "This language's text is half as long again, so the run has to have grown.",
        ).toBeGreaterThan(firstLanguagePageIds.length);

        // Each language's run is whole, read in flow order through the chain.
        assertRunIsIntact(
            chain.groups.map((group) => group.textByLang["en"] ?? ""),
            kTextForSeveralPages,
        );
        assertRunIsIntact(
            chain.groups.map(
                (group) => group.textByLang[kSecondLanguage] ?? "",
            ),
            kSecondLanguageText,
        );
    });

    test("neither language's words are in the other's boxes [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const chain = await readTheChain(page);

        // The two runs are made of words that say which language they belong to, so a word in the
        // wrong box is a word this can name. Flowing one language must never write in another
        // language's box, and the pages the second language added must hold none of the first.
        for (const group of chain.groups) {
            const first = group.textByLang["en"] ?? "";
            const second = group.textByLang[kSecondLanguage] ?? "";
            expect(
                first.match(/f\d{4}/g) ?? [],
                `The first language's box on page ${group.pageId} holds words of the second.`,
            ).toEqual([]);
            expect(
                second.match(/w\d{4}/g) ?? [],
                `The second language's box on page ${group.pageId} holds words of the first.`,
            ).toEqual([]);
        }

        // Note what is NOT asserted here: that the pages the second language added hold no first
        // language text. They may. The pages belong to the chain, so they are boxes the first
        // language's run can use too, and a refit of it will spread the run over them. What would
        // be wrong is a word arriving in a box of the wrong language, which is what the check
        // above is for.
        assertRunIsIntact(
            chain.groups.map((group) => group.textByLang["en"] ?? ""),
            kTextForSeveralPages,
        );
    });
});
