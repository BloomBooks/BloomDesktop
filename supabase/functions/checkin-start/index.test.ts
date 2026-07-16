// Unit tests for checkin-start's handler: PostgREST RPC calls are faked via a fetch
// stub (see _shared/test_support.ts); the MinIO/STS AssumeRole call is faked via
// aws-sdk-client-mock. The live-integration spike (task's Progress log) already
// exercises the real stack end-to-end; these tests pin down the handler's own request
// validation, RPC-argument wiring, and error passthrough cheaply and hermetically.
import { assertEquals } from "jsr:@std/assert@1";
import { AssumeRoleCommand } from "npm:@aws-sdk/client-sts@3";
import {
    callHandler,
    mockRequest,
    routedFetchStub,
    setTestEnv,
    stubAssumeRole,
    withMockFetch,
} from "../_shared/test_support.ts";

setTestEnv();
const { handler } = await import("./index.ts");

const VALID_BODY = {
    collectionId: "11111111-1111-1111-1111-111111111111",
    bookId: null,
    bookInstanceId: "22222222-2222-2222-2222-222222222222",
    proposedName: "My Book",
    checksum: "abc123",
    clientVersion: "1.0.0",
    files: [{ path: "book.htm", sha256: "deadbeef", size: 42 }],
};

Deno.test(
    "checkin-start: happy path returns transactionId, changedPaths and scoped s3 creds",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            {
                when: "rpc/checkin_start_tx",
                status: 200,
                body: {
                    transactionId: "tx-1",
                    bookId: "book-1",
                    changedPaths: ["book.htm"],
                },
            },
            {
                when: "rest/v1/books",
                status: 200,
                body: [{ instance_id: "22222222-2222-2222-2222-222222222222" }],
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(json.transactionId, "tx-1");
        assertEquals(json.changedPaths, ["book.htm"]);
        assertEquals(json.s3.bucket, "bloom-teams-test");
        // CONTRACTS.md: creds scoped to tc/{cid}/books/{instance_id}/* — the instance id read
        // back from the books row, not the request body (see the mismatch test below).
        assertEquals(
            json.s3.prefix,
            "tc/11111111-1111-1111-1111-111111111111/books/22222222-2222-2222-2222-222222222222/",
        );
        assertEquals(json.s3.credentials.sessionToken, "T");
        // bookId is internal-only — CONTRACTS.md's 200 response never exposes it (that's
        // what makes an uncommitted new book invisible until the client re-learns its id
        // via get_collection_state/checkout_book).
        assertEquals("bookId" in json, false);

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: s3 prefix comes from the DB-canonical instance_id, not the caller-supplied bookInstanceId",
    async () => {
        const stsMock = stubAssumeRole();
        // The caller claims an instance id belonging to some OTHER book; the books row for the
        // book actually being checked in has a different (canonical) instance id. The issued
        // credentials must be scoped to the canonical one.
        const fetchStub = routedFetchStub([
            {
                when: "rpc/checkin_start_tx",
                status: 200,
                body: {
                    transactionId: "tx-1",
                    bookId: "book-1",
                    changedPaths: ["book.htm"],
                },
            },
            {
                when: "rest/v1/books",
                status: 200,
                body: [{ instance_id: "99999999-9999-9999-9999-999999999999" }],
            },
        ]);
        const bodyWithForeignInstanceId = {
            ...VALID_BODY,
            bookId: "book-1",
            bookInstanceId: "22222222-2222-2222-2222-222222222222",
        };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(
                handler,
                mockRequest(bodyWithForeignInstanceId),
                bodyWithForeignInstanceId,
            ),
        );

        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(
            json.s3.prefix,
            "tc/11111111-1111-1111-1111-111111111111/books/99999999-9999-9999-9999-999999999999/",
        );

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: missing required field -> 400 before any RPC/S3 call",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([]); // must not be called

        const { checksum: _omit, ...bodyMissingChecksum } = VALID_BODY;
        const res = await withMockFetch(fetchStub, () =>
            callHandler(
                handler,
                mockRequest(bodyMissingChecksum),
                bodyMissingChecksum,
            ),
        );

        assertEquals(res.status, 400);
        const json = await res.json();
        assertEquals(json.error, "invalid_request");
        assertEquals(json.field, "checksum");
        assertEquals(
            stsMock.commandCalls(AssumeRoleCommand).length,
            0,
            "must fail validation before touching S3",
        );

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: RPC 409 LockHeldByOther passes through with the holder payload intact",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            {
                when: "rpc/checkin_start_tx",
                status: 409,
                // PostgREST wraps our RAISE EXCEPTION message like this — see rpc.ts's
                // parsePostgrestErrorBody, which unwraps it back to the flat contract shape.
                body: {
                    message: JSON.stringify({
                        error: "LockHeldByOther",
                        holder: { userId: "u2" },
                    }),
                },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 409);
        const json = await res.json();
        assertEquals(json.error, "LockHeldByOther");
        assertEquals(json.holder.userId, "u2");
        assertEquals(
            stsMock.commandCalls(AssumeRoleCommand).length,
            0,
            "must not issue S3 creds when the RPC itself failed",
        );

        stsMock.restore();
    },
);

Deno.test("checkin-start: RPC 426 ClientOutOfDate passes through", async () => {
    const stsMock = stubAssumeRole();
    const fetchStub = routedFetchStub([
        {
            when: "rpc/checkin_start_tx",
            status: 426,
            body: {
                message: JSON.stringify({
                    error: "ClientOutOfDate",
                    minVersion: "2.0.0",
                }),
            },
        },
    ]);

    const res = await withMockFetch(fetchStub, () =>
        callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
    );

    assertEquals(res.status, 426);
    const json = await res.json();
    assertEquals(json.error, "ClientOutOfDate");
    assertEquals(json.minVersion, "2.0.0");

    stsMock.restore();
});

Deno.test(
    "checkin-start: missing Authorization header -> 401 (defensive; platform normally rejects first)",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([]);

        const reqNoAuth = new Request("http://localhost/test", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(VALID_BODY),
        });
        const res = await callHandler(handler, reqNoAuth, VALID_BODY);

        assertEquals(res.status, 401);
        stsMock.restore();
    },
);
