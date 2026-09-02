// Make a book and put text in it, so a test can build the book it needs instead of shipping one.
//
// A test that carries its own prepared book inherits somebody else's assumptions: the next person
// to edit that book changes what the test means, without reading it. So the default is that a test
// creates its own collection (see ILaunchBloomOptions.collectionSpec) and makes its own book here.
//
// Making the book is setup, not the behavior any of these tests measures, so this takes the fast
// reliable path through Bloom's API wherever there is one, per the UI-vs-API policy in README.md.
// The text itself goes in through the real editor, because there is no API that writes it.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGet, apiGetJson, apiPost } from "./api";
import { waitForCollectionReady } from "./collection";

/** One book as collections/books reports it. Only the fields used here. */
interface IBookInfo {
    title: string;
    folderPath: string;
}

/** One source collection as collections/list reports it. Only the fields used here. */
interface ICollectionInfo {
    id: string;
    name: string;
}

/** What editView/topBar/contentLanguageUsage replies with. */
interface IContentLanguageUsage {
    languages: {
        id: string;
        label: string;
        isUsedForContent: boolean;
    }[];
}

/**
 * Make a new book in the editable collection from one of Bloom's factory templates, e.g.
 * "Basic Book", and return its folder. Bloom lands in the Edit tab, showing the new book's cover.
 *
 * This is the same action as selecting the template in Sources For New Books and clicking
 * MAKE A BOOK USING THIS SOURCE.
 */
export async function makeBookFromTemplate(
    page: Page,
    templateTitle: string,
): Promise<string> {
    await waitForCollectionReady(page);
    // collections/list leaves out every source collection whose books have not been read yet,
    // and Bloom reads the factory templates in the background after the collection opens. So
    // wait for "Templates" to be listed rather than reading the list once.
    let collections: ICollectionInfo[] = [];
    let templates: ICollectionInfo | undefined;
    try {
        // The message is built after the wait fails, so that it can name what WAS listed. A
        // message passed to expect.poll would be built before the first poll, when nothing is.
        await expect
            .poll(
                async () => {
                    collections = await apiGetJson<ICollectionInfo[]>(
                        page,
                        "collections/list",
                    );
                    templates = collections.find((c) => c.name === "Templates");
                    return !!templates;
                },
                { timeout: 60000 },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `Bloom never listed a "Templates" source collection, so there is no ` +
                `"${templateTitle}" to make a book from. Collections: ` +
                collections.map((c) => c.name).join(", "),
        );
    }
    if (!templates)
        throw new Error(
            `Bloom listed "Templates" a moment ago and then lost it.`,
        );
    const books = await apiGetJson<IBookInfo[]>(
        page,
        `collections/books?collection-id=${encodeURIComponent(templates.id)}`,
    );
    const template = books.find((b) => b.title === templateTitle);
    if (!template)
        throw new Error(
            `There is no factory template called "${templateTitle}". ` +
                `Templates: ${books.map((b) => b.title).join(", ")}.`,
        );

    const before = await listEditableBooks(page);
    await apiPost(
        page,
        `collections/selected-book?path=${encodeURIComponent(template.folderPath)}` +
            `&collection-id=${encodeURIComponent(templates.id)}`,
    );
    await apiPost(page, "app/makeFromSelectedBook");

    // Bloom makes the book, selects it, and switches to the Edit tab. Wait for the book to exist
    // rather than for the tab, so the folder we return is real.
    let made: IBookInfo | undefined;
    await expect
        .poll(
            async () => {
                made = (await listEditableBooks(page)).find(
                    (b) =>
                        !before.some((old) => old.folderPath === b.folderPath),
                );
                return !!made;
            },
            {
                timeout: 90000,
                message: `Bloom never added a book made from "${templateTitle}".`,
            },
        )
        .toBe(true);
    await waitForEditablePage(page);
    return made!.folderPath;
}

