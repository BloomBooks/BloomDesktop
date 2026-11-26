// A Captive Widget Page keeps the reader on its widget: Bloom Player hides its own Next and Previous
// arrows while it shows that page, so the widget alone decides when the reader may leave. Checked
// in Bloom's BloomPUB preview, which is Bloom Player itself, as Bloom Reader shows the book.
//
// Test Case ID 538 ("Captive Widget Hides Navigation"). The page template marks itself with
// data-player-options="{'hideNavigation':true}" (customWidgetPage.pug), and Bloom Player hides the
// arrows for a page that says so. The book has an ordinary Widget Page just before the captive one,
// and a text page after it, so both widget pages have a page on each side and would show both
// arrows if nothing hid them; the ordinary one shows that it is the captive page's option, not
// the widget, that hides them.

import { expect, test } from "../fixtures/bloomTest";
import {
    activePreviewPage,
    getActivePreviewPageId,
    getPreviewArrowsShown,
    goToPreviewPage,
} from "../helpers/bloomPlayer";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { chooseWidgetOnPage, writeWidgetFolder } from "../helpers/widgets";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "captive-widget",
        languages: ["en"],
        // HTML5 widgets need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const ORDINARY_WIDGET_TEXT = "TC-538 ordinary widget";
const CAPTIVE_WIDGET_TEXT = "TC-538 captive widget";

test("a Captive Widget Page hides Bloom Player's navigation arrows in the BloomPUB preview [Test Case ID 538]", async ({
    page,
    step,
}, testInfo) => {
    test.setTimeout(600000);

    const pages = await step(
        "Make a book with a Widget Page, a Captive Widget Page and a text page",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Widget Page");
            await addPage(page, "Captive Widget Page");
            await addPage(page, "Just Text", 1, "Basic Book");
            const [ordinary, captive, text] = await getContentPages(page);
            return { ordinary, captive, text };
        },
    );
    if (!pages) throw new Error("The book was never made.");
    const { ordinary, captive, text } = pages;

    await step("Choose a widget on each widget page", async () => {
        await goToPage(page, ordinary.id);
        await chooseWidgetOnPage(
            page,
            writeWidgetFolder(
                testInfo.outputPath("widgets"),
                "OrdinaryWidget",
                ORDINARY_WIDGET_TEXT,
            ),
        );
        await goToPage(page, captive.id);
        await chooseWidgetOnPage(
            page,
            writeWidgetFolder(
                testInfo.outputPath("widgets"),
                "CaptiveWidget",
                CAPTIVE_WIDGET_TEXT,
            ),
        );
        // Leaving the page saves it.
        await goToPage(page, text.id);
    });

    await switchTab(page, "publish");
    await selectPublishDestination(page, "BloomPUB");
    const player = await showBloomPubPreview(page);

    // What a page shows of its widget: the text inside the widget's frame.
    const widgetTextShown = async () => {
        const frameElement = activePreviewPage(player).locator(
            ".bloom-widgetContainer iframe",
        );
        await expect(
            frameElement,
            "The page Bloom Player is showing should hold one widget frame.",
        ).toHaveCount(1, { timeout: 30000 });
        const widget = await frameElement.contentFrame();
        if (!widget) throw new Error("The widget's frame has no document.");
        return widget.locator("#widget-text").innerText({ timeout: 30000 });
    };

    const expectArrows = async (
        expected: { previous: boolean; next: boolean },
        message: string,
    ) =>
        expect
            .poll(() => getPreviewArrowsShown(player), {
                timeout: 30000,
                message,
            })
            .toEqual(expected);

    await step("See both arrows on the ordinary Widget Page", async () => {
        await goToPreviewPage(page, player, ordinary.id);
        expect(await widgetTextShown()).toBe(ORDINARY_WIDGET_TEXT);
        await expectArrows(
            { previous: true, next: true },
            "An ordinary Widget Page, with a page on each side, should show both arrows.",
        );
    });

    await step("See no arrows on the Captive Widget Page", async () => {
        await goToPreviewPage(page, player, captive.id);
        expect(await widgetTextShown()).toBe(CAPTIVE_WIDGET_TEXT);
        await expectArrows(
            { previous: false, next: false },
            "A Captive Widget Page should hide both of Bloom Player's arrows.",
        );
        expect(
            await getActivePreviewPageId(player),
            "Bloom Player should still be showing the Captive Widget Page.",
        ).toBe(captive.id);
    });
});
