// POST /functions/v1/checkin-start — CONTRACTS.md §checkin-start
//
// Req: { collectionId, bookId?, bookInstanceId, proposedName, baseVersionId?,
//        checksum, clientVersion, files: [{path, sha256, size}] }
// 200: { transactionId, changedPaths[], s3: { bucket, region, prefix, credentials } }
// Errors: 401/403 · 409 LockHeldByOther/BaseVersionSuperseded/NameConflict · 426 ClientOutOfDate.
import {
    optionalField,
    requireField,
    serveJsonPost,
} from "../_shared/handler.ts";
import { jsonResponse } from "../_shared/errors.ts";
import { callTcRpc } from "../_shared/rpc.ts";
import { getScopedCredentials, S3_WRITE_ACTIONS } from "../_shared/s3.ts";
import { resolveBookPrefix } from "../_shared/paths.ts";

interface CheckinStartResult {
    transactionId: string;
    bookId: string;
    changedPaths: string[];
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
        },
    );

    // Scope the S3 credentials to the DB-canonical instance_id, never the caller-supplied
    // bookInstanceId: for an existing book checkin_start_tx validates/locks by bookId and
    // ignores the client's instance id, so using the client value here would let a member
    // request write credentials for an arbitrary book's prefix (Greptile P1, PR #8048).
    // resolveBookPrefix reads the canonical value back from the books row (same pattern
    // as checkin-finish); for the new-book path the row was just created from
    // bookInstanceId, so the canonical value is identical.
    const prefix = await resolveBookPrefix(req, collectionId, result.bookId);
    const s3 = await getScopedCredentials(prefix, S3_WRITE_ACTIONS);

    return jsonResponse(200, {
        transactionId: result.transactionId,
        changedPaths: result.changedPaths,
        s3,
    });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
