// The Choose Topic dialog that the front cover's "Choose topic" link opens. Automates the manual
// test "Front Cover Topic Chooser" (Test Case ID 88): each topic the user picks is the one checked
// and the one in bold (BL-11154 was a picked topic that did not become bold), and the topic picked
// last is the one the cover shows once the dialog closes.
//
// The link is a hint bubble that Bloom's editing code puts on the cover's empty topic box
// (BloomHintBubbles.MakeHelpBubble); it calls the workspace's showEditViewTopicChooserDialog, so the
// dialog opens in the workspace page, outside the page frame. Closing it posts editView/setTopic,
// and Bloom reloads the page with the topic on the cover.

import { expect, test } from "../fixtures/bloomTest";
import {
    editablePageFrame,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
} from "../helpers/bookMaking";
import { realClick } from "../helpers/realClick";
import type { Locator, Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "topic-chooser", languages: ["en"] },
});

// The dialog, its radio buttons and its Close button are found by the name of its radio group,
// the topics' English keys (each radio's value) and the button's
// l10n id, so that the test does not depend on the UI language. The cover shows the topic in the
// book's language, English here.
function topicDialog(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ has: page.locator('input[name="topic-radio-group"]') });
}

function topicRadio(page: Page, topicKey: string): Locator {
    return topicDialog(page).locator(
        `input[type="radio"][value="${topicKey}"]`,
    );
}

/** The English keys of the topics whose labels the browser shows in bold, in dialog order. */
async function boldTopics(page: Page): Promise<string[]> {
    return topicDialog(page)
        .locator("label")
        .evaluateAll((labels) =>
            labels
                .filter(
                    (label) =>
                        Number(
                            getComputedStyle(
                                label.querySelector(".MuiTypography-root")!,
                            ).fontWeight,
                        ) >= 700,
                )
                .map(
                    (label) =>
                        (
                            label.querySelector(
                                'input[type="radio"]',
                            ) as HTMLInputElement
                        ).value,
                ),
        );
}

function frontCover(page: Page): Locator {
    return editablePageFrame(page)
        .locator(".bloom-page.outsideFrontCover")
        .first();
}

function topicBox(page: Page): Locator {
    return frontCover(page).locator(".coverBottomBookTopic").first();
}

/**
 * Click the front cover's "Choose topic" link and wait for the dialog. The link's bubble shows
 * while the mouse is over the cover page (editMode.less). Hovering the topic box itself does not
 * work: while the box is empty, the tip of its own bubble lies over the middle of it and takes
 * the pointer.
 */
async function openTopicChooser(page: Page): Promise<void> {
    await waitForEditablePage(page);
    await frontCover(page).hover();
    const link = editablePageFrame(page)
        .locator(".qtip a[href*='showEditViewTopicChooserDialog']")
        .first();
    try {
        await link.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        throw new Error(
            'The front cover showed no "Choose topic" link on its topic box.',
        );
    }
    await realClick(link);
    await expect(
        topicDialog(page),
        "Clicking the link should open the Choose Topic dialog",
    ).toBeVisible({ timeout: 30000 });
}

/** Check that `topicKey`, and no other topic, is checked and in bold. */
async function expectOnlyTopicMarked(
    page: Page,
    topicKey: string,
): Promise<void> {
    await expect(
        topicRadio(page, topicKey),
        `${topicKey} should be checked`,
    ).toBeChecked();
    await expect(
        topicDialog(page).getByRole("radio", { checked: true }),
        `only ${topicKey} should be checked`,
    ).toHaveCount(1);
    await expect
        .poll(() => boldTopics(page), {
            message: `${topicKey}, and no other topic, should be in bold`,
        })
        .toEqual([topicKey]);
}

/** Pick `topicKey` and check that it, and only it, is checked and in bold. */
async function pickTopic(page: Page, topicKey: string): Promise<void> {
    await topicRadio(page, topicKey).click();
    await expectOnlyTopicMarked(page, topicKey);
}

test("the Choose Topic dialog marks the topic picked, and the cover shows it [Test Case ID 88]", async ({
    page,
}) => {
    test.setTimeout(300000);

    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "The Golden Touch");
    await expect(
        topicBox(page),
        "test setup: a new Basic Book has no topic",
    ).toHaveText("");

    await openTopicChooser(page);
    // A book with no topic starts on "No Topic".
    await expectOnlyTopicMarked(page, "No Topic");

    await pickTopic(page, "Bible");
    await pickTopic(page, "Primer");
    await pickTopic(page, "Story Book");

    await topicDialog(page).getByTestId("Common.Close").click();
    await expect(topicDialog(page)).toBeHidden({ timeout: 30000 });
    // Bloom reloads the page with the topic on the cover.
    await expect(
        topicBox(page),
        "the cover should show the topic picked last",
    ).toHaveText("Story Book", { timeout: 30000 });

    // Opening the dialog again starts on the book's topic.
    await openTopicChooser(page);
    await expectOnlyTopicMarked(page, "Story Book");
});
