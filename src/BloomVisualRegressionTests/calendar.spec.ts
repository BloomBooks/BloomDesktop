// End to end: make a Wall Calendar book from the template and check that the front-end
// calendar tooling does everything the old WinForms setup wizard used to do, and the things
// it never did.
//
// This suite starts with nothing: a throwaway copy of the test collection, a Bloom of its own,
// and no calendar book. It then does what a user does — make the book, answer the setup
// dialog, walk through months, type names — and checks the book and the collection's
// configuration.txt after each step. The one thing it cannot do through Bloom's HTTP API is
// answer the dialog, which lives in Bloom's own window, so it attaches to that window over
// CDP and clicks it the way a user would.

import { Browser, chromium, expect, Frame, Page } from "@playwright/test";
import { afterAll, beforeAll, describe, test } from "vitest";
import * as fs from "fs";
import * as Path from "path";
import fetch from "node-fetch";
import {
    cleanupTempCollections,
    IBloomInstance,
    launchDedicatedBloom,
    stopBloom,
} from "./bloomInstance";

/** The template book we make calendars from. It ships with Bloom, in a factory collection. */
const kWallCalendarFolderName = "Wall Calendar";

/** The year we set the calendar to, which is not the year the dialog offers by default. */
const kYear = 2027;

/** The first day of the week we choose, which is not the Sunday the dialog offers by default. */
const kFirstDayOfWeek = 1; // Monday

const kCollectionName = "basic";

/** Haitian Creole: a real language, and one the Wall Calendar template does not seed. */
const kVernacularCode = "hat";
const kVernacularName = "Haitian Creole";

let bloom: IBloomInstance;
let browser: Browser;
// Bloom's own window: the workspace, the page list, and the edit page iframe.
let appPage: Page;

