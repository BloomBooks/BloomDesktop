// A book made from the Playground template is a place to try every feature of a subscription, so
// Bloom lets it use them all as if the collection were Enterprise. In return it may not be
// published, and the AI image editor opens in "look-around" mode, where nothing can be made.
// Automates the manual test "Playground book" (Test Case ID 833).
//
// The collection has an Enterprise subscription, so that an ordinary book in it can be published
// from every screen and can run the AI image editor's tools. Each test does the same with an
// ordinary book first, which shows that what stops the Playground book is that it is one.
//
// Not checked (see the pull request): the card's first section, which lists what a Playground book
// can use and has nothing to verify; the Audio or Video screen, whose Record button stays disabled
// for an ordinary book too under the test's launch; and Apps, which build from the whole collection
// and so go by the collection's own subscription for every book (BL-16855).

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    cancelAiImageEditor,
    declineOpenRouterWelcome,
    dismissLookAroundNotice,
    launchAiImageEditorWithoutOpenRouterKey,
    lookAroundNotice,
    openAiImageEditorFromPicture,
    selectAiImageTool,
    showAllAiImageTools,
} from "../helpers/aiImageEditor";
import { makeBookFromTemplate, typeInGroup } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { chooseImageFile } from "../helpers/images";
import { waitForClickToSettle } from "../helpers/realClick";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    expectMissingRequirements,
    expectUploadStepButtons,
    openPublishToWeb,
    pretendLoginState,
    stopPretendingAboutLogin,
} from "../helpers/libraryPublish";
import {
    selectPublishDestination,
    type PublishDestination,
} from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "playground-book",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const PLAYGROUND_TEMPLATE = "Playground";
const ORDINARY_TEMPLATE = "Basic Book";

const BIRD = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// A tool of the AI image editor that runs in the browser, without OpenRouter, so it can run for an
// ordinary book even though no OpenRouter key is set where the test runs.
const BROWSER_TOOL = "remove_background";

// One tool from each group of the AI image editor's tools (Enhance, Localize, Text, Games, More),
// so that a group the test failed to open shows as a missing tool.
const A_TOOL_OF_EACH_GROUP = [
    "improve_quality",
    "apply_localized_characters",
    "change_text",
    BROWSER_TOOL,
    "generate_image",
];

type Publishing = "enabled" | "disabled";

/**
 * Make a book from `template` with a title and a picture on its cover, and return once the Edit
 * tab shows its cover.
 */
async function makeBook(page: Page, template: string): Promise<void> {
    await makeBookFromTemplate(page, template);
    await typeInGroup(page, ".bookTitle", "en", `${template} TC833`);
    await chooseImageFile(page, BIRD);
}

/**
 * Open one destination of the Publish tab, and return what its screen heard when it asked Bloom
 * whether the book is a Playground book. Every screen that publishes asks this as it opens, and
 * until the answer comes it shows its buttons as its default says (most screens start them
 * disabled), so a button read before then says nothing about the book. This returns once the
 * screen has had the answer and React has drawn it.
 */
async function openDestination(
    page: Page,
    destination: PublishDestination,
): Promise<boolean> {
    const answer = page.waitForResponse(
        (response) => response.url().includes("/api/publish/isPlaygroundBook"),
        { timeout: 60000 },
    );
    if (destination === "Web") await openPublishToWeb(page);
    else await selectPublishDestination(page, destination);
    const response = await answer;
    await response.finished();
    // The screen's handler runs as the response's body arrives in the page; two frames later
    // React has committed what it drew from it.
    await waitForClickToSettle(page);
    return (await response.text()) === "true";
}

/** The button with this test id on the screen showing; screens left behind stay in the page. */
function shownButton(page: Page, testId: string): Locator {
    return page.locator(`[data-testid="${testId}"]:visible`);
}

async function expectButton(
    button: Locator,
    state: Publishing,
    message: string,
): Promise<void> {
    if (state === "enabled")
        await expect(button, message).toBeEnabled({ timeout: 30000 });
    else await expect(button, message).toBeDisabled({ timeout: 30000 });
}

/**
 * Go through Publish's PDF & Print, BloomPUB, ePUB and Web screens with the selected book, and
 * check that the button that publishes it on each is `state`.
 */