/**
 * The folder of the book with this title, in the collection Bloom has open.
 *
 * A test cannot hold on to the folder it got when it made the book: Bloom renames a book's folder
 * to match its title, so the folder changes the first time the book is saved with a new title.
 */
export async function findBookFolder(
    page: Page,
    title: string,
): Promise<string> {
    const books = await listEditableBooks(page);
    const book = books.find((b) => b.title === title);
    if (!book)
        throw new Error(
            `The collection has no book called "${title}". It has: ` +
                books.map((b) => `"${b.title}"`).join(", ") +
                ".",
        );
    return book.folderPath;
}

/** The books in the collection Bloom has open. */
async function listEditableBooks(page: Page): Promise<IBookInfo[]> {
    const name = (
        await apiGet(page, "collections/getCurrentEditableCollectionName")
    ).body;
    const collections = await apiGetJson<ICollectionInfo[]>(
        page,
        "collections/list",
    );
    const editable = collections.find((c) => c.name === name) ?? collections[0];
    return apiGetJson<IBookInfo[]>(
        page,
        `collections/books?collection-id=${encodeURIComponent(editable.id)}`,
    );
}

/**
 * Set exactly which of the collection's languages the book shows, by the same route as the Edit
 * tab's One/Two/Three Languages dropdown. Language 1 is always shown and cannot be turned off.
 *
 * Each change is confirmed before the next is sent. Sending two in quick succession has been seen
 * to lose one, and a state wait is both the honest fix and faster than a fixed pause.
 */
export async function setContentLanguages(
    page: Page,
    tags: string[],
): Promise<void> {
    const usage = await apiGetJson<IContentLanguageUsage>(
        page,
        "editView/topBar/contentLanguageUsage",
    );
    for (const language of usage.languages) {
        const wanted = tags.includes(language.id);
        if (language.isUsedForContent === wanted) continue;
        await apiPost(
            page,
            "editView/topBar/contentLanguageUsageChange",
            JSON.stringify({
                languageTag: language.id,
                isUsedForContent: wanted,
            }),
            "application/json",
        );
        await expect
            .poll(
                async () =>
                    (
                        await apiGetJson<IContentLanguageUsage>(
                            page,
                            "editView/topBar/contentLanguageUsage",
                        )
                    ).languages.find((l) => l.id === language.id)
                        ?.isUsedForContent,
                {
                    timeout: 30000,
                    message: `Bloom never reported ${language.id} as ${wanted ? "shown" : "hidden"}.`,
                },
            )
            .toBe(wanted);
    }
    await waitForEditablePage(page);
}

/** The Edit tab's frame holding the page being edited. Throws if the Edit tab is not showing. */
export function editablePageFrame(page: Page): Frame {
    const frame = page.frame({ name: "page" });
    if (!frame)
        throw new Error(
            "There is no 'page' frame, so Bloom is not showing a page in the Edit tab. " +
                `Frames: ${page
                    .frames()
                    .map((f) => f.name() || "(main)")
                    .join(", ")}.`,
        );
    return frame;
}

/**
 * Wait until the Edit tab is showing a page with at least one editable box in it, and Bloom has
 * finished loading that page and is editing it.
 *
 * The second half matters: while the Edit tab is still navigating to a page, Bloom silently ignores
 * any command that begins with saving it (duplicate, delete, jump elsewhere). The page can look
 * ready in the DOM a moment before Bloom is, so this asks Bloom as well, through the e2e hook that
 * reports its editing state.
 */
export async function waitForEditablePage(
    page: Page,
    timeoutMs = 90000,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const frame = page.frame({ name: "page" });
                if (!frame) return 0;
                return frame
                    .locator(".bloom-editable")
                    .count()
                    .catch(() => 0);
            },
            {
                timeout: timeoutMs,
                message: "The Edit tab never showed a page with editable text.",
            },
        )
        .toBeGreaterThan(0);
    await expect
        .poll(async () => (await apiGet(page, "e2e/isEditingPage")).body, {
            timeout: timeoutMs,
            message:
                "Bloom never finished loading the page in the Edit tab (its editing state never became Editing).",
        })
        .toBe("true");
}

