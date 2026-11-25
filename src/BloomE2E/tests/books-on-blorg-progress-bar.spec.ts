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
import type { Page } from "@playwright/test";
import { apiGet, apiGetJson } from "../helpers/api";
import { restartWithCollectionSettings } from "../helpers/collectionSettings";
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

interface IProgressBarShown {
    /** The label above the bar, e.g. "612 English books on BloomLibrary.org". */
    label: string;
    /** The number of books the label gives. */
    count: number;
    /** How full the bar is, 0 to 100. */
    percentage: number;
}

/**
 * Wait for the progress bar to be ready, which the component marks by setting its percentage once
 * it has the count, the goal and the language name, and read what it shows. The label's wording is
 * localized, so only its number is read from it; the language name is checked separately.
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

/** The bar's percentage for this many books against this goal: full at the goal and beyond. */
function expectedPercentage(count: number, goal: number): number {
    return goal === 0 ? 100 : Math.min((count / goal) * 100, 100);
}

async function getWebGoal(page: Page): Promise<number> {
    return Number((await apiGet(page, "settings/webGoal")).body);
}

test("the progress bar shows the sandbox's live count of Language 1 books [Test Case ID 100]", async ({
    page,
}) => {
    const expectedCount = await countBooksInLanguageOnDevServer("en");
    const shown = await readProgressBar(page);
    expect(
        shown.count,
        `The bar should show dev.bloomlibrary.org's count of English books (${expectedCount}): "${shown.label}"`,
    ).toBe(expectedCount);
    const languageData = await apiGetJson<{
        languageName: string;
        languageCode: string;
    }>(page, "settings/languageData");
    expect(
        languageData.languageCode,
        "sanity check: with no sign language, the bar is for Language 1",
    ).toBe("en");
    expect(
        shown.label,
        "The bar's label should name the collection's language.",
    ).toContain(languageData.languageName);
    const goal = await getWebGoal(page);
    expect(
        shown.percentage,
        `The bar should be filled to ${shown.count} books of the goal of ${goal}.`,
    ).toBeCloseTo(expectedPercentage(shown.count, goal), 5);
});

test("with a sign language, the progress bar counts the sign language's books, not Language 1's [Test Case ID 100]", async ({
    bloomApp,
}) => {
    const page = await restartWithCollectionSettings(bloomApp, {
        languages: ["en"],
        signLanguage: kSignLanguageTag,
    });
    const signLanguageCount =
        await countBooksInLanguageOnDevServer(kSignLanguageTag);
    const language1Count = await countBooksInLanguageOnDevServer("en");
    expect(
        signLanguageCount,
        "sanity check: the sandbox's sign language and English counts differ, so the bar can only match one",
    ).not.toBe(language1Count);

    const shown = await readProgressBar(page);
    expect(
        shown.count,
        `The bar should show the sandbox's count of ${kSignLanguageTag} books (${signLanguageCount}), ` +
            `not of English books (${language1Count}): "${shown.label}"`,
    ).toBe(signLanguageCount);
    const languageData = await apiGetJson<{
        languageName: string;
        languageCode: string;
    }>(page, "settings/languageData");
    expect(
        languageData.languageCode,
        "The bar should be for the collection's sign language.",
    ).toBe(kSignLanguageTag);
    expect(
        shown.label,
        "The bar's label should name the sign language.",
    ).toContain(languageData.languageName);
    expect(
        languageData.languageName,
        "sanity check: the sign language's name is not Language 1's",
    ).not.toBe("English");
    const goal = await getWebGoal(page);
    expect(
        shown.percentage,
        `The bar should be filled to ${shown.count} books of the goal of ${goal}.`,
    ).toBeCloseTo(expectedPercentage(shown.count, goal), 5);
});