async function expectPublishing(
    page: Page,
    isPlayground: boolean,
    state: Publishing,
): Promise<void> {
    const which = isPlayground ? "Playground book" : "ordinary book";

    expect(await openDestination(page, "PDF & Print")).toBe(isPlayground);
    // Bloom makes a PDF only once a mode is chosen, and until then both buttons are disabled
    // whatever the book is.
    await shownButton(page, "PublishTab.OnePagePerPaperRadio").click();
    await expect(
        page.locator('iframe[src*="pdf" i]'),
        "Bloom never showed the Simple PDF.",
    ).toBeVisible({ timeout: 120000 });
    await expectButton(
        shownButton(page, "PublishTab.PrintButton"),
        state,
        `PDF & Print's Print button is not ${state} for the ${which}.`,
    );
    await expectButton(
        shownButton(page, "PublishTab.SaveButton"),
        state,
        `PDF & Print's Save PDF button is not ${state} for the ${which}.`,
    );

    expect(await openDestination(page, "BloomPUB")).toBe(isPlayground);
    // The button that starts the chosen way of sharing a BloomPUB: saving it, a USB cable or WiFi.
    await expectButton(
        page.locator(
            [
                '[data-testid="PublishTab.Save"]:visible',
                '[data-testid="PublishTab.Android.Usb.Start"]:visible',
                '[data-testid="PublishTab.Android.Wifi.Start"]:visible',
            ].join(", "),
        ),
        state,
        `BloomPUB's button that shares the book is not ${state} for the ${which}.`,
    );

    expect(await openDestination(page, "ePUB")).toBe(isPlayground);
    await expectButton(
        shownButton(page, "PublishTab.Save"),
        state,
        `ePUB's Save button is not ${state} for the ${which}.`,
    );

    expect(await openDestination(page, "Web")).toBe(isPlayground);
    await clickToFixMissingItem(page, "Copyright");
    await setCopyrightHolder(page, "TC833 Publisher");
    await openPublishToWeb(page);
    await expectMissingRequirements(
        page,
        [],
        `Publish: Web still asked for something the ${which} has.`,
    );
    await acceptAllAgreements(page);
    await pretendLoginState(page, "e2e-tester@example.com");
    try {
        await expectUploadStepButtons(
            page,
            // Bloom shuts the whole Upload step for a Playground book, Sign out with Upload Book.
            { signIn: "absent", uploadBook: state, signOut: state },
            `Publish: Web's Upload step is not ${state} for the ${which}, signed in, with everything it needs.`,
        );
    } finally {
        await stopPretendingAboutLogin(page);
    }
}

test.describe("a Playground book [Test Case ID 833]", () => {
    test.afterEach(async ({ page }) => {
        // The next test starts on whatever tab this one leaves showing.
        await switchTab(page, "collection");
    });

    test("cannot be published, though an ordinary book can [Test Case ID 833]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await makeBook(page, ORDINARY_TEMPLATE);
        await expectPublishing(page, false, "enabled");

        await switchTab(page, "collection");
        await makeBook(page, PLAYGROUND_TEMPLATE);
        await expectPublishing(page, true, "disabled");
    });

    test("opens the AI image editor in look-around mode, where no tool can run [Test Case ID 833]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await launchAiImageEditorWithoutOpenRouterKey(page);
        // An ordinary book: no look-around notice, and a tool that needs no OpenRouter can run.
        // With no OpenRouter key the editor first offers to connect to it.
        await makeBook(page, ORDINARY_TEMPLATE);
        await openAiImageEditorFromPicture(page);
        await declineOpenRouterWelcome(page);
        await showAllAiImageTools(page);
        const ordinaryRun = await selectAiImageTool(page, BROWSER_TOOL);
        await expect(
            ordinaryRun,
            `The AI image editor would not run ${BROWSER_TOOL} on a picture in an ordinary book.`,
        ).toBeEnabled({ timeout: 30000 });
        await expect(
            lookAroundNotice(page),
            "The AI image editor said it was in look-around mode for an ordinary book.",
        ).toHaveCount(0);
        await cancelAiImageEditor(page);

        await switchTab(page, "collection");
        await makeBook(page, PLAYGROUND_TEMPLATE);
        await openAiImageEditorFromPicture(page);
        await expect(
            lookAroundNotice(page),
            "The AI image editor did not say it was in look-around mode for a Playground book.",
        ).toBeVisible({ timeout: 30000 });
        await dismissLookAroundNotice(page);

        const tools = await showAllAiImageTools(page);
        expect(
            tools,
            "The AI image editor did not show a tool of every group, so a group was left shut.",
        ).toEqual(expect.arrayContaining(A_TOOL_OF_EACH_GROUP));
        const runnable: string[] = [];
        for (const tool of tools) {
            const run = await selectAiImageTool(page, tool);
            if (await run.isEnabled()) runnable.push(tool);
        }
        expect(
            runnable,
            "These tools of the AI image editor can run on a picture in a Playground book.",
        ).toEqual([]);
        await cancelAiImageEditor(page);
    });
});
