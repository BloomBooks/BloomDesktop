// The Collection tab's "books on BloomLibrary.org" progress bar: it shows how many books Bloom
// Library has in the collection's language, live from the server, against the collection's goal,
// and for a collection with a sign language it counts that sign language's books, not Language 1's.
// Automates Notion test case 100, "Language Progress Bar Live".
//
// Every Bloom a test launches talks to the sandbox, dev.bloomlibrary.org (fixtures/launchBloom.ts
// sets BloomSandbox), so "live" here means the sandbox's count, which the test reads from the same
// Bloom Library API query Bloom makes. The sandbox and bloomlibrary.org hold very different numbers
// of English books, so a bar that matches the sandbox is not reading production.
//
// What is NOT covered here, and stays on the manual card: the bar going up after an upload. That
// needs a real sign-in and upload (helpers/bloomLibraryAccount.ts), whose password was not on the
// machine where this test was written, so that part was never seen to work.

import { expect, test } from "../fixtures/bloomTest";
import type { IBloomApp } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import { apiGet, apiGetJson } from "../helpers/api";
import {
    restartWithCollectionSettings,
    type ICollectionSettings,
} from "../helpers/collectionSettings";
import { countBooksInLanguageOnDevServer } from "../helpers/bloomLibraryServer";

test.use({
    collectionSpec: {
        name: "books-on-blorg-progress-bar",
        languages: ["en"],
        signLanguage: "",
    },
});

test.describe.configure({ mode: "serial" });

// American Sign Language: the sandbox has some books in it, far fewer than in English, so the two
// counts cannot be mistaken for each other.
const kSignLanguageTag = "ase";
// Bloom's default goal (CollectionSettings.kDefaultBooksOnWebGoal), which a collection that says
// nothing about its goal gets.
const kDefaultGoal = 200;
// A goal other than the default, small enough that the sandbox's sign language books fill only part
// of the bar.
const kCustomGoal = 50;

interface IProgressBarShown {
    /** The label above the bar, e.g. "612 English books on BloomLibrary.org". */
    label: string;
    /** The number of books the label gives. */
    count: number;
    /** How full the bar is, 0 to 100. */
    percentage: number;
}

interface IProgressBarRead {
    shown: IProgressBarShown;
    /** The sandbox's count just before Bloom started, and just after the bar was read. */
    countBefore: number;
    countAfter: number;
    /** What Bloom says the bar is for. */
    languageName: string;
    languageCode: string;
}

/**
 * Wait for the progress bar to be ready and revealed, and read what it shows. The component sets
 * its percentage only once it has the count, the goal and the language name, and until then keeps
 * the bar and its label clipped out of sight; so this waits for the percentage, and then for the
 * whole bar to come into view as the clip opens. The label's wording is localized, so only its
 * number is read from it; the language name is checked by the caller.
 */
async function readProgressBar(page: Page): Promise<IProgressBarShown> {
    const container = page.getByTestId("books-on-blorg-progress");
    const bar = container.getByRole("progressbar");
    await expect
        .poll(async () => Number(await bar.getAttribute("aria-valuenow")), {
            message:
                "The progress bar should get a percentage once Bloom has the language's book count.",
            timeout: 60000,
        })
        .toBeGreaterThanOrEqual(0);
    // The bar is the last thing in the clipping box, so it is in view only once the box has opened
    // all the way.
    await expect
        .poll(
            () =>
                bar.evaluate((element) => {
                    const clip = element.parentElement!.getBoundingClientRect();
                    const box = element.getBoundingClientRect();
                    return (
                        clip.height > 0 &&
                        box.top >= clip.top &&
                        box.bottom <= clip.bottom
                    );
                }),
            {
                message:
                    "Once it has a percentage, the progress bar should be revealed, not left clipped out of sight.",
                timeout: 10000,
            },
        )
        .toBe(true);
    const label = (
        await container.getByTestId("books-on-blorg-count").innerText()
    ).trim();
    const numbers = label.match(/\d+/g) ?? [];
    expect(
        numbers,
        `The progress bar's label should give exactly one number, the count of books: "${label}"`,
    ).toHaveLength(1);
    return {
        label,
        count: Number(numbers[0]),
        percentage: Number(await bar.getAttribute("aria-valuenow")),
    };
}

