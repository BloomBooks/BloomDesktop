// POST /functions/v1/checkin-finish — CONTRACTS.md §checkin-finish
//
// Req: { transactionId, comment?, keepCheckedOut? }
// Verifies each changed object's sha256 attribute server-side, captures S3
// version-ids, then commits the single atomic DB transaction (tc.checkin_finish_tx).
// 200: { versionId, seq } · 409 MissingOrBadUploads { paths[] } · 410 expired.
import {
    optionalField,
    requireField,
    serveJsonPost,
} from "../_shared/handler.ts";
import { HttpError, jsonResponse } from "../_shared/errors.ts";
import { callTcRpc, selectTcRow } from "../_shared/rpc.ts";
import {
    adminS3Client,
    verifyUploadedObject,
    writeManifestBackup,
} from "../_shared/s3.ts";
import { s3Env } from "../_shared/env.ts";

interface CheckinTransactionRow {
    id: string;
    collection_id: string;
    book_id: string;
    changed_paths: string[];
    proposed_files: { path: string; sha256: string; size: number }[];
    status: string;
}

interface BookRow {
    instance_id: string;
}

interface CheckinFinishResult {
    versionId: string;
    seq: number;
    manifest?: unknown;
}

// Exported so Deno tests can import and call it directly with a mocked Request,
// without triggering Deno.serve — see the `import.meta.main` guard below.
export const handler = async (
    req: Request,
    body: Record<string, unknown>,
): Promise<Response> => {
    const transactionId = requireField<string>(body, "transactionId");
    const comment = optionalField<string>(body, "comment");
    const keepCheckedOut = Boolean(body["keepCheckedOut"]);

    // Read back our own open transaction (RLS restricts this to rows we started) so
    // we know which S3 objects to verify — checkin-finish's request body carries no
    // file list per CONTRACTS.md.
    const tx = await selectTcRow<CheckinTransactionRow>(
        req,
        "checkin_transactions",
        `id=eq.${transactionId}&select=id,collection_id,book_id,changed_paths,proposed_files,status`,
    );
    if (!tx) {
        throw new HttpError(404, { error: "transaction_not_found" });
    }

    const book = await selectTcRow<BookRow>(
        req,
        "books",
        `id=eq.${tx.book_id}&select=instance_id`,
    );
    if (!book) {
        throw new HttpError(404, { error: "book_not_found" });
    }

    const prefix = `tc/${tx.collection_id}/books/${book.instance_id}/`;
    const { bucket } = s3Env();
    const client = adminS3Client();

    // Verify every changed path against S3; anything that fails is simply omitted
    // from `captured` — tc.checkin_finish_tx independently detects and reports the
    // gap as 409 MissingOrBadUploads, so there is no duplicated logic here.
    const captured: { path: string; s3VersionId: string }[] = [];
    for (const path of tx.changed_paths) {
        const proposed = tx.proposed_files.find((f) => f.path === path);
        if (!proposed) continue; // defensive; DB-side check still catches this as missing
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

    const result = await callTcRpc<CheckinFinishResult>(
        req,
        "checkin_finish_tx",
        {
            p_transaction_id: transactionId,
            p_comment: comment,
            p_keep_checked_out: keepCheckedOut,
            p_captured: captured,
        },
    );

    if (result.manifest) {
        // Best-effort backup; never blocks the response (see writeManifestBackup).
        await writeManifestBackup(client, bucket, prefix, result.manifest);
    }

    return jsonResponse(200, { versionId: result.versionId, seq: result.seq });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
