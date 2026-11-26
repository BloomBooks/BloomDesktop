// The two spreadsheet commands in the More submenu of a book's menu on the Collections tab. Below
// the Local Community tier both are offered but marked as needing a subscription (they look
// disabled, and a click opens the Subscription page instead); from Local Community up both are
// offered as ordinary commands. The test walks the tiers up from Basic: Pro, the tier just below
// the boundary, and Local Community, the tier at it, come from the e2e/setBranding hook (a test
// may not use a real code for them); Enterprise comes from a real code, as a person would enter it.
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
    setBranding,
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

type SpreadsheetCommands = Record<
    string,
    Omit<IBookMenuEntry, "label"> | undefined
>;

/** What the selected book's More submenu says of each spreadsheet command. */
async function readSpreadsheetCommands(
    page: Page,
): Promise<SpreadsheetCommands> {
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

/** Both commands shown and clickable; `gated` says whether each is marked as needing a subscription. */
function bothCommandsShown(gated: boolean): SpreadsheetCommands {
    return {
        [kExportToSpreadsheet]: {
            testId: kExportToSpreadsheet,
            enabled: true,
            needsSubscription: gated,
        },
        [kImportFromSpreadsheet]: {
            testId: kImportFromSpreadsheet,
            enabled: true,
            needsSubscription: gated,
        },
    };
}

/**
 * Check what Bloom says of the spreadsheet feature, then what the book menu shows. The feature
 * status is checked first so that a menu read too early (with the previous tier's answer) cannot
 * pass by accident: the menu asks for the status afresh each time it opens.
 */
async function expectSpreadsheetCommands(
    page: Page,
    tierName: string,
    available: boolean,
): Promise<void> {
    await expect
        .poll(
            async () => (await getFeatureStatus(page, "Spreadsheet")).enabled,
            {
                message: `Sanity check: at ${tierName}, Bloom should say the spreadsheet feature is ${available ? "available" : "unavailable"}`,
            },
        )
        .toBe(available);
    expect(
        await readSpreadsheetCommands(page),
        available
            ? `at ${tierName}, both commands should show as ordinary commands`
            : `at ${tierName}, both commands should show, greyed and sending a click to the Subscription page`,
    ).toEqual(bothCommandsShown(!available));
}

test("the spreadsheet commands are offered, but gated, below the Local Community tier [Test Case ID 624]", async ({
    page,
    bloomApp,
    step,
}) => {
    await step(
        "Make a book in a collection without a subscription",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await switchTab(page, "collection");
        },
    );

    await step(
        "Basic: both commands show, marked as needing a subscription",
        async () => {
            await expectSpreadsheetCommands(page, "Basic", false);
        },
    );

    await step("Pro: both commands still need a subscription", async () => {
        await setBranding(page, "Sample-Pro");
        await expectSpreadsheetCommands(page, "Pro", false);
    });

    await step(
        "Local Community: both commands are ordinary commands",
        async () => {
            await setBranding(page, "Local-Community");
            await expectSpreadsheetCommands(page, "Local Community", true);
        },
    );

    let enterprisePage = page;
    await step(
        "Enterprise, from a real code: both commands are ordinary commands",
        async () => {
            enterprisePage = await restartWithCollectionSettings(bloomApp, {
                languages: kLanguages,
                subscriptionCode: kEnterpriseSubscriptionCode,
            });
            await switchTab(enterprisePage, "collection");
            await expectSpreadsheetCommands(enterprisePage, "Enterprise", true);
        },
    );
});