/**
 * Start Bloom on these collection settings and read its progress bar, with the sandbox's count of
 * books in `languageTag` taken just before the start and just after the read. Bloom asks the
 * sandbox for its count somewhere in between, so on a shared sandbox whose count moved meanwhile
 * the bar may rightly show either; when it did not move, the bar must show exactly that count.
 */
async function startAndReadProgressBar(
    bloomApp: IBloomApp,
    settings: ICollectionSettings,
    languageTag: string,
): Promise<IProgressBarRead> {
    const countBefore = await countBooksInLanguageOnDevServer(languageTag);
    const page = await restartWithCollectionSettings(bloomApp, settings);
    const shown = await readProgressBar(page);
    const countAfter = await countBooksInLanguageOnDevServer(languageTag);
    const languageData = await apiGetJson<{
        languageName: string;
        languageCode: string;
    }>(page, "settings/languageData");
    expect(
        Number((await apiGet(page, "settings/webGoal")).body),
        "sanity check: Bloom has the goal the collection was given",
    ).toBe(settings.booksOnWebGoal ?? kDefaultGoal);
    return { shown, countBefore, countAfter, ...languageData };
}

/** Check that the bar showed the sandbox's live count of books in `languageTag`. */
function expectLiveCount(read: IProgressBarRead, languageTag: string): void {
    const low = Math.min(read.countBefore, read.countAfter);
    const high = Math.max(read.countBefore, read.countAfter);
    const sandboxCount = low === high ? `${low}` : `between ${low} and ${high}`;
    expect(
        read.shown.count >= low && read.shown.count <= high,
        `The bar should show dev.bloomlibrary.org's count of "${languageTag}" books (${sandboxCount}): "${read.shown.label}"`,
    ).toBe(true);
}

/** The bar's percentage for this many books against this goal: full at the goal and beyond. */
function expectedPercentage(count: number, goal: number): number {
    return goal === 0 ? 100 : Math.min((count / goal) * 100, 100);
}

test("the progress bar shows the sandbox's live count of Language 1 books [Test Case ID 100]", async ({
    bloomApp,
}) => {
    const read = await startAndReadProgressBar(
        bloomApp,
        { languages: ["en"], signLanguage: "" },
        "en",
    );
    expectLiveCount(read, "en");
    expect(
        read.languageCode,
        "sanity check: with no sign language, the bar is for Language 1",
    ).toBe("en");
    expect(
        read.shown.label,
        "The bar's label should name the collection's language.",
    ).toContain(read.languageName);
    expect(
        read.shown.percentage,
        `The bar should be filled to ${read.shown.count} books of the default goal of ${kDefaultGoal}.`,
    ).toBeCloseTo(expectedPercentage(read.shown.count, kDefaultGoal), 5);
});

test("with a sign language, the progress bar counts the sign language's books, not Language 1's, against the collection's goal [Test Case ID 100]", async ({
    bloomApp,
}) => {
    const language1Count = await countBooksInLanguageOnDevServer("en");
    const read = await startAndReadProgressBar(
        bloomApp,
        {
            languages: ["en"],
            signLanguage: kSignLanguageTag,
            booksOnWebGoal: kCustomGoal,
        },
        kSignLanguageTag,
    );
    expect(
        read.countBefore,
        "sanity check: the sandbox's sign language and English counts differ, so the bar can only match one",
    ).not.toBe(language1Count);
    expect(
        read.countBefore,
        "sanity check: the sandbox's sign language books fill only part of the goal, so the percentage depends on the goal",
    ).toBeLessThan(kCustomGoal);

    expectLiveCount(read, kSignLanguageTag);
    expect(
        read.languageCode,
        "The bar should be for the collection's sign language.",
    ).toBe(kSignLanguageTag);
    expect(
        read.shown.label,
        "The bar's label should name the sign language.",
    ).toContain(read.languageName);
    expect(
        read.languageName,
        "sanity check: the sign language's name is not Language 1's",
    ).not.toBe("English");
    expect(
        read.shown.percentage,
        `The bar should be filled to ${read.shown.count} books of the collection's goal of ${kCustomGoal}.`,
    ).toBeCloseTo(expectedPercentage(read.shown.count, kCustomGoal), 5);
});
