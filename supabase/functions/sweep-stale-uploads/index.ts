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
import { HttpError, jsonResponse } from "../_shared/errors.ts";
import { serveJsonPost } from "../_shared/handler.ts";
import { callTcRpc } from "../_shared/rpc.ts";
import {
    adminS3Client,
    deleteObjectVersion,
    listObjectVersions,
} from "../_shared/s3.ts";
import { s3Env } from "../_shared/env.ts";

interface GarbageRow {
    transaction_kind: string;
    transaction_id: string;
    s3_key: string;
    referenced_version_id: string | null;
}

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

    for (const row of worklist) {
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

        for (const versionId of garbage) {
            await deleteObjectVersion(client, bucket, row.s3_key, versionId);
            versionsDeleted++;
        }
    }

    return jsonResponse(200, {
        keysProcessed,
        versionsDeleted,
        referencedMissing,
    });
};

if (import.meta.main) {
    serveJsonPost(handler);
}
