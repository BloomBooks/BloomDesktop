// Unit tests for collection-files-start's handler: groupKey validation, the
// optimistic-version RPC call, and scoped S3 credential issuance.
import { assertEquals } from "jsr:@std/assert@1";
import { mockClient } from "npm:aws-sdk-client-mock@4";
import { AssumeRoleCommand, STSClient } from "npm:@aws-sdk/client-sts@3";
import {
    callHandler,
    mockRequest,
    routedFetchStub,
    setTestEnv,
    withMockFetch,
} from "../_shared/test_support.ts";

setTestEnv();
const { handler } = await import("./index.ts");

const VALID_BODY = {
    collectionId: "col-1",
    groupKey: "allowed-words",
    expectedVersion: 0,
    files: [{ path: "allowed.txt", sha256: "abc", size: 3 }],
};

const stubAssumeRole = () => {
    const stsMock = mockClient(STSClient);
    stsMock.on(AssumeRoleCommand).resolves({
        Credentials: {
            AccessKeyId: "K",
            SecretAccessKey: "S",
            SessionToken: "T",
            Expiration: new Date("2026-01-01T01:00:00Z"),
        },
    });
    return stsMock;
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
        assertEquals(
            stsMock.commandCalls(AssumeRoleCommand).length,
            0,
            "must not issue creds on a version conflict",
        );

        stsMock.restore();
    },
);
