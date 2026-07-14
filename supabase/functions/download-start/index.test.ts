// Unit tests for download-start's handler: membership check via RPC, then read-only
// scoped S3 credentials (GetObject + GetObjectVersion only — see CONTRACTS.md).
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
    "download-start: happy path returns collection-scoped read-only creds",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            { when: "rpc/download_start_check", status: 200, body: null },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ collectionId: "col-1" }), {
                collectionId: "col-1",
            }),
        );

        assertEquals(res.status, 200);
        const json = await res.json();
        assertEquals(json.s3.prefix, "tc/col-1/");
        assertEquals(json.s3.credentials.sessionToken, "T");

        stsMock.restore();
    },
);

Deno.test(
    "download-start: not a member -> 403, no S3 credentials issued",
    async () => {
        const stsMock = stubAssumeRole();
        const fetchStub = routedFetchStub([
            {
                when: "rpc/download_start_check",
                status: 403,
                body: { message: JSON.stringify({ error: "not_a_member" }) },
            },
        ]);

        const res = await withMockFetch(fetchStub, () =>
            callHandler(handler, mockRequest({ collectionId: "col-1" }), {
                collectionId: "col-1",
            }),
        );

        assertEquals(res.status, 403);
        assertEquals((await res.json()).error, "not_a_member");
        assertEquals(
            stsMock.commandCalls(AssumeRoleCommand).length,
            0,
            "must not issue creds when membership check fails",
        );

        stsMock.restore();
    },
);

Deno.test("download-start: missing collectionId -> 400", async () => {
    const stsMock = stubAssumeRole();
    const fetchStub = routedFetchStub([]);

    const res = await withMockFetch(fetchStub, () =>
        callHandler(handler, mockRequest({}), {}),
    );

    assertEquals(res.status, 400);
    assertEquals((await res.json()).field, "collectionId");

    stsMock.restore();
});
