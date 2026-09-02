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
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiGet, apiGetJson, apiPost } from "./api";
import { selectBook, waitForCollectionReady } from "./collection";
import { switchTab } from "./workspace";

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
    const { collectionId, template } = await findFactoryTemplate(
        page,
        templateTitle,
    );
    await apiPost(
        page,
        `collections/selected-book?path=${encodeURIComponent(template.folderPath)}` +
            `&collection-id=${encodeURIComponent(collectionId)}`,
    );
    return makeBookFromSelectedBook(page, templateTitle);
}

/**
 * The "Templates" source collection's entry for one factory template, e.g. "Basic Book", with the
 * id of that collection. Throws, listing what Bloom does offer, when there is no such template.
 */
async function findFactoryTemplate(
    page: Page,
    templateTitle: string,
): Promise<{ collectionId: string; template: IBookInfo }> {
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
    return { collectionId: templates.id, template };
}

/**
 * Make a new book from a book that is already in the editable collection, and return the new
 * book's folder. Bloom lands in the Edit tab, showing the new book's cover.
 *
 * For a template book in the collection this is the collection tab's "Make a book using this
 * template". The same command derives a book from any book Bloom has selected, which is how a test
 * stands in for "make a derivative of a book someone else made": the book is in this collection,
 * but whatever it was made from need not be.
 */
export async function makeBookFromBookInCollection(
    page: Page,
    bookFolder: string,
): Promise<string> {
    // A person does this from the Collection tab. Asked from the Edit tab, Bloom makes the book
    // and reports it selected, but the Edit tab never loads a page of it.
    await switchTab(page, "collection");
    await selectBook(page, bookFolder);
    return makeBookFromSelectedBook(page, Path.basename(bookFolder));
}

/**
 * Ask Bloom to make a book from whatever book is selected, and wait for the new book to exist.
 * `sourceName` only names the source in the failure message.
 */
async function makeBookFromSelectedBook(
    page: Page,
    sourceName: string,
): Promise<string> {
    const before = await listEditableBooks(page);
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
                message: `Bloom never added a book made from "${sourceName}".`,
            },
        )
        .toBe(true);
    await waitForEditablePage(page);
    return made!.folderPath;
}

/**
 * The folder of one of Bloom's factory templates, e.g. "Basic Book", as installed with this Bloom.
 * A test reads the template's files from here to check the dialog against the disk, or to show
 * that Bloom left them alone.
 */
export async function getFactoryTemplateFolder(
    page: Page,
    templateTitle: string,
): Promise<string> {
    const { template } = await findFactoryTemplate(page, templateTitle);
    return Path.normalize(template.folderPath);
}

/**
 * The labels of the pages a factory template offers to the Add Page dialog, in the template's own
 * order, read from the template's HTML file on disk. This is the dialog's oracle: it comes from
 * the file, not from Bloom's server or from the dialog.
 *
 * A template page is a .bloom-page with an id and data-page="extra"; its label is its .pageLabel
 * text. That is the same filter the dialog applies (see TemplateBookPages.tsx). Pages that a
 * template restricts to one orientation are left out, because which of those the dialog shows
 * depends on the book's layout, so a template with such a page needs this helper taught about it.
 */
