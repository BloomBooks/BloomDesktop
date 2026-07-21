// POST /functions/v1/collection-files-finish — CONTRACTS.md §collection-files-start/finish
// Req: { transactionId } -> bumps the group version atomically.
// 409 VersionConflict ⇒ client pulls first (repo-wins rule); 409 MissingOrBadUploads.
import { requireField, serveJsonPost } from "../_shared/handler.ts";
import { HttpError, jsonResponse } from "../_shared/errors.ts";
import { callTcRpc, selectTcRow } from "../_shared/rpc.ts";
import {
    adminS3Client,
    captureVerifiedUploads,
    writeManifestBackup,
} from "../_shared/s3.ts";
import { collectionFilesPrefix } from "../_shared/paths.ts";
import { s3Env } from "../_shared/env.ts";

interface CollectionFileTransactionRow {
    id: string;
    collection_id: string;
    group_key: string;
    changed_paths: string[];
    proposed_files: { path: string; sha256: string; size: number }[];
}

interface CollectionFilesFinishResult {
    version: number;
    manifest?: unknown;
}

// Exported so Deno tests can import and call it directly — see checkin-start/index.ts's
// comment on the `import.meta.main` guard below.
export const handler = async (
    req: Request,
    body: Record<string, unknown>,
): Promise<Response> => {
    const transactionId = requireField<string>(body, "transactionId");

    const tx = await selectTcRow<CollectionFileTransactionRow>(
        req,
        "collection_file_transactions",
        `id=eq.${transactionId}&select=id,collection_id,group_key,changed_paths,proposed_files`,
    );
    if (!tx) {
        throw new HttpError(404, { error: "transaction_not_found" });
    }

    const prefix = collectionFilesPrefix(tx.collection_id, tx.group_key);
    const { bucket } = s3Env();
    const client = adminS3Client();

    // Same skip-unverified semantics as checkin-finish — see captureVerifiedUploads.
    const captured = await captureVerifiedUploads(
        client,
        bucket,
        prefix,
        tx.changed_paths,
        tx.proposed_files,
    );

    const result = await callTcRpc<CollectionFilesFinishResult>(
        req,
        "collection_files_finish_tx",
        {
            p_transaction_id: transactionId,
            p_captured: captured,
        },
    );

    if (result.manifest) {
        await writeManifestBackup(client, bucket, prefix, result.manifest);
    }

    return jsonResponse(200, { version: result.version });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
