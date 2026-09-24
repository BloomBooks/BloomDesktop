// Unit tests for collection-files-finish's handler — same shape as checkin-finish but
// scoped to a collection_file_transactions row instead of a book.
import { assertEquals } from "@std/assert";
import { mockClient } from "aws-sdk-client-mock";
import { HeadObjectCommand, S3Client } from "@aws-sdk/client-s3";
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
const { handler } = await import("../collection-files-finish/index.ts");
const { hexToBase64 } = await import("../_shared/tc/s3.ts");

const TX_ROW = {
    id: "tx-1",
    collection_id: "col-1",
    group_key: "allowed-words",
    changed_paths: ["allowed.txt"],
    proposed_files: [
        {
            path: "allowed.txt",
            sha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            size: 0,
        },
    ],
    revision: 2,
};

Deno.test(
    "collection-files-finish: happy path verifies checksum under collectionFiles/{groupKey}/ and returns version",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            VersionId: "v-1",
        });

        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                { when: "rpc/current_caller", status: 200, body: TEST_CALLER },
                {
                    when: "collection_file_transactions",
                    status: 200,
                    body: [TX_ROW],
                },
                {
                    when: "rpc/collection_files_finish_tx",
                    status: 200,
                    body: { version: 4 },
                },
            ],
            calls,
        );

        const res = await withMockFetch(fetchStub, () =>
            callHandler(
                handler,
                mockRequest({ transactionId: "tx-1" }, "callers-own-jwt"),
                { transactionId: "tx-1" },
            ),
        );

        assertEquals(res.status, 200);
        assertEquals((await res.json()).version, 4);

        // Identity from the caller's own JWT; the finish RPC as the service role, told
        // that user id (see rpc.ts).
        const identityCall = calls.find((c) =>
            c.url.includes("rpc/current_caller"),
        );
        const finishCall = calls.find((c) =>
            c.url.includes("rpc/collection_files_finish_tx"),
        );
        if (!identityCall || !finishCall) {
            throw new Error(
                `missing expected calls, got ${calls.map((c) => c.url)}`,
            );
        }
        assertEquals(identityCall.authorization, "Bearer callers-own-jwt");
        assertEquals(finishCall.apikey, TEST_SERVICE_ROLE_KEY);
        assertEquals(
            finishCall.authorization,
            `Bearer ${TEST_SERVICE_ROLE_KEY}`,
        );
        assertEquals(finishCall.body?.p_user_id, TEST_CALLER.userId);
        // The revision read with the verified proposal is passed on (see checkin-finish).
        const txRead = calls.find((c) =>
            c.url.includes("collection_file_transactions"),
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
        assertEquals(finishCall.body?.p_expected_revision, 2);

        const headCalls = s3Mock.commandCalls(HeadObjectCommand);
        assertEquals(headCalls.length, 1);
        assertEquals(
            headCalls[0].args[0].input.Key,
            "tc/col-1/collectionFiles/allowed-words/allowed.txt",
        );

        s3Mock.restore();
    },
);

Deno.test(
    "collection-files-finish: RPC 409 VersionConflict at finish time (repo-wins) passes through",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(TX_ROW.proposed_files[0].sha256),
            VersionId: "v-1",
        });

        const fetchStub = routedFetchStub([
            { when: "rpc/current_caller", status: 200, body: TEST_CALLER },
            {
                when: "collection_file_transactions",
                status: 200,
                body: [TX_ROW],
            },
            {
                when: "rpc/collection_files_finish_tx",
                status: 409,
                body: {
                    message: JSON.stringify({
                        error: "VersionConflict",
                        currentVersion: 7,
                    }),
                },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "tx-1" }), {
                transactionId: "tx-1",
            }),
        );

        assertEquals(res.status, 409);
        const json = await res.json();
        assertEquals(json.error, "VersionConflict");
        assertEquals(json.currentVersion, 7);

        s3Mock.restore();
    },
);

Deno.test(
    "collection-files-finish: unknown transactionId -> 404 before any S3 call",
    async () => {
        const s3Mock = mockClient(S3Client);
        const fetchStub = routedFetchStub([
            { when: "rpc/current_caller", status: 200, body: TEST_CALLER },
            { when: "collection_file_transactions", status: 200, body: [] },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ transactionId: "nope" }), {
                transactionId: "nope",
            }),
        );

        assertEquals(res.status, 404);
        assertEquals((await res.json()).error, "transaction_not_found");
        assertEquals(s3Mock.commandCalls(HeadObjectCommand).length, 0);

        s3Mock.restore();
    },
);
