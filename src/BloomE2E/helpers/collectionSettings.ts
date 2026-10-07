// Change the settings of the collection Bloom has open: its languages, its front/back matter
// pack, its branding.
//
// A person changes these in the collection Settings dialog. That dialog is a WinForms surface CDP
// cannot reach, and the settings have no API while it is closed (see AUTOMATION-DEBT.md, "WinForms
// surfaces are invisible to CDP"), so every helper here takes the route that IS open to a test:
// rewrite the .bloomCollection while Bloom is stopped, or use an E2eTestingApi hook. None of this
// is the behavior any test measures; a test that wanted to measure the Settings dialog itself could
// not be written today.
//
// The exception is the NEW, React Collection Settings dialog (the "Settings" button beside "Old
// Settings" while the two coexist), which lives in the shell document and so is reachable. The
// helpers at the bottom of this file drive it; only the pages that have real content so far can
// change anything.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Locator, type Page } from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";
import { makeCollectionXml } from "../fixtures/launchBloom";
import { apiGetJson, apiPost } from "./api";
import { waitForCollectionReady } from "./collection";
import { realClick } from "./realClick";

/** The collection settings a test can rewrite. Everything else keeps Bloom's defaults. */
export interface ICollectionSettings {
    /** Language tags for Language1, Language2 and Language3, in that order. */
    languages: string[];
    /**
     * The front/back matter pack's key, the part of its folder name before "-XMatter", e.g.
     * "Traditional". Left out, the collection gets the pack makeCollectionXml gives by default.
     */
    xmatterPack?: string;
    /**
     * A subscription code, which decides the collection's tier; see kEnterpriseSubscriptionCode.
     * Left out, the collection has no code and so is Basic.
     */
    subscriptionCode?: string;
    /** The Bloom Library bookshelf, by url key; see ICollectionSpec.bookshelf. Left out, none. */
    bookshelf?: string;
}

/**
 * Give the collection these settings and start Bloom again on it, the way a person does by
 * changing them in the Settings dialog and letting Bloom restart. Returns the new shell page; the
 * old one is closed.
 *
 * The .bloomCollection is REPLACED, not edited, so every other setting goes back to what
 * makeCollectionXml writes. Use this on a collection the test itself created (collectionSpec),
 * not on a prepared collection from testing-inputs, whose other settings would be lost. For the
 * same reason, pass every setting again, subscriptionCode and xmatterPack included: a setting left
 * out goes back to its default, so a collection launched on a tier would drop back to Basic.
 *
 * Bloom is killed rather than asked to quit, so leave the page being edited before calling this,
 * or what was typed on it is lost (see goToPage). Each call costs about six seconds.
 */
export async function restartWithCollectionSettings(
    bloomApp: IBloomApp,
    settings: ICollectionSettings,
): Promise<Page> {
    // Read the file name rather than assuming it matches the folder name, so a collection whose
    // two names differ is rewritten instead of gaining a second .bloomCollection.
    const settingsFiles = fs
        .readdirSync(bloomApp.collectionDir)
        .filter((name) => name.endsWith(".bloomCollection"));
    if (settingsFiles.length !== 1)
        throw new Error(
            `Expected one .bloomCollection in ${bloomApp.collectionDir}, found ` +
                `${settingsFiles.length}: ${settingsFiles.join(", ")}.`,
        );
    const settingsPath = Path.join(bloomApp.collectionDir, settingsFiles[0]);
    return bloomApp.restart(() =>
        fs.writeFileSync(
            settingsPath,
            makeCollectionXml(
                settings.languages,
                settings.xmatterPack,
                settings,
            ),
            "utf8",
        ),
    );
}

/**
 * Put the collection under this branding, e.g. "Story-Producer-App", and bring the selected book up
 * to date with it. This stands for entering a subscription code in the Settings dialog, which a
 * test cannot do: the dialog is WinForms, and a real code carries a checksum. The e2e/setBranding
 * hook exists for exactly this.
 *
 * The change lives in memory only. A restart puts the collection back under the branding its
 * .bloomCollection names.
 */
