// Unit tests for checkin-abort's handler — the thinnest of the six, so mostly pinning
// down request validation and RPC error/argument passthrough.
import { assertEquals } from "@std/assert";
import {
    callHandler,
    mockRequest,
    routedFetchStub,
    setTestEnv,
    withMockFetch,
} from "../_shared/tc/test_support.ts";

setTestEnv();
const { handler } = await import("../checkin-abort/index.ts");

Deno.test(
    "checkin-abort: happy path calls checkin_abort_tx with the transactionId and returns 200 {}",
    async () => {
        let sentArgs: unknown;
        const fetchStub: typeof fetch = (input, init) => {
            sentArgs = JSON.parse(String(init?.body));
            return Promise.resolve(new Response("", { status: 200 }));
        };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 200);
        assertEquals(await res.json(), {});
        assertEquals(sentArgs, { p_transaction_id: "tx-1" });
    },
);

Deno.test(
    "checkin-abort: missing transactionId -> 400 before any RPC call",
    async () => {
        const fetchStub = routedFetchStub([]); // must not be called
        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({}), {}),
        );

        assertEquals(res.status, 400);
        const json = await res.json();
        assertEquals(json.error, "invalid_request");
        assertEquals(json.field, "transactionId");
    },
);

Deno.test(
    "checkin-abort: RPC 409 already_finished passes through",
    async () => {
        const fetchStub = routedFetchStub([
            {
                when: "rpc/checkin_abort_tx",
                status: 409,
                body: {
                    message: JSON.stringify({ error: "already_finished" }),
                },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 409);
        assertEquals((await res.json()).error, "already_finished");
    },
);

Deno.test(
    "checkin-abort: a transaction that no longer exists (RPC no-op success) -> 200 {}",
    async () => {
        // checkin_abort_tx returns normally for an unknown id: aborting a never-committed new
        // book deletes its transaction, so a retry after a lost response must still succeed.
        const fetchStub = routedFetchStub([
            { when: "rpc/checkin_abort_tx", status: 200, body: null },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "gone" }), {
                transactionId: "gone",
            }),
        );

        assertEquals(res.status, 200);
        assertEquals(await res.json(), {});
    },
);

Deno.test(
    "checkin-abort: RPC 403 forbidden (someone else's transaction) passes through",
    async () => {
        const fetchStub = routedFetchStub([
            {
                when: "rpc/checkin_abort_tx",
                status: 403,
                body: { message: JSON.stringify({ error: "forbidden" }) },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-other" }), {
                transactionId: "tx-other",
            }),
        );

        assertEquals(res.status, 403);
        assertEquals((await res.json()).error, "forbidden");
    },
);
