// Tests for the Subscription page of the new (React) Collection Settings dialog: a person in a
// collection without a subscription is sent there by the subscription badges, from inside the
// dialog and from elsewhere in Bloom; a code typed there and cancelled is forgotten; and a code
// typed there and saved puts the collection on its tier once Bloom reopens it.
//
// The tests share one Bloom and one collection, and run in order: the last one gives the
// collection a subscription.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    chooseFromSelectedBookMenu,
    closeBookMenu,
} from "../helpers/collection";
import {
    cancelCollectionSettings,
    clickCollectionSettingsSubscriptionBadge,
    getCollectionSettingsOkLabel,
    getFeatureStatus,
    getShowingCollectionSettingsPage,
    getSubscriptionPage,
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    readSavedSubscriptionCode,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
    typeSubscriptionCode,
} from "../helpers/collectionSettings";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "subscription-settings", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// What the Subscription page shows a collection without a subscription.
const kNoSubscription = {
    code: "",
    tier: undefined,
    communityInvitation: true,
};

test("a gated command in a collection without a subscription opens the Subscription page [Test Case ID 849]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await switchTab(page, "collection");
    expect(
        (await getFeatureStatus(page, "Spreadsheet")).enabled,
        "Sanity check: without a subscription, export to a spreadsheet should be unavailable",
    ).toBe(false);

    await chooseFromSelectedBookMenu(page, ["More", "Export to Spreadsheet"]);

    await expect
        .poll(() => getShowingCollectionSettingsPage(page), {
            message: "the dialog should have opened on its Subscription page",
            timeout: 30000,
        })
        .toBe("Subscription");
    expect(await getSubscriptionPage(page)).toEqual(kNoSubscription);
    await cancelCollectionSettings(page);
    // A gated command leaves the book menu open behind the dialog (it always has; the old dialog
    // did the same), and the next test needs the Collections tab clickable.
    await closeBookMenu(page);
});

test("a subscription badge inside the dialog takes it to the Subscription page [Test Case ID 849]", async ({
    page,
}) => {
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Bloom Library");

    await clickCollectionSettingsSubscriptionBadge(page);

    await expect
        .poll(() => getShowingCollectionSettingsPage(page), {
            message:
                "the badge should have switched the open dialog to its Subscription page",
            timeout: 30000,
        })
        .toBe("Subscription");
    await cancelCollectionSettings(page);
});

test("a code typed on the Subscription page and cancelled is not saved [Test Case ID 849]", async ({
    page,
    bloomApp,
}) => {
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Subscription");

    await typeSubscriptionCode(page, kEnterpriseSubscriptionCode);
    // The page describes the code before it is saved.
    await expect
        .poll(async () => (await getSubscriptionPage(page)).tier)
        .toBe("Enterprise");
    await cancelCollectionSettings(page);

    expect(readSavedSubscriptionCode(bloomApp.collectionDir)).toBeUndefined();
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Subscription");
    expect(await getSubscriptionPage(page)).toEqual(kNoSubscription);
    await cancelCollectionSettings(page);
});

test("entering a subscription code saves it and reopens the collection on its tier [Test Case ID 849]", async ({
    page,
    bloomApp,
}) => {
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Subscription");
    expect(await getSubscriptionPage(page)).toEqual(kNoSubscription);
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");

    await typeSubscriptionCode(page, kEnterpriseSubscriptionCode);
    await expect
        .poll(() => getSubscriptionPage(page), {
            message:
                "the page should show the code's tier and drop the invitation",
        })
        .toEqual({
            code: kEnterpriseSubscriptionCode,
            tier: "Enterprise",
            communityInvitation: false,
        });
    // The branding, and so every book, comes from the code, so a new one needs a restart.
    expect(await getCollectionSettingsOkLabel(page)).toBe("Restart");

    const reopened = await restartFromCollectionSettings(bloomApp);

    expect(readSavedSubscriptionCode(bloomApp.collectionDir)).toBe(
        kEnterpriseSubscriptionCode,
    );
    expect(
        (await getFeatureStatus(reopened, "Spreadsheet")).enabled,
        "the reopened collection should have the code's tier",
    ).toBe(true);
    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, "Subscription");
    await expect
        .poll(() => getSubscriptionPage(reopened))
        .toEqual({
            code: kEnterpriseSubscriptionCode,
            tier: "Enterprise",
            communityInvitation: false,
        });
    await cancelCollectionSettings(reopened);
});
