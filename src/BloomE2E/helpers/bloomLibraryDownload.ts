// Fetch a public book's files from Bloom Library (bloomlibrary.org itself, not the sandbox) so a
// test can work on a real book that people made. Nothing here talks to Bloom, and nothing here
// writes to Bloom Library: it reads the book's record and then its files, as anyone may.
//
// A person gets such a book with the website's "Download into Bloom Editor" button, which goes
// through the system browser and a bloom:// link that starts Bloom; a test cannot drive that trip.
// So this does what Bloom's own download does once it has the link (BloomS3Client.DownloadBook):
// it reads where the book's files are from the book's record, lists them in Bloom Library's public
// S3 bucket, and copies them into a folder named as the uploaded book folder was, leaving out the
// files Bloom leaves out (BloomS3Client.AvoidThisFile).

import * as fs from "node:fs";
import * as Path from "node:path";

/** Bloom Library's parse-server, where the book records live. */
const PARSE_SERVER_URL = "https://server.bloomlibrary.org/parse";
/** Bloom Library's public application id, the one its website sends; it reads only what anyone may. */
const PARSE_APPLICATION_ID = "R6qNTeumQXjJCMutAJYAwPtip1qBulkFyLefkCE5";

/** How long one request to Bloom Library may take before the download gives up. */
const REQUEST_TIMEOUT_MS = 60000;

/**
 * The files Bloom does not download with a book: a PDF, source maps, Windows' thumbs.db, and the
 * uploader's collection files (BloomS3Client.AvoidThisFile).
 */
function bloomLeavesOut(key: string): boolean {
    const lower = key.toLowerCase();
    return (
        lower.endsWith("/thumbs.db") ||
        lower.endsWith(".pdf") ||
        lower.endsWith(".map") ||
        lower.includes("collectionfiles/")
    );
}

/** GET `url`, failing with a message that names it when the answer is not 200. */
async function fetchOk(
    url: string,
    headers: Record<string, string> = {},
): Promise<Response> {
    let response: Response;
    try {
        response = await fetch(url, {
            headers,
            signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
        });
    } catch (error) {
        throw new Error(
            `Could not reach ${url}; is Bloom Library reachable from this machine? (${error})`,
        );
    }
    if (!response.ok) throw new Error(`${url} answered ${response.status}.`);
    return response;
}

/**
 * Copy the files of the Bloom Library book with this id (the id in its link,
 * https://bloomlibrary.org/book/<id>) into a new folder inside `parentFolder`, named as the
 * uploader's book folder is, and return that folder. Fails when the folder is already there.
 */
export async function downloadBookFromBloomLibrary(
    bookId: string,
    parentFolder: string,
): Promise<string> {
    const record = (await (
        await fetchOk(
            `${PARSE_SERVER_URL}/classes/books/${encodeURIComponent(bookId)}?keys=baseUrl`,
            { "X-Parse-Application-Id": PARSE_APPLICATION_ID },
        )
    ).json()) as { baseUrl?: string };
    // e.g. https://s3.amazonaws.com/BloomLibraryBooks/<uploader>%2f<guid>%2f<book folder>%2f
    const match = /^https:\/\/s3\.amazonaws\.com\/([^/]+)\/(.+)$/.exec(
        record.baseUrl ?? "",
    );
    if (!match)
        throw new Error(
            `Bloom Library's record of book ${bookId} has no files on S3 (baseUrl ${record.baseUrl}).`,
        );
    const [, bucket, encodedPrefix] = match;
    let prefix = decodeURIComponent(encodedPrefix.replace(/\+/g, " "));
    if (!prefix.endsWith("/")) prefix += "/";
    const bookFolderName = prefix
        .split("/")
        .filter((part) => part)
        .at(-1)!;

    // S3 lists at most 1000 files a page; a longer listing ends with a token for the next page.
    const keys: string[] = [];
    let token: string | undefined;
    do {
        const listing = await (
            await fetchOk(
                `https://s3.amazonaws.com/${bucket}?list-type=2&prefix=${encodeURIComponent(prefix)}` +
                    (token
                        ? `&continuation-token=${encodeURIComponent(token)}`
                        : ""),
            )
        ).text();
        for (const [, key] of listing.matchAll(/<Key>([^<]+)<\/Key>/g))
            keys.push(key.replace(/&amp;/g, "&"));
        token = /<IsTruncated>true<\/IsTruncated>/.test(listing)
            ? /<NextContinuationToken>([^<]+)<\/NextContinuationToken>/
                  .exec(listing)?.[1]
                  .replace(/&amp;/g, "&")
            : undefined;
    } while (token);
    if (keys.length === 0)
        throw new Error(
            `Bloom Library has no files for book ${bookId} under ${bucket}/${prefix}.`,
        );

    const bookFolder = Path.join(parentFolder, bookFolderName);
    if (fs.existsSync(bookFolder))
        throw new Error(`${bookFolder} is already there.`);
    for (const key of keys) {
        const relative = key.slice(prefix.length);
        if (!relative || relative.endsWith("/") || bloomLeavesOut(key))
            continue;
        const file = Path.join(bookFolder, ...relative.split("/"));
        if (!file.startsWith(bookFolder + Path.sep))
            throw new Error(
                `Bloom Library listed a file outside the book: ${key}`,
            );
        fs.mkdirSync(Path.dirname(file), { recursive: true });
        const response = await fetchOk(
            `https://s3.amazonaws.com/${bucket}/${key
                .split("/")
                .map(encodeURIComponent)
                .join("/")}`,
        );
        fs.writeFileSync(file, Buffer.from(await response.arrayBuffer()));
    }
    return bookFolder;
}
