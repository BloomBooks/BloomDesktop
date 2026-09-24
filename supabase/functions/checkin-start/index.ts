// POST /functions/v1/checkin-start — CONTRACTS.md §checkin-start
//
// Req: { collectionId, bookId?, bookInstanceId, proposedName, baseVersionId?,
//        checksum, clientVersion, files: [{path, sha256, size}], checkoutGuid? }
// 200: { transactionId, changedPaths[], checkoutGuid?, s3: { bucket, region, prefix, credentials } }
//      (checkoutGuid only when this call issued a new checkout: new book, or a free lock taken)
// Errors: 400 InvalidManifest · 401/403 · 409 LockHeldByOther/CheckoutElsewhere/BaseVersionSuperseded/
//         NameConflict · 426 ClientOutOfDate.
import {
    optionalField,
    requireField,
    serveJsonPost,
} from "../_shared/tc/handler.ts";
import { jsonResponse } from "../_shared/tc/errors.ts";
import { callTcRpc } from "../_shared/tc/rpc.ts";
import { getScopedCredentials, S3_WRITE_ACTIONS } from "../_shared/tc/s3.ts";
import { bookPrefix, resolveBookPrefix } from "../_shared/tc/paths.ts";

interface CheckinStartResult {
    transactionId: string;
    bookId: string;
    changedPaths: string[];
    checkoutGuid?: string;
}

// Exported (rather than only passed inline to serveJsonPost) so Deno tests can import
// and call it directly with a mocked Request, without triggering Deno.serve — see the
// `import.meta.main` guard below.
export const handler = async (
    req: Request,
    body: Record<string, unknown>,
): Promise<Response> => {
    const collectionId = requireField<string>(body, "collectionId");
    const bookInstanceId = requireField<string>(body, "bookInstanceId");
    const proposedName = requireField<string>(body, "proposedName");
    const checksum = requireField<string>(body, "checksum");
    const clientVersion = requireField<string>(body, "clientVersion");
    const files = requireField<unknown[]>(body, "files");
    const bookId = optionalField<string>(body, "bookId");
    const baseVersionId = optionalField<string>(body, "baseVersionId");
    // The book folder's .checkout GUID, if the client has one (CONTRACTS.md v1.9).
    const checkoutGuid = optionalField<string>(body, "checkoutGuid");

    // Get the S3 credentials BEFORE calling checkin_start_tx. That RPC can commit a new
    // checkout GUID (taking a free lock, or creating a new book) which only this response
    // carries back to the client; if anything could still fail after it (the books read,
    // STS), the GUID would be lost, and every retry from this copy would then be refused
    // with CheckoutElsewhere. So everything that can fail happens first, and the response
    // is built as soon as the RPC returns. If the RPC refuses, these credentials are
    // simply discarded (never returned).
    //
    // Scope the credentials to the DB-canonical instance_id, never a caller-supplied one
    // for an existing book: checkin_start_tx validates/locks an existing book by bookId and
    // ignores the client's instance id, so using the client value would let a member
    // request write credentials for an arbitrary book's prefix (Greptile P1, PR #8048).
    // resolveBookPrefix reads the canonical value from the books row with the caller's own
    // JWT (instance_id never changes, so reading it before the RPC is as good as after).
    // For a new book (no bookId) the RPC creates, or resumes, only a row whose instance_id
    // IS bookInstanceId (any other row with that instance id is a NameConflict), so the
    // request's value is the canonical one.
    const prefix = bookId
        ? await resolveBookPrefix(req, collectionId, bookId)
        : bookPrefix(collectionId, bookInstanceId);
    const s3 = await getScopedCredentials(prefix, S3_WRITE_ACTIONS);

    const result = await callTcRpc<CheckinStartResult>(
        req,
        "checkin_start_tx",
        {
            p_collection_id: collectionId,
            p_book_id: bookId,
            p_book_instance_id: bookInstanceId,
            p_proposed_name: proposedName,
            p_base_version_id: baseVersionId,
            p_checksum: checksum,
            p_client_version: clientVersion,
            p_files: files,
            p_checkout_guid: checkoutGuid,
        },
    );

    return jsonResponse(200, {
        transactionId: result.transactionId,
        changedPaths: result.changedPaths,
        // Present only when this call issued a new checkout; the client saves it in the
        // book folder's .checkout file.
        ...(result.checkoutGuid ? { checkoutGuid: result.checkoutGuid } : {}),
        s3,
    });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
