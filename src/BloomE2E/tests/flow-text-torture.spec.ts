// The one test that flows a book-sized run of text, and the only place in this suite where the
// number of pages is the point.
//
// It is tagged @torture and the ordinary run leaves it out (see playwright.config.ts and
// README.md, "The torture test"). Every other flow-text spec flows three pages, because three is
// what proves the feature works; this one flows tens of them, and what it proves is different:
// that the work Bloom does for a whole chain grows with the number of pages rather than with the
// square of it, and that it does not keep something per page.
//
// Both measurements are ratios rather than absolute numbers, which is what makes them safe to
// assert. A slow machine makes both halves slower and leaves the ratio alone; only a change in the
// SHAPE of the work moves it.

import { expect, test } from "../fixtures/bloomTest";
import { execFileSync } from "node:child_process";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertRunIsIntact,
    buildLongText,
    clickCreatePagesAndContinue,
    enableFlowTextFeature,
    getBookChains,
    kCharactersPerPage,
    kFlowTextCollection,
    kFlowTextFeatures,
    splitIntoParagraphs,
    typeParagraphs,
} from "../helpers/flowText";
import type { Page } from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";

// The same collection and feature token as the ordinary specs, so that on the rare run that does
// include this file it shares their Bloom rather than launching one of its own.
test.use({
    collectionSpec: kFlowTextCollection,
    experimentalFeatures: kFlowTextFeatures,
});

test.beforeAll(async ({ bloomApp }) => {
    await enableFlowTextFeature(bloomApp.page);
});

test.describe.configure({ mode: "serial" });

/** The two sizes of run this file flows, in pages. The second is deliberately double the first. */
const kSmallRunPages = 10;
const kLargeRunPages = 20;

/** How much longer the double-sized run may take than twice the smaller one. */
const kLinearSlack = 1.8;

/**
 * How much the working set may grow per extra page of the larger run. Flowing a page reads it,
 * moves text through it and saves it; nothing about that work has to be remembered afterwards, so
 * the right answer is zero and this allowance is for the noise below.
 */
const kAllowedBytesPerPage = 2 * 1024 * 1024;

/** What one flow of a book-sized run cost. */
interface IFlowCost {
    /** Pages the run ended up spread over. */
    pages: number;
    /** How long the whole flow took, in milliseconds. */
    milliseconds: number;
    /** Bloom's working set once the flow had finished, in bytes. */
    bytesAfter: number;
}

/**
 * The working set of the Bloom under test, in bytes, read from Windows.
 *
 * What this can see: the memory of the Bloom.exe process itself, which is where the book, the DOM
 * C# builds for each page it refits, and everything a walk allocates live.
 *
 * What it cannot see, and so what this test cannot catch: memory held by the WebView2 processes,
 * which are children of their own; a leak smaller than the noise of .NET's garbage collector,
 * which runs when it chooses and not when a test asks; and anything freed by the time the reading
 * is taken. A growing number here is worth investigating; a flat one is not proof of no leak.
 */
function workingSetBytes(bloomApp: IBloomApp): number {
    const output = execFileSync(
        "powershell",
        [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            `(Get-Process -Id ${bloomApp.bloomPid}).WorkingSet64`,
        ],
        { encoding: "utf8", windowsHide: true },
    ).trim();
    const bytes = Number(output);
    if (!Number.isFinite(bytes) || bytes <= 0)
        throw new Error(
            `Windows did not report a working set for the Bloom under test ` +
                `(process ${bloomApp.bloomPid}); it said ${JSON.stringify(output)}.`,
        );
    return bytes;
}

/**
 * Make a book, put a run of `pages` pages' worth of text into its first box, and let Bloom make
 * every page that run needs. Returns what the flow cost.
 *
 * The whole of the work is inside one operation — the offer to make the pages and carry the text
 * through them — so the timing is of Bloom's work and not of a test's clicking about.
 */
async function flowARunOf(
    page: Page,
    bloomApp: IBloomApp,
    pages: number,
): Promise<IFlowCost> {
    await makeBookFromTemplate(page, "Basic Book");
    await addJustTextPage(page);
    const text = buildLongText(kCharactersPerPage * pages);
    const paragraphs = splitIntoParagraphs(text, pages * 2);
    await typeParagraphs(page, 0, paragraphs);

    const startedAt = Date.now();
    await clickCreatePagesAndContinue(page, 0);
    const milliseconds = Date.now() - startedAt;

    const chains = await getBookChains(page);
    expect(
        chains,
        "The run has to be one chain, or the flow did not do what this measures.",
    ).toHaveLength(1);
    assertRunIsIntact(
        chains[0].groups.map((group) => group.textByLang["en"] ?? ""),
        text,
    );
    return {
        pages: chains[0].groups.length,
        milliseconds,
        bytesAfter: workingSetBytes(bloomApp),
    };
}

