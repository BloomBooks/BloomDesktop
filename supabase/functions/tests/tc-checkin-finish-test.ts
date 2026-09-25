// Unit tests for checkin-finish's handler. PostgREST calls (both the selectTcRow reads
// of checkin_transactions/books and the checkin_finish_tx RPC) are faked via a fetch
// stub; the S3 HeadObject checksum verification is faked via aws-sdk-client-mock. The
// live-integration spike already exercises the real MinIO checksum round-trip; these
// tests pin down the handler's own wiring: which paths get verified, what gets sent to
// the RPC, and error passthrough.
import { assertEquals } from "@std/assert";
import { mockClient } from "aws-sdk-client-mock";
import {
    HeadObjectCommand,
    PutObjectCommand,
    S3Client,
} from "@aws-sdk/client-s3";
import {
    callHandler,
    mockRequest,
    type RecordedCall,
    routedFetchStub,
    setTestEnv,
    TEST_CALLER,
    TEST_SERVICE_ROLE_KEY,
    withMockFetch,
} from "../_shared/tc/test_support.ts";

setTestEnv();
const { handler } = await import("../checkin-finish/index.ts");
const { hexToBase64 } = await import("../_shared/tc/s3.ts");

const TX_ROW = {
    id: "tx-1",
    collection_id: "col-1",
    book_id: "book-1",
    changed_paths: ["book.htm"],
    proposed_files: [
        {
            path: "book.htm",
            sha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            size: 0,
        },
    ],
    status: "open",
    // Resumed a few times by checkin-start; finish must pass this exact value on.
    revision: 3,
};
const BOOK_ROW = { instance_id: "instance-1" };

const routesFor = (
    txRow: unknown,
    bookRow: unknown,
    finishStatus: number,
    finishBody: unknown,
    calls?: RecordedCall[],
) =>
    routedFetchStub(
        [
            { when: "rpc/current_caller", status: 200, body: TEST_CALLER },
            {
                when: "checkin_transactions",
                status: txRow ? 200 : 200,
                body: txRow ? [txRow] : [],
            },
            {
                when: "/books?",
                status: bookRow ? 200 : 200,
                body: bookRow ? [bookRow] : [],
            },
            {
                when: "rpc/checkin_finish_tx",
                status: finishStatus,
                body: finishBody,
            },
        ],
        calls,
    );

Deno.test(
    "checkin-finish: happy path verifies checksum, captures version-id, returns versionId+seq",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            LastModified: new Date(), // a fresh upload, inside the commit window
            VersionId: "v-42",
        });

        const fetchStub = routesFor(TX_ROW, BOOK_ROW, 200, {
            versionId: "ver-1",
            seq: 3,
        });

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(json.versionId, "ver-1");
        assertEquals(json.seq, 3);

        // The HeadObject must have been issued against the right key (prefix + path).
        const headCalls = s3Mock.commandCalls(HeadObjectCommand);
        assertEquals(headCalls.length, 1);
        assertEquals(
            headCalls[0].args[0].input.Key,
            "tc/col-1/books/instance-1/book.htm",
        );

        s3Mock.restore();
    },
);

Deno.test(
    "checkin-finish: unverifiable upload is omitted from `captured` (DB RPC reports MissingOrBadUploads)",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).rejects(new Error("NotFound")); // never uploaded

        let capturedSentToRpc: unknown;
        const fetchStub: typeof fetch = (input, init) => {
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                      ? input.href
                      : input.url;
            if (url.includes("rpc/current_caller")) {
                return Promise.resolve(
                    new Response(JSON.stringify(TEST_CALLER), { status: 200 }),
                );
            }
            if (url.includes("checkin_transactions")) {
                return Promise.resolve(
                    new Response(JSON.stringify([TX_ROW]), { status: 200 }),
                );
            }
            if (url.includes("/books?")) {
                return Promise.resolve(
                    new Response(JSON.stringify([BOOK_ROW]), { status: 200 }),
                );
            }
            if (url.includes("rpc/checkin_finish_tx")) {
                capturedSentToRpc = JSON.parse(String(init?.body)).p_captured;
                return Promise.resolve(
                    new Response(
                        JSON.stringify({
                            message: JSON.stringify({
                                error: "MissingOrBadUploads",
                                paths: ["book.htm"],
                            }),
                        }),
                        { status: 409 },
                    ),
                );
            }
            throw new Error(`unexpected fetch: ${url}`);
        };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 409);
        const json = await res.json();
        assertEquals(json.error, "MissingOrBadUploads");
        assertEquals(json.paths, ["book.htm"]);
        // The edge function must not have fabricated a captured entry for the failed path —
        // it lets the DB-side check (which independently re-verifies) report the gap.
        assertEquals(
            capturedSentToRpc,
            [],
            "unverified path must not appear in p_captured",
        );
        assertEquals("stalePaths" in json, false, "nothing was stale");

        s3Mock.restore();
    },
);

