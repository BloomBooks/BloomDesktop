// The two choice games of the Games template, "Choose Word from Image" and "Choose Image from
// Word", need a subscription. In a collection without one, the Add Page dialog offers the page but
// will not add it: in place of its Add Page button it shows a notice naming the tier the games need,
// with a button that takes the person to the Subscription page of Collection Settings.
//
// choose-word-picture-activities.spec.ts plays the games in a collection that has a subscription.

import { expect, test } from "../fixtures/bloomTest";
import {
    clickAddPageSubscriptionSettings,
    getAddPageOffer,
    getAddPageSubscriptionNoticeText,
    openAddPageDialog,
    selectPageInAddPageDialog,
} from "../helpers/addPageDialog";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    cancelCollectionSettings,
    getFeatureStatus,
    getShowingCollectionSettingsPage,
} from "../helpers/collectionSettings";

test.use({
    collectionSpec: { name: "choice-activities-basic", languages: ["en"] },
});

test("without a subscription, a Choose Word from Image page is not added and its notice leads to the Subscription page [Test Case ID 534]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    const game = await getFeatureStatus(page, "game");
    expect(
        game.enabled,
        "Sanity check: a collection without a subscription should not reach the games",
    ).toBe(false);

    await openAddPageDialog(page);
    await selectPageInAddPageDialog(page, "Choose Word from Image");

    // The dialog asks Bloom for the page's feature status when the page is selected, and offers
    // Add Page with an empty notice until the answer comes; so both of these wait for it.
    await expect
        .poll(() => getAddPageOffer(page), {
            message:
                "The Add Page dialog should show the subscription notice in place of its Add Page button",
            timeout: 15000,
        })
        .toEqual({
            addButtonOffered: false,
            requiresSubscriptionNotice: true,
        });
    await expect
        .poll(() => getAddPageSubscriptionNoticeText(page), {
            message: "The notice should name the subscription the games need",
            timeout: 15000,
        })
        .toContain(game.subscriptionTier);

    await clickAddPageSubscriptionSettings(page);
    await expect
        .poll(() => getShowingCollectionSettingsPage(page), {
            message:
                "Bloom Subscription Settings should have opened Collection Settings on its Subscription page",
            timeout: 30000,
        })
        .toBe("Subscription");
    await cancelCollectionSettings(page);
});
