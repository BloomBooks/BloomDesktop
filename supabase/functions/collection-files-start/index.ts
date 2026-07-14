// POST /functions/v1/collection-files-start — CONTRACTS.md §collection-files-start/finish
// Req: { collectionId, groupKey: 'other'|'allowed-words'|'sample-texts', expectedVersion,
//        files[] } -> two-phase like check-in.
// 409 VersionConflict ⇒ client pulls first (repo-wins rule).
import { requireField, serveJsonPost } from "../_shared/handler.ts";
import { HttpError, jsonResponse } from "../_shared/errors.ts";
import { callTcRpc } from "../_shared/rpc.ts";
import { getScopedCredentials } from "../_shared/s3.ts";

const VALID_GROUP_KEYS = new Set(["other", "allowed-words", "sample-texts"]);

const COLLECTION_FILES_ACTIONS = [
    "s3:PutObject",
    "s3:GetObject",
    "s3:GetObjectVersion",
    "s3:AbortMultipartUpload",
    "s3:ListMultipartUploadParts",
];

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

    const prefix = `tc/${collectionId}/collectionFiles/${groupKey}/`;
    const s3 = await getScopedCredentials(prefix, COLLECTION_FILES_ACTIONS);

    return jsonResponse(200, {
        transactionId: result.transactionId,
        changedPaths: result.changedPaths,
        s3,
    });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
