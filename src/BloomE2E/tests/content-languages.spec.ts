// Which languages a book's ordinary text boxes show, chosen two ways that must agree: the language
// button on the Edit tab's top bar ("One Language", "Two Languages", "Three Languages"), and the
// Languages page of Book and Page Settings ("Show <language>"). A language that is not shown on the
// page appears in the box's source bubble instead. At least one language is always shown, so the
// last one cannot be turned off in either place. The front cover's title has settings of its own,
// which the top bar's choice must not change.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { openBookSettings } from "../helpers/bookSettings";
import { realClick } from "../helpers/realClick";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";

// L1, L2 and L3 of the collection, in that order, and the text the test puts in each.
const L1 = "en";
const L2 = "fr";
const L3 = "es";
const TEXT: Record<string, string> = {
    [L1]: "The heron waits",
    [L2]: "Le héron attend",
    [L3]: "La garza espera",
};

test.use({
    collectionSpec: { name: "content-languages", languages: [L1, L2, L3] },
});

const GROUP = ".bloom-translationGroup.bloom-trailingElement";

/** The languages whose box is showing in the page's text group, in document order. */
async function getLanguagesShownOnPage(page: Page): Promise<string[]> {
    return editablePageFrame(page)
        .locator(`${GROUP} .bloom-editable[lang]`)
        .evaluateAll((boxes) =>
            boxes
                .filter(
                    (box) =>
                        box.getAttribute("lang") !== "z" &&
                        getComputedStyle(box).display !== "none",
                )
                .map((box) => box.getAttribute("lang") ?? ""),
        );
}

/** Wait until the page shows exactly these languages' boxes. */
async function expectLanguagesShownOnPage(
    page: Page,
    languages: string[],
    message: string,
): Promise<void> {
    await expect
        .poll(async () => (await getLanguagesShownOnPage(page)).sort(), {
            timeout: 30000,
            message,
        })
        .toEqual([...languages].sort());
}

/** The source bubble of the page's text group. */
function sourceBubble(page: Page): Locator {
    return editablePageFrame(page).locator(".qtip.uibloomSourceTextsBubble");
}

/**
 * Put the insertion point in the first box that the page's text group shows, which is when Bloom
 * shows the group's source bubble.
 */
async function clickInTextGroup(page: Page): Promise<void> {
    const shown = await getLanguagesShownOnPage(page);
    await realClick(
        editablePageFrame(page)
            .locator(`${GROUP} .bloom-editable[lang="${shown[0]}"]`)
            .first(),
    );
    await expect
        .poll(
            async () =>
                editablePageFrame(page)
                    .locator("body")
                    .evaluate(
                        (body, group) =>
                            !!body.ownerDocument.activeElement?.closest(group),
                        GROUP,
                    ),
            {
                timeout: 15000,
                message:
                    "Clicking in the page's text group should put the insertion point there",
            },
        )
        .toBe(true);
}

/**
 * Check that the page's text group shows a source bubble that holds the text of each of
 * `inBubble` and of none of `notInBubble`.
 */
async function expectSourceBubble(
    page: Page,
    inBubble: string[],
    notInBubble: string[],
): Promise<void> {
    await clickInTextGroup(page);
    const bubble = sourceBubble(page);
    await expect(
        bubble,
        `The text group should have a source bubble, for ${inBubble.join(" and ")}`,
    ).toBeVisible({ timeout: 15000 });
    // Sanity check for expectNoSourceBubble: this mark is how it knows a group has a bubble.
    await expect(
        editablePageFrame(page).locator(GROUP),
        "A text group with a source bubble should be marked as having one",
    ).toHaveAttribute("data-hasqtip");
    // The bubble shows one language at a time, with a tab for each. A language whose text is
    // behind another's is brought forward by its tab, as a person would, so that each text is
    // checked as something that can be read and not only as something in the bubble's HTML.
    for (const language of inBubble) {
        const text = bubble.locator(`.source-text[lang="${language}"]`);
        await expect(
            text,
            `${language} should be in the source bubble`,
        ).toContainText(TEXT[language]);
        if (!(await text.isVisible()))
            await bubble
                .locator(`li[id="${language}"] a.sourceTextTab`)
                .click();
        await expect(
            text,
            `The source bubble should show the ${language} text`,
        ).toBeVisible();
    }
    for (const language of notInBubble)
        await expect(
            bubble,
            `${language} should not be in the source bubble`,
        ).not.toContainText(TEXT[language]);
}

