// Helpers for live checks of a running Bloom's Collection Settings dialog, the language chooser,
// the Edit tab's page list and the reader tools, over CDP. For an agent's own scripts: import
// what you need. (An e2e test uses src/BloomE2E/helpers instead, which wait with expect().)
//
//   // On Windows, import an absolute path as a file:/// URL:
//   import { connectToBloom, openCollectionSettings, ... } from "file:///<repo>/.claude/skills/run-bloom/driveCollectionSettings.mjs";
//   const bloom = await connectToBloom(8089);   // Bloom's http port, from launcherControl --status
//   await bloom.assertCollection("My Scratch Collection");   // before anything that saves
//   await openCollectionSettings(bloom.page);
//   ...
//   await bloom.close();
//
// Everything here clicks and types inside the web view, through CDP; nothing moves the pointer.
// Test against a scratch collection, never the developer's own: see "Verify the open collection"
// in reference.md. To use the Edit tab's page list or the toolbox, start Bloom with
// `launcherControl.mjs --ensure-running --vite-port 5173`: on any other port they load nothing.
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
// repo root is three levels up from .claude/skills/run-bloom/
const repoRoot = path.resolve(here, "..", "..", "..");
const { chromium } = createRequire(
    path.join(
        repoRoot,
        "src/BloomBrowserUI/react_components/component-tester/package.json",
    ),
)("playwright");

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * Attach to the Bloom whose HTTP port is `httpPort` (its CDP port is httpPort + 2, as Bloom's
 * port blocks are laid out). Returns the shell page, its HTTP API helpers and close().
 */
export async function connectToBloom(httpPort) {
    const browser = await chromium.connectOverCDP(
        `http://127.0.0.1:${httpPort + 2}`,
    );
    // Bloom's dialogs that host their own WebView2 (a problem report, for one) are pages here too,
    // so take the one with the workspace tabs: Bloom's main window.
    // Just after Bloom starts (or restarts) its page is still loading, so keep looking a while.
    let page;
    for (let attempt = 0; attempt < 30 && !page; attempt++) {
        for (const candidate of browser.contexts().flatMap((c) => c.pages())) {
            if (candidate.url().startsWith("devtools")) continue;
            const isShell = await candidate
                .evaluate(
                    () =>
                        !!document.querySelector(
                            '[data-testid^="workspace-tab-"]',
                        ),
                )
                .catch(() => false);
            if (isShell) {
                page = candidate;
                break;
            }
        }
        if (!page) await sleep(1000);
    }
    if (!page)
        throw new Error(`No Bloom main window on CDP port ${httpPort + 2}.`);
    const api = (endpoint) =>
        `http://localhost:${httpPort}/bloom/api/${endpoint}`;
    return {
        browser,
        page,
        httpPort,
        /** GET collection/settings: the open collection's settings as the dialog gets them. */
        settings: async () => (await fetch(api("collection/settings"))).json(),
        /** Fail unless the open collection is `name`: call before anything that saves. */
        async assertCollection(name) {
            const open = (await this.settings()).values.advanced.collectionName;
            if (open !== name)
                throw new Error(`Bloom has "${open}" open, not "${name}".`);
        },
        close: () => browser.close(),
    };
}

/** The Collection Settings dialog (the one with a tab list). */
export function collectionSettingsDialog(page) {
    return page
        .getByRole("dialog")
        .filter({ has: page.getByRole("tab") })
        .first();
}

/** Open the Collection Settings dialog, going to the Collections tab first if need be. */
export async function openCollectionSettings(page) {
    const settings = page.getByRole("button", {
        name: "Settings",
        exact: true,
    });
    // The Settings button is on the Collections tab only.
    if (!(await settings.isVisible().catch(() => false))) {
        await page.getByTestId("workspace-tab-collection").click();
        await settings.waitFor({ timeout: 30000 });
    }
    await settings.click();
    await collectionSettingsDialog(page)
        .getByRole("tab")
        .first()
        .waitFor({ timeout: 30000 });
}

/** Show one page of the dialog, e.g. "Languages". */
export async function showSettingsPage(page, name) {
    const tab = collectionSettingsDialog(page).getByRole("tab", {
        name,
        exact: true,
    });
    await tab.click();
    await page.waitForFunction(
        (el) => el.getAttribute("aria-selected") === "true",
        await tab.elementHandle(),
    );
}

/** The box (an MUI Paper) holding the group whose heading is `heading`, e.g. "Written Language 1". */
export function settingsGroup(page, heading) {
    return collectionSettingsDialog(page)
        .locator(`xpath=.//h3[normalize-space(.)="${heading}"]`)
        .first()
        .locator(
            'xpath=following::*[contains(concat(" ",normalize-space(@class)," ")," MuiPaper-root ")][1]',
        );
}

