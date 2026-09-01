import { Browser, Page, chromium, expect, test } from "playwright/test";
import {
    bloomApiUrl,
    cdpEndpoints,
    discoverLauncherPorts,
} from "../../../component-tester/bloomExeCdp";

// End-to-end test for switching Bloom's UI language through the real UI-language menu
// (UiLanguageMenu.tsx). It cycles the running Bloom through several languages and, after
// each switch, verifies one visible string from each of the distinct mechanisms that put
// localized text into the web UI:
//
//   1. The useL10n hook              - the factory "Templates" collection heading
//                                      (CollectionsTabPane), id CollectionTab.Templates.
//   2. LocalizableElement components - the <H1 l10nKey="CollectionTab.BookSourceHeading">
//                                      "Sources For New Books" heading.
//   3. <Span> inside the top bar     - the Edit workspace tab label, id EditTab.Edit.
//   4. C#-localized data via the API - the "Basic Book" book-button caption, localized by
//                                      CollectionApi with TemplateBooks.BookName.Basic Book,
//                                      and the UI-language menu button's own label
//                                      (workspace/uiLanguageLabel).
//
// (The remaining mechanism, data-i18n attributes resolved by i18n/loadStrings, only occurs
// inside the Edit tab's book/toolbox iframes and is not covered here.)
//
// It then turns on "Show translations which have not been approved yet" and verifies that
// unapproved translations really start to show: Turkish serves as the unapproved language,
// because every string this test checks has been translated into Turkish but never approved
// (no approved="yes" in DistFiles/localization/tr/Bloom.xlf), and those translations have
// not changed since January 2018, so they should be safe to depend on. While the setting is
// off, the Turkish UI must fall back to English for these strings; once it is on, the
// Turkish text must appear.
//
// PRECONDITIONS:
// - Bloom is running via ./go.sh (the dev launcher). The launcher matters: toggling the
//   unapproved-translations setting makes Bloom restart itself (Program.RestartBloom), and
//   a Bloom started by ./go.sh delegates that restart to the launcher, which brings it
//   back up - possibly on DIFFERENT HTTP/CDP ports, since Bloom picks a free port at
//   startup, which is why this test re-discovers the ports from the launcher before every
//   API call and CDP connection. Picking a language "only" reopens the current project,
//   but that still destroys and recreates the WebView2 page, so the test reconnects over
//   CDP after every change.
// - A collection is open (any collection - the test only looks at factory content that is
//   always present, like the Templates source collection).
// - The UI language and the unapproved-translations setting may start in any state: the
//   test records what it finds, normalizes to English/approved-only for its assertions,
//   and restores the recorded state when it finishes, even after a failure.
// - This is a dev/alpha build (always true when running from source): dev builds list a
//   language in the menu once >=1% of its strings are approved, which admits Turkish (~7%).
//   A release build's 25% threshold would hide it.
//
// The test takes a few minutes: four project reopens and two full Bloom restarts.
//
// Run from src/BloomBrowserUI:
//   pnpm exec playwright test --config react_components/component-tester/playwright.bloom-exe.config.ts \
//       react_components/TopBar/workspaceTopRightControls/component-tests/bloom-exe-ui-language.uitest.ts

interface IWorkspaceConnection {
    browser: Browser;
    page: Page;
}

// One visible string per localization mechanism (see the header comment for which is which),
// plus the language menu button's own label, which doubles as the "which language is Bloom
// in now" gate when reconnecting after a reload or restart.
interface ILocalizedStrings {
    templates: string;
    sources: string;
    editTab: string;
    basicBook: string;
    languageMenuButton: string;
}

const stringsByLanguage: Record<string, ILocalizedStrings> = {
    en: {
        templates: "Templates",
        sources: "Sources For New Books",
        editTab: "Edit",
        basicBook: "Basic Book",
        languageMenuButton: "English",
    },
    fr: {
        templates: "Modèles",
        sources: "Sources pour des nouveaux livres",
        editTab: "Éditer",
        basicBook: "Livre simple",
        languageMenuButton: "français",
    },
    es: {
        templates: "Plantillas",
        sources: "Fuentes para nuevos libros",
        editTab: "Editar",
        basicBook: "Libro básico",
        languageMenuButton: "español",
    },
    // Translated on Crowdin but never approved - see the header comment.
    tr: {
        templates: "Şablonlar",
        sources: "Yeni Kitaplar İçin Kaynaklar",
        editTab: "Düzenle",
        basicBook: "Temel Kitap",
        languageMenuButton: "Türkçe",
    },
};

