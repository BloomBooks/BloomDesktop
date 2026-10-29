// Journey tests for the Team Collection page of the new (React) Collection Settings dialog: while a
// new collection name is unsaved, the page says to save it first and Create is disabled; without
// one, Create opens the Create a Team Collection dialog, and cancelling it leaves the collection
// as it was; and Create & Restart makes the collection a Team Collection in the chosen folder.
//
// The create runs last: afterwards the collection is a Team Collection.

import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import { apiPost } from "../helpers/api";
import { waitForCollectionReady } from "../helpers/collection";
import {
    cancelCollectionSettings,
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { realClick } from "../helpers/realClick";
import { armFileChooser } from "../helpers/talkingBook";

// The page shows only while Team Collections is turned on, and needs a tier above Basic.
test.use({
    collectionSpec: {
        name: "team-collection-settings",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["team-collections"],
});

function createButton(page: Page) {
    return page.getByRole("button", {
        name: "Create a Team Collection",
        exact: true,
    });
}

// Found by its folder label: its checklist mentions Collection Settings, so the settings dialog's
// helpers would match it too while it is open.
function createDialog(page: Page) {
    return page
        .getByRole("dialog")
        .filter({ hasText: "LAN or Dropbox Folder:" });
}

function teamCollectionLinkFile(collectionDir: string): string {
    return Path.join(collectionDir, "TeamCollectionLink.txt");
}

test("the Team Collection page holds Create back while a rename is unsaved, and Cancel in the Create dialog makes nothing [Test Case ID 853]", async ({
    page,
    bloomApp,
}) => {
    const linkFile = teamCollectionLinkFile(bloomApp.collectionDir);
    expect(
        fs.existsSync(linkFile),
        "the collection starts out not a Team Collection",
    ).toBe(false);
    const saveRenameFirst = page.getByText(
        "You have changed the collection's name.",
    );

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Advanced");
    await setCollectionSettingsText(page, "Collection Name", "Renamed Books");
    await showCollectionSettingsPage(page, "Team Collection");
    await expect(saveRenameFirst).toBeVisible();
    await expect(createButton(page)).toBeDisabled();
    await cancelCollectionSettings(page);

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Team Collection");
    await expect(saveRenameFirst).toBeHidden();
    await realClick(createButton(page));
    await expect(createDialog(page)).toBeVisible();
    // Nothing is ticked yet.
    await expect(
        createDialog(page).getByRole("button", { name: /Create & Restart/i }),
    ).toBeDisabled();
    await realClick(
        createDialog(page).getByRole("button", { name: /^Cancel$/i }),
    );
    await expect(createDialog(page)).toBeHidden();

    // Back on the settings dialog, which Cancel closes without saving anything.
    await expect(createButton(page)).toBeEnabled();
    await cancelCollectionSettings(page);
    expect(
        fs.existsSync(linkFile),
        "cancelling must not make a Team Collection",
    ).toBe(false);
});

test("Create & Restart makes the collection a Team Collection in the chosen folder [Test Case ID 853]", async ({
    page,
    bloomApp,
}) => {
    const linkFile = teamCollectionLinkFile(bloomApp.collectionDir);
    expect(fs.existsSync(linkFile)).toBe(false);
    const sharedFolder = fs.mkdtempSync(
        Path.join(os.tmpdir(), "bloom-e2e-tc-"),
    );
    try {
        // Creating a Team Collection needs the user's email; the registration dialog that asks
        // for it is not what this test is about.
        await apiPost(
            page,
            "registration/userInfo",
            JSON.stringify({
                firstName: "E2E",
                surname: "Tester",
                email: "e2e.tester@example.com",
                organization: "SIL",
                usingFor: "Testing",
                hadEmailAlready: false,
            }),
            "application/json",
        );

        await openCollectionSettings(page);
        await showCollectionSettingsPage(page, "Team Collection");
        await realClick(createButton(page));
        const dialog = createDialog(page);
        await expect(dialog).toBeVisible();

        await armFileChooser(page, sharedFolder);
        await realClick(dialog.getByRole("button", { name: /Choose Folder/i }));
        await expect(
            dialog.getByText(sharedFolder, { exact: true }),
        ).toBeVisible();
        const checkboxes = dialog.getByRole("checkbox");
        await expect(checkboxes).toHaveCount(4);
        for (const checkbox of await checkboxes.all())
            await realClick(checkbox);
        const createAndRestart = dialog.getByRole("button", {
            name: /Create & Restart/i,
        });
        await expect(createAndRestart).toBeEnabled();

        const pageClosed = page.waitForEvent("close", { timeout: 60000 });
        // Pre-handled so a click failing for another reason cannot surface later as an unhandled
        // rejection charged to some other test.
        pageClosed.catch(() => undefined);
        // Bloom can tear the page down before Playwright finishes its click; a "closed" error
        // means the click landed.
        await realClick(createAndRestart).catch((error) => {
            if (!/closed/i.test(String(error))) throw error;
        });
        await pageClosed;
        await waitForCollectionReady(await bloomApp.reattachToShell());

        expect(
            fs.existsSync(linkFile),
            "the collection should now be linked to a Team Collection",
        ).toBe(true);
        const repoFolder = fs.readFileSync(linkFile, "utf8").trim();
        expect(Path.dirname(repoFolder)).toBe(sharedFolder);
        expect(fs.existsSync(repoFolder)).toBe(true);
    } finally {
        fs.rmSync(sharedFolder, { recursive: true, force: true });
    }
});
