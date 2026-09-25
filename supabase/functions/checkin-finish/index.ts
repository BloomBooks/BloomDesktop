// POST /functions/v1/checkin-finish — CONTRACTS.md §checkin-finish
//
// Req: { transactionId, comment?, keepCheckedOut? }
// Verifies each changed object's sha256 attribute server-side, captures S3
// version-ids, then commits the single atomic DB transaction (tc.checkin_finish_tx).
// 200: { versionId, seq } · 409 MissingOrBadUploads { paths[], stalePaths? } (stalePaths:
// uploads older than the commit window, see uploadWindows.ts) · 409 TransactionChanged (a
// concurrent checkin-start resume rewrote the transaction while we verified it) · 410 expired.
import {
    optionalField,
    requireField,
    serveJsonPost,
} from "../_shared/tc/handler.ts";
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
import { resolveBookPrefix } from "../_shared/tc/paths.ts";
import { s3Env } from "../_shared/tc/env.ts";

interface CheckinTransactionRow {
    id: string;
    collection_id: string;
    book_id: string;
    changed_paths: string[];
    proposed_files: { path: string; sha256: string; size: number }[];
    status: string;
    revision: number;
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

    // Who is calling, established from their own JWT (see rpc.ts). Done first so a bad
    // token is rejected before any S3 work.
    const caller = await callerIdentity(req);

    // Read back our own open transaction (RLS restricts this to rows we started) so
    // we know which S3 objects to verify — checkin-finish's request body carries no
    // file list per CONTRACTS.md.
    const tx = await selectTcRow<CheckinTransactionRow>(
        req,
        "checkin_transactions",
        `id=eq.${transactionId}&select=id,collection_id,book_id,changed_paths,proposed_files,status,revision`,
    );
    if (!tx) {
        throw new HttpError(404, { error: "transaction_not_found" });
    }

    const prefix = await resolveBookPrefix(req, tx.collection_id, tx.book_id);
    const { bucket } = s3Env();
    const client = adminS3Client();

    // Verify every changed path against S3; anything that fails is simply omitted
    // from `captured` — tc.checkin_finish_tx independently detects and reports the
    // gap as 409 MissingOrBadUploads, so there is no duplicated logic here. An upload too
    // old to commit safely (the stale-upload sweep may delete it; see uploadWindows.ts) is
    // omitted too, and named in that error's `stalePaths`.
    const { captured, stalePaths } = await captureVerifiedUploads(
        client,
        bucket,
        prefix,
        tx.changed_paths,
        tx.proposed_files,
    );

    // Service-role call: checkin_finish_tx trusts p_captured, so only this function
    // (which has just verified those uploads) may call it. The RPC itself re-checks
    // that caller.userId started the transaction and still holds the book's lock.
    const result = await callTcServiceRpc<CheckinFinishResult>(
        "checkin_finish_tx",
        {
            p_transaction_id: transactionId,
            p_user_id: caller.userId,
            p_user_email: caller.email,
            p_user_name: caller.name,
            p_comment: comment,
            p_keep_checked_out: keepCheckedOut,
            p_captured: captured,
            // The proposal we verified; a checkin-start resume since then changes it, and
            // the RPC refuses (409 TransactionChanged) rather than commit a mismatch.
            p_expected_revision: tx.revision,
        },
    ).catch((e) => {
        throw withStalePaths(e, stalePaths);
    });

    if (result.manifest) {
        // Best-effort backup; never blocks the response (see writeManifestBackup).
        await writeManifestBackup(client, bucket, prefix, result.manifest);
    }

    return jsonResponse(200, { versionId: result.versionId, seq: result.seq });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
