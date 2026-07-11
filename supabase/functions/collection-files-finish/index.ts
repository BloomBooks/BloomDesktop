// POST /functions/v1/collection-files-finish — CONTRACTS.md §collection-files-start/finish
// Req: { transactionId } -> bumps the group version atomically.
// 409 VersionConflict ⇒ client pulls first (repo-wins rule); 409 MissingOrBadUploads.
import { requireField, serveJsonPost } from "../_shared/handler.ts";
import { HttpError, jsonResponse } from "../_shared/errors.ts";
import { callTcRpc, selectTcRow } from "../_shared/rpc.ts";
import {
    adminS3Client,
    verifyUploadedObject,
    writeManifestBackup,
} from "../_shared/s3.ts";
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

    const prefix = `tc/${tx.collection_id}/collectionFiles/${tx.group_key}/`;
    const { bucket } = s3Env();
    const client = adminS3Client();

    const captured: { path: string; s3VersionId: string }[] = [];
    for (const path of tx.changed_paths) {
        const proposed = tx.proposed_files.find((f) => f.path === path);
        if (!proposed) continue;
        const verified = await verifyUploadedObject(
            client,
            bucket,
            `${prefix}${path}`,
            proposed.sha256,
        );
        if (verified) {
            captured.push({ path, s3VersionId: verified.s3VersionId });
        }
    }

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