/** One page of the selected book, as e2e/pages reports it. */
export interface IBookPage {
    /** The page's id, which is what editView/jumpToPage takes. */
    id: string;
    /** The page list's caption for the page: its number, or its name for front and back matter. */
    caption: string;
    /** False for the cover, the credits page, and the rest of the front and back matter. */
    isContentPage: boolean;
}

/**
 * The pages of the selected book, in order.
 *
 * This asks Bloom rather than reading the page-list thumbnails, because the thumbnails do not say
 * which pages are front or back matter, and a test that guessed from their markup would break the
 * first time that markup changed.
 */
export async function getPages(page: Page): Promise<IBookPage[]> {
    return apiGetJson<IBookPage[]>(page, "e2e/pages");
}

/** The book's content pages: everything except the front and back matter. */
export async function getContentPages(page: Page): Promise<IBookPage[]> {
    return (await getPages(page)).filter((p) => p.isContentPage);
}

/** One template page the Add Page dialog offers, as e2e/templatePages reports it. */
interface ITemplatePage {
    id: string;
    /** The label the Add Page dialog shows under the thumbnail, e.g. "Basic Text & Picture". */
    label: string;
    /** The template book that holds the page. The addPage API needs it as well as the id. */
    templateBookPath: string;
}

/**
 * Add content pages to the selected book, by the same route as the Add Page dialog: pick the
 * template page with this label, then insert it `times` times.
 *
 * A book made from a template starts with front and back matter only, because every page of a
 * template book is a template page. So a test that needs a content page adds one.
 */
export async function addPage(
    page: Page,
    templatePageLabel: string,
    times = 1,
): Promise<void> {
    const templates = await apiGetJson<ITemplatePage[]>(
        page,
        "e2e/templatePages",
    );
    const template = templates.find((t) => t.label === templatePageLabel);
    if (!template)
        throw new Error(
            `This book's template offers no page called "${templatePageLabel}". ` +
                `It offers: ${templates.map((t) => t.label).join(", ")}.`,
        );
    const before = (await getPages(page)).length;
    await apiPost(
        page,
        "addPage",
        JSON.stringify({
            templateBookPath: template.templateBookPath,
            pageId: template.id,
            numberToAdd: times,
            // The Add Page dialog always sends these three, and the API reads all of them, so
            // leaving one out makes it throw. See PageChooserDialog.tsx.
            convertWholeBook: false,
            allowDataLoss: false,
            dataToolId: "",
        }),
        "application/json",
    );
    await expect
        .poll(async () => (await getPages(page)).length, {
            timeout: 60000,
            message: `Bloom never added the "${templatePageLabel}" page(s).`,
        })
        .toBe(before + times);
    await waitForEditablePage(page);
}

/**
 * Show a page in the Edit tab. This is also how a test SAVES what it typed: Bloom writes the page
 * it is leaving, so text typed into a box reaches the file only once the book moves off that page.
 */
export async function goToPage(page: Page, pageId: string): Promise<void> {
    // The Edit tab drops a jump that arrives while it is still loading a page, so wait for it to
    // be showing one before asking for another.
    await waitForEditablePage(page);

    // Ask up to three times. Coming back from the Publish tab, the Edit tab can still swallow a
    // jump after it looks ready, and asking again costs a few seconds where failing costs the run.
    const showing = async () =>
        (await page
            .frame({ name: "page" })
            ?.locator(`.bloom-page[id="${pageId}"]`)
            .count()
            .catch(() => 0)) ?? 0;
    for (let attempt = 1; attempt <= 3; attempt++) {
        await apiPost(page, "editView/jumpToPage", pageId, "text/plain");
        try {
            await expect.poll(showing, { timeout: 20000 }).toBe(1);
            await waitForEditablePage(page);
            return;
        } catch {
            // fall through and ask again
        }
    }
    throw new Error(
        `Bloom never showed page ${pageId} in the Edit tab, after three attempts.`,
    );
}

