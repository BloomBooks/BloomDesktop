// POST /functions/v1/collection-files-start — CONTRACTS.md §collection-files-start/finish
// Req: { collectionId, groupKey: 'other'|'allowed-words'|'sample-texts', expectedVersion,
//        files[] } -> two-phase like check-in.
// 409 VersionConflict ⇒ client pulls first (repo-wins rule).
import { requireField, serveJsonPost } from "../_shared/tc/handler.ts";
import { HttpError, jsonResponse } from "../_shared/tc/errors.ts";
import { callTcRpc } from "../_shared/tc/rpc.ts";
import { getScopedCredentials, S3_WRITE_ACTIONS } from "../_shared/tc/s3.ts";
import { collectionFilesPrefix } from "../_shared/tc/paths.ts";

const VALID_GROUP_KEYS = new Set(["other", "allowed-words", "sample-texts"]);

interface CollectionFilesStartResult {
    transactionId: string;
    changedPaths: string[];
}

// Exported so Deno tests can import and call it directly — see checkin-start/index.ts's
// comment on the `import.meta.main` guard below.
export const handler = async (
    req: Request,
    body: Record<string, unknown>,
): Promise<Response> => {
    const collectionId = requireField<string>(body, "collectionId");
    const groupKey = requireField<string>(body, "groupKey");
    const expectedVersion = requireField<number>(body, "expectedVersion");
    const files = requireField<unknown[]>(body, "files");

    if (!VALID_GROUP_KEYS.has(groupKey)) {
        throw new HttpError(400, {
            error: "invalid_request",
            field: "groupKey",
        });
    }

    // Get the S3 credentials BEFORE collection_files_start_tx commits a transaction (as
    // checkin-start does), so a credential failure cannot leave an open transaction the
    // client never heard of. If the RPC refuses, these credentials are simply discarded.
    const prefix = collectionFilesPrefix(collectionId, groupKey);
    const s3 = await getScopedCredentials(prefix, S3_WRITE_ACTIONS);

    const result = await callTcRpc<CollectionFilesStartResult>(
        req,
        "collection_files_start_tx",
        {
            p_collection_id: collectionId,
            p_group_key: groupKey,
            p_expected_version: expectedVersion,
            p_files: files,
        },
    );

    return jsonResponse(200, {
        transactionId: result.transactionId,
        changedPaths: result.changedPaths,
        s3,
    });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
