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

/**
 * The text of the source bubble of the page's text group, which Bloom shows when the insertion
 * point is in a box of that group; "" when the group has no bubble.
 */
async function getSourceBubbleText(page: Page): Promise<string> {
    const shown = await getLanguagesShownOnPage(page);
    const box = editablePageFrame(page)
        .locator(`${GROUP} .bloom-editable[lang="${shown[0]}"]`)
        .first();
    await realClick(box);
    const bubble = editablePageFrame(page).locator(".qtip:visible");
    if (
        !(await bubble
            .first()
            .waitFor({ state: "visible", timeout: 5000 })
            .then(() => true)
            .catch(() => false))
    )
        return "";
    return (await bubble.first().textContent()) ?? "";
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

/** Each language in the top bar's menu: its name, whether it is ticked, and whether it can be clicked. */
async function getLanguageMenuItems(
    page: Page,
): Promise<{ name: string; checked: boolean; enabled: boolean }[]> {
    const menu = await openLanguageMenu(page);
    const items = await menu.getByRole("menuitem").evaluateAll((elements) =>
        elements.map((item) => ({
            name: (item.textContent ?? "").trim(),
            checked:
                (item.querySelector("input[type=checkbox]") as HTMLInputElement)
                    ?.checked ?? false,
            enabled: item.getAttribute("aria-disabled") !== "true",
        })),
    );
    await closeLanguageMenu(page);
    return items;
}

/** The name the top bar's menu and the Languages page give a language, e.g. "French". */
let languageNames: Record<string, string> = {};

/** Click one language in the top bar's menu, and wait until the page shows the result. */
async function toggleInTopBar(page: Page, language: string): Promise<void> {
    await waitForEditTabSettled(page);
    const menu = await openLanguageMenu(page);
    await realClick(
        menu.getByRole("menuitem").filter({ hasText: languageNames[language] }),
    );
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
    page,
    step,
}) => {
    test.setTimeout(400000);

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

    const items = await getLanguageMenuItems(page);
    expect(
        items.length,
        "Sanity check: the top bar's menu should list the collection's three languages",
    ).toBe(3);
    // The menu lists L1, L2 and L3 in that order.
    languageNames = {
        [L1]: items[0].name,
        [L2]: items[1].name,
        [L3]: items[2].name,
    };

    await step(
        "See only L1, in the top bar, the Languages page and the page",
        async () => {
            await expect(languageButton(page)).toHaveText(/One Language/);
            expect(
                (await getLanguageMenuItems(page)).map((i) => i.checked),
                "The top bar's menu should tick only L1",
            ).toEqual([true, false, false]);
            await expectLanguagesPageShows(page, [L1], "With only L1 shown");
            await expectLanguagesShownOnPage(
                page,
                [L1],
                "The page should show only L1",
            );
            const bubble = await getSourceBubbleText(page);
            expect(bubble, "L2 should be in the source bubble").toContain(
                TEXT[L2],
            );
            expect(bubble, "L3 should be in the source bubble").toContain(
                TEXT[L3],
            );
        },
    );

    await step("Turn on L2 in the top bar", async () => {
        await toggleInTopBar(page, L2);
        await expectLanguagesShownOnPage(
            page,
            [L1, L2],
            "The page should show L1 and L2",
        );
        expect(
            await getSourceBubbleText(page),
            "L2 should no longer be in the source bubble",
        ).not.toContain(TEXT[L2]);
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
        expect(
            await getSourceBubbleText(page),
            "L3 should no longer be in the source bubble",
        ).not.toContain(TEXT[L3]);
        await expect(languageButton(page)).toHaveText(/Three Languages/);
        expect(
            (await getLanguageMenuItems(page)).map((i) => i.checked),
            "The top bar's menu should tick all three",
        ).toEqual([true, true, true]);
    });

    await step(
        "Turn off L1 and L2 in the top bar, and try to turn off L3",
        async () => {
            await toggleInTopBar(page, L1);
            await toggleInTopBar(page, L2);
            await expectLanguagesShownOnPage(
                page,
                [L3],
                "The page should show only L3",
            );
            const items = await getLanguageMenuItems(page);
            expect(
                items[2],
                "The top bar should not let the last language, L3, be turned off",
            ).toEqual({
                name: languageNames[L3],
                checked: true,
                enabled: false,
            });
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
        expect(
            (await getLanguageMenuItems(page))[1],
            "The top bar should not let the last language, L2, be turned off",
        ).toEqual({ name: languageNames[L2], checked: true, enabled: false });
    });

    await step("Turn on L1 and turn off L2 in the top bar", async () => {
        await toggleInTopBar(page, L1);
        await toggleInTopBar(page, L2);
        await expectLanguagesShownOnPage(
            page,
            [L1],
            "The page should show only L1",
        );
        expect(
            (await getLanguageMenuItems(page))[0],
            "The top bar should not let the last language, L1, be turned off",
        ).toEqual({ name: languageNames[L1], checked: true, enabled: false });
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
            await toggleInTopBar(page, L2);
            await toggleInTopBar(page, L3);
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