describe("the Wall Calendar tooling", () => {
    beforeAll(async () => {
        bloom = await launchDedicatedBloom({
            tempFolderPrefix: "bloom-calendar-",
            collectionName: kCollectionName,
            // The calendar book is made from a template that ships with Bloom, so all this
            // collection has to be is a real, empty-ish collection to make it in.
            populate: (dest) => {
                fs.cpSync(Path.join(process.cwd(), "collections"), dest, {
                    recursive: true,
                });
                useAVernacularLanguage(dest);
            },
        });
        await waitForCollection();
        appPage = await attachToBloomWindow();
    });

    afterAll(async () => {
        await browser?.close();
        stopBloom();
        cleanupTempCollections();
    });

    test("sets a new calendar book up and fills its months in as they are opened", async () => {
        // --- Make the book. Nothing blocks creation now that the wizard is gone. ---
        const template = await findBook(kWallCalendarFolderName);
        expect(
            template,
            `The ${kWallCalendarFolderName} template is not in any of this Bloom's collections`,
        ).toBeTruthy();
        await apiPost(
            `collections/selected-book?path=${encodeURIComponent(template!.folderPath)}` +
                `&collection-id=${encodeURIComponent(template!.collectionId)}`,
        );
        await apiPost("app/makeFromSelectedBook");
        // Making a book opens it in the edit tab. Wait for that before answering anything: the
        // dialog we want belongs to the new book, and one that came up while the template was
        // selected would set up the wrong book. Copying a 24-page template and opening it is
        // the slowest thing this suite waits for, hence the generous limit.
        await getEditFrame(180000);

        // --- Answer the setup dialog, which the tooling shows because the book has no year. ---
        await answerSetupDialog(kYear, kFirstDayOfWeek);
        // The tooling now takes us to the first month grid. Let that finish before asking for
        // pages ourselves, so our requests never land while Bloom is between pages.
        await getEditFrame();

        // The tooling takes us to the first month grid, which lays itself out on arrival.
        const gridPageIds = await getGridPageIds();
        expect(
            gridPageIds.length,
            "a new Wall Calendar book should have twelve month-grid pages",
        ).toBe(12);

        // --- January: laid out for 2027 with Monday first. ---
        await openPage(gridPageIds[0]);
        await expectLaidOut();
        // 1 January 2027 is a Friday, which is the fifth column of a Monday-first week.
        // The four blanks before day 1 and the cells after day 31 are all unused.
        expect(await getDayNumbers()).toEqual(blanksThen(4, 31));
        expect(await getUnusedDayCellCount()).toBe(42 - 31);
        expect(await getYearShown()).toBe(String(kYear));
        // The title's own-language slot ships empty, with the five seed languages beside it.
        expect(await getMonthNameTyped()).toBe("");
        expect(await getMonthNameSeeds()).toEqual(
            expect.arrayContaining(["January", "janvier", "enero"]),
        );

        // --- February: a 28-day month, laid out when we arrive, not before. ---
        await openPage(gridPageIds[1]);
        await expectLaidOut();
        // 1 February 2027 is a Monday, so with Monday first there are no leading blanks.
        expect(await getDayNumbers()).toEqual(blanksThen(0, 28));
        expect(await getYearShown()).toBe(String(kYear));

        // --- A late month, to show that this is not just the first two. ---
        await openPage(gridPageIds[10]); // November
        await expectLaidOut();
        // 1 November 2027 is a Monday as well.
        expect(await getDayNumbers()).toEqual(blanksThen(0, 30));
        expect(await getYearShown()).toBe(String(kYear));

        // --- Type a weekday name on one month; it fills in on the next. ---
        await openPage(gridPageIds[0]);
        await typeWeekdayName(kFirstDayOfWeek, "Mande");
        await openPage(gridPageIds[1]);
        expect(
            await getWeekdayNameTyped(kFirstDayOfWeek),
            "the name typed on January should have filled February's empty slot",
        ).toBe("Mande");

        // A different name typed on February must not travel backwards over January's.
        await typeWeekdayName(kFirstDayOfWeek, "Lundi");
        await openPage(gridPageIds[0]);
        expect(
            await getWeekdayNameTyped(kFirstDayOfWeek),
            "January's own name should survive a different name being typed on February",
        ).toBe("Mande");

        // The collection remembers the most recent one.
        expect(readConfiguredDayNames()[kFirstDayOfWeek]).toBe("Lundi");

        // --- A month name lands in the collection's settings too. ---
        await typeMonthName("Janvye");
        await openPage(gridPageIds[1]); // leaving January saves it
        expect(readConfiguredMonthNames()[0]).toBe("Janvye");
    });

    test("reconciles the book with the collection's names when the book is reopened", async () => {
        // Change one day name in the collection and empty one month name, behind Bloom's back,
        // the way moving a book to another collection would.
        const settings = readCalendarSettings();
        settings.dayNames[kFirstDayOfWeek] = "Lindi";
        settings.monthNames[0] = "";
        writeCalendarSettings(settings);

        await reopenTheCalendarBook();

        const gridPageIds = await getGridPageIds();
        await openPage(gridPageIds[0]);
        expect(
            await getWeekdayNameTyped(kFirstDayOfWeek),
            "the collection's name should win on the first open of a page in a session",
        ).toBe("Lindi");
        expect(
            await getMonthNameTyped(),
            "the month name the book still has should survive an emptied setting",
        ).toBe("Janvye");
        // ... and the emptied setting is filled back in from the book.
        await openPage(gridPageIds[1]);
        expect(readConfiguredMonthNames()[0]).toBe("Janvye");
    });

    test("offers a second calendar book the names the first one taught it", async () => {
        const template = await findBook(kWallCalendarFolderName);
        await apiPost(
            `collections/selected-book?path=${encodeURIComponent(template!.folderPath)}` +
                `&collection-id=${encodeURIComponent(template!.collectionId)}`,
        );
        await apiPost("app/makeFromSelectedBook");
        await getEditFrame(180000);

        // The dialog comes up with the first day of the week the collection remembers.
        expect(await getSetupDialogFirstDayOfWeek()).toBe(kFirstDayOfWeek);
        await answerSetupDialog(kYear + 1, kFirstDayOfWeek);

        const gridPageIds = await getGridPageIds();
        await openPage(gridPageIds[0]);
        await expectLaidOut();
        expect(await getYearShown()).toBe(String(kYear + 1));
        expect(
            await getWeekdayNameTyped(kFirstDayOfWeek),
            "a new book should start with the names the collection has learnt",
        ).toBe("Lindi");
    });
});

// --- Talking to Bloom -----------------------------------------------------------------