export async function setBranding(page: Page, branding: string): Promise<void> {
    await apiPost(page, "e2e/setBranding", branding, "text/plain");
}

/** Bloom's subscription tiers, lowest first. A tier includes every lower tier's features. */
export type SubscriptionTier =
    | "Basic"
    | "Pro"
    | "LocalCommunity"
    | "Enterprise";

/**
 * A subscription code that puts a collection on the Enterprise tier, which includes every lower
 * tier. Give it to a test's collectionSpec (or to restartWithCollectionSettings) when the test's
 * subject is behind a subscription tier.
 *
 * It has to be a real code rather than an API hook that sets the tier: Bloom reads the tier out of
 * the code as it opens the collection, and several parts of Bloom then keep the Subscription object
 * they were handed at startup, so a tier changed later is invisible to them. FeatureStatusApi is
 * one, which means the Canvas tool's palette goes on hiding a tier-gated item however the tier is
 * changed after launch.
 *
 * This is the same code Bloom's own unit tests use (SubscriptionTests.cs), so it adds no new code
 * to the repository. Its descriptor, "Test", is the branding folder src/content/branding/Test,
 * which stamps a butterfly on the corner of every page, so a book made with it is visibly a test
 * book. That changes what a page looks like, so a spec that compares screenshots should stay on
 * the Default branding. It expires around the year 3900. Do NOT mint another code for tests; if
 * this one stops serving, ask.
 */
export const kEnterpriseSubscriptionCode = "Test-727011-1339";

/**
 * The bookshelves the Test subscription (kEnterpriseSubscriptionCode) owns on Bloom Library, by
 * url key, which Contentful knows about. A collection under that code can name either as the
 * bookshelf its books are uploaded to, as the Settings dialog offers a person.
 */
export const kTestBookshelves = ["test-bookshelf-1", "test-bookshelf-2"];

/**
 * What Bloom says about one feature, as features/status reports it. It answers with more than
 * this, all of it about how to word the message offering an upgrade; these are the fields that say
 * whether the feature works.
 */
export interface IFeatureStatus {
    /**
     * The tier the feature REQUIRES, not the tier the collection has. So this says "Pro" for a
     * Pro feature whatever the collection's own subscription is; `enabled` is what tells you
     * whether the collection reaches it.
     */
    subscriptionTier: SubscriptionTier;
    /** True when the collection's tier reaches the feature's. */
    enabled: boolean;
    /**
     * True when Bloom should show the feature's controls at all. A feature that is also an
     * experiment is visible only while the experiment is on.
     */
    visible: boolean;
}

/**
 * Ask Bloom whether a feature is available here, e.g. getFeatureStatus(page, "canvas").
 *
 * This is the same answer the front end asks for before it decides whether to show a feature's
 * controls, so it is the right sanity check for a test whose subject is behind a subscription tier
 * or an experiment: when the control is missing, this says whether the tier or the experiment is
 * the reason.
 */
export async function getFeatureStatus(
    page: Page,
    featureName: string,
): Promise<IFeatureStatus> {
    return apiGetJson<IFeatureStatus>(
        page,
        `features/status?featureName=${encodeURIComponent(featureName)}&forPublishing=false`,
    );
}

/**
 * What is on disk at a collection folder: its name and the settings files (.bloomCollection) in
 * it, or undefined when there is no such folder. A rename should leave one settings file, named
 * after the folder.
 */
export function describeCollectionFolder(
    collectionDir: string,
): { name: string; settingsFiles: string[] } | undefined {
    if (!fs.existsSync(collectionDir)) return undefined;
    return {
        name: Path.basename(collectionDir),
        settingsFiles: fs
            .readdirSync(collectionDir)
            .filter((file) => file.endsWith(".bloomCollection")),
    };
}

