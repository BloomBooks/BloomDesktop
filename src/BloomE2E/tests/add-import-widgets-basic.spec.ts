// "Add And Import Widgets" (Test Case ID 536) asks the tester to confirm that widgets need a
// subscription. In a collection without one, the Add Page dialog offers both kinds of widget page
// but will not add them: in place of its Add Page button it shows a notice that names the tier the
// widgets need (FeatureRegistry's entry for Widget).
//
// add-import-widgets.spec.ts adds the pages and imports widgets in a collection with a
// subscription.

import { expect, test } from "../fixtures/bloomTest";
import {
    getAddPageOffer,
    getAddPageSubscriptionNoticeText,
    openAddPageDialog,
    selectPageInAddPageDialog,
} from "../helpers/addPageDialog";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { getFeatureStatus } from "../helpers/collectionSettings";

test.use({
    collectionSpec: { name: "add-import-widgets-basic", languages: ["en"] },
});

test("without a subscription, neither kind of widget page can be added [Test Case ID 536]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    const widget = await getFeatureStatus(page, "widget");
    expect(
        widget.enabled,
        "Sanity check: a collection without a subscription should not reach widgets",
    ).toBe(false);
    await openAddPageDialog(page);
    for (const label of ["Widget Page", "Captive Widget Page"]) {
        await selectPageInAddPageDialog(page, label);
        // The dialog asks Bloom for the page's feature status when the page is selected, and
        // offers Add Page with an empty notice until the answer comes; so both of these wait.
        await expect
            .poll(() => getAddPageOffer(page), {
                message: `The Add Page dialog should show the subscription notice in place of its Add Page button for "${label}"`,
                timeout: 15000,
            })
            .toEqual({
                addButtonOffered: false,
                requiresSubscriptionNotice: true,
            });
        await expect
            .poll(() => getAddPageSubscriptionNoticeText(page), {
                message: `The notice for "${label}" should name the subscription widgets need`,
                timeout: 15000,
            })
            .toContain(widget.subscriptionTier);
    }
});