// What the two flows cost, filled in by the first test and read by the second.
let small: IFlowCost;
let large: IFlowCost;

test.describe("flowing a book-sized run of text", () => {
    test("the time it takes grows with the pages, not with their square @torture", async ({
        page,
        bloomApp,
    }) => {
        // Twenty pages at about a second a page, twice over, and the machine may be busy.
        test.setTimeout(1800000);

        small = await flowARunOf(page, bloomApp, kSmallRunPages);
        large = await flowARunOf(page, bloomApp, kLargeRunPages);

        // What it measured, printed whether or not the bounds below hold: these numbers are the
        // point of the test, and a run that passes has nothing else to show for itself.
        const megabytes = (bytes: number) => (bytes / (1024 * 1024)).toFixed(1);
        for (const [name, cost] of [
            ["smaller", small],
            ["larger", large],
        ] as const) {
            console.log(
                `flow text, ${name} run: ${cost.pages} pages in ${cost.milliseconds}ms ` +
                    `(${Math.round(cost.milliseconds / cost.pages)}ms a page), Bloom holding ` +
                    `${megabytes(cost.bytesAfter)} MB afterwards.`,
            );
        }

        // The measurement is only worth making if the two runs really are of very different
        // sizes, and if neither was so quick that the clock cannot tell them apart.
        expect(
            small.pages,
            "The smaller run has to be a book-sized one, or this proves nothing about scale.",
        ).toBeGreaterThanOrEqual(kSmallRunPages - 2);
        expect(
            large.pages / small.pages,
            "The larger run has to be nearly twice the smaller one for the ratio to mean " +
                "anything.",
        ).toBeGreaterThan(1.5);
        expect(
            small.milliseconds,
            "The smaller run finished too fast to time; flow more pages.",
        ).toBeGreaterThan(1000);

        // THE ASSERTION: work that is linear in the pages takes (large/small) times as long;
        // work that is quadratic takes the square of that, which for a doubling is twice the
        // linear figure. The bound sits between the two, so a quadratic regression fails here
        // and a slow machine does not: both halves of the ratio slow down together.
        const pageRatio = large.pages / small.pages;
        const timeRatio = large.milliseconds / small.milliseconds;
        expect(
            timeRatio,
            `Flowing ${large.pages} pages took ${timeRatio.toFixed(2)} times as long as ` +
                `flowing ${small.pages} (${small.milliseconds}ms then ${large.milliseconds}ms). ` +
                `Linear would be about ${pageRatio.toFixed(2)}; the square of that, which is ` +
                `what a per-page pass over the whole chain would cost, would be about ` +
                `${(pageRatio * pageRatio).toFixed(2)}.`,
        ).toBeLessThan(pageRatio * kLinearSlack);
    });

    test("flowing twice as many pages does not cost memory per page @torture", async ({
        page,
    }) => {
        void page;
        expect(
            small,
            "This test reads what the one before it measured.",
        ).toBeTruthy();

        // THE ASSERTION: the extra pages of the larger run must not have left anything behind.
        // Both readings are taken after a whole run has been flowed and the work has finished, so
        // what is compared is what Bloom is still holding, not what it used while working.
        const extraPages = large.pages - small.pages;
        const growth = large.bytesAfter - small.bytesAfter;
        const megabytes = (bytes: number) => (bytes / (1024 * 1024)).toFixed(1);
        expect(
            growth,
            `Bloom held ${megabytes(small.bytesAfter)} MB after flowing ${small.pages} pages ` +
                `and ${megabytes(large.bytesAfter)} MB after flowing ${large.pages}, which is ` +
                `${megabytes(growth)} MB more for ${extraPages} more pages. Flowing a page does ` +
                `not need anything kept afterwards, so a figure that rises with the page count ` +
                `means something is being kept. See workingSetBytes for what this reading can ` +
                `and cannot see.`,
        ).toBeLessThan(extraPages * kAllowedBytesPerPage);
    });
});
