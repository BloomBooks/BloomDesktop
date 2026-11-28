// Journey test: a person types an expired enterprise subscription code on the Subscription page
// of the Collection Settings dialog. Bloom says at once that the code has expired, and on what date
// the subscription expired, with a request to email for a renewal. Once Bloom restarts on that
// code, the Collections tab says that the subscription expired, naming it, and the collection gets
// nothing from the code.

import { expect, test } from "../fixtures/bloomTest";
import {
    getCollectionSettingsDialog,
    getCollectionSettingsOkLabel,
    getFeatureStatus,
    getSubscriptionCodeMessages,
    getSubscriptionPage,
    openCollectionSettings,
    readSavedSubscriptionCode,
    readSubscriptionExpiryNotices,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
    typeSubscriptionCode,
} from "../helpers/collectionSettings";
import { setUiStateViaApi } from "../helpers/uiLanguage";

// The manual case's code: the Juarez-Guatemala subscription, whose code says it expired on
// 2019-08-09. Bloom has that subscription's branding, so the code is not reported as unknown.
const kExpiredCode = "Juarez-Guatemala-003686-8686";
const kSubscriptionName = "Juarez-Guatemala";
const kRenewalLink = "mailto:subscriptions@bloomlibrary.org";

test.use({
    collectionSpec: { name: "expired-subscription-code", languages: ["en"] },
});

test("an expired subscription code says it has expired, in Settings and on the Collections tab [Test Case ID 123]", async ({
    bloomApp,
    step,
}) => {
    // The messages are checked in English, and a fresh Bloom's UI language follows the operating
    // system's. It is a user setting, so it holds across the restart below.
    await setUiStateViaApi(bloomApp, "en", false);

    // Bloom shows the expiry date in the browser's own date format.
    const expiryDate = await bloomApp.page.evaluate(() =>
        new Date(2019, 7, 9).toLocaleDateString(),
    );

    await step(
        "Set the subscription to Juarez-Guatemala-003686-8686; Settings says the code has expired",
        async () => {
            const page = bloomApp.page;
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Subscription");
            expect(
                await getSubscriptionCodeMessages(page),
                "Sanity check: before the code is typed, the page should say nothing about a code",
            ).toEqual([]);

            await typeSubscriptionCode(page, kExpiredCode);

            await expect
                .poll(() => getSubscriptionCodeMessages(page), {
                    message:
                        "the Subscription page should say the code has expired",
                })
                .toEqual(["That code has expired."]);
            await expect
                .poll(
                    () =>
                        readSubscriptionExpiryNotices(
                            getCollectionSettingsDialog(page),
                        ),
                    {
                        message:
                            "the Subscription page should say when the subscription expired and how to renew it",
                    },
                )
                .toEqual([
                    {
                        kind: "expired",
                        text: `Your subscription expired on ${expiryDate}. Please email subscriptions@bloomlibrary.org to renew.`,
                        links: [kRenewalLink],
                    },
                ]);
            expect(
                (await getSubscriptionPage(page)).tier,
                "an expired code should give no tier",
            ).toBeUndefined();
            expect(
                await getCollectionSettingsOkLabel(page),
                "OK should say Restart, since the subscription code changed",
            ).toBe("Restart");
        },
    );

    await step(
        "Restart; the Collections tab says the subscription expired",
        async () => {
            const page = await restartFromCollectionSettings(bloomApp);
            expect(
                readSavedSubscriptionCode(bloomApp.collectionDir),
                "the collection should keep the code that was typed",
            ).toBe(kExpiredCode);
            expect(
                (await getFeatureStatus(page, "Spreadsheet")).enabled,
                "the expired code should leave the collection without a subscription's features",
            ).toBe(false);
            await expect
                .poll(
                    () => readSubscriptionExpiryNotices(page.locator("body")),
                    {
                        message:
                            "the Collections tab should say the subscription expired, naming it, and how to renew it",
                        timeout: 30000,
                    },
                )
                .toEqual([
                    {
                        kind: "expired",
                        text: `Your ${kSubscriptionName} subscription expired on ${expiryDate}. Please email subscriptions@bloomlibrary.org to renew.`,
                        links: [kRenewalLink],
                    },
                ]);
        },
    );
});