for (const [label, lastModified] of [
    [
        "older than the commit window",
        new Date(Date.now() - 25 * 60 * 60 * 1000),
    ],
    ["with no LastModified", undefined],
] as const) {
    Deno.test(
        `checkin-finish: a verified upload ${label} is not committed; the 409 names it in stalePaths`,
        async () => {
            const s3Mock = mockClient(S3Client);
            s3Mock.on(HeadObjectCommand).resolves({
                ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
                LastModified: lastModified,
                VersionId: "v-old",
            });
            const calls: RecordedCall[] = [];
            const fetchStub = routesFor(
                TX_ROW,
                BOOK_ROW,
                409,
                {
                    message: JSON.stringify({
                        error: "MissingOrBadUploads",
                        paths: ["book.htm"],
                    }),
                },
                calls,
            );

            const res = await withMockFetch(fetchStub, () =>
                callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                    transactionId: "tx-1",
                }),
            );

            assertEquals(
                s3Mock.commandCalls(HeadObjectCommand).length,
                1,
                "sanity check: the upload really was looked at (and its checksum matched)",
            );
            const rpcCall = calls.find((c) =>
                c.url.includes("rpc/checkin_finish_tx"),
            );
            assertEquals(
                rpcCall?.body?.p_captured,
                [],
                "a stale upload must not be captured",
            );
            assertEquals(res.status, 409);
            assertEquals(await res.json(), {
                error: "MissingOrBadUploads",
                paths: ["book.htm"],
                stalePaths: ["book.htm"],
            });

            s3Mock.restore();
        },
    );
}

Deno.test(
    "checkin-finish: an upload just inside the commit window is still committed",
    async () => {
        const { UPLOAD_COMMIT_WINDOW_MS } = await import(
            "../_shared/tc/uploadWindows.ts"
        );
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            LastModified: new Date(
                Date.now() - UPLOAD_COMMIT_WINDOW_MS + 60 * 1000,
            ),
            VersionId: "v-recent",
        });
        const calls: RecordedCall[] = [];
        const fetchStub = routesFor(
            TX_ROW,
            BOOK_ROW,
            200,
            { versionId: "ver-1", seq: 3 },
            calls,
        );

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 200);
        const rpcCall = calls.find((c) =>
            c.url.includes("rpc/checkin_finish_tx"),
        );
        assertEquals(rpcCall?.body?.p_captured, [
            { path: "book.htm", s3VersionId: "v-recent" },
        ]);

        s3Mock.restore();
    },
);

Deno.test(
    "checkin-finish: unknown transactionId -> 404 before any S3 call",
    async () => {
        const s3Mock = mockClient(S3Client);
        const fetchStub = routedFetchStub([
            { when: "rpc/current_caller", status: 200, body: TEST_CALLER },
            { when: "checkin_transactions", status: 200, body: [] }, // selectTcRow finds nothing
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "nope" }), {
                transactionId: "nope",
            }),
        );

        assertEquals(res.status, 404);
        const json = await res.json();
        assertEquals(json.error, "transaction_not_found");
        assertEquals(s3Mock.commandCalls(HeadObjectCommand).length, 0);

        s3Mock.restore();
    },
);

