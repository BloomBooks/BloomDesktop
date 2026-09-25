// Unit tests for checkin-start's handler: PostgREST RPC calls are faked via a fetch
// stub (see _shared/tc/test_support.ts); the MinIO/STS AssumeRole call is faked via
// aws-sdk-client-mock. The live-integration spike (task's Progress log) already
// exercises the real stack end-to-end; these tests pin down the handler's own request
// validation, RPC-argument wiring, and error passthrough cheaply and hermetically.
import { assertEquals, assertRejects } from "@std/assert";
import { AssumeRoleCommand } from "@aws-sdk/client-sts";
import {
    callHandler,
    mockRequest,
    type RecordedCall,
    routedFetchStub,
    setTestEnv,
    stubAssumeRole,
    withMockFetch,
} from "../_shared/tc/test_support.ts";

setTestEnv();
const { handler } = await import("../checkin-start/index.ts");

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
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rpc/checkin_start_tx",
                    status: 200,
                    body: {
                        transactionId: "tx-1",
                        bookId: "book-1",
                        changedPaths: ["book.htm"],
                    },
                },
            ],
            calls,
        );
        assertEquals(
            VALID_BODY.bookId,
            null,
            "test data sanity check: a new book",
        );

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(json.transactionId, "tx-1");
        assertEquals(json.changedPaths, ["book.htm"]);
        assertEquals(json.s3.bucket, "bloom-teams-test");
        // CONTRACTS.md: creds scoped to tc/{cid}/books/{instance_id}/*. For a new book that is
        // the request's bookInstanceId (the RPC only creates or resumes a row with that
        // instance id), so no books read is needed; an existing book's instance id is read
        // from its row (see the mismatch test below).
        assertEquals(
            calls.map((c) => new URL(c.url).pathname),
            ["/rest/v1/rpc/checkin_start_tx"],
        );
        assertEquals(
            json.s3.prefix,
            "tc/11111111-1111-1111-1111-111111111111/books/22222222-2222-2222-2222-222222222222/",
        );
        assertEquals(json.s3.credentials.sessionToken, "T");
        // bookId is internal-only — CONTRACTS.md's 200 response never exposes it (that's
        // what makes an uncommitted new book invisible until the client re-learns its id
        // via get_collection_state/checkout_book).
        assertEquals("bookId" in json, false);
        // v1.10: check-in never issues a checkout GUID.
        assertEquals("checkoutGuid" in json, false);

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
        // Credentials are obtained before the RPC (see the ordering tests below), but a
        // refused start must never hand them out.
        assertEquals(
            "s3" in json,
            false,
            "must not return S3 creds when the RPC failed",
        );

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: forwards checkoutGuid as p_checkout_guid and never returns a checkoutGuid",
    async () => {
        const stsMock = stubAssumeRole();
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rpc/checkin_start_tx",
                    status: 200,
                    // A stray checkoutGuid (v1.9's RPC returned one) must not be passed on:
                    // the response carries only the contract's fields.
                    body: {
                        transactionId: "tx-1",
                        bookId: "book-1",
                        changedPaths: ["book.htm"],
                        checkoutGuid: "0b7c5d4e-1f2a-4b3c-8d9e-0a1b2c3d4e5f",
                    },
                },
                {
                    when: "rest/v1/books",
                    status: 200,
                    body: [
                        { instance_id: "22222222-2222-2222-2222-222222222222" },
                    ],
                },
            ],
            calls,
        );
        const bodyWithGuid = {
            ...VALID_BODY,
            bookId: "book-1",
            checkoutGuid: "3f2c9a1e-5b6d-4c7e-8f90-a1b2c3d4e5f6",
        };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(bodyWithGuid), bodyWithGuid),
        );

        assertEquals(res.status, 200);
        const rpcCall = calls.find((c) =>
            c.url.includes("rpc/checkin_start_tx"),
        );
        if (!rpcCall) {
            throw new Error("checkin_start_tx was never called");
        }
        assertEquals(
            rpcCall.body?.p_checkout_guid,
            "3f2c9a1e-5b6d-4c7e-8f90-a1b2c3d4e5f6",
        );
        const json = await res.json();
        assertEquals(
            json.transactionId,
            "tx-1",
            "sanity check: a real 200 body",
        );
        assertEquals("checkoutGuid" in json, false);

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: no checkoutGuid in the body -> p_checkout_guid is null",
    async () => {
        const stsMock = stubAssumeRole();
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rpc/checkin_start_tx",
                    status: 200,
                    body: {
                        transactionId: "tx-1",
                        bookId: "book-1",
                        changedPaths: [],
                    },
                },
                {
                    when: "rest/v1/books",
                    status: 200,
                    body: [
                        { instance_id: "22222222-2222-2222-2222-222222222222" },
                    ],
                },
            ],
            calls,
        );
        assertEquals(
            "checkoutGuid" in VALID_BODY,
            false,
            "test data sanity check",
        );

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 200);
        const rpcCall = calls.find((c) =>
            c.url.includes("rpc/checkin_start_tx"),
        );
        if (!rpcCall) {
            throw new Error("checkin_start_tx was never called");
        }
        assertEquals(rpcCall.body?.p_checkout_guid, null);

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: RPC 409 CheckoutElsewhere passes through as a flat error envelope, with no S3 creds",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            {
                when: "rpc/checkin_start_tx",
                status: 409,
                body: {
                    message: JSON.stringify({ error: "CheckoutElsewhere" }),
                },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 409);
        const json = await res.json();
        // Exactly the error envelope: no S3 creds are handed out.
        assertEquals(json, { error: "CheckoutElsewhere" });

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

