// Unit tests for _shared/tc/s3.ts: the local-mode MinIO AssumeRole credential seam, checksum
// verification, and the manifest backup write. S3Client/STSClient calls are mocked via
// aws-sdk-client-mock (patches the SDK client prototypes) rather than a live MinIO —
// the live-integration spike (see the task's Progress log) already exercised the real
// MinIO AssumeRole round-trip; these tests cover the wiring/logic cheaply and
// hermetically instead.
import { assertEquals, assertExists } from "@std/assert";
import { mockClient } from "aws-sdk-client-mock";
import { AssumeRoleCommand, STSClient } from "@aws-sdk/client-sts";
import {
    HeadObjectCommand,
    PutObjectCommand,
    S3Client,
} from "@aws-sdk/client-s3";
import { setTestEnv } from "../_shared/tc/test_support.ts";

setTestEnv();

// deno-lint-ignore no-import-assign
const {
    getScopedCredentials,
    hexToBase64,
    verifyUploadedObject,
    writeManifestBackup,
} = await import("../_shared/tc/s3.ts");

Deno.test(
    "hexToBase64 round-trips a known SHA-256 hex digest to its base64 form",
    () => {
        // sha256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        const hex =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        const b64 = hexToBase64(hex);
        // Sanity: decoding the base64 back to bytes and re-hex-encoding must match the input.
        const decoded = atob(b64);
        const rehexed = Array.from(decoded)
            .map((c) => c.charCodeAt(0).toString(16).padStart(2, "0"))
            .join("");
        assertEquals(
            rehexed,
            hex,
            "hexToBase64 must be a faithful hex->base64 re-encoding, not a no-op",
        );
    },
);

Deno.test(
    "getScopedCredentials (dev mode) calls MinIO AssumeRole and returns the STS response shape",
    async () => {
        const stsMock = mockClient(STSClient);
        stsMock.on(AssumeRoleCommand).resolves({
            Credentials: {
                AccessKeyId: "MOCK_ACCESS_KEY",
                SecretAccessKey: "MOCK_SECRET",
                SessionToken: "MOCK_SESSION_TOKEN",
                Expiration: new Date("2026-01-01T01:00:00Z"),
            },
        });

        const result = await getScopedCredentials("tc/col1/books/book1/", [
            "s3:PutObject",
        ]);

        assertEquals(result.bucket, "bloom-teams-test");
        assertEquals(result.prefix, "tc/col1/books/book1/");
        assertEquals(result.credentials.accessKeyId, "MOCK_ACCESS_KEY");
        assertEquals(result.credentials.sessionToken, "MOCK_SESSION_TOKEN");
        assertEquals(result.credentials.expiration, "2026-01-01T01:00:00.000Z");

        // Dev mode must NOT pass a session Policy (see s3.ts's comment: MinIO dev creds get
        // the parent identity's full access; scoping is a production-only measure).
        const call = stsMock.commandCalls(AssumeRoleCommand)[0];
        assertEquals(call.args[0].input.Policy, undefined);

        stsMock.restore();
    },
);

Deno.test(
    "getScopedCredentials (dev mode) throws if MinIO AssumeRole returns no credentials",
    async () => {
        const stsMock = mockClient(STSClient);
        stsMock.on(AssumeRoleCommand).resolves({}); // no Credentials field

        let threw = false;
        try {
            await getScopedCredentials("tc/col1/books/book1/", [
                "s3:PutObject",
            ]);
        } catch (err) {
            threw = true;
            assertEquals(
                (err as Error).message,
                "MinIO AssumeRole did not return credentials",
            );
        }
        assertEquals(
            threw,
            true,
            "must fail fast rather than return a half-formed credential object",
        );

        stsMock.restore();
    },
);

Deno.test(
    "verifyUploadedObject returns the version-id + checksum when they match",
    async () => {
        const s3Mock = mockClient(S3Client);
        const expectedHex =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(expectedHex),
            VersionId: "v1",
        });

        const client = new S3Client({ region: "us-east-1" });
        const result = await verifyUploadedObject(
            client,
            "bucket",
            "tc/col1/books/book1/book.htm",
            expectedHex,
        );

        assertExists(result);
        assertEquals(result!.s3VersionId, "v1");

        s3Mock.restore();
    },
);

