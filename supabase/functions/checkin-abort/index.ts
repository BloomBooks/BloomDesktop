// POST /functions/v1/checkin-abort — CONTRACTS.md §checkin-abort
// Req: { transactionId } -> 200. Idempotent; rolls back a never-finished new book.
import { requireField, serveJsonPost } from "../_shared/handler.ts";
import { jsonResponse } from "../_shared/errors.ts";
import { callTcRpc } from "../_shared/rpc.ts";

// Exported so Deno tests can import and call it directly — see checkin-start/index.ts's
// comment on the `import.meta.main` guard below.
export const handler = async (req: Request, body: Record<string, unknown>): Promise<Response> => {
    const transactionId = requireField<string>(body, "transactionId");

    await callTcRpc(req, "checkin_abort_tx", { p_transaction_id: transactionId });

    return jsonResponse(200, {});
};

if (import.meta.main) {
    serveJsonPost(handler);
}
