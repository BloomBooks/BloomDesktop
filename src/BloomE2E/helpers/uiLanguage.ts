// Drive and read Bloom's UI-language menu (the language chooser in the top-right corner),
// including the "Show translations which have not been approved yet" item at its bottom.
//
// Two quirks of this surface shape everything here:
//
//  - Choosing a language makes Bloom REOPEN THE WHOLE PROJECT ("many UI surfaces don't fully
//    refresh their localized strings without a full workspace reload" - WorkspaceView.cs). The
//    WebView2 shell page is destroyed and a new one created in the same process, so every
//    language change ends by re-finding the shell page (bloomApp.reattachToShell).
//  - Changing the unapproved-translations setting makes a production Bloom restart itself
//    entirely. In e2e mode Bloom skips that self-restart (it would relaunch without the --e2e
//    and --automation flags; see ToggleShowingOnlyApprovedTranslations), so the helper applies
//    the saved setting with the fixture's own bloomApp.restart().
//
// Both settings live in Bloom's MACHINE-WIDE user profile, not in the collection: even our
// dedicated e2e Bloom writes the same profile the developer's Bloom reads. A test that changes
// them must record what it found and put it back (see setUiStateViaApi), even when it fails.

import { expect, type Locator, type Page } from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";
import { apiGet, apiGetJson, apiPost } from "./api";
import { waitForCollectionReady } from "./collection";
import { waitForActiveTab } from "./workspace";

/**
 * One visible string per mechanism that puts localized text into Bloom's web UI, plus the
 * language-menu button's own label. expectUiStrings asserts all of them, so a test that switches
 * the UI language proves every localization pathway reacted, not just one component.
 */
export interface IUiLanguageStrings {
    /** The factory "Templates" collection heading - localized by the useL10n hook. */
    templates: string;
    /** The "Sources For New Books" heading - a LocalizableElement class component. */
    sources: string;
    /** The Edit workspace tab - a <Span> LocalizableElement inside the top bar. */
    editTab: string;
    /** The "Basic Book" button caption - localized in C# and delivered as API data. */
    basicBook: string;
    /** The UI-language menu button's label - C#-localized data (workspace/uiLanguageLabel). */
    languageMenuButton: string;
}

/** The current UI language tag, e.g. "en" or "es-ES", as Bloom's localization manager has it. */
export async function getUiLanguageTag(page: Page): Promise<string> {
    return (await apiGet(page, "currentUiLanguage")).body.trim();
}

/** Whether "Show translations which have not been approved yet" is on. */
export async function getShowUnapprovedTranslations(
    page: Page,
): Promise<boolean> {
    const response = await apiGet(page, "workspace/showUnapprovedTranslations");
    return response.body.trim() === "true";
}

/**
 * The exact text the UI-language menu shows for a language tag - the language's name in its own
 * language, with an English subtitle only for non-Latin scripts (so plain "français", but
 * "ไทย (Thai)"). Bloom identifies a language by this text when one is chosen, so helpers ask for
 * it rather than guessing. Accepts a regional variant: Bloom's Spanish localization is es-ES.
 */
export async function getMenuTextForLanguageTag(
    page: Page,
    tag: string,
): Promise<{ menuText: string; resolvedTag: string }> {
    const parsed = await apiGetJson<{
        languages: Array<{ label: string; tag: string }>;
    }>(page, "uiLanguages");
    const found = parsed.languages.find(
        (language) =>
            language.tag === tag || language.tag.startsWith(tag + "-"),
    );
    if (!found) {
        throw new Error(
            `Bloom has no localization for language tag "${tag}". It has: ` +
                parsed.languages.map((language) => language.tag).join(", "),
        );
    }
    return { menuText: found.label, resolvedTag: found.tag };
}

/**
 * Open the UI-language dropdown and return a locator for the open menu. Stands for clicking the
 * language button in the top-right corner of the workspace.
 */
export async function openUiLanguageMenu(page: Page): Promise<Locator> {
    // A menu left open by an earlier failure would swallow the click on the button (its modal
    // backdrop covers the screen); Escape closes any open menu and is harmless otherwise.
    await page.keyboard.press("Escape");
    await page.locator("#uiLanguageMenuButton").click();
    const menu = page.locator("ul[role='menu']:visible");
    await expect(menu, "the UI language menu should open").toHaveCount(1, {
        timeout: 10000,
    });
    return menu;
}

