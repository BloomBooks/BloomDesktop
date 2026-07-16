// Canonical S3 key layout for Cloud Team Collections (CONTRACTS.md "S3 layout").
// Everything a collection owns lives under tc/{collectionId}/; these helpers are the
// single source of truth for that layout so handlers never hand-template key strings.
// The trailing slashes are load-bearing: the returned values serve both as
// credential-scope prefixes (…/* in the session policy) and as concatenation bases
// for individual object keys.
import { HttpError } from "./errors.ts";
import { selectTcRow } from "./rpc.ts";

/** Root prefix for everything belonging to one collection (books, collectionFiles,
 * manifests). download-start scopes its read-only credentials here. */
export const collectionPrefix = (collectionId: string): string =>
    `tc/${collectionId}/`;

/** Prefix for one book's files, keyed by the book's DB-canonical instance id. */
export const bookPrefix = (collectionId: string, instanceId: string): string =>
    `${collectionPrefix(collectionId)}books/${instanceId}/`;

/** Prefix for one collection-files group ('other' | 'allowed-words' | 'sample-texts'). */
export const collectionFilesPrefix = (
    collectionId: string,
    groupKey: string,
): string => `${collectionPrefix(collectionId)}collectionFiles/${groupKey}/`;

/** Reads back a book row (under RLS, with the caller's own JWT) to learn its
 * DB-canonical instance_id, and returns that book's S3 prefix. Throws 404 if the row
 * is missing or invisible to the caller. Both checkin-start and checkin-finish must
 * scope S3 operations by the DB-canonical instance id, never a caller-supplied one —
 * see the security note at checkin-start's call site (Greptile P1, PR #8048). */
export const resolveBookPrefix = async (
    req: Request,
    collectionId: string,
    bookId: string,
): Promise<string> => {
    const book = await selectTcRow<{ instance_id: string }>(
        req,
        "books",
        `id=eq.${bookId}&select=instance_id`,
    );
    if (!book) {
        throw new HttpError(404, { error: "book_not_found" });
    }
    return bookPrefix(collectionId, book.instance_id);
};
