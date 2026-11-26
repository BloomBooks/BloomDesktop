// Making the open collection a Team Collection, and checking its books out and in, the way a
// person does: through the Team Collection page of Collection Settings and the status panel on the
// Collections tab.

import { expect } from "@playwright/test";
import type { Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import {
    clickAndFollowRestart,
    openCollectionSettings,
    showCollectionSettingsPage,
} from "./collectionSettings";
import type { IBloomApp } from "../fixtures/bloomTest";
import { realClick } from "./realClick";
import { armFileChooser } from "./talkingBook";

/**
 * Give Bloom a registered user with an email. Creating a Team Collection and checking a book out
 * both need one, and the registration dialog that otherwise asks for it is not what a test of
 * either is about.
 */
export async function registerTestUser(page: Page): Promise<void> {
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
}

/** The Create a Team Collection button on the Team Collection page of Collection Settings. */
export function createTeamCollectionButton(page: Page) {
    return page.getByRole("button", {
        name: "Create a Team Collection",
        exact: true,
    });
}

/**
 * The Create a Team Collection dialog, found by its folder label: its checklist mentions
 * Collection Settings, so the settings dialog's helpers would match it too while it is open.
 */
export function createTeamCollectionDialog(page: Page) {
    return page
        .getByRole("dialog")
        .filter({ hasText: "LAN or Dropbox Folder:" });
}

/**
 * Make the open collection a Team Collection whose shared folder goes in `sharedFolder`, as a
 * person does: Collection Settings > Team Collection > Create a Team Collection, choose the folder,
 * tick the four checklist boxes, Create & Restart. Bloom copies every book of the collection into
 * the shared folder, checked in, then reopens the collection. Returns the new shell page once the
 * collection is ready again. Registers a user first, since Bloom will not create one without.
 *
 * The collection must be on a tier above Basic, with the "team-collections" experimental feature on.
 */
export async function createTeamCollection(
    bloomApp: IBloomApp,
    sharedFolder: string,
): Promise<Page> {
    const page = bloomApp.page;
    await registerTestUser(page);
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Team Collection");
    await realClick(createTeamCollectionButton(page));
    const dialog = createTeamCollectionDialog(page);
    await expect(dialog).toBeVisible();

    await armFileChooser(page, sharedFolder);
    await realClick(dialog.getByRole("button", { name: /Choose Folder/i }));
    await expect(dialog.getByText(sharedFolder, { exact: true })).toBeVisible();
    const checkboxes = dialog.getByRole("checkbox");
    await expect(checkboxes).toHaveCount(4);
    for (const checkbox of await checkboxes.all()) await realClick(checkbox);
    const createAndRestart = dialog.getByRole("button", {
        name: /Create & Restart/i,
    });
    await expect(createAndRestart).toBeEnabled();

    return clickAndFollowRestart(bloomApp, createAndRestart);
}

/** Whether Bloom says the selected book may be changed here (in a Team Collection: checked out to me). */
export async function isSelectedBookSaveable(page: Page): Promise<boolean> {
    const info = await apiGetJson<{ saveable: boolean }>(
        page,
        "app/selectedBookInfo",
    );
    return info.saveable;
}

/**
 * Check the selected book out with the Check out book button of the Team Collection status panel
 * on the Collections tab, and wait until Bloom says the book may be changed.
 */
export async function checkOutSelectedBook(page: Page): Promise<void> {
    expect(
        await isSelectedBookSaveable(page),
        "Sanity check: the book to check out should not already be changeable",
    ).toBe(false);
    await realClick(page.locator("button.checkout-button"));
    await expect
        .poll(() => isSelectedBookSaveable(page), {
            timeout: 30000,
            message: "Bloom never said the book was checked out to me",
        })
        .toBe(true);
    await expect(
        page.locator("button.checkin-button"),
        "the status panel should offer to check the book in",
    ).toBeVisible();
}

/**
 * Check the selected book in with the Check in book button of the Team Collection status panel on
 * the Collections tab, and wait until Bloom says the book may no longer be changed.
 */
export async function checkInSelectedBook(page: Page): Promise<void> {
    expect(
        await isSelectedBookSaveable(page),
        "Sanity check: the book to check in should be checked out to me",
    ).toBe(true);
    await realClick(page.locator("button.checkin-button"));
    await expect
        .poll(() => isSelectedBookSaveable(page), {
            timeout: 60000,
            message: "Bloom never said the book was checked in",
        })
        .toBe(false);
    await expect(
        page.locator("button.checkout-button"),
        "the status panel should offer to check the book out",
    ).toBeVisible();
}