Deno.test(
    "verifyUploadedObject returns null when the stored checksum does not match",
    async () => {
        const s3Mock = mockClient(S3Client);
        const wrongHex =
            "0000000000000000000000000000000000000000000000000000000000000000";
        s3Mock.on(HeadObjectCommand).resolves({
            ChecksumSHA256: hexToBase64(wrongHex),
            VersionId: "v1",
        });

        const client = new S3Client({ region: "us-east-1" });
        const expectedHex =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        const result = await verifyUploadedObject(
            client,
            "bucket",
            "tc/col1/books/book1/book.htm",
            expectedHex,
        );

        assertEquals(
            result,
            null,
            "a checksum mismatch must be reported as unverified, not thrown",
        );

        s3Mock.restore();
    },
);

Deno.test(
    "verifyUploadedObject returns null (not throw) when the object is missing",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(HeadObjectCommand).rejects(new Error("NotFound"));

        const client = new S3Client({ region: "us-east-1" });
        const result = await verifyUploadedObject(
            client,
            "bucket",
            "tc/col1/books/book1/missing.htm",
            "abc",
        );

        assertEquals(
            result,
            null,
            "a missing object must surface as 'not verified', not an unhandled rejection",
        );

        s3Mock.restore();
    },
);

Deno.test(
    "verifyUploadedObject returns null when VersionId is absent (unversioned bucket misconfig)",
    async () => {
        const s3Mock = mockClient(S3Client);
        const hex =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        s3Mock
            .on(HeadObjectCommand)
            .resolves({ ChecksumSHA256: hexToBase64(hex) }); // no VersionId

        const client = new S3Client({ region: "us-east-1" });
        const result = await verifyUploadedObject(
            client,
            "bucket",
            "tc/col1/books/book1/book.htm",
            hex,
        );

        assertEquals(result, null);

        s3Mock.restore();
    },
);

Deno.test(
    "writeManifestBackup never throws even when the PUT fails (best-effort backup)",
    async () => {
        const s3Mock = mockClient(S3Client);
        s3Mock.on(PutObjectCommand).rejects(new Error("simulated S3 outage"));

        const client = new S3Client({ region: "us-east-1" });
        // Must resolve, not reject — checkin-finish's response to the client must not
        // depend on this backup write succeeding (see s3.ts's doc comment).
        await writeManifestBackup(client, "bucket", "tc/col1/books/book1/", 1, {
            some: "manifest",
        });

        s3Mock.restore();
    },
);

// A tiny S3 stand-in for the manifest backup keys, honouring If-Match / If-None-Match the way
// S3 does (412 PreconditionFailed). `beforeConditionalPut` lets a test slip another writer in
// between our read and our write.
interface StoredObject {
    body: string;
    seq: string | undefined;
    etag: string;
}
const fakeManifestStore = (
    s3Mock: ReturnType<typeof mockClient>,
    beforeConditionalPut?: (store: Map<string, StoredObject>) => void,
) => {
    const store = new Map<string, StoredObject>();
    let nextEtag = 1;
    const preconditionFailed = () =>
        Object.assign(new Error("PreconditionFailed"), {
            name: "PreconditionFailed",
            $metadata: { httpStatusCode: 412 },
        });
    s3Mock.on(HeadObjectCommand).callsFake((input: { Key: string }) => {
        const o = store.get(input.Key);
        if (!o) {
            throw Object.assign(new Error("NotFound"), {
                name: "NotFound",
                $metadata: { httpStatusCode: 404 },
            });
        }
        return {
            ETag: o.etag,
            Metadata: o.seq ? { "manifest-seq": o.seq } : {},
        };
    });
    s3Mock
        .on(PutObjectCommand)
        .callsFake(
            (input: {
                Key: string;
                Body: string;
                Metadata?: Record<string, string>;
                IfMatch?: string;
                IfNoneMatch?: string;
            }) => {
                if (
                    input.IfMatch !== undefined ||
                    input.IfNoneMatch !== undefined
                ) {
                    beforeConditionalPut?.(store);
                    const existing = store.get(input.Key);
                    if (input.IfNoneMatch === "*" && existing)
                        throw preconditionFailed();
                    if (
                        input.IfMatch !== undefined &&
                        existing?.etag !== input.IfMatch
                    ) {
                        throw preconditionFailed();
                    }
                }
                store.set(input.Key, {
                    body: input.Body,
                    seq: input.Metadata?.["manifest-seq"],
                    etag: `"e${nextEtag++}"`,
                });
                return { ETag: `"e${nextEtag}"` };
            },
        );
    return store;
};

