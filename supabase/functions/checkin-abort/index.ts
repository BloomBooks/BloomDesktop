// POST /functions/v1/checkin-abort — CONTRACTS.md §checkin-abort
// Req: { transactionId } -> 200. Idempotent; rolls back a never-finished new book.
import { requireField, serveJsonPost } from "../_shared/handler.ts";
import { jsonResponse } from "../_shared/errors.ts";
import { callTcRpc } from "../_shared/rpc.ts";

serveJsonPost(async (req, body) => {
    const transactionId = requireField<string>(body, "transactionId");

    await callTcRpc(req, "checkin_abort_tx", { p_transaction_id: transactionId });

    return jsonResponse(200, {});
});