// checkin_start_tx can commit a new checkout GUID that only this response carries back, so
// everything that can fail (the books read, STS) must happen before it; see the handler.
Deno.test(
    "checkin-start: an STS failure happens before checkin_start_tx, so nothing is committed",
    async () => {
        const stsMock = stubAssumeRole();
        stsMock
            .on(AssumeRoleCommand)
            .rejects(new Error("simulated STS outage"));
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rest/v1/books",
                    status: 200,
                    body: [
                        { instance_id: "99999999-9999-9999-9999-999999999999" },
                    ],
                },
                {
                    when: "rpc/checkin_start_tx",
                    status: 200,
                    body: {
                        transactionId: "tx-1",
                        bookId: "book-1",
                        changedPaths: [],
                    },
                },
            ],
            calls,
        );

        for (const body of [VALID_BODY, { ...VALID_BODY, bookId: "book-1" }]) {
            calls.length = 0;
            const before = stsMock.commandCalls(AssumeRoleCommand).length;
            await assertRejects(
                () =>
                    withMockFetch(fetchStub, () =>
                        callHandler(handler, mockRequest(body), body),
                    ),
                Error,
                "simulated STS outage",
            );
            assertEquals(
                stsMock.commandCalls(AssumeRoleCommand).length,
                before + 1,
                "sanity check: STS was really asked (and failed)",
            );
            assertEquals(
                calls.some((c) => c.url.includes("rpc/checkin_start_tx")),
                false,
                `checkin_start_tx must not run once STS has failed (bookId ${body.bookId})`,
            );
        }

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: an existing book's instance id is read, and credentials issued, before checkin_start_tx",
    async () => {
        const stsMock = stubAssumeRole();
        const calls: RecordedCall[] = [];
        const rpcCallsAtEachStsCall: number[] = [];
        stsMock.on(AssumeRoleCommand).callsFake(() => {
            rpcCallsAtEachStsCall.push(
                calls.filter((c) => c.url.includes("rpc/checkin_start_tx"))
                    .length,
            );
            return {
                Credentials: {
                    AccessKeyId: "K",
                    SecretAccessKey: "S",
                    SessionToken: "T",
                    Expiration: new Date("2026-01-01T01:00:00Z"),
                },
            };
        });
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rest/v1/books",
                    status: 200,
                    body: [
                        { instance_id: "99999999-9999-9999-9999-999999999999" },
                    ],
                },
                {
                    when: "rpc/checkin_start_tx",
                    status: 200,
                    body: {
                        transactionId: "tx-1",
                        bookId: "book-1",
                        changedPaths: ["book.htm"],
                    },
                },
            ],
            calls,
        );
        const body = { ...VALID_BODY, bookId: "book-1" };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(body), body),
        );

        assertEquals(res.status, 200);
        assertEquals(
            calls.map((c) => new URL(c.url).pathname),
            ["/rest/v1/books", "/rest/v1/rpc/checkin_start_tx"],
            "the books read must come before the RPC",
        );
        assertEquals(
            rpcCallsAtEachStsCall,
            [0],
            "STS must be called once, before the RPC",
        );
        const json = await res.json();
        assertEquals(json.changedPaths, ["book.htm"]);
        assertEquals(
            json.s3.prefix,
            "tc/11111111-1111-1111-1111-111111111111/books/99999999-9999-9999-9999-999999999999/",
        );

        stsMock.restore();
    },
);

Deno.test(
    "checkin-start: an existing book the caller cannot see -> 404, without calling checkin_start_tx",
    async () => {
        const stsMock = stubAssumeRole();
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [{ when: "rest/v1/books", status: 200, body: [] }],
            calls,
        );
        const body = { ...VALID_BODY, bookId: "book-1" };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(body), body),
        );

        assertEquals(res.status, 404);
        assertEquals((await res.json()).error, "book_not_found");
        assertEquals(calls.length, 1, "only the books read may happen");
        assertEquals(stsMock.commandCalls(AssumeRoleCommand).length, 0);

        stsMock.restore();
    },
);
