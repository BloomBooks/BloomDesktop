// POST /functions/v1/checkin-start — CONTRACTS.md §checkin-start
//
// Req: { collectionId, bookId?, bookInstanceId, proposedName, baseVersionId?,
//        checksum, clientVersion, files: [{path, sha256, size}] }
// 200: { transactionId, changedPaths[], s3: { bucket, region, prefix, credentials } }
// Errors: 401/403 · 409 LockHeldByOther/BaseVersionSuperseded/NameConflict · 426 ClientOutOfDate.
import { optionalField, requireField, serveJsonPost } from "../_shared/handler.ts";
import { jsonResponse } from "../_shared/errors.ts";
import { callTcRpc } from "../_shared/rpc.ts";
import { getScopedCredentials } from "../_shared/s3.ts";

interface CheckinStartResult {
    transactionId: string;
    bookId: string;
    changedPaths: string[];
}

// The credentials handed to the client for the duration of a check-in: they need to
// PUT new/changed content and read back what's already there (e.g. to resume after
// an interrupted upload).
const CHECKIN_ACTIONS = [
    "s3:PutObject",
    "s3:GetObject",
    "s3:GetObjectVersion",
    "s3:AbortMultipartUpload",
    "s3:ListMultipartUploadParts",
];

serveJsonPost(async (req, body) => {
    const collectionId = requireField<string>(body, "collectionId");
    const bookInstanceId = requireField<string>(body, "bookInstanceId");
    const proposedName = requireField<string>(body, "proposedName");
    const checksum = requireField<string>(body, "checksum");
    const clientVersion = requireField<string>(body, "clientVersion");
    const files = requireField<unknown[]>(body, "files");
    const bookId = optionalField<string>(body, "bookId");
    const baseVersionId = optionalField<string>(body, "baseVersionId");

    const result = await callTcRpc<CheckinStartResult>(req, "checkin_start_tx", {
        p_collection_id: collectionId,
        p_book_id: bookId,
        p_book_instance_id: bookInstanceId,
        p_proposed_name: proposedName,
        p_base_version_id: baseVersionId,
        p_checksum: checksum,
        p_client_version: clientVersion,
        p_files: files,
    });

    const prefix = `tc/${collectionId}/books/${bookInstanceId}/`;
    const s3 = await getScopedCredentials(prefix, CHECKIN_ACTIONS);

    return jsonResponse(200, {
        transactionId: result.transactionId,
        changedPaths: result.changedPaths,
        s3,
    });
});
