// Talk to the Bloom Library SANDBOX, dev.bloomlibrary.org, from a test: which books are there,
// and deleting the ones a test uploaded. Nothing here talks to Bloom, and nothing here can reach
// bloomlibrary.org itself: every address below is the sandbox's.
//
// The sandbox has two back ends, the same two the website talks to (see BloomLibrary2's
// src/connection): a parse-server, which holds the book records and answers anonymous reads with
// the application id below, and the Bloom Library API, which takes the writes and wants the
// signed-in user's parse session token. Both ids here are public; the website ships them.
//
// The files of an uploaded book sit in the sandbox's public S3 bucket, at the record's baseUrl; a
// test reads the uploaded HTML from there to see what Bloom actually sent.

import { xmatterPackInBookHtml } from "./bookHtml";

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
    /** The bookshelves the book sits on, by url key: the "bookshelf:" tags without their prefix. */
    bookshelves: string[];
    /** The email of the account that uploaded it. */
    uploaderEmail: string | undefined;
    /**
     * Where the book's files are on S3, as Bloom wrote it (BloomS3Client.GetBaseUrl): URL-encoded
     * with the slashes as %2f and spaces as +. Ends with the book folder's name.
     */
    baseUrl: string;
    /** When the record was made, which is when the book was first uploaded (ISO 8601). */
    createdAt: string;
    /**
     * The subscription the record names (its brandingProjectName), e.g. "Test", or undefined while
     * it names none. Uploads do not set it; the harvester does, from the uploaded meta.json, when it
     * finishes processing an upload (BL-17234).
     */
    subscription: string | undefined;
}

const parseHeaders = {
    "X-Parse-Application-Id": DEV_PARSE_APPLICATION_ID,
    "Content-Type": "application/json",
};

/**
 * Query the sandbox's book records with this parse "where" clause, as the signed-in user when a
 * login is given (the website sends the session token for a user's own records, so a test does
 * too). Returns the records in no particular order.
 */
async function queryBooksOnDevServer(
    where: object,
    describe: string,
    login?: IBloomLibraryLogin,
): Promise<IBookOnServer[]> {
    const url =
        `${DEV_PARSE_SERVER_URL}/classes/books?where=${encodeURIComponent(JSON.stringify(where))}` +
        `&include=uploader&keys=title,bookInstanceId,tags,uploader,baseUrl,createdAt,brandingProjectName&limit=1000`;
    const headers = login
        ? { ...parseHeaders, "X-Parse-Session-Token": login.sessionToken }
        : parseHeaders;
    const response = await fetch(url, { headers });
    if (!response.ok)
        throw new Error(
            `The sandbox parse-server answered ${response.status} to a query for ${describe}: ${await response.text()}`,
        );
    const body = (await response.json()) as {
        results: {
            objectId: string;
            bookInstanceId: string;
            title: string;
            tags?: string[];
            uploader?: { email?: string };
            baseUrl: string;
            createdAt: string;
            brandingProjectName?: string;
        }[];
    };
    const bookshelfPrefix = "bookshelf:";
    return body.results.map((r) => ({
        objectId: r.objectId,
        bookInstanceId: r.bookInstanceId,
        title: r.title,
        tags: r.tags ?? [],
        bookshelves: (r.tags ?? [])
            .filter((tag) => tag.startsWith(bookshelfPrefix))
            .map((tag) => tag.substring(bookshelfPrefix.length)),
        uploaderEmail: r.uploader?.email,
        baseUrl: r.baseUrl,
        createdAt: r.createdAt,
        subscription: r.brandingProjectName || undefined,
    }));
}

/**
 * The book records on the sandbox for these book instance ids, in no particular order. A book that
 * is not there is simply absent from the result, so a test compares lengths.
 */
export async function findBooksOnDevServer(
    bookInstanceIds: string[],
): Promise<IBookOnServer[]> {
    return queryBooksOnDevServer(
        { bookInstanceId: { $in: bookInstanceIds } },
        "books",
    );
}

