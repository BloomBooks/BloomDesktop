// Make a book and put text in it, so a test can build the book it needs instead of shipping one.
//
// A test that carries its own prepared book inherits somebody else's assumptions: the next person
// to edit that book changes what the test means, without reading it. So the default is that a test
// creates its own collection (see ILaunchBloomOptions.collectionSpec) and makes its own book here.
//
// Making the book is setup, not the behavior any of these tests measures, so this takes the fast
// reliable path through Bloom's API wherever there is one, per the UI-vs-API policy in README.md.
// The text itself goes in through the real editor, because there is no API that writes it.

import { expect, type Frame, type Page } from "@playwright/test";
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
    const collections = await apiGetJson<ICollectionInfo[]>(
        page,
        "collections/list",
    );
    const templates = collections.find((c) => c.name === "Templates");
    if (!templates)
        throw new Error(
            `Bloom is not showing a "Templates" source collection, so there is no ` +
                `"${templateTitle}" to make a book from. Collections: ` +
                collections.map((c) => c.name).join(", "),
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

/** Wait until the Edit tab is showing a page with at least one editable box in it. */
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
 * Ask this before any request that changes the page. The Edit tab defers such a request while it
 * navigates, and a deferred one is lost (AUTOMATION-DEBT.md: "A page change asked for while the
 * Edit tab is still loading a page can be lost"). A page announces itself to Bloom more than once,
 * and a request that arrives between two of those announcements is lost as well, so waiting for
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
    await waitForEditTabSettled(page);
}

/**
 * Show a page in the Edit tab. This is also how a test SAVES what it typed: Bloom writes the page
 * it is leaving, so text typed into a box reaches the file only once the book moves off that page.
 */
export async function goToPage(page: Page, pageId: string): Promise<void> {
    // Wait for the Edit tab first. It queues a jump that arrives while it is loading a page, and
    // one of those queued jumps is lost (see waitForEditTabSettled). Asking at a moment when the
    // tab acts on the jump at once avoids the whole queue.
    await waitForEditTabSettled(page);
    // Then one request is enough: the tab answers with an error if it can neither jump nor queue
    // (EditingModel.JumpToPage). It used to drop such a jump and report success, which is why this
    // helper used to ask three times.
    await apiPost(page, "editView/jumpToPage", pageId, "text/plain");
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
        // was lost leaves the tab in SavePending or Navigating on the page it already had.
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
}

/**
 * Duplicate the page Bloom is showing, `times` times, and wait for the new pages to appear.
 * Used to give a book more than one content page without driving the Add Page dialog.
 */
export async function duplicateCurrentPage(
    page: Page,
    times = 1,
): Promise<void> {
    const before = (await getPages(page)).length;
    await apiPost(
        page,
        "editView/duplicatePageMany",
        JSON.stringify({ numberOfTimes: times }),
        "application/json",
    );
    await expect
        .poll(async () => (await getPages(page)).length, {
            timeout: 60000,
            message: "Bloom never added the duplicated page(s).",
        })
        .toBe(before + times);
    await waitForEditTabSettled(page);
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
    const box = editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first();
    await box.waitFor({ state: "visible", timeout: 30000 });
    // Click in, select what is there, and type over it. A box here is a CKEditor surface, and
    // filling it directly leaves part of the old text behind.
    await box.click();
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