/**
 * Attach to Bloom's own window over CDP.
 *
 * Two gotchas, pulling in opposite directions (see .github/skills/bloom-automation/SKILL.md):
 * Node resolves "localhost" to IPv6 first but the WebView2 debugging port answers only on
 * IPv4, so we ask 127.0.0.1 and rewrite the websocket address it hands back; and Bloom's HTTP
 * server rejects a Host header of 127.0.0.1, which is why every API call below is made from
 * inside the page rather than from here.
 */
async function attachToBloomWindow(): Promise<Page> {
    expect(
        bloom.cdpPort,
        "Bloom did not report a WebView2 debugging port",
    ).toBeTruthy();
    // Bloom says it is ready as soon as its server is listening, which is before the first
    // WebView2 exists, and the debugging port is Chromium's, not Bloom's. So wait for it.
    const version = await waitForCdpEndpoint(bloom.cdpPort!);
    browser = await chromium.connectOverCDP(
        version.webSocketDebuggerUrl.replace("localhost", "127.0.0.1"),
    );
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        const pages = browser.contexts().flatMap((c) => c.pages());
        const workspace = pages.find((p) => p.url().includes("/bloom/"));
        if (workspace) {
            reportBrowserProblems(workspace);
            return workspace;
        }
        await pause(500);
    }
    throw new Error("Could not find Bloom's workspace page over CDP");
}

/**
 * Echo Bloom's own errors to our standard error.
 *
 * Without this a script error inside Bloom's window is invisible here: the test just watches
 * something never happen and reports a timeout, with nothing to say why.
 */
function reportBrowserProblems(page: Page): void {
    page.on("pageerror", (error) =>
        process.stderr.write(`[bloom page error] ${error.message}\n`),
    );
    page.on("console", (message) => {
        if (message.type() === "error" || message.type() === "warning") {
            process.stderr.write(
                `[bloom console ${message.type()}] ${message.text()}\n`,
            );
        }
    });
}

/** The CDP endpoint's own description, once Chromium has opened the port. */
async function waitForCdpEndpoint(
    cdpPort: number,
): Promise<{ webSocketDebuggerUrl: string }> {
    const deadline = Date.now() + 120000;
    let lastError = "";
    while (Date.now() < deadline) {
        try {
            const response = await fetch(
                `http://127.0.0.1:${cdpPort}/json/version`,
            );
            if (response.ok) {
                return (await response.json()) as {
                    webSocketDebuggerUrl: string;
                };
            }
            lastError = `status ${response.status}`;
        } catch (error) {
            lastError = String(error);
        }
        await pause(500);
    }
    throw new Error(
        `Bloom's WebView2 debugging port ${cdpPort} never answered: ${lastError}`,
    );
}

/** Wait until Bloom has its collection loaded and its books enumerable. */
async function waitForCollection(): Promise<void> {
    const deadline = Date.now() + 120000;
    while (Date.now() < deadline) {
        try {
            const r = await fetch(
                `${bloom.origin}/bloom/api/e2e/isCollectionReady`,
            );
            if (r.ok && (await r.text()).includes("true")) return;
        } catch (e) {
            // Bloom is not answering yet.
        }
        await pause(1000);
    }
    throw new Error("Bloom did not finish loading its collection within 120s");
}

/** GET one of Bloom's APIs, from inside Bloom's own page so the Host header is right. */
async function apiGet(path: string): Promise<unknown> {
    return appPage.evaluate(async (p) => {
        // globalThis.fetch, not the bare name: vitest's transform rewrites a bare `fetch` in
        // here into the node-fetch this file imports, which does not exist in Bloom's page.
        const response = await (globalThis as any).fetch(`/bloom/api/${p}`);
        const text = await response.text();
        try {
            return JSON.parse(text);
        } catch (e) {
            return text;
        }
    }, path);
}

/** POST one of Bloom's APIs, from inside Bloom's own page. */
async function apiPost(path: string, body?: unknown): Promise<number> {
    return appPage.evaluate(
        async (args) => {
            const response = await (globalThis as any).fetch(
                `/bloom/api/${args.path}`,
                {
                    method: "POST",
                    headers:
                        args.body === undefined
                            ? undefined
                            : { "Content-Type": "application/json" },
                    body:
                        args.body === undefined
                            ? undefined
                            : JSON.stringify(args.body),
                },
            );
            return response.status;
        },
        { path, body },
    );
}

