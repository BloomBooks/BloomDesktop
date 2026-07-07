// Unit tests for checkin-finish's handler. PostgREST calls (both the selectTcRow reads
// of checkin_transactions/books and the checkin_finish_tx RPC) are faked via a fetch
// stub; the S3 HeadObject checksum verification is faked via aws-sdk-client-mock. The
// live-integration spike already exercises the real MinIO checksum round-trip; these
// tests pin down the handler's own wiring: which paths get verified, what gets sent to
// the RPC, and error passthrough.
import { assertEquals } from "jsr:@std/assert@1";
import { mockClient } from "npm:aws-sdk-client-mock@4";
import { HeadObjectCommand, PutObjectCommand, S3Client } from "npm:@aws-sdk/client-s3@3";
import { callHandler, mockRequest, routedFetchStub, setTestEnv, withMockFetch } from "../_shared/test_support.ts";

setTestEnv();
const { handler } = await import("./index.ts");
const { hexToBase64 } = await import("../_shared/s3.ts");

const TX_ROW = {
    id: "tx-1",
    collection_id: "col-1",
    book_id: "book-1",
    changed_paths: ["book.htm"],
    proposed_files: [{ path: "book.htm", sha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", size: 0 }],
    status: "open",
};
const BOOK_ROW = { instance_id: "instance-1" };

const routesFor = (txRow: unknown, bookRow: unknown, finishStatus: number, finishBody: unknown) =>
    routedFetchStub([
        { when: "checkin_transactions", status: txRow ? 200 : 200, body: txRow ? [txRow] : [] },
        { when: "/books?", status: bookRow ? 200 : 200, body: bookRow ? [bookRow] : [] },
        { when: "rpc/checkin_finish_tx", status: finishStatus, body: finishBody },
    ]);

Deno.test("checkin-finish: happy path verifies checksum, captures version-id, returns versionId+seq", async () => {
    const s3Mock = mockClient(S3Client);
    s3Mock.on(HeadObjectCommand).resolves({
        ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
        VersionId: "v-42",
    });

    const fetchStub = routesFor(TX_ROW, BOOK_ROW, 200, { versionId: "ver-1", seq: 3 });

    const res = await withMockFetch(
        fetchStub,
        () => callHandler(handler, mockRequest({ transactionId: "tx-1" }), { transactionId: "tx-1" }),
    );

    assertEquals(res.status, 200);
    const json = await res.json();
    assertEquals(json.versionId, "ver-1");
    assertEquals(json.seq, 3);

    // The HeadObject must have been issued against the right key (prefix + path).
    const headCalls = s3Mock.commandCalls(HeadObjectCommand);
    assertEquals(headCalls.length, 1);
    assertEquals(headCalls[0].args[0].input.Key, "tc/col-1/books/instance-1/book.htm");

    s3Mock.restore();
});

Deno.test("checkin-finish: unverifiable upload is omitted from `captured` (DB RPC reports MissingOrBadUploads)", async () => {
    const s3Mock = mockClient(S3Client);
    s3Mock.on(HeadObjectCommand).rejects(new Error("NotFound")); // never uploaded

    let capturedSentToRpc: unknown;
    const fetchStub: typeof fetch = (input, init) => {
        const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
        if (url.includes("checkin_transactions")) {
            return Promise.resolve(new Response(JSON.stringify([TX_ROW]), { status: 200 }));
        }
        if (url.includes("/books?")) {
            return Promise.resolve(new Response(JSON.stringify([BOOK_ROW]), { status: 200 }));
        }
        if (url.includes("rpc/checkin_finish_tx")) {
            capturedSentToRpc = JSON.parse(String(init?.body)).p_captured;
            return Promise.resolve(
                new Response(
                    JSON.stringify({ message: JSON.stringify({ error: "MissingOrBadUploads", paths: ["book.htm"] }) }),
                    { status: 409 },
                ),
            );
        }
        throw new Error(`unexpected fetch: ${url}`);
    };

    const res = await withMockFetch(
        fetchStub,
        () => callHandler(handler, mockRequest({ transactionId: "tx-1" }), { transactionId: "tx-1" }),
    );

    assertEquals(res.status, 409);
    const json = await res.json();
    assertEquals(json.error, "MissingOrBadUploads");
    assertEquals(json.paths, ["book.htm"]);
    // The edge function must not have fabricated a captured entry for the failed path —
    // it lets the DB-side check (which independently re-verifies) report the gap.
    assertEquals(capturedSentToRpc, [], "unverified path must not appear in p_captured");

    s3Mock.restore();
});

Deno.test("checkin-finish: unknown transactionId -> 404 before any S3 call", async () => {
    const s3Mock = mockClient(S3Client);
    const fetchStub = routedFetchStub([
        { when: "checkin_transactions", status: 200, body: [] }, // selectTcRow finds nothing
    ]);

    const res = await withMockFetch(
        fetchStub,
        () => callHandler(handler, mockRequest({ transactionId: "nope" }), { transactionId: "nope" }),
    );

    assertEquals(res.status, 404);
    const json = await res.json();
    assertEquals(json.error, "transaction_not_found");
    assertEquals(s3Mock.commandCalls(HeadObjectCommand).length, 0);

    s3Mock.restore();
});

Deno.test("checkin-finish: writes a .manifest.json backup when the RPC returns one, but it never affects the response", async () => {
    const s3Mock = mockClient(S3Client);
    s3Mock.on(HeadObjectCommand).resolves({
        ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
        VersionId: "v-1",
    });
    s3Mock.on(PutObjectCommand).rejects(new Error("simulated backup-write outage"));

    const fetchStub = routesFor(TX_ROW, BOOK_ROW, 200, {
        versionId: "ver-9", seq: 9, manifest: [{ path: "book.htm" }],
    });

    const res = await withMockFetch(
        fetchStub,
        () => callHandler(handler, mockRequest({ transactionId: "tx-1" }), { transactionId: "tx-1" }),
    );

    // Even though the manifest backup PUT fails, the client-facing response must be
    // unaffected (writeManifestBackup is documented best-effort/never-throws).
    assertEquals(res.status, 200);
    const json = await res.json();
    assertEquals(json.versionId, "ver-9");
    assertEquals("manifest" in json, false, "the internal `manifest` field must never leak to the client");

    s3Mock.restore();
});
