// The two spreadsheet commands in the More submenu of a book's menu on the Collections tab: in a
// collection without the Enterprise tier both are offered but marked as needing a subscription
// (they look disabled, and a click opens the Subscription page instead), and once the collection
// has an Enterprise code both are offered as ordinary commands.
//
// Automates the manual test "Spreadsheet Menu Availability" (Test Case ID 624). The manual case
// also turns on a "Spreadsheet" experimental feature; Bloom no longer has one (the commands are
// gated by the subscription tier alone), so that step has nothing left to drive.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { readSelectedBookMenu } from "../helpers/collection";
import type { IBookMenuEntry } from "../helpers/collection";
import {
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
    restartWithCollectionSettings,
} from "../helpers/collectionSettings";
import { switchTab } from "../helpers/workspace";
import type { Page } from "@playwright/test";

const kLanguages = ["en"];

test.use({
    collectionSpec: { name: "spreadsheet-menu", languages: kLanguages },
});

const kExportToSpreadsheet = "CollectionTab.BookMenu.ExportToSpreadsheet";
const kImportFromSpreadsheet =
    "CollectionTab.BookMenu.ImportContentFromSpreadsheet";

/** What the selected book's More submenu says of each spreadsheet command. */
async function readSpreadsheetCommands(
    page: Page,
): Promise<Record<string, Omit<IBookMenuEntry, "label"> | undefined>> {
    const { more } = await readSelectedBookMenu(page, "right-click");
    const pick = (testId: string) => {
        const entry = more.find((e) => e.testId === testId);
        if (!entry) return undefined;
        const { label: _label, ...rest } = entry;
        return rest;
    };
    return {
        [kExportToSpreadsheet]: pick(kExportToSpreadsheet),
        [kImportFromSpreadsheet]: pick(kImportFromSpreadsheet),
    };
}

test("the spreadsheet commands are offered, but gated, until the collection is Enterprise [Test Case ID 624]", async ({
    page,
    bloomApp,
    step,
}) => {
    await step(
        "Make a book in a collection without a subscription",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await switchTab(page, "collection");
            expect(
                (await getFeatureStatus(page, "Spreadsheet")).enabled,
                "Sanity check: without a subscription, the spreadsheet feature should be unavailable",
            ).toBe(false);
        },
    );

    await step(
        "See both spreadsheet commands in More, marked as needing a subscription",
        async () => {
            expect(
                await readSpreadsheetCommands(page),
                "without Enterprise, both commands should show, greyed and sending a click to the Subscription page",
            ).toEqual({
                [kExportToSpreadsheet]: {
                    testId: kExportToSpreadsheet,
                    enabled: true,
                    needsSubscription: true,
                },
                [kImportFromSpreadsheet]: {
                    testId: kImportFromSpreadsheet,
                    enabled: true,
                    needsSubscription: true,
                },
            });
        },
    );

    let enterprisePage = page;
    await step("Reopen the collection with an Enterprise code", async () => {
        enterprisePage = await restartWithCollectionSettings(bloomApp, {
            languages: kLanguages,
            subscriptionCode: kEnterpriseSubscriptionCode,
        });
        await switchTab(enterprisePage, "collection");
        expect(
            (await getFeatureStatus(enterprisePage, "Spreadsheet")).enabled,
            "Sanity check: with an Enterprise code, the spreadsheet feature should be available",
        ).toBe(true);
    });

    await step(
        "See both spreadsheet commands in More as ordinary commands",
        async () => {
            expect(
                await readSpreadsheetCommands(enterprisePage),
                "with Enterprise, both commands should show, available",
            ).toEqual({
                [kExportToSpreadsheet]: {
                    testId: kExportToSpreadsheet,
                    enabled: true,
                    needsSubscription: false,
                },
                [kImportFromSpreadsheet]: {
                    testId: kImportFromSpreadsheet,
                    enabled: true,
                    needsSubscription: false,
                },
            });
        },
    );
});