/**
 * Click in one language's box of one translation group on the page being shown, so that it has the
 * focus, the way a person starts editing it. `groupSelector` picks the group, e.g. ".bookTitle" for
 * the cover title. Waits until the box has the focus, and returns it.
 *
 * Focusing a box is also what makes Bloom show the box's format gear (see helpers/formatDialog.ts).
 */
export async function clickInGroup(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<Locator> {
    const box = editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first();
    await box.waitFor({ state: "visible", timeout: 30000 });
    await box.click();
    await expect(
        box,
        `Clicking in the "${languageTag}" box of "${groupSelector}" did not give it the focus.`,
    ).toBeFocused({ timeout: 15000 });
    return box;
}

/**
 * Type text into one language's box of one translation group on the page being shown, the way a
 * person does. `groupSelector` picks the group, e.g. ".bookTitle" for the cover title.
 *
 * Pass an empty string to clear the box; that is how a test makes a translation incomplete.
 * Nothing reaches the file until the book leaves this page — see goToPage.
 */
export async function typeInGroup(
    page: Page,
    groupSelector: string,
    languageTag: string,
    text: string,
): Promise<void> {
    // Click in, select what is there, and type over it. A box here is a CKEditor surface, and
    // filling it directly leaves part of the old text behind.
    const box = await clickInGroup(page, groupSelector, languageTag);
    await box.press("Control+a");
    await box.press("Delete");
    if (text) await box.pressSequentially(text);
    // Bloom's editor reacts to typing; confirm the box holds what we meant before moving on, so a
    // later failure cannot be blamed on text that never arrived.
    await expect(box).toHaveText(text, { timeout: 15000 });
}

/** One front or back matter page, as the Edit tab showed it. */
export interface IShownXmatterPage {
    /** The page list's caption for the page, e.g. "Front Cover". */
    caption: string;
    /**
     * Which front or back matter page this is, as the pack names it in data-xmatter-page:
     * "frontCover", "titlePage", "credits", "insideFrontCover", "insideBackCover",
     * "outsideBackCover", or a pack's own name such as "spConfigurationPage".
     */
    xmatterPage: string | null;
    /** The file names of the stylesheets the page was shown with, e.g. "Traditional-XMatter.css". */
    stylesheets: string[];
}

/**
 * Show every front and back matter page of the selected book in the Edit tab, in order, the way a
 * person flips through them, and report what was shown on each: which xmatter page it is and which
 * stylesheets Bloom gave it. The Edit tab must be showing.
 *
 * This is how a test checks which front/back matter pack a book has: the pack decides which pages
 * exist and in what order, and it puts its own <Pack>-XMatter.css on every page.
 */
export async function visitXmatterPages(
    page: Page,
): Promise<IShownXmatterPage[]> {
    const xmatterPages = (await getPages(page)).filter((p) => !p.isContentPage);
    if (xmatterPages.length === 0)
        throw new Error(
            "The selected book has no front or back matter pages at all.",
        );
    const shown: IShownXmatterPage[] = [];
    for (const xmatterPage of xmatterPages) {
        await goToPage(page, xmatterPage.id);
        const frame = editablePageFrame(page);
        const kind = await frame
            .locator(`.bloom-page[id="${xmatterPage.id}"]`)
            .getAttribute("data-xmatter-page");
        const hrefs = await frame
            .locator('link[rel="stylesheet"]')
            .evaluateAll((links) =>
                links.map((link) => link.getAttribute("href") ?? ""),
            );
        shown.push({
            caption: xmatterPage.caption,
            xmatterPage: kind,
            // Bloom serves a stylesheet by a path with a cache-busting query; keep the file name.
            stylesheets: hrefs.map((href) =>
                decodeURIComponent(href.split("?")[0].split("/").pop() ?? ""),
            ),
        });
    }
    return shown;
}
