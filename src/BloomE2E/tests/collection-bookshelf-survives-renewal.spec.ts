// Journey test for BL-15056: an enterprise collection's Bloom Library bookshelf survives its
// subscription lapsing and being renewed. A person chooses a bookshelf, the collection's code is
// later found expired (the test puts an expired code for the same subscription into the
// .bloomCollection, as the manual case does with Notepad), and the person types the current code on
// the Subscription page and lets Bloom restart. The bookshelf must still be chosen.
//
// While the code is expired Bloom shows the bookshelf as None (the case allows that) but must keep
// it in the .bloomCollection, so that renewing brings it back.
//
// The list of bookshelves comes from Contentful over the internet, as it does for a person, so this
// test needs a connection. The Test subscription (kEnterpriseSubscriptionCode) owns
// kTestBookshelves there.

import * as fs from "fs";
import * as Path from "path";
import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    chooseCollectionSettingsListChoice,
    getCollectionSettingsList,
    getCollectionSettingsOkLabel,
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
    openCollectionSettings,
    readSavedBookshelf,
    readSavedSubscriptionCode,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
    typeSubscriptionCode,
} from "../helpers/collectionSettings";

// An earlier code of the same Test subscription as kEnterpriseSubscriptionCode (same descriptor,
// valid checksum). Its date is in the year 3000, which Bloom treats as a retired never-ending code
// that expired on Subscription.kExpiryDateForDeprecatedCodes (2025-07-01), so Bloom reads it as
// expired. It is the expired code the developers used on BL-15056; it unlocks nothing.
const kExpiredTestSubscriptionCode = "Test-361769-1088";

// Not the first bookshelf, so a fallback to the first choice cannot pass for the remembered one.
const kBookshelf = kTestBookshelves[1];

test.use({
    collectionSpec: {
        name: "bookshelf-renewal",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test("a collection's bookshelf survives its subscription expiring and being renewed [Test Case ID 713]", async ({
    page,
    bloomApp,
    step,
}) => {
    await step(
        "In a collection with a valid subscription, select a bookshelf",
        async () => {
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Bloom Library");
            // Polled: the list stays disabled until the bookshelves have come from Contentful.
            await expect
                .poll(() => getCollectionSettingsList(page, "Bookshelf"), {
                    message:
                        "the Bookshelf list should show None and be enabled",
                    timeout: 30000,
                })
                .toEqual({ shown: "None", enabled: true });
            await chooseCollectionSettingsListChoice(
                page,
                "Bookshelf",
                kBookshelf,
            );
            await expect
                .poll(() => getCollectionSettingsOkLabel(page))
                .toBe("Restart");
            await restartFromCollectionSettings(bloomApp);
            expect(readSavedBookshelf(bloomApp.collectionDir)).toBe(kBookshelf);
        },
    );

    await step(
        "Close Bloom, put an expired code for the same subscription in the .bloomCollection, and reopen Bloom",
        async () => {
            await bloomApp.restart(() =>
                replaceSavedSubscriptionCode(
                    bloomApp.collectionDir,
                    kExpiredTestSubscriptionCode,
                ),
            );
            expect(
                (await getFeatureStatus(bloomApp.page, "Spreadsheet")).enabled,
                "Sanity check: the expired code should leave the collection without its subscription",
            ).toBe(false);
        },
    );

    await step("Renew with a valid code; Bloom restarts", async () => {
        const page = bloomApp.page;
        await openCollectionSettings(page);
        await showCollectionSettingsPage(page, "Subscription");
        await typeSubscriptionCode(page, kEnterpriseSubscriptionCode);
        await expect
            .poll(() => getCollectionSettingsOkLabel(page), {
                message: "OK should become Restart once the code changes",
            })
            .toBe("Restart");
        await restartFromCollectionSettings(bloomApp);
        expect(readSavedSubscriptionCode(bloomApp.collectionDir)).toBe(
            kEnterpriseSubscriptionCode,
        );
    });

    await step(
        "Check the bookshelf is still selected in Settings",
        async () => {
            const page = bloomApp.page;
            expect(
                readSavedBookshelf(bloomApp.collectionDir),
                "the .bloomCollection should name the bookshelf chosen before the code expired",
            ).toBe(kBookshelf);
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Bloom Library");
            await expect
                .poll(() => getCollectionSettingsList(page, "Bookshelf"), {
                    message:
                        "the Bookshelf list should show the bookshelf chosen before the code expired",
                    timeout: 30000,
                })
                .toEqual({ shown: kBookshelf, enabled: true });
            await cancelCollectionSettings(page);
        },
    );
});

/**
 * Change only the <SubscriptionCode> of the collection's .bloomCollection, the way a person edits
 * it in Notepad, leaving every other setting (the bookshelf above all) as Bloom saved it.
 */
function replaceSavedSubscriptionCode(collectionDir: string, code: string) {
    const settingsFiles = fs
        .readdirSync(collectionDir)
        .filter((name) => name.endsWith(".bloomCollection"));
    expect(settingsFiles, "the collection's settings files").toHaveLength(1);
    const settingsPath = Path.join(collectionDir, settingsFiles[0]);
    const xml = fs.readFileSync(settingsPath, "utf8");
    const pattern = /<SubscriptionCode>[^<]*<\/SubscriptionCode>/;
    expect(xml, "the .bloomCollection should hold a subscription code").toMatch(
        pattern,
    );
    fs.writeFileSync(
        settingsPath,
        xml.replace(pattern, `<SubscriptionCode>${code}</SubscriptionCode>`),
        "utf8",
    );
}