/**
 * Change Bloom's UI language the way a user does: open the language menu and click the language.
 * Bloom then reopens the whole project, so this waits out the reload, re-finds the shell page
 * (bloomApp.page from here on), and returns once Bloom reports the new language and the
 * collection is ready. Returns the new shell page.
 */
export async function chooseUiLanguage(
    bloomApp: IBloomApp,
    languageTag: string,
): Promise<Page> {
    const page = bloomApp.page;
    const { menuText, resolvedTag } = await getMenuTextForLanguageTag(
        page,
        languageTag,
    );
    const menu = await openUiLanguageMenu(page);
    const item = menu.locator("li[role='menuitem']").filter({
        hasText: menuText,
    });
    await expect(
        item,
        `expected exactly one menu item matching "${menuText}"`,
    ).toHaveCount(1);

    const pageClosed = page.waitForEvent("close", { timeout: 60000 });
    // If the click fails for a reason other than the teardown race below, we rethrow and never
    // await pageClosed; pre-handling its rejection keeps that from surfacing as an unrelated
    // unhandled-rejection error.
    pageClosed.catch(() => undefined);
    // The click makes Bloom tear the page down, sometimes before Playwright finishes its click
    // protocol; a "page closed" error here means the click landed, not that it failed. (If it
    // genuinely never landed, the pageClosed wait below times out.)
    await item.click().catch((error) => {
        if (!/closed/i.test(String(error))) {
            throw error;
        }
    });
    await pageClosed;

    const newPage = await bloomApp.reattachToShell();
    await expect
        .poll(() => getUiLanguageTag(newPage), {
            timeout: 60000,
            message: `Bloom never reported the UI language as "${resolvedTag}" after choosing "${menuText}".`,
        })
        .toBe(resolvedTag);
    await waitForCollectionReady(newPage);
    await waitForActiveTab(newPage, "collection");
    return newPage;
}

/**
 * Turn "Show translations which have not been approved yet" on or off the way a user does: the
 * checkbox item at the bottom of the UI-language menu. In production this restarts Bloom; in e2e
 * mode Bloom saves the setting without restarting itself (see the helper module comment), so this
 * applies it with the fixture's restart and returns the new shell page. No-op when the setting
 * already has the wanted value.
 */
export async function setShowUnapprovedTranslations(
    bloomApp: IBloomApp,
    value: boolean,
): Promise<Page> {
    const page = bloomApp.page;
    if ((await getShowUnapprovedTranslations(page)) === value) {
        return page;
    }
    const menu = await openUiLanguageMenu(page);
    // The item is located by its checkbox - the only one in the menu - because its label text
    // changes with the UI language.
    const item = menu.locator("li[role='menuitem']").filter({
        has: page.locator("input[type='checkbox']"),
    });
    await expect(
        item,
        "expected exactly one menu item with a checkbox (the unapproved-translations toggle)",
    ).toHaveCount(1);
    await item.click();
    await expect
        .poll(() => getShowUnapprovedTranslations(page), {
            timeout: 15000,
            message:
                "the unapproved-translations setting never changed after clicking its menu item",
        })
        .toBe(value);
    // The already-rendered UI still shows the strings from before the change; restarting Bloom
    // is what makes every surface reflect it, exactly as the production self-restart would.
    const newPage = await bloomApp.restart();
    // Wait for the restarted Bloom to be usable before returning - the collection loaded and
    // the Collections tab (where a fresh Bloom lands) active - so no caller needs its own wait.
    await waitForCollectionReady(newPage);
    await waitForActiveTab(newPage, "collection");
    return newPage;
}