const sleep = (ms: number): Promise<void> =>
    new Promise((resolve) => setTimeout(resolve, ms));

// The ports Bloom is currently on. Bloom picks a free port at startup, and this test
// restarts Bloom, so the ports can change mid-test: every API call and CDP connection
// re-asks the ./go.sh launcher first. When there is no launcher (Bloom started some
// other way, which also means nothing can restart it on a new port), the env-derived
// values from bloomExeCdp serve as the fallback.
let launcherPorts: { httpPort: number; cdpPort: number } | undefined;
const refreshLauncherPorts = async (): Promise<void> => {
    // Keep the last known ports if the launcher momentarily fails to answer.
    launcherPorts = (await discoverLauncherPorts()) ?? launcherPorts;
};
const apiUrl = (suffix: string): string =>
    launcherPorts
        ? `http://localhost:${launcherPorts.httpPort}/bloom/api/${suffix}`
        : bloomApiUrl(suffix);
const currentCdpEndpoints = (): string[] =>
    launcherPorts
        ? [
              `http://127.0.0.1:${launcherPorts.cdpPort}`,
              `http://localhost:${launcherPorts.cdpPort}`,
          ]
        : cdpEndpoints;

// GETs one of Bloom's API endpoints from the test process and returns the body as text.
const getApiText = async (suffix: string): Promise<string> => {
    await refreshLauncherPorts();
    const response = await fetch(apiUrl(suffix));
    if (!response.ok) {
        throw new Error(
            `GET ${suffix} failed: ${response.status} ${response.statusText}`,
        );
    }
    return (await response.text()).trim();
};

// POSTs a JSON body to one of Bloom's API endpoints from the test process.
const postApiJson = async (suffix: string, body: string): Promise<void> => {
    await refreshLauncherPorts();
    const response = await fetch(apiUrl(suffix), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body,
    });
    if (!response.ok) {
        throw new Error(
            `POST ${suffix} failed: ${response.status} ${response.statusText}`,
        );
    }
};

// Looks up the exact menu text Bloom uses for a language tag (e.g. "français (French)"
// for "fr"). workspace/uiLanguageAction identifies languages by that text, not the tag.
const getMenuTextForLanguageTag = async (tag: string): Promise<string> => {
    await refreshLauncherPorts();
    const response = await fetch(apiUrl("uiLanguages"));
    if (!response.ok) {
        throw new Error(`GET uiLanguages failed: ${response.status}`);
    }
    const parsed = (await response.json()) as {
        languages: Array<{ label: string; tag: string }>;
    };
    // Accept a regional variant: Bloom's Spanish localization is tagged es-ES, for example.
    const found = parsed.languages.find(
        (language) =>
            language.tag === tag || language.tag.startsWith(tag + "-"),
    );
    if (!found) {
        throw new Error(
            `Language tag "${tag}" not found among Bloom's localized languages: ` +
                parsed.languages.map((language) => language.tag).join(", "),
        );
    }
    return found.label;
};

// Scans the connected browser's CDP targets for Bloom's main workspace page - the one
// with the top-bar workspace tabs AND a UI-language menu button whose label contains the
// expected language name. (The collection chooser dialog and the splash screen also have
// /bloom/ URLs, and the chooser even has its own UI-language button, so the tab check
// matters. The label check keeps us from grabbing a page from before the change we just
// made.) Returns undefined if no such page exists yet.
const tryFindWorkspacePage = async (
    browser: Browser,
    expectedLanguageLabel: string,
): Promise<Page | undefined> => {
    const pages = browser.contexts().flatMap((context) => context.pages());
    for (const page of pages) {
        try {
            const url = page.url();
            if (!url.includes("/bloom/") || url.startsWith("devtools://")) {
                continue;
            }
            if ((await page.locator("a[role='tab']").count()) === 0) {
                continue;
            }
            const button = page.locator("#uiLanguageMenuButton");
            if ((await button.count()) === 0) {
                continue;
            }
            const label = (await button.innerText()).trim().toLowerCase();
            if (label.includes(expectedLanguageLabel.toLowerCase())) {
                return page;
            }
        } catch {
            // The page may be mid-navigation or closing; try the next one, or the next poll.
        }
    }
    return undefined;
};