/**
 * Check that the page's text group has no source bubble, as when the page shows every language
 * that the group has text in. Bloom draws a bubble only when its group first needs it, so this
 * also checks that the group has none waiting to be drawn.
 */
async function expectNoSourceBubble(page: Page): Promise<void> {
    await clickInTextGroup(page);
    await expect(
        editablePageFrame(page).locator(GROUP),
        "The text group should have no source bubble",
    ).not.toHaveAttribute("data-hasqtip");
    await expect(
        sourceBubble(page),
        "The page should show no source bubble",
    ).toHaveCount(0);
}

/** The top bar's language button. */
function languageButton(page: Page): Locator {
    return page.locator("#contentLanguagesDropdownButton");
}

/** Open the top bar's language menu and return it. */
async function openLanguageMenu(page: Page): Promise<Locator> {
    await languageButton(page).click();
    const menu = page.getByRole("menu");
    await menu.waitFor({ state: "visible", timeout: 15000 });
    return menu;
}

/** Close the top bar's language menu without choosing anything. */
async function closeLanguageMenu(page: Page): Promise<void> {
    await page.keyboard.press("Escape");
    await expect(page.getByRole("menu")).toHaveCount(0);
}

/** A language in the top bar's menu: its name, whether it is ticked, and whether it can be clicked. */
interface ILanguageMenuItem {
    name: string;
    checked: boolean;
    enabled: boolean;
}

/** The languages in the top bar's menu, which must be open, as it shows them at this moment. */
async function readLanguageMenuItems(
    menu: Locator,
): Promise<ILanguageMenuItem[]> {
    return menu.getByRole("menuitem").evaluateAll((elements) =>
        elements.map((item) => ({
            name: (item.textContent ?? "").trim(),
            checked:
                (item.querySelector("input[type=checkbox]") as HTMLInputElement)
                    ?.checked ?? false,
            enabled: item.getAttribute("aria-disabled") !== "true",
        })),
    );
}

/** The name the top bar's menu and the Languages page give a language, e.g. "French". */
let languageNames: Record<string, string> = {};

/**
 * Read the collection's three language names from the top bar's menu, which lists L1, L2 and L3
 * in that order.
 */
async function readLanguageNames(page: Page): Promise<void> {
    const menu = await openLanguageMenu(page);
    await expect(
        menu.getByRole("menuitem"),
        "Sanity check: the top bar's menu should list the collection's three languages",
    ).toHaveCount(3);
    const items = await readLanguageMenuItems(menu);
    languageNames = {
        [L1]: items[0].name,
        [L2]: items[1].name,
        [L3]: items[2].name,
    };
    await closeLanguageMenu(page);
}

/**
 * Check that the top bar's menu ticks exactly these languages, and that it lets every language
 * be clicked except a ticked one that is the only one ticked.
 *
 * The menu opens showing what it showed last time and then asks Bloom for the current state, so
 * right after it opens it can still show a choice from before a change made elsewhere. So this
 * waits for the menu to show the expected state rather than reading it once.
 */
async function expectLanguageMenuShows(
    page: Page,
    languages: string[],
    message: string,
): Promise<void> {
    const menu = await openLanguageMenu(page);
    await expect
        .poll(() => readLanguageMenuItems(menu), { timeout: 15000, message })
        .toEqual(
            [L1, L2, L3].map((language) => ({
                name: languageNames[language],
                checked: languages.includes(language),
                enabled: !languages.includes(language) || languages.length > 1,
            })),
        );
    await closeLanguageMenu(page);
}

/**
 * Turn one language on or off by clicking it in the top bar's menu, and wait until the page shows
 * the result.
 *
 * What a click asks Bloom for is the opposite of the tick that the menu shows, and the menu can
 * open showing an old state (see expectLanguageMenuShows). So this waits for the menu to show the
 * language as it is before the change, and as something that can be clicked, before it clicks.
 */