/**
 * Put the UI language and the unapproved-translations setting into the given state through the
 * API - the fast path for test setup and cleanup, not the user's path. Restoring what a test
 * found is the main use: these are machine-wide settings, so a test must put back whatever the
 * developer had, even when it fails.
 *
 * Quirk this absorbs: choosing a language silently does nothing unless the language is in the
 * menu, and the menu only lists languages clearing a completeness threshold - counted over
 * approved strings while unapproved translations are hidden, over translated strings while they
 * are shown. So a target language that is not currently listed (e.g. Turkish while unapproved
 * translations are hidden) needs the setting turned on first; the setting then gets its final
 * value afterwards.
 *
 * Limitation: the visible UI is not refreshed for a final unapproved-translations value that
 * differs from what the last project reopen saw - fine for cleanup, where nobody looks at this
 * Bloom again. A test that needs the UI to reflect the change uses setShowUnapprovedTranslations.
 */
export async function setUiStateViaApi(
    bloomApp: IBloomApp,
    languageTag: string,
    showUnapproved: boolean,
): Promise<void> {
    // Cleanup can run after a failure anywhere - including between a project teardown and the
    // re-attach - so first make sure there is a live shell page to talk through.
    if (bloomApp.page.isClosed()) {
        await bloomApp.reattachToShell();
    }
    // In e2e mode this saves the setting without restarting Bloom, and the menu's language list
    // recomputes its threshold from the saved setting immediately.
    const postShowUnapproved = async (value: boolean): Promise<void> => {
        await apiPost(
            bloomApp.page,
            "workspace/showUnapprovedTranslations",
            value ? "true" : "false",
            "application/json",
        );
    };

    if ((await getUiLanguageTag(bloomApp.page)) !== languageTag) {
        const { menuText, resolvedTag } = await getMenuTextForLanguageTag(
            bloomApp.page,
            languageTag,
        );
        if (resolvedTag !== languageTag) {
            throw new Error(
                `setUiStateViaApi needs the exact language tag ("${resolvedTag}"), not "${languageTag}": ` +
                    `it must compare what currentUiLanguage reports against the target.`,
            );
        }
        const listedNow = await apiGetJson<string[]>(
            bloomApp.page,
            "workspace/uiLanguages",
        );
        if (!listedNow.includes(menuText)) {
            await postShowUnapproved(true);
        }
        const pageClosed = bloomApp.page.waitForEvent("close", {
            timeout: 60000,
        });
        await apiPost(
            bloomApp.page,
            "workspace/uiLanguageAction",
            JSON.stringify({ action: "setLanguage", languageName: menuText }),
            "application/json",
        );
        // Choosing a language reopens the project: wait out the page teardown and re-attach.
        await pageClosed;
        const newPage = await bloomApp.reattachToShell();
        await expect
            .poll(() => getUiLanguageTag(newPage), {
                timeout: 60000,
                message: `Bloom never reported the UI language as "${languageTag}" while setting the UI state.`,
            })
            .toBe(languageTag);
        await waitForCollectionReady(newPage);
    }

    await postShowUnapproved(showUnapproved);
}

/**
 * Assert that the workspace shows these strings - one per localization pathway (see
 * IUiLanguageStrings). The caller is on the Collections tab; every string this checks lives
 * there or in the top bar.
 */
export async function expectUiStrings(
    page: Page,
    strings: IUiLanguageStrings,
): Promise<void> {
    await expect(
        page.locator("#uiLanguageMenuButton"),
        `language menu button should show "${strings.languageMenuButton}"`,
    ).toContainText(strings.languageMenuButton, {
        ignoreCase: true,
        timeout: 30000,
    });
    await expect(
        page
            .getByRole("heading", {
                level: 2,
                name: strings.templates,
                exact: true,
            })
            .first(),
        `Templates collection heading (useL10n) should read "${strings.templates}"`,
    ).toBeVisible({ timeout: 30000 });
    await expect(
        page.getByRole("heading", { level: 1, name: strings.sources }).first(),
        `book-sources heading (LocalizableElement) should read "${strings.sources}"`,
    ).toBeVisible({ timeout: 30000 });
    await expect(
        page.getByRole("tab", { name: strings.editTab, exact: true }),
        `Edit tab (top bar Span) should read "${strings.editTab}"`,
    ).toBeVisible({ timeout: 30000 });
    await expect(
        page
            .locator(".bookButton")
            .filter({ hasText: strings.basicBook })
            .first(),
        `Basic Book button (C#-localized API data) should read "${strings.basicBook}"`,
    ).toBeVisible({ timeout: 30000 });
}