// Connects to Bloom over CDP and waits until the main workspace page is up in the expected
// UI language. Keeps retrying for the whole timeout, so it survives both a project reopen
// (language change) and a full Bloom restart (unapproved-translations toggle), during which
// the CDP endpoint itself is down for a while.
const connectToWorkspace = async (
    expectedLanguageLabel: string,
    timeoutMs: number,
): Promise<IWorkspaceConnection> => {
    const deadline = Date.now() + timeoutMs;
    let lastError: unknown;
    while (Date.now() < deadline) {
        let browser: Browser | undefined;
        try {
            await refreshLauncherPorts();
            for (const endpoint of currentCdpEndpoints()) {
                try {
                    browser = await chromium.connectOverCDP(endpoint);
                    break;
                } catch (error) {
                    lastError = error;
                }
            }
            if (browser) {
                const page = await tryFindWorkspacePage(
                    browser,
                    expectedLanguageLabel,
                );
                if (page) {
                    return { browser, page };
                }
                await browser.close();
            }
        } catch (error) {
            lastError = error;
            await browser?.close().catch(() => undefined);
        }
        await sleep(1000);
    }
    throw new Error(
        `Timed out (${timeoutMs}ms) waiting for Bloom's workspace page with UI language button showing "${expectedLanguageLabel}". ` +
            `Is Bloom running via ./go.sh? Last connection error: ${lastError}`,
    );
};

// Opens the UI-language dropdown and returns a locator for the open menu. (Other MUI menus
// in the workspace are keepMounted, so a bare ul[role=menu] locator can match hidden ones;
// :visible narrows it to the menu we just opened.)
const openUiLanguageMenu = async (page: Page) => {
    // A previous failed run can leave a menu open, and an open menu's backdrop would
    // swallow the click on the button; Escape closes any open menu and is harmless
    // otherwise.
    await page.keyboard.press("Escape");
    await page.locator("#uiLanguageMenuButton").click();
    const menu = page.locator("ul[role='menu']:visible");
    await expect(menu, "the UI language menu should open").toHaveCount(1, {
        timeout: 10000,
    });
    return menu;
};

// Switches Bloom's UI language by clicking the language's item in the UI-language menu,
// then waits for the current page to be torn down (a language change reopens the whole
// project, destroying the WebView2 page) and reconnects to the reopened workspace.
// Waiting for the close before reconnecting guarantees we never run assertions against
// the outgoing page - its language-button label updates over the websocket ahead of the
// reload, so the label alone cannot distinguish old from new.
const switchUiLanguage = async (
    connection: IWorkspaceConnection,
    languageTag: string,
    expectedLanguageLabel: string,
): Promise<IWorkspaceConnection> => {
    // The item's text is the language's MenuText - its name in its own language, with an
    // English subtitle only for non-Latin scripts (e.g. plain "français") - so ask Bloom
    // for the exact text rather than guessing it.
    const menuText = await getMenuTextForLanguageTag(languageTag);
    const menu = await openUiLanguageMenu(connection.page);
    const item = menu.locator("li[role='menuitem']").filter({
        hasText: menuText,
    });
    await expect(
        item,
        `expected exactly one menu item matching "${menuText}"`,
    ).toHaveCount(1);
    const pageClosed = connection.page.waitForEvent("close", {
        timeout: 120000,
    });
    // If the click fails for a reason other than the teardown race below, we rethrow and
    // never await pageClosed; this keeps its eventual rejection from surfacing as an
    // unhandled-rejection error that would obscure the real failure.
    pageClosed.catch(() => undefined);
    // The click makes Bloom tear the page down, sometimes before Playwright finishes its
    // click protocol; a "page closed" error here means the click landed, not that it
    // failed. (If it genuinely never landed, the pageClosed wait below times out.)
    await item.click().catch((error) => {
        if (!/closed/i.test(String(error))) {
            throw error;
        }
    });
    await pageClosed;
    await connection.browser.close().catch(() => undefined);
    return connectToWorkspace(expectedLanguageLabel, 180000);
};

// Toggles "Show translations which have not been approved yet". The item is located by its
// checkbox - the only one in the menu - because its label text changes with the UI
// language. Toggling restarts Bloom completely, so allow a long wait for it to come back.
const toggleShowUnapprovedTranslations = async (
    connection: IWorkspaceConnection,
    expectedLanguageLabel: string,
): Promise<IWorkspaceConnection> => {
    const menu = await openUiLanguageMenu(connection.page);
    const item = menu.locator("li[role='menuitem']").filter({
        has: connection.page.locator("input[type='checkbox']"),
    });
    await expect(
        item,
        "expected exactly one menu item with a checkbox (the unapproved-translations toggle)",
    ).toHaveCount(1);
    const pageClosed = connection.page.waitForEvent("close", {
        timeout: 180000,
    });
    // See switchUiLanguage for both of these: the pre-handled rejection, and the
    // restart tearing the page down mid-click.
    pageClosed.catch(() => undefined);
    await item.click().catch((error) => {
        if (!/closed/i.test(String(error))) {
            throw error;
        }
    });
    await pageClosed;
    await connection.browser.close().catch(() => undefined);
    return connectToWorkspace(expectedLanguageLabel, 360000);
};

