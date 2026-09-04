// Talk to the Bloom Library SANDBOX, dev.bloomlibrary.org, from a test: which books are there,
// and deleting the ones a test uploaded. Nothing here talks to Bloom, and nothing here can reach
// bloomlibrary.org itself: every address below is the sandbox's.
//
// The sandbox has two back ends, the same two the website talks to (see BloomLibrary2's
// src/connection): a parse-server, which holds the book records and answers anonymous reads with
// the application id below, and the Bloom Library API, which takes the writes and wants the
// signed-in user's parse session token. Both ids here are public; the website ships them.

/** The sandbox parse-server, where the book records live. */
export const DEV_PARSE_SERVER_URL = "https://dev-server.bloomlibrary.org/parse";
export const DEV_PARSE_APPLICATION_ID =
    "yrXftBF6mbAuVu3fO6LnhCJiHxZPIdE7gl1DUVGR";

/** The Bloom Library API; "env=dev" on every request keeps it to the sandbox. */
const BLOOM_LIBRARY_API_URL = "https://api.bloomlibrary.org/v1";

/** A signed-in Bloom Library user, as the parse-server describes one. */
export interface IBloomLibraryLogin {
    email: string;
    /** The parse-server's id for the user; Bloom keeps it as LastLoginUserId. */
    userId: string;
    /** The parse session token; Bloom keeps it as LastLoginSessionToken and sends it with uploads. */
    sessionToken: string;
}

/** One book record on the sandbox. Only the fields a test reads. */
export interface IBookOnServer {
    /** The record's own id, which the API's delete route takes. */
    objectId: string;
    /** The book's id inside Bloom (meta.json's bookInstanceId), which uploads are matched on. */
    bookInstanceId: string;
    title: string;
    /** Bloom's tags, e.g. "bookshelf:test-bookshelf-1". */
    tags: string[];
    /** The email of the account that uploaded it. */
    uploaderEmail: string | undefined;
}

const parseHeaders = {
    "X-Parse-Application-Id": DEV_PARSE_APPLICATION_ID,
    "Content-Type": "application/json",
};

/**
 * The book records on the sandbox for these book instance ids, in no particular order. A book that
 * is not there is simply absent from the result, so a test compares lengths.
 */
export async function findBooksOnDevServer(
    bookInstanceIds: string[],
): Promise<IBookOnServer[]> {
    const where = JSON.stringify({ bookInstanceId: { $in: bookInstanceIds } });
    const url =
        `${DEV_PARSE_SERVER_URL}/classes/books?where=${encodeURIComponent(where)}` +
        `&include=uploader&keys=title,bookInstanceId,tags,uploader&limit=1000`;
    const response = await fetch(url, { headers: parseHeaders });
    if (!response.ok)
        throw new Error(
            `The sandbox parse-server answered ${response.status} to a query for books: ${await response.text()}`,
        );
    const body = (await response.json()) as {
        results: {
            objectId: string;
            bookInstanceId: string;
            title: string;
            tags?: string[];
            uploader?: { email?: string };
        }[];
    };
    return body.results.map((r) => ({
        objectId: r.objectId,
        bookInstanceId: r.bookInstanceId,
        title: r.title,
        tags: r.tags ?? [],
        uploaderEmail: r.uploader?.email,
    }));
}

/**
 * Every book the signed-in account has on the sandbox, whatever a test named them. Cleanup uses
 * this so a run can delete not only the books it just uploaded but any a crashed earlier run left
 * behind, which is safe because the account exists only for these tests.
 */
export async function findBooksUploadedBy(
    login: IBloomLibraryLogin,
): Promise<IBookOnServer[]> {
    const where = JSON.stringify({
        uploader: {
            __type: "Pointer",
            className: "_User",
            objectId: login.userId,
        },
    });
    const url =
        `${DEV_PARSE_SERVER_URL}/classes/books?where=${encodeURIComponent(where)}` +
        `&include=uploader&keys=title,bookInstanceId,tags,uploader&limit=1000`;
    const response = await fetch(url, {
        headers: {
            ...parseHeaders,
            "X-Parse-Session-Token": login.sessionToken,
        },
    });
    if (!response.ok)
        throw new Error(
            `The sandbox parse-server answered ${response.status} to a query for the account's books: ${await response.text()}`,
        );
    const body = (await response.json()) as {
        results: {
            objectId: string;
            bookInstanceId: string;
            title: string;
            tags?: string[];
            uploader?: { email?: string };
        }[];
    };
    return body.results.map((r) => ({
        objectId: r.objectId,
        bookInstanceId: r.bookInstanceId,
        title: r.title,
        tags: r.tags ?? [],
        uploaderEmail: r.uploader?.email,
    }));
}

/**
 * Delete every book the signed-in account has on the sandbox, and return how many. The safety net a
 * real-upload test calls in cleanup, so nothing it uploaded (or a crashed run uploaded before it)
 * is left on dev.bloomlibrary.org.
 */
export async function deleteAllBooksUploadedBy(
    login: IBloomLibraryLogin,
): Promise<number> {
    const books = await findBooksUploadedBy(login);
    for (const book of books) await deleteBookFromDevServer(book, login);
    return books.length;
}

/**
 * Delete one book from the sandbox, the way the website's Delete button does (BloomLibrary2's
 * deleteBook): through the Bloom Library API, as the signed-in user, who must be its uploader.
 * Returns once the parse-server no longer lists the record.
 */
export async function deleteBookFromDevServer(
    book: IBookOnServer,
    login: IBloomLibraryLogin,
): Promise<void> {
    const response = await fetch(
        `${BLOOM_LIBRARY_API_URL}/books/${book.objectId}?env=dev`,
        {
            method: "DELETE",
            headers: { "Authentication-Token": login.sessionToken },
        },
    );
    if (!response.ok)
        throw new Error(
            `The Bloom Library API answered ${response.status} to deleting book ${book.objectId} ` +
                `("${book.title}") from the sandbox: ${await response.text()}`,
        );
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
        const left = await findBooksOnDevServer([book.bookInstanceId]);
        if (!left.some((b) => b.objectId === book.objectId)) return;
        await new Promise((resolve) => setTimeout(resolve, 1000));
    }
    throw new Error(
        `Book ${book.objectId} ("${book.title}") is still on the sandbox 30 seconds after the API accepted its deletion.`,
    );
}