/** The new (React) Collection Settings dialog, by the title it shows. */
function collectionSettingsDialog(page: Page): Locator {
    // Not getByRole's name option: the dialog's aria-labelledby="title" does not give it the
    // accessible name "Collection Settings".
    return page.getByRole("dialog").filter({ hasText: "Collection Settings" });
}

/**
 * Open the new Collection Settings dialog the way a person does: click "Settings" on the
 * Collections tab's top bar (not "Old Settings", the WinForms dialog CDP cannot reach). Returns
 * once the dialog shows its pages.
 */
export async function openCollectionSettings(page: Page): Promise<void> {
    await realClick(
        page.getByRole("button", { name: "Settings", exact: true }),
    );
    await expect(
        collectionSettingsDialog(page).getByRole("tab").first(),
        "the Collection Settings dialog never showed its pages",
    ).toBeVisible({ timeout: 30000 });
}

/**
 * Show one page of the open Collection Settings dialog by clicking its name in the list on the
 * left, e.g. "Experimental". Fails naming the pages the dialog offers when there is no such page.
 */
export async function showCollectionSettingsPage(
    page: Page,
    pageName: string,
): Promise<void> {
    const dialog = collectionSettingsDialog(page);
    const tab = dialog.getByRole("tab", { name: pageName, exact: true });
    if ((await tab.count()) !== 1) {
        const offered = await dialog.getByRole("tab").allInnerTexts();
        throw new Error(
            `The Collection Settings dialog has no page "${pageName}"; it offers: ${offered.join(", ")}.`,
        );
    }
    await realClick(tab);
    await expect(tab).toHaveAttribute("aria-selected", "true");
}

/** The checkbox of the setting with this label on the showing page of the dialog. */
function collectionSettingsCheckbox(page: Page, label: string): Locator {
    // Config-R puts the setting's label on the element that wraps its checkbox.
    return collectionSettingsDialog(page).locator(
        `span[label="${label}"] input[type="checkbox"]`,
    );
}

/** What a checkbox setting in the Collection Settings dialog shows. */
export interface ICollectionSettingsCheckboxState {
    checked: boolean;
    /** False when the user cannot change it, e.g. because the subscription tier lacks it. */
    enabled: boolean;
}

/**
 * Read the checkbox setting with this label, e.g. "Team Collections", on the page of the
 * Collection Settings dialog that is showing.
 */
export async function getCollectionSettingsCheckbox(
    page: Page,
    label: string,
): Promise<ICollectionSettingsCheckboxState> {
    const checkbox = collectionSettingsCheckbox(page, label);
    await expect(
        checkbox,
        `the showing Collection Settings page has no checkbox labelled "${label}"`,
    ).toHaveCount(1);
    return {
        checked: await checkbox.isChecked(),
        enabled: await checkbox.isEnabled(),
    };
}

/**
 * Tick or untick the checkbox setting with this label on the showing page of the Collection
 * Settings dialog, by clicking it if it isn't that way already, and return once it shows that
 * state. Nothing is saved until OK.
 */
export async function setCollectionSettingsCheckbox(
    page: Page,
    label: string,
    checked: boolean,
): Promise<void> {
    const checkbox = collectionSettingsCheckbox(page, label);
    if ((await checkbox.isChecked()) !== checked) await realClick(checkbox);
    await expect(checkbox).toBeChecked({ checked });
}

/** The text box of the setting with this label on the showing page of the dialog. */
function collectionSettingsTextBox(page: Page, label: string): Locator {
    // Config-R names the box after the setting's path, not its label; the label is a heading
    // in the same row.
    return collectionSettingsDialog(page)
        .locator("li")
        .filter({ has: page.locator("h4").getByText(label, { exact: true }) })
        .locator('input[type="text"]');
}

/** What a text setting in the Collection Settings dialog shows. */
export interface ICollectionSettingsTextState {
    value: string;
    /** False when the user cannot change it, e.g. the name of a Team Collection. */
    enabled: boolean;
}

/**
 * Read the text setting with this label, e.g. "Collection Name", on the page of the Collection
 * Settings dialog that is showing.
 */
