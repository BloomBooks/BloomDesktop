// A book can name a front-end module that looks after it while it is being edited.
//
// The book's head carries `<meta name="bookTooling" content="<name>">`, put there by the
// template it was made from. A module registers itself here under that name and is then told
// when the book is opened, when each page is opened, and when each page is about to be saved.
// The Wall Calendar's month grids are the first thing built this way; the mechanism itself
// knows nothing about calendars.
//
// This module lives in the edit view's outer frame, not in the page iframe, because the page
// iframe is thrown away and rebuilt for every page: anything a tooling module has to remember
// from one page to the next has to be held out here.
//
// Reading the whole book is allowed. WRITING is only ever to the page the user is looking at,
// which Bloom then saves the way it saves any other edit. Nothing here writes to the book file.

import { getAsync } from "../../utils/bloomApi";

/** The name of the meta a book uses to say which tooling module looks after it. */
export const kBookToolingMetaName = "bookTooling";

/** What a tooling module can be told. Every hook is optional. */
export interface IBookTooling {
    /**
     * The user has opened a book this module looks after. `bookDom` is a read-only copy of the
     * book's own .htm, or undefined when it could not be fetched; a module that needs
     * book-level values it cannot get from the page should read them through an API instead of
     * relying on it. Awaited before the first page's onPageOpened.
     */
    onBookOpened?(bookDom: Document | undefined): Promise<void> | void;

    /**
     * A page of the book has finished loading in the edit iframe. `pageElement` is that
     * page's div.bloom-page, live in the iframe's document, so changes made to it are what
     * the user sees and what Bloom saves.
     */
    onPageOpened?(pageElement: HTMLElement): Promise<void> | void;

    /**
     * The page is about to be serialized and sent to C# to be saved. Called synchronously,
     * because the page is serialized as soon as this returns.
     */
    onPageSaved?(pageElement: HTMLElement): void;
}

const registeredTooling = new Map<string, IBookTooling>();

/**
 * Say which module looks after books whose bookTooling meta holds `name`. Call this at module
 * scope from a file the outer frame's bundle imports.
 */
export function registerBookTooling(name: string, tooling: IBookTooling): void {
    if (registeredTooling.has(name)) {
        throw new Error(
            `Two modules are registered as the book tooling "${name}"`,
        );
    }
    registeredTooling.set(name, tooling);
}

/** The module that looks after the book the given page belongs to, if any. */
function getToolingForPage(pageElement: HTMLElement): IBookTooling | undefined {
    // The edit iframe's document carries the whole book's head, so the meta is right here.
    const meta = pageElement.ownerDocument.querySelector<HTMLMetaElement>(
        `meta[name="${kBookToolingMetaName}"]`,
    );
    const name = meta?.content?.trim();
    return name ? registeredTooling.get(name) : undefined;
}

// The book whose onBookOpened we have already run. The outer frame outlives page changes but
// not a change of book, so this is how we tell a new page of the same book from the first page
// of a different one.
let bookIdWeHaveOpened: string | undefined;

/** Bloom's id for the book being edited. */
async function getCurrentBookId(): Promise<string> {
    const result = await getAsync("editView/currentBookId");
    return String(result.data ?? "");
}

/**
 * A read-only copy of the book's own .htm.
 *
 * The edit iframe's base URL is the book's folder (BookStorage.MakeDomRelocatable sets it so
 * the page's relative image paths work), and a book's .htm is named after its folder, so the
 * two together give the file's address. A book whose file name has drifted from its folder
 * name is the case this cannot serve, hence the undefined.
 */
async function fetchBookDom(
    pageDocument: Document,
): Promise<Document | undefined> {
    const folderUrl = pageDocument.baseURI.replace(/[^/]*$/, "");
    const folderName = decodeURIComponent(
        folderUrl.replace(/\/$/, "").replace(/^.*\//, ""),
    );
    const bookUrl = folderUrl + encodeURIComponent(folderName) + ".htm";
    try {
        const response = await fetch(bookUrl);
        if (!response.ok) {
            console.warn(
                `bookTooling: ${bookUrl} came back ${response.status}`,
            );
            return undefined;
        }
        return new DOMParser().parseFromString(
            await response.text(),
            "text/html",
        );
    } catch (error) {
        console.warn(`bookTooling: could not read ${bookUrl}`, error);
        return undefined;
    }
}

/**
 * Tell the book's tooling module, if it has one, that this page has loaded. Called from the
 * page iframe at the end of its page setup. On the first page of a book this runs
 * onBookOpened first, and waits for it, so the module has whatever it works out there before
 * it sees a page.
 */
export async function notifyBookToolingPageOpened(
    pageElement: HTMLElement,
): Promise<void> {
    const tooling = getToolingForPage(pageElement);
    if (!tooling) return;
    const bookId = await getCurrentBookId();
    if (bookId !== bookIdWeHaveOpened) {
        bookIdWeHaveOpened = bookId;
        if (tooling.onBookOpened) {
            await tooling.onBookOpened(
                await fetchBookDom(pageElement.ownerDocument),
            );
        }
    }
    await tooling.onPageOpened?.(pageElement);
}

/** Tell the book's tooling module, if it has one, that this page is about to be saved. */
export function notifyBookToolingPageSaved(pageElement: HTMLElement): void {
    getToolingForPage(pageElement)?.onPageSaved?.(pageElement);
}

/**
 * Forget which book we have run onBookOpened for. Only for tests: nothing in the running
 * program has any reason to make a book look unopened.
 */
export function forgetOpenedBookForTests(): void {
    bookIdWeHaveOpened = undefined;
}