/** Open the More page of the language group `heading`. */
export async function openLanguageMore(page, heading) {
    await settingsGroup(page, heading)
        .getByText("More", { exact: true })
        .click();
    await collectionSettingsDialog(page)
        .getByText("Fonts", { exact: true })
        .waitFor();
}

/** Go back from a More page to the Languages page. */
export async function closeLanguageMore(page) {
    await collectionSettingsDialog(page)
        .getByText("More", { exact: true })
        .first()
        .click();
    await collectionSettingsDialog(page)
        .getByTestId("language-name")
        .first()
        .waitFor();
}

/**
 * The row of the setting labelled `label` on the showing page. Config-R draws most rows as an
 * `li`, but a checkbox row as an MUI ListItemButton.
 */
function settingRow(page, label) {
    return collectionSettingsDialog(page)
        .locator("li, .MuiListItemButton-root")
        .filter({ has: page.locator("h4").getByText(label, { exact: true }) });
}

/** The drop-down list of the setting labelled `label`. */
export function settingList(page, label) {
    return settingRow(page, label).locator('[aria-haspopup="listbox"]').first();
}

/** The checkbox of the setting labelled `label`. */
export function settingCheckbox(page, label) {
    return settingRow(page, label).locator('input[type="checkbox"]');
}

/** The text box of the setting labelled `label`. */
export function settingTextBox(page, label) {
    return settingRow(page, label)
        .locator("textarea:not([aria-hidden]), input[type=text]")
        .first();
}

/** The Default Font list of the language group `heading` on the Languages page. */
export function languageFontList(page, heading) {
    return settingsGroup(page, heading)
        .locator('[aria-haspopup="listbox"]')
        .first();
}

/** What a drop-down list shows (MUI puts a zero-width space in an empty one). */
export async function listShown(list) {
    return ((await list.textContent()) ?? "").replace(/​/g, "").trim();
}

/** Open a drop-down list and pick the choice whose text is `choice`. */
export async function chooseInList(page, list, choice) {
    await list.click();
    const options = page.getByRole("listbox").getByRole("option");
    await options.first().waitFor();
    await options
        .filter({
            hasText: new RegExp(
                `^${choice.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}$`,
            ),
        })
        .first()
        .click();
    await page.getByRole("listbox").waitFor({ state: "detached" });
}

/**
 * Pick a language for the group `heading` through its Change…/Set… button and the Choose
 * Language dialog. `code` is the language's code as the result shows it (e.g. "arb"). `script`
 * answers the chooser when it asks for one (Arabic does: "Arabic", "Hebrew" or "Syriac").
 */
export async function chooseLanguage(
    page,
    heading,
    { search, code, script, customName },
) {
    await settingsGroup(page, heading)
        .getByRole("button", { name: /^(Change|Set)/ })
        .click();
    // An earlier chooser can stay in the page, hidden, after it closes; only a visible one counts.
    const chooser = page
        .getByRole("dialog")
        .filter({ hasText: "Choose Language", visible: true })
        .last();
    await chooser.locator("#search-bar").fill(search);
    const result = chooser.getByText(code, { exact: true }).first();
    await result.waitFor({ timeout: 30000 });
    await result.click();
    if (script) {
        // Each script is a card whose name is an h2. The language's own result card has an h2
        // too, which can have the same name, and the script cards come after it.
        await chooser
            .locator("button.MuiCardActionArea-root h2")
            .filter({ hasText: new RegExp(`^${script}$`) })
            .last()
            .click();
    }
    if (customName)
        await chooser.locator("#language-name-bar").fill(customName);
    const ok = chooser.getByRole("button", { name: /^OK$/i });
    if (await ok.isDisabled())
        throw new Error("The chooser's OK is disabled: does it want a script?");
    await ok.click();
    await chooser.waitFor({ state: "hidden" });
}

/** Click Remove at the right of the heading of Written Language 3 or Sign Language. */
export async function removeLanguage(page, heading) {
    await collectionSettingsDialog(page)
        .locator(`xpath=.//h3[normalize-space(.)="${heading}"]/..`)
        .getByRole("button", { name: /^remove$/i })
        .click();
}

/** The dialog's OK button's label: "OK", or "Restart" when a change needs Bloom to reopen. */
export async function okLabel(page) {
    return (
        (await collectionSettingsDialog(page)
            .getByRole("button", { name: /^(OK|Restart)$/i })
            .textContent()) ?? ""
    ).trim();
}

/** Cancel the dialog. */
export async function cancelCollectionSettings(page) {
    await collectionSettingsDialog(page)
        .getByRole("button", { name: /^Cancel$/i })
        .click();
    await collectionSettingsDialog(page).waitFor({ state: "detached" });
}

/**
 * Click OK (or Restart). After a Restart Bloom reopens the collection with a new shell page, so
 * this closes the connection and waits for the reopened collection's settings to answer; connect
 * again afterwards. Returns the label that was clicked.
 */