// Makes sure the Collections tab is the active one; the strings this test checks live there.
// The Collections tab is always the first workspace tab, whatever language it is labeled in.
const ensureCollectionsTabActive = async (page: Page): Promise<void> => {
    // A menu left open (for example by an earlier failed run) aria-hides everything
    // behind it, which would blind the getByRole queries below; Escape closes any open
    // menu and is harmless otherwise.
    await page.keyboard.press("Escape");
    const collectionsTab = page.getByRole("tab").first();
    await expect(collectionsTab).toBeVisible({ timeout: 30000 });
    if ((await collectionsTab.getAttribute("aria-selected")) !== "true") {
        await collectionsTab.click();
    }
};

// Asserts that one visible string from each localization mechanism (see the header comment)
// shows the expected text for the current UI language.
const expectWorkspaceStrings = async (
    page: Page,
    strings: ILocalizedStrings,
): Promise<void> => {
    await ensureCollectionsTabActive(page);
    // The language menu button label: C#-localized data (workspace/uiLanguageLabel).
    await expect(
        page.locator("#uiLanguageMenuButton"),
        `language menu button should show "${strings.languageMenuButton}"`,
    ).toContainText(strings.languageMenuButton, {
        ignoreCase: true,
        timeout: 30000,
    });
    // The factory "Templates" collection heading: the useL10n hook.
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
    // The "Sources For New Books" heading: an <H1> LocalizableElement.
    await expect(
        page.getByRole("heading", { level: 1, name: strings.sources }).first(),
        `book-sources heading (LocalizableElement) should read "${strings.sources}"`,
    ).toBeVisible({ timeout: 30000 });
    // The Edit workspace tab: a <Span> LocalizableElement in the top bar.
    await expect(
        page.getByRole("tab", { name: strings.editTab, exact: true }),
        `Edit tab (top bar Span) should read "${strings.editTab}"`,
    ).toBeVisible({ timeout: 30000 });
    // The "Basic Book" button caption: localized in C# and delivered as API data.
    await expect(
        page
            .locator(".bookButton")
            .filter({ hasText: strings.basicBook })
            .first(),
        `Basic Book button (C#-localized API data) should read "${strings.basicBook}"`,
    ).toBeVisible({ timeout: 30000 });
};

// Puts Bloom's UI language and unapproved-translations setting into the given state,
// driving the same APIs the menu uses, from Node. Used to normalize the starting state
// (the assertions assume English with approved-only translations) and to put back
// whatever state the developer actually had when the test is done - even after a
// failure, so a broken run cannot leave their Bloom in Turkish. Polls patiently,
// because a language change makes Bloom reload its project and changing the
// unapproved-translations setting makes it restart outright.
const setUiStateViaApi = async (
    languageTag: string,
    showUnapproved: boolean,
): Promise<void> => {
    const deadline = Date.now() + 360000;
    const pollApiText = async (suffix: string): Promise<string> => {
        let lastError: unknown;
        while (Date.now() < deadline) {
            try {
                return await getApiText(suffix);
            } catch (error) {
                lastError = error;
                await sleep(2000);
            }
        }
        throw new Error(
            `Bloom's API did not answer ${suffix} while setting the UI state: ${lastError}`,
        );
    };

    // Set the unapproved-translations setting BEFORE the language. setLanguage only
    // accepts languages that clear the menu's completeness threshold, and that threshold
    // is computed from approved strings unless the setting is on - so restoring a state
    // like (Turkish, unapproved shown) would silently fail if the language went first,
    // while a threshold-clearing language like English works in either order.
    const wanted = showUnapproved ? "true" : "false";
    let actual = await pollApiText("workspace/showUnapprovedTranslations");
    if (actual !== wanted) {
        // Changing this restarts Bloom, so poll until the restarted Bloom answers.
        await postApiJson("workspace/showUnapprovedTranslations", wanted);
        while (Date.now() < deadline && actual !== wanted) {
            await sleep(2000);
            try {
                actual = await getApiText(
                    "workspace/showUnapprovedTranslations",
                );
            } catch {
                // Bloom is mid-restart; keep polling.
            }
        }
        if (actual !== wanted) {
            throw new Error(
                `Timed out waiting for the unapproved-translations setting to become ${wanted}`,
            );
        }
    }

    let language = await pollApiText("currentUiLanguage");
    if (language !== languageTag) {
        const menuText = await getMenuTextForLanguageTag(languageTag);
        await postApiJson(
            "workspace/uiLanguageAction",
            JSON.stringify({
                action: "setLanguage",
                languageName: menuText,
            }),
        );
        while (Date.now() < deadline && language !== languageTag) {
            await sleep(2000);
            try {
                language = await getApiText("currentUiLanguage");
            } catch {
                // Bloom is mid-reload; keep polling.
            }
        }
        if (language !== languageTag) {
            throw new Error(
                `Timed out waiting for the UI language to become "${languageTag}"`,
            );
        }
    }
};