const PREFIX = "tc/col1/books/book1/";

Deno.test(
    "writeManifestBackup: finishes completing out of commit order never regress the latest backup",
    async () => {
        const s3Mock = mockClient(S3Client);
        const store = fakeManifestStore(s3Mock);
        const client = new S3Client({ region: "us-east-1" });

        // Version 5's finish writes its backup first; version 4's, which committed earlier,
        // only gets there afterwards.
        await writeManifestBackup(client, "bucket", PREFIX, 5, { v: 5 });
        assertEquals(
            store.get(`${PREFIX}.manifest.json`)?.seq,
            "5",
            "sanity check: v5 is latest",
        );
        await writeManifestBackup(client, "bucket", PREFIX, 4, { v: 4 });

        const latest = store.get(`${PREFIX}.manifest.json`);
        assertEquals(latest?.seq, "5");
        assertEquals(JSON.parse(latest?.body ?? "null"), { v: 5 });
        // Every committed version still has its own backup.
        assertEquals(
            JSON.parse(store.get(`${PREFIX}.manifests/4.json`)?.body ?? "null"),
            { v: 4 },
        );
        assertEquals(
            JSON.parse(store.get(`${PREFIX}.manifests/5.json`)?.body ?? "null"),
            { v: 5 },
        );

        s3Mock.restore();
    },
);

Deno.test(
    "writeManifestBackup: a newer version is the latest backup, first time and after",
    async () => {
        const s3Mock = mockClient(S3Client);
        const store = fakeManifestStore(s3Mock);
        const client = new S3Client({ region: "us-east-1" });

        await writeManifestBackup(client, "bucket", PREFIX, 1, { v: 1 });
        assertEquals(
            store.get(`${PREFIX}.manifest.json`)?.seq,
            "1",
            "created with If-None-Match",
        );
        await writeManifestBackup(client, "bucket", PREFIX, 2, { v: 2 });
        assertEquals(
            store.get(`${PREFIX}.manifest.json`)?.seq,
            "2",
            "replaced with If-Match",
        );

        s3Mock.restore();
    },
);

Deno.test(
    "writeManifestBackup: a newer backup written between our read and our write is not overwritten",
    async () => {
        const s3Mock = mockClient(S3Client);
        let raced = false;
        const store = fakeManifestStore(s3Mock, (s) => {
            // The first time we try to replace v3, version 5's finish gets there first.
            if (raced) return;
            raced = true;
            s.set(`${PREFIX}.manifest.json`, {
                body: '{"v":5}',
                seq: "5",
                etag: '"other"',
            });
        });
        store.set(`${PREFIX}.manifest.json`, {
            body: '{"v":3}',
            seq: "3",
            etag: '"e0"',
        });
        const client = new S3Client({ region: "us-east-1" });

        await writeManifestBackup(client, "bucket", PREFIX, 4, { v: 4 });

        assertEquals(
            raced,
            true,
            "sanity check: the other writer really slipped in",
        );
        assertEquals(store.get(`${PREFIX}.manifest.json`)?.seq, "5");
        assertEquals(
            s3Mock.commandCalls(HeadObjectCommand).length,
            2,
            "re-read after the 412",
        );
        assertExists(store.get(`${PREFIX}.manifests/4.json`));

        s3Mock.restore();
    },
);