/** The book with the given folder name, wherever in Bloom's collections it lives. */
async function findBook(
    folderName: string,
): Promise<{ folderPath: string; collectionId: string } | undefined> {
    const collections = (await apiGet("collections/list")) as Array<{
        id: string;
    }>;
    for (const collection of collections) {
        const books = (await apiGet(
            `collections/books?collection-id=${encodeURIComponent(collection.id)}`,
        )) as Array<{
            folderName: string;
            folderPath: string;
            collectionId: string;
        }>;
        const book = books?.find?.((b) => b.folderName === folderName);
        if (book)
            return {
                folderPath: book.folderPath,
                collectionId: book.collectionId,
            };
    }
    return undefined;
}

// --- Driving the edit view ------------------------------------------------------------

/**
 * The iframe the book's current page is edited in, if it is showing a page just now.
 *
 * Must be the frame named "page": the edit view also has an unnamed wrapper frame whose URL
 * carries the page's own URL as a query parameter, so matching on the URL alone finds the
 * wrapper, which holds no .bloom-page.
 */
async function tryGetEditFrame(): Promise<Frame | undefined> {
    const frame = appPage.frames().find((f) => f.name() === "page");
    if (!frame) return undefined;
    // The frame is swapped out from under us as pages change, which makes this throw rather
    // than return; that is just "not showing a page at the moment".
    const hasPage = await frame.$(".bloom-page").catch(() => null);
    return hasPage ? frame : undefined;
}

/** The iframe the book's current page is edited in. */
async function getEditFrame(waitMs = 120000): Promise<Frame> {
    const deadline = Date.now() + waitMs;
    while (Date.now() < deadline) {
        const frame = await tryGetEditFrame();
        if (frame) return frame;
        await pause(250);
    }
    throw new Error(
        `The edit page iframe never showed a .bloom-page. Bloom's window has these frames:\n  ${describeFrames()}`,
    );
}

/**
 * Make the throwaway collection's own language one the calendar template does not seed.
 *
 * The shared test collection is in English, and the template seeds every month and weekday
 * name in English, French, Spanish, Indonesian and Portuguese. In an English collection the
 * English seed IS the slot the user types in, so no name is ever empty and there is nothing
 * for the tooling to fill in. The people this feature is for are writing a calendar in a
 * language that has no seed, so that is what this suite works in.
 */
function useAVernacularLanguage(tempCollectionsRoot: string): void {
    const file = Path.join(
        tempCollectionsRoot,
        kCollectionName,
        `${kCollectionName}.bloomCollection`,
    );
    const original = fs.readFileSync(file, "utf8");
    const changed = original
        .replace(
            /<Language1Iso639Code>[^<]*<\/Language1Iso639Code>/,
            `<Language1Iso639Code>${kVernacularCode}</Language1Iso639Code>`,
        )
        .replace(
            /<Language1Name>[^<]*<\/Language1Name>/,
            `<Language1Name>${kVernacularName}</Language1Name>`,
        );
    expect(
        changed,
        "the test collection should name a first language for us to change",
    ).not.toBe(original);
    fs.writeFileSync(file, changed);
}

/** What frames Bloom's window has just now, for a failure message. */
function describeFrames(): string {
    return appPage
        .frames()
        .map((f) => `${f.name() || "(no name)"} @ ${f.url()}`)
        .join("\n  ");
}

/**
 * The ids of the book's month-grid pages, in order.
 *
 * Making a book and opening it in the edit tab is several asynchronous steps in Bloom, and
 * pageList/pages answers "could not find book" until they finish, so wait rather than take the
 * first answer.
 */
async function getGridPageIds(): Promise<string[]> {
    const deadline = Date.now() + 60000;
    let pages: Array<{ key: string; isXMatter: boolean }> | undefined;
    while (Date.now() < deadline) {
        pages = (
            (await apiGet("pageList/pages")) as {
                pages?: Array<{ key: string; isXMatter: boolean }>;
            }
        )?.pages;
        if (pages?.length) break;
        await pause(250);
    }
    expect(
        pages?.length,
        "Bloom never listed the pages of the book",
    ).toBeTruthy();
    const ids: string[] = [];
    for (const page of pages!.filter((p) => !p.isXMatter)) {
        const content = (await apiGet(
            `pageList/pageContent?page-id=${encodeURIComponent(page.key)}`,
        )) as { content?: string };
        if (String(content?.content ?? "").includes("data-calendar-month"))
            ids.push(page.key);
    }
    return ids;
}

