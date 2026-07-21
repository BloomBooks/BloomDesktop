// Unit tests for sweep-stale-uploads. The worklist RPC's SQL (which transactions are stale,
// the live-transaction exclusion, the referenced-version watermark) is covered by pgTAP
// (01_tc_schema_test.sql §12); here we mock the worklist and pin down what the edge function
// itself decides: delete only the versions newer than the referenced one, the referenced-null
// and referenced-missing cases, and the service-role gate.
import { assertEquals } from "jsr:@std/assert@1";
import { mockClient } from "npm:aws-sdk-client-mock@4";
import {
    DeleteObjectCommand,
    ListObjectVersionsCommand,
    S3Client,
} from "npm:@aws-sdk/client-s3@3";
import {
    callHandler,
    mockRequest,
    routedFetchStub,
    setTestEnv,
    withMockFetch,
} from "../_shared/test_support.ts";

setTestEnv();
const { handler } = await import("./index.ts");

const KEY = "tc/c1/books/i1/index.htm";

// A bearer token whose JWT payload decodes to role=service_role (the sweep is service-role only).
const serviceRoleToken = (() => {
    const payload = btoa(JSON.stringify({ role: "service_role" }))
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=+$/, "");
    return `h.${payload}.s`;
})();

const worklistFetch = (rows: unknown[]) =>
    routedFetchStub([
        { when: "rpc/list_stale_upload_garbage", status: 200, body: rows },
    ]);

// One S3 version entry as ListObjectVersions returns it (Key must match — the helper filters
// to the exact key).
const version = (versionId: string, isLatest = false) => ({
    Key: KEY,
    VersionId: versionId,
    IsLatest: isLatest,
});

const deletedVersionIds = (
    s3: ReturnType<typeof mockClient>,
): (string | undefined)[] =>
    s3
        .commandCalls(DeleteObjectCommand)
        .map((c) => (c.args[0].input as { VersionId?: string }).VersionId);

Deno.test(
    "sweep: deletes only the versions newer than the referenced (committed) one",
    async () => {
        const s3 = mockClient(S3Client);
        // Newest-first, as S3 returns them: two garbage uploads above the committed version,
        // plus one older committed-history version below it.
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [
                version("garbage2", true),
                version("garbage1"),
                version("committed"),
                version("older-history"),
            ],
        });
        s3.on(DeleteObjectCommand).resolves({});

        const res = await withMockFetch(
            worklistFetch([
                {
                    transaction_kind: "book",
                    transaction_id: "t1",
                    s3_key: KEY,
                    referenced_version_id: "committed",
                },
            ]),
            () => callHandler(handler, mockRequest({}, serviceRoleToken), {}),
        );

        assertEquals(res.status, 200);
        assertEquals(await res.json(), {
            keysProcessed: 1,
            versionsDeleted: 2,
            referencedMissing: 0,
        });
        // Only the two newer-than-committed uploads; NOT the committed version or older history.
        assertEquals(deletedVersionIds(s3), ["garbage2", "garbage1"]);
        s3.restore();
    },
);

Deno.test(
    "sweep: referenced_version_id null -> every version is orphaned and deleted",
    async () => {
        const s3 = mockClient(S3Client);
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [version("v3", true), version("v2"), version("v1")],
        });
        s3.on(DeleteObjectCommand).resolves({});

        const res = await withMockFetch(
            worklistFetch([
                {
                    transaction_kind: "book",
                    transaction_id: "t1",
                    s3_key: KEY,
                    referenced_version_id: null,
                },
            ]),
            () => callHandler(handler, mockRequest({}, serviceRoleToken), {}),
        );

        assertEquals((await res.json()).versionsDeleted, 3);
        assertEquals(deletedVersionIds(s3), ["v3", "v2", "v1"]);
        s3.restore();
    },
);

Deno.test(
    "sweep: referenced version missing from S3 -> deletes nothing, reports referencedMissing",
    async () => {
        const s3 = mockClient(S3Client);
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [version("something-else", true)],
        });
        s3.on(DeleteObjectCommand).resolves({});

        const res = await withMockFetch(
            worklistFetch([
                {
                    transaction_kind: "book",
                    transaction_id: "t1",
                    s3_key: KEY,
                    referenced_version_id: "committed-but-gone",
                },
            ]),
            () => callHandler(handler, mockRequest({}, serviceRoleToken), {}),
        );

        assertEquals(await res.json(), {
            keysProcessed: 1,
            versionsDeleted: 0,
            referencedMissing: 1,
        });
        assertEquals(s3.commandCalls(DeleteObjectCommand).length, 0);
        s3.restore();
    },
);

Deno.test(
    "sweep: a non-service-role caller is rejected 403 before any RPC or S3 call",
    async () => {
        // routedFetchStub([]) throws if called; requireServiceRole must reject first.
        const res = await withMockFetch(routedFetchStub([]), () =>
            // default mockRequest token is not a service_role JWT
            callHandler(handler, mockRequest({}), {}),
        );
        assertEquals(res.status, 403);
        assertEquals((await res.json()).error, "service_role_required");
    },
);