/**
 * How many books the sandbox counts in this language, by the same Bloom Library API query that
 * Bloom's Collection tab progress bar makes (BloomLibraryBookApiClient.GetBookCountByLanguage):
 * the books whose language is this tag, leaving out drafts, rebrands and books out of circulation.
 * Anonymous, like Bloom's own request.
 */
export async function countBooksInLanguageOnDevServer(
    languageTag: string,
): Promise<number> {
    const response = await fetch(
        `${BLOOM_LIBRARY_API_URL}/books?lang=${encodeURIComponent(languageTag)}&limit=0&count=true&env=dev`,
    );
    if (!response.ok)
        throw new Error(
            `The Bloom Library API answered ${response.status} to counting the sandbox's books in ` +
                `"${languageTag}": ${await response.text()}`,
        );
    const body = (await response.json()) as { count: number };
    if (typeof body.count !== "number")
        throw new Error(
            `The Bloom Library API's count of the sandbox's books in "${languageTag}" has no number: ${JSON.stringify(body)}`,
        );
    return body.count;
}

/**
 * The books the signed-in account has on the sandbox whose titles start with `titlePrefix`. Cleanup
 * uses this so a run can delete not only the books it just uploaded but any a crashed earlier run
 * of the same test left behind.
 *
 * Every real-upload test signs in as the same account, and the nightly runs its shards at the same
 * time, so two such tests can have books on the account at once. Each test therefore gives its
 * books titles with a prefix of its own and reads and deletes only those: a test that counted or
 * deleted all the account's books would break whichever other one was running.
 */
export async function findBooksUploadedBy(
    login: IBloomLibraryLogin,
    titlePrefix: string,
): Promise<IBookOnServer[]> {
    const books = await queryBooksOnDevServer(
        {
            uploader: {
                __type: "Pointer",
                className: "_User",
                objectId: login.userId,
            },
        },
        "the account's books",
        login,
    );
    return books.filter((book) => book.title.startsWith(titlePrefix));
}

/**
 * The S3 folder a baseUrl names, decoded: Bloom writes baseUrl with HttpUtility.UrlEncode, which
 * puts spaces as "+", so undo that before the general decoding turns the %2f slashes (and any %2b
 * plus) back into themselves.
 */
function decodeBaseUrl(baseUrl: string): string {
    return decodeURIComponent(baseUrl.replace(/\+/g, " "));
}

/**
 * The name of the book folder an upload wrote, the last part of its baseUrl. Bloom names it after
 * the book's folder on disk, so a test can tell which book an uploaded copy is.
 */
export function folderNameOfUploadedBook(baseUrl: string): string {
    return decodeBaseUrl(baseUrl).split("/").filter(Boolean).pop()!;
}

/**
 * One file of a book as it was uploaded, read from the sandbox's S3 bucket. `baseUrl` is the folder
 * the upload wrote, in the form Bloom writes it (BloomS3Client.GetBaseUrl): take it from the
 * bulk-upload results file (IBulkUploadBookResult.baseUrl), not from the book's record, which can point
 * at an older upload's folder once the harvester has written a stale record back (BL-16921).
 * `fileName` is a name inside that folder, or a function of the folder's name (the .htm is named
 * after the folder). The bucket is public-read; that is how the website shows books.
 */
async function fetchUploadedBookFile(
    baseUrl: string,
    fileName: string | ((folderName: string) => string),
    describe: string,
): Promise<string> {
    const folderPath = decodeBaseUrl(baseUrl);
    const folderName = folderNameOfUploadedBook(baseUrl);
    const name = typeof fileName === "string" ? fileName : fileName(folderName);
    // The URL constructor re-encodes the spaces and the rest of the path for the request.
    const url = new URL(folderPath).href + encodeURIComponent(name);
    const response = await fetch(url);
    if (!response.ok)
        throw new Error(
            `S3 answered ${response.status} for the uploaded ${describe} of "${folderName}" at ${url}`,
        );
    return response.text();
}