export async function getFactoryTemplatePageLabels(
    page: Page,
    templateTitle: string,
): Promise<string[]> {
    const folder = await getFactoryTemplateFolder(page, templateTitle);
    const htmlFiles = fs
        .readdirSync(folder)
        .filter((name) => /\.html?$/i.test(name));
    if (htmlFiles.length !== 1)
        throw new Error(
            `Expected one HTML file in ${folder}, found: ${htmlFiles.join(", ") || "(none)"}.`,
        );
    const html = fs.readFileSync(Path.join(folder, htmlFiles[0]), "utf8");
    // Parse in the browser: Node has no HTML parser, and the page's DOMParser is the same one the
    // dialog uses to read the template.
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return Array.from(
            dom.querySelectorAll('.bloom-page[id][data-page="extra"]'),
        )
            .filter((p) => !p.hasAttribute("data-initial-page-orientation"))
            .map(
                (p) => p.querySelector(".pageLabel")?.textContent?.trim() ?? "",
            );
    }, html);
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
    timeoutMs = 30000,
): Promise<string> {
    // The collection learns a new title when the book is saved, a moment after the page it was
    // typed on is left, so keep asking for a while before giving up.
    let books: IBookInfo[] = [];
    let book: IBookInfo | undefined;
    try {
        await expect
            .poll(
                async () => {
                    books = await listEditableBooks(page);
                    book = books.find((b) => b.title === title);
                    return !!book;
                },
                { timeout: timeoutMs },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `The collection has no book called "${title}". It has: ` +
                books.map((b) => `"${b.title}"`).join(", ") +
                ".",
        );
    }
    return book!.folderPath;
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
    // Each change makes Bloom reload the page, so this is a page-changing request and must not
    // arrive while the Edit tab is still loading one. See waitForEditTabSettled.
    await waitForEditTabSettled(page);
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
    await waitForEditTabSettled(page);
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
 * Wait until the Edit tab is showing a page, and Bloom has finished loading that page and is
 * editing it. Not every page has a text box (the "Image For Thumbnail" page a new template starts
 * on has none), so this waits for the page itself; a helper that types waits for its own box.
 *
 * The second half matters: while the Edit tab is still navigating to a page, Bloom silently ignores
 * any command that begins with saving it (add a page, duplicate, delete, jump elsewhere). The page
 * can look ready in the DOM a moment before Bloom is, so this asks Bloom as well, through the e2e
 * hook that reports its editing state.
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
                    .locator(".bloom-page")
                    .count()
                    .catch(() => 0);
            },
            {
                timeout: timeoutMs,
                message: "The Edit tab never showed a page.",
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

/** What e2e/editState replies with: what the Edit tab is doing. */
interface IEditTabState {
    /** A name from Bloom's State enum: NoPage, Navigating, Editing, SavePending, SavedAndStripped. */
    state: string;
    /** The page that state is about, or "". */
    pageId: string;
    /** Whether the Edit tab is the tab being shown. */
    visible: boolean;
    /** How many times the page now shown has told Bloom its DOM had loaded. */
    pageLoadAnnouncements: number;
}

/**
 * Wait until the Edit tab is settled on a page: showing it, in the Editing state, and done
 * announcing that the page has loaded.
 *
 * Ask this before any request that changes the page. The Edit tab refuses such a request while it
 * navigates, and a page announces itself to Bloom more than once, so a request that arrives
 * between two of those announcements starts a save that the second announcement leaves unanswered
 * (AUTOMATION-DEBT.md: "A page change asked for while the Edit tab is still loading a page can be
 * lost"). Waiting for
 * the Editing state alone is not enough. Hence the second reading: the tab is settled once the
 * count of announcements has stopped rising.
 *
 * The DOM is no help here. Bloom leaves the previous page in the frame while it loads the next
 * one, so a test looking at the frame sees a settled Edit tab throughout.
 */
export async function waitForEditTabSettled(
    page: Page,
    timeoutMs = 90000,
): Promise<void> {
    const editState = () => apiGetJson<IEditTabState>(page, "e2e/editState");
    await expect
        .poll(
            async () => {
                const first = await editState();
                if (!first.visible || first.state !== "Editing")
                    return first.state;
                // Long enough to cover the gap between two announcements of one page load, which
                // has been seen to reach a second.
                await page.waitForTimeout(1500);
                const second = await editState();
                if (
                    second.state !== "Editing" ||
                    second.pageId !== first.pageId
                )
                    return second.state;
                if (
                    second.pageLoadAnnouncements !== first.pageLoadAnnouncements
                )
                    return "Navigating";
                return "Editing";
            },
            {
                timeout: timeoutMs,
                message:
                    "The Edit tab never settled on a page, so a request to change pages could be lost.",
            },
        )
        .toBe("Editing");
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
    /** The label the Add Page dialog shows for the thumbnail, e.g. "Basic Text & Image". */
    label: string;
    /** The template book that holds the page. The addPage API needs it as well as the id. */
    templateBookPath: string;
    /** The title of that template book, which is the group heading in the Add Page dialog. */
    templateBookTitle: string;
}

/**
 * Add content pages to the selected book, by the same route as the Add Page dialog: pick the
 * template page with this label, then insert it `times` times.
 *
 * The pages on offer are the ones the dialog would show, in its order: the book's own template
 * first, then Basic Book and the other installed templates. The first page with the label wins,
 * so a label the book's own template has always comes from there; pass `templateBookTitle` to
 * insist on a particular template, e.g. "Basic Book" for a fresh template book of your own, which
 * has no pages of its own yet.
 *
 * A book made from a template starts with front and back matter only, because every page of a
 * template book is a template page. So a test that needs a content page adds one.
 */
export async function addPage(
    page: Page,
    templatePageLabel: string,
    times = 1,
    templateBookTitle?: string,
): Promise<void> {
    const templates = await apiGetJson<ITemplatePage[]>(
        page,
        "e2e/templatePages",
    );
    const template = templates.find(
        (t) =>
            t.label === templatePageLabel &&
            (!templateBookTitle || t.templateBookTitle === templateBookTitle),
    );
    if (!template)
        throw new Error(
            `No template offers a page called "${templatePageLabel}"` +
                (templateBookTitle ? ` in "${templateBookTitle}"` : "") +
                `. On offer: ` +
                templates
                    .map((t) => `${t.templateBookTitle}: ${t.label}`)
                    .join(", ") +
                ".",
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
    // Bloom answers this request 200 even when it drops the add because the Edit tab was not yet
    // ready for one (see AUTOMATION-DEBT.md), so the page count is the only honest confirmation.
    await expect
        .poll(async () => (await getPages(page)).length, {
            timeout: 60000,
            message: `Bloom never added the "${templatePageLabel}" page(s).`,
        })
        .toBe(before + times);
    await waitForEditTabSettled(page);
}

/**
 * Show a page in the Edit tab. This is also how a test SAVES what it typed: Bloom writes the page
 * it is leaving, so text typed into a box reaches the file only once the book moves off that page.
 */
export async function goToPage(page: Page, pageId: string): Promise<void> {
    // Wait for the Edit tab first. A jump asked for while it is still loading a page is refused,
    // and one asked for between two announcements of one page load wedges it (see
    // waitForEditTabSettled). Asking at a moment when the tab acts on the jump at once avoids
    // both.
    // Then ask, and ask again if the tab was busy after all. The tab answers with an error when
    // it cannot jump (EditingModel.JumpToPage), rather than dropping the jump and reporting
    // success as it used to, so a refusal is visible here instead of turning into a 60-second
    // wait for a page that is not coming. Three tries, because the wait above closes the window
    // for a refusal without quite shutting it: the tab can start loading the page list in the
    // moment between the wait finishing and this request arriving.
    let refusal: unknown;
    for (let attempt = 0; attempt < 3; attempt++) {
        if (attempt > 0) await waitForEditTabSettled(page);
        refusal = undefined;
        await apiPost(page, "editView/jumpToPage", pageId, "text/plain").catch(
            (error) => {
                refusal = error;
            },
        );
        if (refusal === undefined) break;
    }
    if (refusal !== undefined)
        throw new Error(
            `The Edit tab refused to show page ${pageId} three times running: ${refusal}`,
        );
    const showing = async () =>
        (await page
            .frame({ name: "page" })
            ?.locator(`.bloom-page[id="${pageId}"]`)
            .count()
            .catch(() => 0)) ?? 0;
    try {
        await expect.poll(showing, { timeout: 60000 }).toBe(1);
    } catch {
        // Say what the frame does hold. "Bloom never showed the page" on its own cannot tell a
        // page that never arrived from a page that arrived and was replaced by another one.
        const frame = page.frame({ name: "page" });
        const heldPageIds = frame
            ? await frame
                  .locator(".bloom-page")
                  .evaluateAll((pages) => pages.map((p) => p.id))
                  .catch(() => ["(could not read the frame)"])
            : [];
        // Read this the forgiving way. A shell document Bloom is not driving is one of the
        // failures this message exists to explain, and in exactly that case the request can
        // fail. Letting it throw here would replace the whole diagnostic with its own error.
        const drivenShell = await apiGet(page, "e2e/shellUrl")
            .then((response) => response.body)
            .catch((error) => `(could not be read: ${error})`);
        // The state the Edit tab is stuck in says which of the two failures this is: a jump that
        // wedged the tab leaves it in SavePending or Navigating on the page it already had.
        const editState = await apiGet(page, "e2e/editState")
            .then((response) => response.body)
            .catch((error) => `(could not be read: ${error})`);
        throw new Error(
            `Bloom never showed page ${pageId} in the Edit tab. ` +
                `The Edit tab is at ${editState}. ` +
                (frame
                    ? `The 'page' frame is at ${frame.url()} and holds [${heldPageIds.join(", ")}].`
                    : `There is no 'page' frame.`) +
                ` Bloom drives the shell at ${drivenShell}; ` +
                `this test is watching ${page.url()}. If those name different documents, the test ` +
                `attached to the wrong one and nothing Bloom does will ever appear (see AUTOMATION-DEBT.md).`,
        );
    }
    // The page being in the frame is not the whole of arriving: Bloom is still setting it up, and
    // it re-navigates the page list too. Leave the Edit tab settled, so whatever the test does
    // next is not working against a frame that is about to be replaced.
    await waitForEditTabSettled(page);
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
    // One insertion rather than a key press per character: the box has focus, and CKEditor and
    // Bloom's own markup code both work from the input event this raises, so the result is the
    // same and the cost does not grow with the length of the text.
    //
    // What this does NOT do is raise keydown, keypress or keyup. So a test that types here does
    // not exercise anything in Bloom that listens for a key rather than for input, and the
    // assertion below cannot tell the difference. A test whose subject IS a key press needs a
    // helper of its own that presses that key. (AUTOMATION-DEBT.md: "Typing in a text box raises
    // no key events".)
    if (text) await page.keyboard.insertText(text);
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