async function toggleInTopBar(
    page: Page,
    language: string,
    turnOn: boolean,
): Promise<void> {
    await waitForEditTabSettled(page);
    const menu = await openLanguageMenu(page);
    const item = menu
        .getByRole("menuitem")
        .filter({ hasText: languageNames[language] });
    await expect(
        item.locator("input[type=checkbox]"),
        `Before it is turned ${turnOn ? "on" : "off"} in the top bar, ${language} should ${turnOn ? "not " : ""}be ticked there`,
    ).toBeChecked({ checked: !turnOn });
    await expect(
        item,
        `The top bar should let ${language} be turned ${turnOn ? "on" : "off"}`,
    ).not.toHaveAttribute("aria-disabled", "true");
    await realClick(item);
    await waitForEditTabSettled(page);
}

/**
 * The "Show <language>" check box on the Languages page of the open Book and Page Settings. The
 * check box has no name of its own; the row that holds it is named for the language.
 */
function showCheckbox(page: Page, language: string): Locator {
    return page
        .getByRole("dialog")
        .filter({ hasText: "Book and Page Settings" })
        .getByRole("button", {
            name: `Show ${languageNames[language]}`,
            exact: true,
        })
        .getByRole("checkbox");
}

/** Open Book and Page Settings on its Languages page. */
async function openLanguagesPage(page: Page): Promise<void> {
    await waitForEditTabSettled(page);
    await openBookSettings(page, "Languages");
}

/** Click OK in Book and Page Settings, and wait until the page has been redrawn. */
async function okBookSettings(page: Page): Promise<void> {
    await realClick(
        page
            .getByRole("dialog")
            .filter({ hasText: "Book and Page Settings" })
            .getByTestId("dialog-ok"),
    );
    await expect(
        page.getByRole("dialog").filter({ hasText: "Book and Page Settings" }),
    ).toHaveCount(0, { timeout: 30000 });
    await waitForEditTabSettled(page);
}

/** Close Book and Page Settings with Cancel. */
async function cancelBookSettings(page: Page): Promise<void> {
    await realClick(
        page
            .getByRole("dialog")
            .filter({ hasText: "Book and Page Settings" })
            .getByTestId("dialog-cancel"),
    );
    await expect(
        page.getByRole("dialog").filter({ hasText: "Book and Page Settings" }),
    ).toHaveCount(0, { timeout: 30000 });
}

/** Check that the Languages page ticks exactly these languages, and close it with Cancel. */
async function expectLanguagesPageShows(
    page: Page,
    languages: string[],
    where: string,
): Promise<void> {
    await openLanguagesPage(page);
    for (const language of [L1, L2, L3]) {
        await expect(
            showCheckbox(page, language),
            `${where}, the Languages page should ${languages.includes(language) ? "" : "not "}tick ${language}`,
        ).toBeChecked({ checked: languages.includes(language) });
    }
    await cancelBookSettings(page);
}

/** The languages whose title box shows on the front cover. */
async function getCoverTitleLanguages(page: Page): Promise<string[]> {
    return editablePageFrame(page)
        .locator(".bookTitle .bloom-editable[lang]")
        .evaluateAll((boxes) =>
            boxes
                .filter(
                    (box) =>
                        box.getAttribute("lang") !== "z" &&
                        getComputedStyle(box).display !== "none",
                )
                .map((box) => box.getAttribute("lang") ?? "")
                .sort(),
        );
}

