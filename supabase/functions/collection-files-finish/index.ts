// POST /functions/v1/collection-files-finish — CONTRACTS.md §collection-files-start/finish
// Req: { transactionId } -> bumps the group version atomically.
// 409 VersionConflict ⇒ client pulls first (repo-wins rule); 409 MissingOrBadUploads
// { paths[], stalePaths? } (stalePaths: uploads older than the commit window, uploadWindows.ts).
// 409 TransactionChanged: a concurrent collection-files-start resume rewrote the transaction.
import { requireField, serveJsonPost } from "../_shared/tc/handler.ts";
import { HttpError, jsonResponse } from "../_shared/tc/errors.ts";
import {
    callerIdentity,
    callTcServiceRpc,
    selectTcRow,
} from "../_shared/tc/rpc.ts";
import {
    adminS3Client,
    captureVerifiedUploads,
    withStalePaths,
    writeManifestBackup,
} from "../_shared/tc/s3.ts";
import { collectionFilesPrefix } from "../_shared/tc/paths.ts";
import { s3Env } from "../_shared/tc/env.ts";

interface CollectionFileTransactionRow {
    id: string;
    collection_id: string;
    group_key: string;
    changed_paths: string[];
    proposed_files: { path: string; sha256: string; size: number }[];
    revision: number;
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

    // See checkin-finish: identity from the caller's own JWT, before any S3 work.
    const caller = await callerIdentity(req);

    const tx = await selectTcRow<CollectionFileTransactionRow>(
        req,
        "collection_file_transactions",
        `id=eq.${transactionId}&select=id,collection_id,group_key,changed_paths,proposed_files,revision`,
    );
    if (!tx) {
        throw new HttpError(404, { error: "transaction_not_found" });
    }

    const prefix = collectionFilesPrefix(tx.collection_id, tx.group_key);
    const { bucket } = s3Env();
    const client = adminS3Client();

    // Same skip-unverified (and skip-too-old) semantics as checkin-finish — see
    // captureVerifiedUploads.
    const { captured, stalePaths } = await captureVerifiedUploads(
        client,
        bucket,
        prefix,
        tx.changed_paths,
        tx.proposed_files,
    );

    // Service-role call, for the same reason as in checkin-finish.
    const result = await callTcServiceRpc<CollectionFilesFinishResult>(
        "collection_files_finish_tx",
        {
            p_transaction_id: transactionId,
            p_user_id: caller.userId,
            p_user_email: caller.email,
            p_user_name: caller.name,
            p_captured: captured,
            // See checkin-finish: refused (409 TransactionChanged) if a resume changed the
            // proposal we just verified.
            p_expected_revision: tx.revision,
        },
    ).catch((e) => {
        throw withStalePaths(e, stalePaths);
    });

    if (result.manifest) {
        await writeManifestBackup(
            client,
            bucket,
            prefix,
            result.version,
            result.manifest,
        );
    }

    return jsonResponse(200, { version: result.version });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