export async function saveCollectionSettings(bloom) {
    const label = await okLabel(bloom.page);
    await collectionSettingsDialog(bloom.page)
        .getByRole("button", { name: /^(OK|Restart)$/i })
        .click();
    if (!/restart/i.test(label)) {
        await collectionSettingsDialog(bloom.page).waitFor({
            state: "detached",
        });
        return label;
    }
    await bloom.close().catch(() => {});
    await sleep(8000); // Bloom closes the collection before it reopens it
    for (let i = 0; i < 60; i++) {
        try {
            if ((await bloom.settings())?.values) return label;
        } catch {
            // still reopening
        }
        await sleep(2000);
    }
    throw new Error("Bloom did not come back after Restart.");
}

/**
 * In the Edit tab, show the page whose page-list caption is `caption` (e.g. "1", "Title Page"),
 * through the same editView/jumpToPage the e2e goToPage uses. The page list must have loaded
 * (Bloom started with --vite-port 5173).
 */
export async function goToPageCaptioned(page, caption) {
    const pageList = page.frames().find((f) => f.name() === "pageList");
    if (!pageList)
        throw new Error("There is no page list: is the Edit tab showing?");
    // A thumbnail gets its page id a moment after its caption shows, so wait for the id.
    const id = await pageList
        .waitForFunction(
            (c) => {
                const label = Array.from(
                    document.querySelectorAll(".thumbnailCaption"),
                ).find((n) => n.textContent.trim() === c);
                let p = label;
                while (p && !p.querySelector(".bloom-page[id]"))
                    p = p.parentElement;
                return p?.querySelector(".bloom-page[id]")?.id;
            },
            caption,
            { timeout: 30000 },
        )
        .then((handle) => handle.jsonValue())
        .catch(() => {
            throw new Error(
                `The page list has no page captioned "${caption}" (or its page id never loaded).`,
            );
        });
    await page.evaluate(
        (pageId) =>
            fetch("/bloom/api/editView/jumpToPage", {
                method: "POST",
                headers: { "Content-Type": "text/plain" },
                body: pageId,
            }),
        id,
    );
    await page
        .frames()
        .find((f) => f.name() === "page")
        .waitForFunction((pid) => !!document.getElementById(pid), id);
}

/**
 * Turn a reader tool on for the book being edited, re-enter the Edit tab so the toolbox offers
 * it, and mark the book decodable or leveled through its endpoint, as the e2e
 * enableDecodableReaderTool and enableLeveledReaderTool do. `tool` is "decodableReader" or
 * "leveledReader".
 * This changes the book: use a scratch collection.
 */
export async function enableReaderTool(page, tool) {
    const post = (endpoint, body, type) =>
        page.evaluate(
            ([e, b, t]) =>
                fetch(`/bloom/api/${e}`, {
                    method: "POST",
                    headers: { "Content-Type": t },
                    body: b,
                }).then((r) => r.status),
            [endpoint, body, type],
        );
    await post(
        "editView/saveToolboxSetting",
        `active\t${tool}Check\t1`,
        "text/plain",
    );
    // Mark the book before the reload, so the panel comes up with its switch already on.
    // Clicking the switch instead is unreliable: a re-render during loading puts it back.
    await post(
        tool === "decodableReader" ? "toolbox/decodable" : "toolbox/leveled",
        "true",
        "application/json",
    );
    await page.getByTestId("workspace-tab-collection").click();
    await sleep(3000);
    await page.getByTestId("workspace-tab-edit").click();
    // (The e2e suite's isEditingPage endpoint exists only under --e2e, so wait for the page.)
    await page.waitForFunction(
        () =>
            !!document
                .querySelector('iframe[name="page"]')
                ?.contentDocument?.querySelector(".bloom-page"),
        null,
        { timeout: 90000, polling: 500 },
    );
    const toolbox = await expandTool(page, tool);
    if (tool === "leveledReader")
        // The panel shows Set Up Levels only while the book is leveled.
        await toolbox
            .locator('[data-testid="set-up-levels-button"]')
            .waitFor({ timeout: 30000 });
}

/** The toolbox frame, with the drawer opened and the tool `tool` expanded. */
export async function expandTool(page, tool) {
    if (
        !(await page.evaluate(
            () => document.querySelector("#pure-toggle-right")?.checked,
        ))
    )
        await page
            .locator('label.pure-toggle-label[for="pure-toggle-right"]')
            .click();
    const toolbox = page.frames().find((f) => f.name() === "toolbox");
    const header = toolbox
        .locator(`.MuiAccordionSummary-root:has([data-toolid="${tool}"])`)
        .first();
    await header.waitFor({ timeout: 30000 });
    if ((await header.getAttribute("aria-expanded")) !== "true")
        await header.click();
    return toolbox;
}