test("the top bar and the Languages page choose the same languages, and one always stays [Test Case ID 405]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(400000);

    // The top bar's button, the Book and Page Settings dialog and its "Show <language>" rows are
    // found by their English wording, and a freshly launched Bloom takes its UI language from the
    // operating system.
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    await step("Make a book with a text page", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
    });
    const [contentPage] = await getContentPages(page);
    await step(
        "Fill in the page in all three languages, and show only L1",
        async () => {
            await goToPage(page, contentPage.id);
            // Setup, so through the API: show every language long enough to type in it.
            await setContentLanguages(page, [L1, L2, L3]);
            for (const language of [L1, L2, L3])
                await typeInGroup(page, GROUP, language, TEXT[language]);
            await setContentLanguages(page, [L1]);
        },
    );

    await readLanguageNames(page);

    await step(
        "See only L1, in the top bar, the Languages page and the page",
        async () => {
            await expect(languageButton(page)).toHaveText(/One Language/);
            await expectLanguageMenuShows(
                page,
                [L1],
                "The top bar's menu should tick only L1",
            );
            await expectLanguagesPageShows(page, [L1], "With only L1 shown");
            await expectLanguagesShownOnPage(
                page,
                [L1],
                "The page should show only L1",
            );
            await expectSourceBubble(page, [L2, L3], []);
        },
    );

    await step("Turn on L2 in the top bar", async () => {
        await toggleInTopBar(page, L2, true);
        await expectLanguagesShownOnPage(
            page,
            [L1, L2],
            "The page should show L1 and L2",
        );
        // L3 is still not on the page, so the bubble stays, with L3 alone.
        await expectSourceBubble(page, [L3], [L2]);
        await expect(languageButton(page)).toHaveText(/Two Languages/);
        await expectLanguageMenuShows(
            page,
            [L1, L2],
            "The top bar's menu should tick L1 and L2",
        );
        await expectLanguagesPageShows(
            page,
            [L1, L2],
            "After turning on L2 in the top bar",
        );
    });

    await step("Turn on L3 in the Languages page", async () => {
        await openLanguagesPage(page);
        await realClick(showCheckbox(page, L3));
        await expect(showCheckbox(page, L3)).toBeChecked();
        await okBookSettings(page);
        await expectLanguagesShownOnPage(
            page,
            [L1, L2, L3],
            "The page should show all three",
        );
        // The group has text in no other language, so nothing is left for a bubble.
        await expectNoSourceBubble(page);
        await expect(languageButton(page)).toHaveText(/Three Languages/);
        await expectLanguageMenuShows(
            page,
            [L1, L2, L3],
            "The top bar's menu should tick all three",
        );
    });

    await step(
        "Turn off L1 and L2 in the top bar, and try to turn off L3",
        async () => {
            await toggleInTopBar(page, L1, false);
            await toggleInTopBar(page, L2, false);
            await expectLanguagesShownOnPage(
                page,
                [L3],
                "The page should show only L3",
            );
            await expectLanguageMenuShows(
                page,
                [L3],
                "The top bar should tick only L3, and not let this last language be turned off",
            );
            await openLanguagesPage(page);
            await expect(showCheckbox(page, L3)).toBeChecked();
            await expect(
                showCheckbox(page, L3),
                "The Languages page should not let the last language, L3, be turned off",
            ).toBeDisabled();
            await cancelBookSettings(page);
        },
    );

    await step("Turn on L2 and turn off L3 in the Languages page", async () => {
        await openLanguagesPage(page);
        await realClick(showCheckbox(page, L2));
        await expect(showCheckbox(page, L2)).toBeChecked();
        await realClick(showCheckbox(page, L3));
        await expect(showCheckbox(page, L3)).toBeChecked({ checked: false });
        await expect(
            showCheckbox(page, L2),
            "The Languages page should not let the last language, L2, be turned off",
        ).toBeDisabled();
        await okBookSettings(page);
        await expectLanguagesShownOnPage(
            page,
            [L2],
            "The page should show only L2",
        );
        await expectLanguageMenuShows(
            page,
            [L2],
            "The top bar should tick only L2, and not let this last language be turned off",
        );
    });

    await step("Turn on L1 and turn off L2 in the top bar", async () => {
        await toggleInTopBar(page, L1, true);
        await toggleInTopBar(page, L2, false);
        await expectLanguagesShownOnPage(
            page,
            [L1],
            "The page should show only L1",
        );
        await expectLanguageMenuShows(
            page,
            [L1],
            "The top bar should tick only L1, and not let this last language be turned off",
        );
        await openLanguagesPage(page);
        await expect(showCheckbox(page, L1)).toBeDisabled();
        await cancelBookSettings(page);
    });

    await step(
        "See that the top bar's choice does not change the cover title's languages",
        async () => {
            const cover = (await getPages(page))[0];
            await goToPage(page, cover.id);
            const before = await getCoverTitleLanguages(page);
            expect(
                before.length,
                "Sanity check: the cover should show the title in at least one language",
            ).toBeGreaterThan(0);
            await goToPage(page, contentPage.id);
            await toggleInTopBar(page, L2, true);
            await toggleInTopBar(page, L3, true);
            await expectLanguagesShownOnPage(
                page,
                [L1, L2, L3],
                "Sanity check: the page should show all three",
            );
            await goToPage(page, cover.id);
            expect(
                await getCoverTitleLanguages(page),
                "Turning languages on in the top bar should not change the cover title's languages",
            ).toEqual(before);
        },
    );
});