test.describe("Bloom exe CDP: UI language switching", () => {
    test("changing the UI language updates strings from every localization pathway, including unapproved translations", async () => {
        // Four project reopens plus two full Bloom restarts; each inner wait has its own
        // tighter deadline with a more specific error message.
        test.setTimeout(25 * 60 * 1000);

        // Record how the developer had Bloom configured (these are machine-wide user
        // settings, shared with their other work), then normalize to the state the
        // assertions assume: English, approved translations only. The finally block puts
        // the recorded state back.
        const originalLanguage = await getApiText("currentUiLanguage");
        const originalShowUnapproved =
            (await getApiText("workspace/showUnapprovedTranslations")) ===
            "true";
        // Everything from the normalization on sits inside the try, so the finally can
        // put the recorded state back no matter where a failure happens.
        let connection: IWorkspaceConnection | undefined;
        try {
            if (originalLanguage !== "en" || originalShowUnapproved) {
                await setUiStateViaApi("en", false);
            }
            connection = await connectToWorkspace(
                stringsByLanguage.en.languageMenuButton,
                120000,
            );
            // Sanity-check the normalized starting state so the assertions below cannot
            // pass (or fail) for the wrong reason.
            expect(await getApiText("currentUiLanguage")).toBe("en");
            expect(
                await getApiText("workspace/showUnapprovedTranslations"),
            ).toBe("false");
            await expectWorkspaceStrings(connection.page, stringsByLanguage.en);

            connection = await switchUiLanguage(
                connection,
                "fr",
                stringsByLanguage.fr.languageMenuButton,
            );
            await expectWorkspaceStrings(connection.page, stringsByLanguage.fr);

            connection = await switchUiLanguage(
                connection,
                "es",
                stringsByLanguage.es.languageMenuButton,
            );
            await expectWorkspaceStrings(connection.page, stringsByLanguage.es);

            // Turkish while unapproved translations are still hidden: Bloom is IN Turkish
            // (the menu button label proves it), but every string we check must fall back
            // to English because its Turkish translation exists and is unapproved.
            connection = await switchUiLanguage(
                connection,
                "tr",
                stringsByLanguage.tr.languageMenuButton,
            );
            await expectWorkspaceStrings(connection.page, {
                ...stringsByLanguage.en,
                languageMenuButton: stringsByLanguage.tr.languageMenuButton,
            });

            // Turn on "Show translations which have not been approved yet" (restarts
            // Bloom); the same strings must now show their unapproved Turkish text.
            connection = await toggleShowUnapprovedTranslations(
                connection,
                stringsByLanguage.tr.languageMenuButton,
            );
            expect(
                await getApiText("workspace/showUnapprovedTranslations"),
                "the unapproved-translations setting should be on after toggling it",
            ).toBe("true");
            await expectWorkspaceStrings(connection.page, stringsByLanguage.tr);

            // Back to the default state through the same UI, which re-verifies English
            // one more time after everything settles.
            connection = await switchUiLanguage(
                connection,
                "en",
                stringsByLanguage.en.languageMenuButton,
            );
            connection = await toggleShowUnapprovedTranslations(
                connection,
                stringsByLanguage.en.languageMenuButton,
            );
            expect(
                await getApiText("workspace/showUnapprovedTranslations"),
                "the unapproved-translations setting should be off again",
            ).toBe("false");
            await expectWorkspaceStrings(connection.page, stringsByLanguage.en);
        } finally {
            await connection?.browser.close().catch(() => undefined);
            // Put Bloom back exactly the way the developer had it (they may deliberately
            // run with another UI language or with unapproved translations showing).
            await setUiStateViaApi(originalLanguage, originalShowUnapproved);
        }
    });
});
