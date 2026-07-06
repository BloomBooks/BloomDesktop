// POST /functions/v1/download-start — CONTRACTS.md §download-start
// Req: { collectionId } -> 200 { s3: {...} } read-only creds
// (GetObject + GetObjectVersion) scoped tc/{cid}/*, 1h.
import { requireField, serveJsonPost } from "../_shared/handler.ts";
import { jsonResponse } from "../_shared/errors.ts";
import { callTcRpc } from "../_shared/rpc.ts";
import { getScopedCredentials } from "../_shared/s3.ts";

const DOWNLOAD_ACTIONS = ["s3:GetObject", "s3:GetObjectVersion"];

serveJsonPost(async (req, body) => {
    const collectionId = requireField<string>(body, "collectionId");

    await callTcRpc(req, "download_start_check", { p_collection_id: collectionId });

    const prefix = `tc/${collectionId}/`;
    const s3 = await getScopedCredentials(prefix, DOWNLOAD_ACTIONS);

    return jsonResponse(200, { s3 });
});