Deno.test(
    "checkin-finish: identity comes from the caller's own JWT; the finish RPC is called with the service-role key and that user id",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            LastModified: new Date(), // a fresh upload, inside the commit window
            VersionId: "v-42",
        });
        const calls: RecordedCall[] = [];
        const fetchStub = routesFor(
            TX_ROW,
            BOOK_ROW,
            200,
            { versionId: "ver-1", seq: 3 },
            calls,
        );

        const res = await withMockFetch(fetchStub, () =>
            callHandler(
                handler,
                mockRequest({ transactionId: "tx-1" }, "callers-own-jwt"),
                { transactionId: "tx-1", userId: "someone-else" },
            ),
        );
        assertEquals(res.status, 200);

        const identityCall = calls.find((c) =>
            c.url.includes("rpc/current_caller"),
        );
        const finishCall = calls.find((c) =>
            c.url.includes("rpc/checkin_finish_tx"),
        );
        if (!identityCall || !finishCall) {
            throw new Error(
                `expected both current_caller and checkin_finish_tx calls, got ${calls.map((c) => c.url)}`,
            );
        }
        assertEquals(identityCall.authorization, "Bearer callers-own-jwt");
        assertEquals(identityCall.apikey, "test-anon-key");
        assertEquals(
            calls.indexOf(identityCall) < calls.indexOf(finishCall),
            true,
            "identity must be established before the finish RPC",
        );
        // The finish RPC never sees the caller's token: it runs as the service role...
        assertEquals(finishCall.apikey, TEST_SERVICE_ROLE_KEY);
        assertEquals(
            finishCall.authorization,
            `Bearer ${TEST_SERVICE_ROLE_KEY}`,
        );
        // ...and is told who the caller is by the identity call, not by the request body.
        assertEquals(finishCall.body?.p_user_id, TEST_CALLER.userId);
        assertEquals(finishCall.body?.p_user_email, TEST_CALLER.email);
        assertEquals(finishCall.body?.p_user_name, TEST_CALLER.name);
        assertEquals(finishCall.body?.p_captured, [
            { path: "book.htm", s3VersionId: "v-42" },
        ]);
        // The revision read with the proposal just verified goes to the RPC, which refuses
        // (TransactionChanged) if a concurrent checkin-start resume has changed it since.
        const txRead = calls.find((c) =>
            c.url.includes("checkin_transactions"),
        );
        if (!txRead) {
            throw new Error("the transaction row was never read");
        }
        assertEquals(
            new URL(txRead.url).searchParams
                .get("select")
                ?.split(",")
                .includes("revision"),
            true,
            "the transaction read must fetch the revision with the proposal",
        );
        assertEquals(finishCall.body?.p_expected_revision, 3);

        s3Mock.restore();
    },
);

Deno.test(
    "checkin-finish: a token PostgREST rejects -> 401, with no S3 work and no service-role call",
    async () => {
        const s3Mock = mockClient(S3Client);
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rpc/current_caller",
                    status: 401,
                    body: { message: "JWT expired", code: "PGRST303" },
                },
            ],
            calls,
        );

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 401);
        assertEquals(calls.length, 1, "only the identity call may happen");
        assertEquals(s3Mock.commandCalls(HeadObjectCommand).length, 0);
        s3Mock.restore();
    },
);

Deno.test(
    "checkin-finish: writes a .manifest.json backup when the RPC returns one, but it never affects the response",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            LastModified: new Date(), // a fresh upload, inside the commit window
            VersionId: "v-1",
        });
        s3Mock
            .on(PutObjectCommand)
            .rejects(new Error("simulated backup-write outage"));

        const fetchStub = routesFor(TX_ROW, BOOK_ROW, 200, {
            versionId: "ver-9",
            seq: 9,
            manifest: [{ path: "book.htm" }],
        });

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        // Even though the manifest backup PUT fails, the client-facing response must be
        // unaffected (writeManifestBackup is documented best-effort/never-throws).
        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(json.versionId, "ver-9");
        assertEquals(
            "manifest" in json,
            false,
            "the internal `manifest` field must never leak to the client",
        );

        s3Mock.restore();
    },
);

Deno.test(
    "checkin-finish: RPC 409 TransactionChanged (a concurrent resume) passes through, with no manifest backup",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            LastModified: new Date(), // a fresh upload, inside the commit window
            VersionId: "v-42",
        });

        const fetchStub = routesFor(TX_ROW, BOOK_ROW, 409, {
            message: JSON.stringify({ error: "TransactionChanged" }),
        });

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 409);
        assertEquals(await res.json(), { error: "TransactionChanged" });
        assertEquals(s3Mock.commandCalls(PutObjectCommand).length, 0);

        s3Mock.restore();
    },
);