/** The HTML of a book as it was uploaded: `<baseUrl><folder>.htm`. */
export async function fetchUploadedBookHtml(baseUrl: string): Promise<string> {
    return fetchUploadedBookFile(baseUrl, (folder) => `${folder}.htm`, "HTML");
}

/**
 * The bookshelves the uploaded copy of a book names, by url key: the "bookshelf:" tags of the
 * meta.json Bloom uploaded with it. Bloom writes the collection's bookshelf into those tags just
 * before uploading (BookUpload.UploadBookAsync), so this is exactly what Bloom sent.
 *
 * A test checks this rather than the record's own tags (IBookOnServer.bookshelves) because the
 * sandbox's harvester can overwrite the record after an upload: it processes each upload in the
 * background and, when it finishes, writes the whole record back from the copy it read when it
 * started, so a re-upload that lands mid-harvest loses its new tags there (BL-16921). What Bloom
 * uploaded is unaffected, and it is Bloom's behavior a test here is about.
 */
export async function getBookshelvesOfUploadedBook(
    baseUrl: string,
): Promise<string[]> {
    const meta = JSON.parse(
        await fetchUploadedBookFile(baseUrl, "meta.json", "meta.json"),
    ) as { tags?: string[] };
    const bookshelfPrefix = "bookshelf:";
    return (meta.tags ?? [])
        .filter((tag) => tag.startsWith(bookshelfPrefix))
        .map((tag) => tag.substring(bookshelfPrefix.length));
}

/**
 * The subscription the uploaded copy of a book was made under, e.g. "Test" or "Default": the
 * brandingProjectName of the meta.json Bloom uploaded with it (BookMetaData.SubscriptionDescriptor).
 */
export async function getSubscriptionOfUploadedBook(
    baseUrl: string,
): Promise<string | undefined> {
    const meta = JSON.parse(
        await fetchUploadedBookFile(baseUrl, "meta.json", "meta.json"),
    ) as { brandingProjectName?: string };
    return meta.brandingProjectName;
}

/**
 * Where the latest upload of `book` put its files, as a baseUrl in the form Bloom writes it. Each
 * upload goes to a new folder, `<bucket>/<record id>/<upload time>/<book folder>/`, and the record's
 * own baseUrl can point at an older upload's folder once the harvester has written a stale record
 * back (BL-16921). So this lists the record's folders in the sandbox's bucket, which is public-read,
 * and takes the one with the latest upload time.
 */
export async function getLatestUploadBaseUrl(
    book: IBookOnServer,
): Promise<string> {
    const match = /^(https:\/\/s3\.amazonaws\.com\/[^/]+\/)(.+)$/.exec(
        book.baseUrl,
    );
    if (!match)
        throw new Error(
            `The record of "${book.title}" has a baseUrl that is not on S3: ${book.baseUrl}`,
        );
    const [, bucketUrl, encodedPrefix] = match;
    const recordFolder = decodeBaseUrl(encodedPrefix).split("/")[0];
    /** The folders directly inside `prefix` in the bucket, each ending with "/". */
    const listFolders = async (prefix: string): Promise<string[]> => {
        const url = `${bucketUrl}?list-type=2&delimiter=/&prefix=${encodeURIComponent(prefix)}`;
        const response = await fetch(url);
        if (!response.ok)
            throw new Error(`S3 answered ${response.status} to listing ${url}`);
        return [
            ...(await response.text()).matchAll(
                /<CommonPrefixes><Prefix>([^<]+)<\/Prefix><\/CommonPrefixes>/g,
            ),
        ].map(([, folder]) => folder.replace(/&amp;/g, "&"));
    };
    const uploads = (await listFolders(`${recordFolder}/`)).sort(
        (a, b) => Number(a.split("/")[1]) - Number(b.split("/")[1]),
    );
    if (uploads.length === 0)
        throw new Error(
            `The sandbox's bucket has no uploads of "${book.title}" under ${bucketUrl}${recordFolder}/.`,
        );
    const latest = uploads[uploads.length - 1];
    const bookFolders = await listFolders(latest);
    if (bookFolders.length !== 1)
        throw new Error(
            `The latest upload of "${book.title}" (${latest}) holds ${bookFolders.length} book folders: ${bookFolders.join(", ")}`,
        );
    return bucketUrl + encodeURIComponent(bookFolders[0]);
}

