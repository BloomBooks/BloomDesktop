// Unit tests for sweep-stale-uploads. The worklist RPC's SQL (which transactions are stale,
// the live-transaction exclusion, the referenced-version watermark) is covered by pgTAP
// (01_tc_schema_test.sql §12); here we mock the worklist and pin down what the edge function
// itself decides: delete only the versions newer than the referenced one, the referenced-null
// and referenced-missing cases, the grace period and the just-before-delete re-check that keep
// it from deleting work committed after the snapshot, and the service-role gate.
import { assertEquals } from "@std/assert";
import { mockClient } from "aws-sdk-client-mock";
import {
    DeleteObjectCommand,
    ListObjectVersionsCommand,
    S3Client,
} from "@aws-sdk/client-s3";
import {
    callHandler,
    mockRequest,
    type RecordedCall,
    routedFetchStub,
    setTestEnv,
    withMockFetch,
} from "../_shared/tc/test_support.ts";

setTestEnv();
const { handler } = await import("../sweep-stale-uploads/index.ts");

const KEY = "tc/c1/books/i1/index.htm";

// A bearer token whose JWT payload decodes to role=service_role (the sweep is service-role only).
const serviceRoleToken = (() => {
    const payload = btoa(JSON.stringify({ role: "service_role" }))
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=+$/, "");
    return `h.${payload}.s`;
})();

interface WorklistRow {
    transaction_kind: string;
    transaction_id: string;
    s3_key: string;
    referenced_version_id: string | null;
}

// The worklist, plus the per-key re-check the sweep makes right before deleting. By default
// the re-check reports the key unchanged (still stale, same referenced version as the row).
const worklistFetch = (
    rows: WorklistRow[],
    keyStateNow?: { stillStale: boolean; referencedVersionId: string | null },
    calls?: RecordedCall[],
) =>
    routedFetchStub(
        [
            { when: "rpc/list_stale_upload_garbage", status: 200, body: rows },
            {
                when: "rpc/stale_upload_key_state",
                status: 200,
                body: keyStateNow ?? {
                    stillStale: true,
                    referencedVersionId: rows[0].referenced_version_id,
                },
            },
        ],
        calls,
    );

const OLD = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000); // well past the grace period
const YOUNG = new Date(Date.now() - 60 * 60 * 1000); // an hour ago: inside the grace period

// One S3 version entry as ListObjectVersions returns it (Key must match — the helper filters
// to the exact key).
const version = (versionId: string, isLatest = false, lastModified = OLD) => ({
    Key: KEY,
    VersionId: versionId,
    IsLatest: isLatest,
    LastModified: lastModified,
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
            keysChanged: 0,
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
            keysChanged: 0,
        });
        assertEquals(s3.commandCalls(DeleteObjectCommand).length, 0);
        s3.restore();
    },
);

Deno.test(
    "sweep: a check-in committed after the worklist snapshot -> its version is not deleted",
    async () => {
        const s3 = mockClient(S3Client);
        // The snapshot said "committed" is referenced, so "newer" looks like garbage. But by
        // the time the sweep gets to this key someone has checked in "newer" (old upload of a
        // long-resumed transaction, so the grace period alone would not protect it).
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [version("newer", true), version("committed")],
        });
        s3.on(DeleteObjectCommand).resolves({});
        const calls: RecordedCall[] = [];

        const res = await withMockFetch(
            worklistFetch(
                [
                    {
                        transaction_kind: "book",
                        transaction_id: "t1",
                        s3_key: KEY,
                        referenced_version_id: "committed",
                    },
                ],
                { stillStale: true, referencedVersionId: "newer" },
                calls,
            ),
            () => callHandler(handler, mockRequest({}, serviceRoleToken), {}),
        );

        assertEquals(await res.json(), {
            keysProcessed: 1,
            versionsDeleted: 0,
            referencedMissing: 0,
            keysChanged: 1,
        });
        assertEquals(s3.commandCalls(DeleteObjectCommand).length, 0);
        const recheck = calls.find((c) =>
            c.url.includes("rpc/stale_upload_key_state"),
        );
        assertEquals(recheck?.body, { p_s3_key: KEY });
        s3.restore();
    },
);

Deno.test(
    "sweep: a key a live transaction has started using since the snapshot -> nothing deleted",
    async () => {
        const s3 = mockClient(S3Client);
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [version("garbage", true), version("committed")],
        });
        s3.on(DeleteObjectCommand).resolves({});

        const res = await withMockFetch(
            worklistFetch(
                [
                    {
                        transaction_kind: "book",
                        transaction_id: "t1",
                        s3_key: KEY,
                        referenced_version_id: "committed",
                    },
                ],
                { stillStale: false, referencedVersionId: null },
            ),
            () => callHandler(handler, mockRequest({}, serviceRoleToken), {}),
        );

        assertEquals((await res.json()).keysChanged, 1);
        assertEquals(s3.commandCalls(DeleteObjectCommand).length, 0);
        s3.restore();
    },
);

Deno.test(
    "sweep: versions younger than the grace period are never deleted",
    async () => {
        const s3 = mockClient(S3Client);
        // Nothing references the key, so every version is a candidate -- but "fresh" was
        // uploaded an hour ago (its transaction may still commit) and "no-date" has no
        // timestamp; only "stale" is old enough.
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [
                version("fresh", true, YOUNG),
                { Key: KEY, VersionId: "no-date", IsLatest: false },
                version("stale"),
            ],
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

        assertEquals((await res.json()).versionsDeleted, 1);
        assertEquals(deletedVersionIds(s3), ["stale"]);
        s3.restore();
    },
);

Deno.test(
    "sweep: when every candidate is inside the grace period, no re-check and no delete happen",
    async () => {
        const s3 = mockClient(S3Client);
        s3.on(ListObjectVersionsCommand).resolves({
            Versions: [version("fresh", true, YOUNG), version("committed")],
        });
        s3.on(DeleteObjectCommand).resolves({});
        const calls: RecordedCall[] = [];

        const res = await withMockFetch(
            worklistFetch(
                [
                    {
                        transaction_kind: "book",
                        transaction_id: "t1",
                        s3_key: KEY,
                        referenced_version_id: "committed",
                    },
                ],
                undefined,
                calls,
            ),
            () => callHandler(handler, mockRequest({}, serviceRoleToken), {}),
        );

        assertEquals((await res.json()).versionsDeleted, 0);
        assertEquals(s3.commandCalls(DeleteObjectCommand).length, 0);
        assertEquals(
            calls.some((c) => c.url.includes("rpc/stale_upload_key_state")),
            false,
        );
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
