// Journey tests for the Bloom Library page of the new (React) Collection Settings dialog: a person
// in an enterprise collection chooses the bookshelf its books are uploaded to and presses Restart,
// and Bloom saves it and reopens the collection; in a collection without a subscription the list
// offers nothing to choose.
//
// The list of bookshelves comes from Contentful over the internet, as it does for a person, so these
// tests need a connection. The Test subscription (kEnterpriseSubscriptionCode) owns kTestBookshelves
// there.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    chooseCollectionSettingsListChoice,
    getCollectionSettingsList,
    getCollectionSettingsListChoices,
    getCollectionSettingsOkLabel,
    getCollectionSettingsSubscriptionBadge,
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
    openCollectionSettings,
    readSavedBookshelf,
    restartFromCollectionSettings,
    restartWithCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";

const kLanguages = ["en"];

test.use({
    collectionSpec: {
        name: "bloom-library-settings",
        languages: kLanguages,
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test("choosing a bookshelf on the Bloom Library page saves it and reopens the collection [Test Case ID 845]", async ({
    page,
    bloomApp,
}) => {
    expect(readSavedBookshelf(bloomApp.collectionDir)).toBeUndefined();

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Bloom Library");
    // Polled: the list stays disabled until the bookshelves have come from Contentful.
    await expect
        .poll(() => getCollectionSettingsList(page, "Bookshelf"), {
            message: "the Bookshelf list should show None and be enabled",
            timeout: 30000,
        })
        .toEqual({ shown: "None", enabled: true });
    expect(await getCollectionSettingsListChoices(page, "Bookshelf")).toEqual([
        "None",
        ...kTestBookshelves,
    ]);
    await expect
        .poll(() => getCollectionSettingsSubscriptionBadge(page))
        .toBe("Available with your Bloom Subscription");
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");

    await chooseCollectionSettingsListChoice(
        page,
        "Bookshelf",
        kTestBookshelves[0],
    );
    // Open books carry the bookshelf, so a change needs a restart.
    await expect
        .poll(() => getCollectionSettingsOkLabel(page), {
            message: "OK should become Restart once the bookshelf changes",
        })
        .toBe("Restart");

    const reopened = await restartFromCollectionSettings(bloomApp);

    expect(readSavedBookshelf(bloomApp.collectionDir)).toBe(
        kTestBookshelves[0],
    );
    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, "Bloom Library");
    await expect
        .poll(() => getCollectionSettingsList(reopened, "Bookshelf"), {
            timeout: 30000,
        })
        .toEqual({ shown: kTestBookshelves[0], enabled: true });
    await cancelCollectionSettings(reopened);
});

test("without a subscription the Bloom Library page offers no bookshelf to choose [Test Case ID 845]", async ({
    bloomApp,
}) => {
    // The same collection with its subscription code taken away, so it is Basic.
    const page = await restartWithCollectionSettings(bloomApp, {
        languages: kLanguages,
    });

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Bloom Library");
    expect(await getCollectionSettingsSubscriptionBadge(page)).toBe(
        "Feature Requires Higher Subscription Tier",
    );
    expect(await getCollectionSettingsList(page, "Bookshelf")).toEqual({
        shown: "None",
        enabled: false,
    });
    await cancelCollectionSettings(page);
});
