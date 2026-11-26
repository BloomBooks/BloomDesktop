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

function topicDialog(page: Page): Locator {
    return page.getByRole("dialog").filter({ hasText: "Choose Topic" });
}

function topicBox(page: Page): Locator {
    return editablePageFrame(page)
        .locator(".bloom-page.outsideFrontCover .coverBottomBookTopic")
        .first();
}

/** Click the front cover's "Choose topic" link and wait for the dialog. */
async function openTopicChooser(page: Page): Promise<void> {
    await waitForEditablePage(page);
    await topicBox(page).hover();
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

/** The font weight of a topic's label in the dialog, as the browser computes it. */
async function labelWeight(page: Page, topic: string): Promise<number> {
    return topicDialog(page)
        .locator("label")
        .filter({ has: page.getByRole("radio", { name: topic, exact: true }) })
        .locator(".MuiTypography-root")
        .evaluate((element) => Number(getComputedStyle(element).fontWeight));
}

/** Pick `topic` and check that it, and only it, is checked and in bold. */
async function pickTopic(
    page: Page,
    topic: string,
    previous: string,
): Promise<void> {
    const radio = topicDialog(page).getByRole("radio", {
        name: topic,
        exact: true,
    });
    await radio.click();
    await expect(radio, `${topic} should be checked`).toBeChecked();
    await expect(
        topicDialog(page).getByRole("radio", { checked: true }),
        `only ${topic} should be checked`,
    ).toHaveCount(1);
    await expect
        .poll(() => labelWeight(page, topic), {
            message: `${topic} should be in bold once picked`,
        })
        .toBeGreaterThanOrEqual(700);
    await expect
        .poll(() => labelWeight(page, previous), {
            message: `${previous} should no longer be in bold once ${topic} is picked`,
        })
        .toBeLessThan(700);
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
    await expect(
        topicDialog(page).getByRole("radio", { name: "No Topic", exact: true }),
    ).toBeChecked();
    expect(
        await labelWeight(page, "No Topic"),
        "No Topic should start in bold",
    ).toBeGreaterThanOrEqual(700);

    await pickTopic(page, "Bible", "No Topic");
    await pickTopic(page, "Primer", "Bible");
    await pickTopic(page, "Story Book", "Primer");

    await topicDialog(page).getByRole("button", { name: "Close" }).click();
    await expect(topicDialog(page)).toBeHidden({ timeout: 30000 });
    // Bloom reloads the page with the topic on the cover.
    await expect(
        topicBox(page),
        "the cover should show the topic picked last",
    ).toHaveText("Story Book", { timeout: 30000 });

    // Opening the dialog again starts on the book's topic.
    await openTopicChooser(page);
    await expect(
        topicDialog(page).getByRole("radio", {
            name: "Story Book",
            exact: true,
        }),
    ).toBeChecked();
    expect(
        await labelWeight(page, "Story Book"),
        "the book's topic should be in bold when the dialog opens",
    ).toBeGreaterThanOrEqual(700);
});
