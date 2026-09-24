// POST /functions/v1/sweep-stale-uploads
// Reference-aware GC for uploads left orphaned when a check-in uploads to S3 but never
// commits (see Design/CloudTeamCollections/GOING-LIVE.md "Orphaned-upload sweep").
//
// The S3 NoncurrentVersionExpiration lifecycle rule reaps versions superseded by a real
// commit on its own. This sweep handles the case that rule gets WRONG: a successful upload
// whose commit failed demotes the still-referenced committed version to "noncurrent" (so the
// lifecycle would eventually delete the version we still need) while the garbage upload sits
// as "current" (which the lifecycle never touches). For each file touched by a dead
// (aborted/expired) transaction -- and NOT one a live transaction is currently uploading -- we
// delete every S3 version newer than the one the current manifest references, which restores
// that referenced version to "current" and removes the garbage.
//
// Operational job: works across ALL collections, so it runs only for the service role. Intended
// to be invoked ~daily by a scheduler with the service-role key; safe to run more often
// (idempotent -- a second run finds nothing newer than the referenced version).
//
// The worklist is a snapshot, and check-ins keep happening while the sweep runs, so two guards
// keep it from deleting an upload that a check-in has since committed (or is still using):
//   - it only ever deletes versions older than UPLOAD_GRACE_MS (the check-in transaction
//     lifetime), so an upload belonging to a transaction that could still be live is never a
//     candidate; and
//   - immediately before deleting a key's candidates it re-reads that key's state
//     (tc.stale_upload_key_state) and skips the key unless it is still stale and still
//     references the version the candidates were chosen against.
import { HttpError, jsonResponse } from "../_shared/tc/errors.ts";
import { serveJsonPost } from "../_shared/tc/handler.ts";
import { callTcRpc } from "../_shared/tc/rpc.ts";
import {
    adminS3Client,
    deleteObjectVersion,
    listObjectVersions,
} from "../_shared/tc/s3.ts";
import { s3Env } from "../_shared/tc/env.ts";

interface GarbageRow {
    transaction_kind: string;
    transaction_id: string;
    s3_key: string;
    referenced_version_id: string | null;
}

interface KeyState {
    stillStale: boolean;
    referencedVersionId: string | null;
}

/** Only S3 versions at least this old are ever deleted: the check-in / collection-files
 * transaction lifetime (expires_at = started_at + 48 h in 03_tables.sql), so an upload that a
 * still-live transaction may yet commit is never touched. */
export const UPLOAD_GRACE_MS = 48 * 60 * 60 * 1000;

/** This job deletes S3 objects across every collection, so it must only run for the service
 * role. The worklist RPC is granted only to service_role too (defense in depth), but reject a
 * non-ops caller early for a clean 403 and so we never touch S3 on their behalf. */
const requireServiceRole = (req: Request): void => {
    const token = (req.headers.get("Authorization") ?? "").replace(
        /^Bearer\s+/i,
        "",
    );
    let role: unknown;
    try {
        const b64 = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
        const padded = b64 + "=".repeat((4 - (b64.length % 4)) % 4);
        role = JSON.parse(atob(padded)).role;
    } catch {
        role = undefined;
    }
    if (role !== "service_role") {
        throw new HttpError(403, { error: "service_role_required" });
    }
};

// Exported so Deno tests can import and call it directly, without triggering Deno.serve
// (see the import.meta.main guard at the bottom).
export const handler = async (
    req: Request,
    _body: Record<string, unknown>,
): Promise<Response> => {
    requireServiceRole(req);

    const worklist = await callTcRpc<GarbageRow[]>(
        req,
        "list_stale_upload_garbage",
        {},
    );

    const client = adminS3Client();
    const { bucket } = s3Env();

    let keysProcessed = 0;
    let versionsDeleted = 0;
    let referencedMissing = 0;
    let keysChanged = 0;
    const cutoff = Date.now() - UPLOAD_GRACE_MS;
    // A key appears once per dead transaction that touched it; one pass per key is enough.
    const seen = new Set<string>();

    for (const row of worklist) {
        if (seen.has(row.s3_key)) continue;
        seen.add(row.s3_key);
        keysProcessed++;
        const versions = await listObjectVersions(client, bucket, row.s3_key);

        let garbage: string[];
        if (row.referenced_version_id === null) {
            // No current manifest references this key, so every uploaded version is orphaned.
            garbage = versions.map((v) => v.versionId);
        } else {
            const refIndex = versions.findIndex(
                (v) => v.versionId === row.referenced_version_id,
            );
            if (refIndex < 0) {
                // The committed version is not in S3 (already reaped, or never uploaded). Do
                // NOT guess which versions are safe to delete -- skip and surface it so it can
                // be investigated (it may mean the lifecycle rule already lost referenced data,
                // which the sweep cadence is meant to prevent).
                referencedMissing++;
                console.warn(
                    `sweep-stale-uploads: referenced version ${row.referenced_version_id} not found for ${row.s3_key}`,
                );
                continue;
            }
            // S3 lists newest-first, so everything before the referenced version is a newer
            // (therefore uncommitted) upload.
            garbage = versions.slice(0, refIndex).map((v) => v.versionId);
        }

        // Grace period: drop anything too young (or with no timestamp -- fail safe).
        const oldEnough = new Set(
            versions
                .filter(
                    (v) =>
                        v.lastModified !== undefined &&
                        v.lastModified.getTime() < cutoff,
                )
                .map((v) => v.versionId),
        );
        garbage = garbage.filter((id) => oldEnough.has(id));
        if (garbage.length === 0) continue;

        // Re-read the key right before deleting: a check-in may have committed a new version
        // (or started uploading this path) since the worklist snapshot.
        const now = await callTcRpc<KeyState>(req, "stale_upload_key_state", {
            p_s3_key: row.s3_key,
        });
        if (
            !now.stillStale ||
            now.referencedVersionId !== row.referenced_version_id
        ) {
            keysChanged++;
            continue;
        }

        for (const versionId of garbage) {
            await deleteObjectVersion(client, bucket, row.s3_key, versionId);
            versionsDeleted++;
        }
    }

    return jsonResponse(200, {
        keysProcessed,
        versionsDeleted,
        referencedMissing,
        keysChanged,
    });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