/**
 * Open a page the way clicking its thumbnail does, and wait for it to be showing.
 *
 * EditingModel drops a page-selection request that arrives while it is between pages (its
 * "wrong state, do nothing" branch), and says nothing about having done so, which leaves the
 * edit iframe on about:blank for good. So ask again every few seconds until the page appears.
 */
async function openPage(pageId: string): Promise<void> {
    const deadline = Date.now() + 180000;
    let lastSeen = "nothing";
    let nextAsk = 0;
    while (Date.now() < deadline) {
        const frame = await tryGetEditFrame();
        // Only ask while a page is actually showing. A request that arrives while Bloom is
        // between pages is dropped, and asking again during the same gap just prolongs it.
        if (frame && Date.now() >= nextAsk) {
            await apiPost("pageList/pageClicked", { pageId, detail: "" });
            nextAsk = Date.now() + 5000;
        }
        const shownId = await frame
            ?.evaluate(() => {
                const doc = (globalThis as any).document;
                return doc.querySelector(".bloom-page")?.id ?? "";
            })
            .catch(() => undefined);
        if (shownId === pageId) return;
        lastSeen = shownId ?? "no page in the iframe";
        await pause(250);
    }
    throw new Error(
        `Page ${pageId} never appeared in the edit iframe; the last thing seen there was ` +
            `"${lastSeen}". Bloom's window has these frames:\n  ${describeFrames()}`,
    );
}

/** Wait for the tooling to have laid this page's grid out, and say what it laid it out for. */
async function expectLaidOut(): Promise<void> {
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        const frame = await getEditFrame();
        const signature: string | null = await frame.evaluate(() => {
            const doc = (globalThis as any).document;
            return (
                doc
                    .querySelector(".bloom-page .bloom-table")
                    ?.getAttribute("data-calendar-laid-out") ?? null
            );
        });
        if (signature) {
            // The year and the first day of the week, in the shape calendarLayoutSignature
            // (layOutCalendarMonthPage.ts) builds.
            expect(signature).toBe(`${kYearBeingChecked()},${kFirstDayOfWeek}`);
            return;
        }
        await pause(250);
    }
    throw new Error("The calendar tooling never laid this month grid out");
}

// The second book is for the following year; every check of a laid-out page uses whichever
// year the book under test was set up for.
let yearOfBookUnderTest = kYear;
function kYearBeingChecked(): number {
    return yearOfBookUnderTest;
}

/** The day number shown in each of the forty-two cells, blank for a cell with no day. */
async function getDayNumbers(): Promise<string[]> {
    const frame = await getEditFrame();
    return frame.evaluate(() => {
        const doc = (globalThis as any).document;
        return Array.from(doc.querySelectorAll(".calendarDayCell")).map(
            (cell: any) =>
                cell.querySelector(".calendarDayNumber")?.textContent?.trim() ??
                "",
        );
    });
}

/** What getDayNumbers should return for a month of `days` days after `leading` blank cells. */
function blanksThen(leading: number, days: number): string[] {
    const cells = new Array(42).fill("");
    for (let day = 1; day <= days; day++)
        cells[leading + day - 1] = String(day);
    return cells;
}

async function getUnusedDayCellCount(): Promise<number> {
    const frame = await getEditFrame();
    return frame.evaluate(
        () =>
            (globalThis as any).document.querySelectorAll(
                ".calendarDayCell.calendarUnusedDay",
            ).length,
    );
}

async function getYearShown(): Promise<string> {
    const frame = await getEditFrame();
    return frame.evaluate(
        () =>
            (globalThis as any).document
                .querySelector("[data-book='calendarYear']")
                ?.textContent?.trim() ?? "",
    );
}

/** The month name in the book's own language, which ships empty for the user to type. */
async function getMonthNameTyped(): Promise<string> {
    const frame = await getEditFrame();
    return frame.evaluate(
        () =>
            (globalThis as any).document
                .querySelector(
                    ".calendarMonthName .bloom-editable.bloom-content1",
                )
                ?.textContent?.trim() ?? "",
    );
}

