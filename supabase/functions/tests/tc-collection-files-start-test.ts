// Unit tests for collection-files-start's handler: groupKey validation, the
// optimistic-version RPC call, and scoped S3 credential issuance.
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
const { handler } = await import("../collection-files-start/index.ts");

const VALID_BODY = {
    collectionId: "col-1",
    groupKey: "allowed-words",
    expectedVersion: 0,
    files: [{ path: "allowed.txt", sha256: "abc", size: 3 }],
};

Deno.test(
    "collection-files-start: happy path scopes creds under collectionFiles/{groupKey}/",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            {
                when: "rpc/collection_files_start_tx",
                status: 200,
                body: { transactionId: "tx-1", changedPaths: ["allowed.txt"] },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(json.transactionId, "tx-1");
        assertEquals(json.s3.prefix, "tc/col-1/collectionFiles/allowed-words/");

        stsMock.restore();
    },
);

Deno.test(
    "collection-files-start: invalid groupKey -> 400 before any RPC/S3 call",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([]);
        const badBody = { ...VALID_BODY, groupKey: "not-a-real-group" };

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(badBody), badBody),
        );

        assertEquals(res.status, 400);
        assertEquals((await res.json()).field, "groupKey");
        assertEquals(stsMock.commandCalls(AssumeRoleCommand).length, 0);

        stsMock.restore();
    },
);

Deno.test(
    "collection-files-start: RPC 409 VersionConflict passes through with currentVersion",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            {
                when: "rpc/collection_files_start_tx",
                status: 409,
                body: {
                    message: JSON.stringify({
                        error: "VersionConflict",
                        currentVersion: 5,
                    }),
                },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
        );

        assertEquals(res.status, 409);
        const json = await res.json();
        assertEquals(json.error, "VersionConflict");
        assertEquals(json.currentVersion, 5);
        // Credentials are obtained before the RPC (see the STS-failure test below), but a
        // refused start must never hand them out.
        assertEquals(
            "s3" in json,
            false,
            "must not return creds on a version conflict",
        );

        stsMock.restore();
    },
);

Deno.test(
    "collection-files-start: an STS failure happens before collection_files_start_tx, so no transaction is opened",
    async () => {
        const stsMock = stubAssumeRole();
        stsMock
            .on(AssumeRoleCommand)
            .rejects(new Error("simulated STS outage"));
        const calls: RecordedCall[] = [];
        const fetchStub = routedFetchStub(
            [
                {
                    when: "rpc/collection_files_start_tx",
                    status: 200,
                    body: {
                        transactionId: "tx-1",
                        changedPaths: ["allowed.txt"],
                    },
                },
            ],
            calls,
        );

        await assertRejects(
            () =>
                withMockFetch(fetchStub, () =>
                    callHandler(handler, mockRequest(VALID_BODY), VALID_BODY),
                ),
            Error,
            "simulated STS outage",
        );
        assertEquals(
            stsMock.commandCalls(AssumeRoleCommand).length,
            1,
            "sanity check: STS was really asked (and failed)",
        );
        assertEquals(
            calls.some((c) => c.url.includes("rpc/collection_files_start_tx")),
            false,
            "collection_files_start_tx must not run once STS has failed",
        );

        stsMock.restore();
    },
);