/** Undo the five XML escapes S3 uses in a listing. */
function unescapeXml(text: string): string {
    return text
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&amp;/g, "&");
}

/**
 * Every file an upload wrote, as paths inside its book folder (e.g. "audio/x.mp3"), read by listing
 * the sandbox's public bucket. `baseUrl` is the upload's folder in the form Bloom writes it, such as
 * getLatestUploadBaseUrl returns. S3 lists at most 1000 keys per answer, so this follows the
 * continuation tokens until the listing is complete.
 */
export async function listUploadedBookFiles(
    baseUrl: string,
): Promise<string[]> {
    const match = /^(https:\/\/s3\.amazonaws\.com\/[^/]+\/)(.+)$/.exec(
        decodeBaseUrl(baseUrl),
    );
    if (!match) throw new Error(`The baseUrl is not on S3: ${baseUrl}`);
    const [, bucketUrl, folder] = match;
    const prefix = folder.endsWith("/") ? folder : `${folder}/`;
    const files: string[] = [];
    let continuationToken: string | undefined;
    do {
        const url =
            `${bucketUrl}?list-type=2&prefix=${encodeURIComponent(prefix)}` +
            (continuationToken
                ? `&continuation-token=${encodeURIComponent(continuationToken)}`
                : "");
        const response = await fetch(url);
        if (!response.ok)
            throw new Error(`S3 answered ${response.status} to listing ${url}`);
        const listing = await response.text();
        for (const [, key] of listing.matchAll(/<Key>([^<]+)<\/Key>/g))
            files.push(unescapeXml(key).substring(prefix.length));
        continuationToken = /<IsTruncated>true<\/IsTruncated>/.test(listing)
            ? unescapeXml(
                  /<NextContinuationToken>([^<]+)<\/NextContinuationToken>/.exec(
                      listing,
                  )?.[1] ?? "",
              ) || undefined
            : undefined;
        if (
            /<IsTruncated>true<\/IsTruncated>/.test(listing) &&
            !continuationToken
        )
            throw new Error(
                `S3's listing of ${url} is truncated but gives no continuation token.`,
            );
    } while (continuationToken);
    return files;
}

/**
 * The front/back matter pack the uploaded copy of a book carries (see xmatterPackInBookHtml): how
 * a test sees that an upload sent the book with the collection's current pack.
 */
export async function getXmatterPackOfUploadedBook(
    baseUrl: string,
): Promise<string> {
    return xmatterPackInBookHtml(await fetchUploadedBookHtml(baseUrl));
}

/**
 * Delete every book the signed-in account has on the sandbox whose title starts with
 * `titlePrefix`, and return how many. The safety net a real-upload test calls in cleanup, so
 * nothing it uploaded (or a crashed run of it uploaded before) is left on dev.bloomlibrary.org.
 * See findBooksUploadedBy for why it takes a prefix.
 *
 * With `olderThanMinutes`, only books first uploaded at least that long ago are deleted: a run
 * clearing up after crashed runs must not delete the book of another run of the same test that is
 * going on now (the nightly and a bot's verification run can overlap).
 */
export async function deleteBooksUploadedBy(
    login: IBloomLibraryLogin,
    titlePrefix: string,
    olderThanMinutes?: number,
): Promise<number> {
    const cutoff =
        olderThanMinutes === undefined
            ? Infinity
            : Date.now() - olderThanMinutes * 60000;
    const books = (await findBooksUploadedBy(login, titlePrefix)).filter(
        (book) => Date.parse(book.createdAt) <= cutoff,
    );
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