/** The month names the template seeds, one per seed language. */
async function getMonthNameSeeds(): Promise<string[]> {
    const frame = await getEditFrame();
    return frame.evaluate(() => {
        const doc = (globalThis as any).document;
        return Array.from(
            doc.querySelectorAll(
                ".calendarMonthName .bloom-editable:not(.bloom-content1)",
            ),
        ).map((e: any) => e.textContent?.trim() ?? "");
    });
}

async function getWeekdayNameTyped(weekday: number): Promise<string> {
    const frame = await getEditFrame();
    return frame.evaluate(
        (day) =>
            (globalThis as any).document
                .querySelector(
                    `[data-calendar-weekday="${day}"] .bloom-editable.bloom-content1`,
                )
                ?.textContent?.trim() ?? "",
        weekday,
    );
}

/**
 * Type into one of the page's own-language slots the way the user does, through CKEditor.
 *
 * The click goes on the cell, not the slot. An empty bloom-editable is zero pixels wide, so
 * Playwright refuses to click it, while the cell around it is the full width of the column;
 * that cell is what the user's pointer lands on too.
 */
async function typeInto(selector: string, text: string): Promise<void> {
    const frame = await getEditFrame();
    const editable = frame.locator(selector);
    const cell = frame
        .locator(selector)
        .locator("xpath=ancestor::*[contains(@class,'bloom-cell')][1]");
    await cell.scrollIntoViewIfNeeded().catch(() => undefined);
    // Bloom's source-text bubble sits over a cell whose slot is empty and can swallow the
    // click, so the click is a best effort and the focus below is what the typing relies on.
    await cell.click({ timeout: 5000 }).catch(() => undefined);
    await editable.evaluate((element: any) => element.focus());
    await appPage.keyboard.press("Control+A");
    await appPage.keyboard.type(text);
    // Leave the field so CKEditor writes what we typed into the DOM.
    await frame.locator(".bloom-page").click({ position: { x: 2, y: 2 } });
    await expect(editable).toHaveText(text);
}

async function typeWeekdayName(weekday: number, text: string): Promise<void> {
    await typeInto(
        `[data-calendar-weekday="${weekday}"] .bloom-editable.bloom-content1`,
        text,
    );
}

async function typeMonthName(text: string): Promise<void> {
    await typeInto(".calendarMonthName .bloom-editable.bloom-content1", text);
}

// --- The setup dialog -----------------------------------------------------------------

/** Wait for the Calendar Setup dialog and return its year box. */
async function waitForSetupDialog() {
    const year = appPage.locator("#calendar-setup-year");
    await year.waitFor({ state: "visible", timeout: 120000 });
    return year;
}

/** Which day of the week the dialog is offering, without changing it. */
async function getSetupDialogFirstDayOfWeek(): Promise<number> {
    await waitForSetupDialog();
    return appPage.evaluate(() => {
        const doc = (globalThis as any).document;
        // MUI's select keeps the chosen value in a hidden input beside the visible button.
        const input = doc.querySelector(
            "input[name='calendar-setup-first-day-of-week'], #calendar-setup-first-day-of-week ~ input",
        );
        return parseInt(input?.value ?? "-1", 10);
    });
}

/** Fill the Calendar Setup dialog in and click OK. */
async function answerSetupDialog(
    year: number,
    firstDayOfWeek: number,
): Promise<void> {
    yearOfBookUnderTest = year;
    const yearBox = await waitForSetupDialog();
    await yearBox.fill(String(year));
    // MUI renders a select as a button that opens a list in a portal; click it, wait for the
    // list, then click the day we want.
    await appPage.locator("#calendar-setup-first-day-of-week").click();
    const dayList = appPage.locator("[role='listbox']");
    await dayList.waitFor({ state: "visible", timeout: 30000 });
    await dayList
        .locator(`[data-value="${firstDayOfWeek}"]`)
        .click({ timeout: 30000 });
    await appPage.getByRole("button", { name: "OK" }).click();
    await yearBox.waitFor({ state: "detached", timeout: 30000 });
}

/**
 * Close the calendar book and open it again, so the tooling treats every page as being opened
 * for the first time in a session.
 */