export async function getCollectionSettingsText(
    page: Page,
    label: string,
): Promise<ICollectionSettingsTextState> {
    const box = collectionSettingsTextBox(page, label);
    await expect(
        box,
        `the showing Collection Settings page has no text box labelled "${label}"`,
    ).toHaveCount(1);
    return {
        value: await box.inputValue(),
        enabled: await box.isEnabled(),
    };
}

/**
 * Replace what the text setting with this label says, as a person does by typing over it, and
 * return once the box shows the new text. Nothing is saved until OK.
 */
export async function setCollectionSettingsText(
    page: Page,
    label: string,
    text: string,
): Promise<void> {
    const box = collectionSettingsTextBox(page, label);
    await box.fill(text);
    await expect(box).toHaveValue(text);
}

/**
 * Click OK when it says "OK" (so no restart is coming): Bloom saves the settings and the dialog
 * closes. Returns once it has closed. For a change that needs a restart, use
 * restartFromCollectionSettings.
 */
export async function saveCollectionSettings(page: Page): Promise<void> {
    const dialog = collectionSettingsDialog(page);
    const ok = dialog.getByRole("button", { name: /^OK$/i });
    await expect(
        ok,
        "the dialog has no OK button (it says Restart if a change needs one)",
    ).toBeEnabled();
    await realClick(ok);
    await expect(dialog, "the dialog did not close after OK").toBeHidden();
}

/** Click Cancel: the dialog closes and nothing is saved. Returns once it has closed. */
export async function cancelCollectionSettings(page: Page): Promise<void> {
    const dialog = collectionSettingsDialog(page);
    await realClick(dialog.getByRole("button", { name: /^Cancel$/i }));
    await expect(dialog, "the dialog did not close after Cancel").toBeHidden();
}

/**
 * The label on the dialog's OK button: "OK", or "Restart" when a change the user has made needs
 * Bloom to reopen the collection.
 */
export async function getCollectionSettingsOkLabel(
    page: Page,
): Promise<string> {
    // textContent, not innerText: the button's CSS shows its text in capitals.
    return (
        (await collectionSettingsDialog(page)
            .getByRole("button", { name: /^(OK|Restart)$/i })
            .textContent()) ?? ""
    ).trim();
}

/**
 * Click OK when it says "Restart": Bloom saves the settings and reopens the whole collection,
 * which destroys the shell page. Waits out the reopen, re-finds the shell page (bloomApp.page from
 * here on) and returns it once the collection is ready again.
 *
 * When the change was a new collection name, pass the folder name Bloom will give the collection
 * as `renamedTo`: Bloom renames the folder by starting a new copy of itself on it, and this then
 * follows that copy (bloomApp.collectionDir becomes the renamed folder).
 */
export async function restartFromCollectionSettings(
    bloomApp: IBloomApp,
    renamedTo?: string,
): Promise<Page> {
    const page = bloomApp.page;
    const restart = collectionSettingsDialog(page).getByRole("button", {
        name: "Restart",
        exact: true,
    });
    await expect(
        restart,
        "OK does not say Restart, so pressing it would not reopen the collection",
    ).toBeVisible();
    const pageClosed = page.waitForEvent("close", { timeout: 60000 });
    // Pre-handled so a click failing for another reason cannot surface later as an unhandled
    // rejection charged to some other test.
    pageClosed.catch(() => undefined);
    // Bloom can tear the page down before Playwright finishes its click; a "closed" error means
    // the click landed. (If it never landed, the pageClosed wait times out.)
    await realClick(restart).catch((error) => {
        if (!/closed/i.test(String(error))) throw error;
    });
    await pageClosed;
    const newPage = renamedTo
        ? await bloomApp.followRelaunch(
              Path.join(Path.dirname(bloomApp.collectionDir), renamedTo),
          )
        : await bloomApp.reattachToShell();
    await waitForCollectionReady(newPage);
    return newPage;
}