async function reopenTheCalendarBook(): Promise<void> {
    const calendarBook = await findMostRecentCalendarBookInTheCollection();
    const otherBook = await findAnyOtherBookInTheCollection(
        calendarBook.folderPath,
    );
    // The human order, one step fully done before the next: leave the Edit tab, change the
    // selection from the Collections tab, then enter the Edit tab. Changing the selection
    // while the Edit tab is open on another book leaves the edit view showing a book the
    // model has moved away from; the next page request then fails a null guard in
    // EditingModel.GetEditPageIframeContents, which on a Debug build kills Bloom. Leaving
    // the Edit tab also saves the open page, and the tab change completes only after that
    // save, so waiting for the Collections tab is what makes the selection change safe.
    await selectTabAndWait("collection");
    await apiPost(
        `collections/selected-book?path=${encodeURIComponent(otherBook.folderPath)}`,
    );
    await selectTabAndWait("edit");
    await getEditFrame(180000);
    await selectTabAndWait("collection");
    await apiPost(
        `collections/selected-book?path=${encodeURIComponent(calendarBook.folderPath)}`,
    );
    await selectTabAndWait("edit");
    await getEditFrame(180000);
}

/** Ask for a workspace tab and wait until Bloom says it is the active one. */
async function selectTabAndWait(tab: "collection" | "edit"): Promise<void> {
    await apiPost("workspace/selectTab", { tab });
    const deadline = Date.now() + 180000;
    while (Date.now() < deadline) {
        const tabs = (await apiGet("workspace/tabs")) as {
            tabStates?: Record<string, string>;
        };
        if (tabs.tabStates?.[tab] === "active") return;
        await new Promise((resolve) => setTimeout(resolve, 250));
    }
    throw new Error(`Bloom never made ${tab} the active tab`);
}

/** The books of the collection we are working in. */
async function getEditableCollectionBooks(): Promise<
    Array<{ folderName: string; folderPath: string }>
> {
    return (await apiGet("collections/books")) as Array<{
        folderName: string;
        folderPath: string;
    }>;
}

async function findMostRecentCalendarBookInTheCollection(): Promise<{
    folderPath: string;
}> {
    const books = await getEditableCollectionBooks();
    const calendars = books.filter((b) =>
        fs.existsSync(Path.join(b.folderPath, `${b.folderName}.htm`))
            ? fs
                  .readFileSync(
                      Path.join(b.folderPath, `${b.folderName}.htm`),
                      "utf8",
                  )
                  .includes('content="calendar"')
            : false,
    );
    expect(
        calendars.length,
        "there should be a calendar book in the collection by now",
    ).toBeGreaterThan(0);
    return calendars[calendars.length - 1];
}

async function findAnyOtherBookInTheCollection(
    notThisFolderPath: string,
): Promise<{ folderPath: string }> {
    const books = await getEditableCollectionBooks();
    const other = books.find((b) => b.folderPath !== notThisFolderPath);
    expect(
        other,
        "the test collection needs a second book so we can close the calendar one",
    ).toBeTruthy();
    return other!;
}

// --- The collection's configuration.txt ------------------------------------------------

interface IConfiguredCalendarSettings {
    monthNames: string[];
    dayNames: string[];
    firstDayOfWeek: number | null;
}

function configurationFilePath(): string {
    return Path.join(
        bloom.tempCollectionsRoot,
        kCollectionName,
        "configuration.txt",
    );
}

function readCalendarSettings(): IConfiguredCalendarSettings {
    const path = configurationFilePath();
    expect(
        fs.existsSync(path),
        `Bloom should have written the calendar settings to ${path}`,
    ).toBe(true);
    const root = JSON.parse(fs.readFileSync(path, "utf8"));
    const calendar = root.library?.calendar ?? root.calendar;
    expect(calendar, `${path} has no calendar section`).toBeTruthy();
    return {
        monthNames: calendar.monthNames ?? [],
        dayNames: calendar.dayNames ?? [],
        firstDayOfWeek: calendar.firstDayOfWeek ?? null,
    };
}

function writeCalendarSettings(settings: IConfiguredCalendarSettings): void {
    const path = configurationFilePath();
    const root = JSON.parse(fs.readFileSync(path, "utf8"));
    if (root.library?.calendar) root.library.calendar = settings;
    else root.calendar = settings;
    fs.writeFileSync(path, JSON.stringify(root, null, 2));
}

function readConfiguredDayNames(): string[] {
    return readCalendarSettings().dayNames;
}

function readConfiguredMonthNames(): string[] {
    return readCalendarSettings().monthNames;
}

function pause(milliseconds: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
