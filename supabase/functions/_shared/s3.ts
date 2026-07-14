// S3 credential-issuance seam (dev MinIO AssumeRole vs. production AWS STS) and the
// small set of admin S3 operations edge functions need (checksum verification,
// version-id capture, .manifest.json writes). Only the functions under
// supabase/functions/** ever construct clients with these credentials — see
// server/dev/DEV-CREDENTIALS.md for the full spec this implements.
import { STSClient, AssumeRoleCommand } from "npm:@aws-sdk/client-sts@3";
import {
    HeadObjectCommand,
    PutObjectCommand,
    S3Client,
} from "npm:@aws-sdk/client-s3@3";
import {
    adminS3Credentials,
    isLocalMode,
    minioRootCredentials,
    prodBrokerConfig,
    s3Env,
} from "./env.ts";

export interface ScopedCredentials {
    accessKeyId: string;
    secretAccessKey: string;
    sessionToken: string;
    expiration: string; // ISO 8601
}

export interface S3Descriptor {
    bucket: string;
    region: string;
    prefix: string;
    credentials: ScopedCredentials;
}

const DEFAULT_DURATION_SECONDS = 3600;

/** Builds an IAM-style session policy scoped to one prefix. MinIO's AssumeRole
 * accepts a Policy parameter but — per DEV-CREDENTIALS.md — local mode deliberately
 * does NOT pass one (prefix scoping is a production security measure; MinIO local
 * creds get the parent/root identity's full access). Only used in prod mode. */
const buildSessionPolicy = (
    bucket: string,
    prefix: string,
    actions: string[],
): string =>
    JSON.stringify({
        Version: "2012-10-17",
        Statement: [
            {
                Effect: "Allow",
                Action: actions,
                Resource: [`arn:aws:s3:::${bucket}/${prefix}*`],
            },
        ],
    });

/** Issues short-lived, per-request S3 credentials scoped to `prefix`, in the
 * IDENTICAL shape whether backed by MinIO (local) or real AWS STS (production) — see
 * DEV-CREDENTIALS.md. `actions` is only enforced in production (a real IAM session
 * policy); local mode gets full-bucket root-derived temp credentials. */
export const getScopedCredentials = async (
    prefix: string,
    actions: string[],
    durationSeconds: number = DEFAULT_DURATION_SECONDS,
): Promise<S3Descriptor> => {
    const env = s3Env();

    if (isLocalMode()) {
        const root = minioRootCredentials();
        const sts = new STSClient({
            endpoint: env.endpoint,
            region: env.region,
            credentials: {
                accessKeyId: root.accessKeyId,
                secretAccessKey: root.secretAccessKey,
            },
        });
        // MinIO's AssumeRole ignores RoleArn/RoleSessionName content but the AWS SDK's
        // TS types require them — see DEV-CREDENTIALS.md's "empirical correction":
        // local mode MUST mint real MinIO temp creds (fabricated tokens are rejected).
        const result = await sts.send(
            new AssumeRoleCommand({
                RoleArn:
                    "arn:aws:iam::000000000000:role/bloom-teams-dev-placeholder",
                RoleSessionName: `bloom-dev-${crypto.randomUUID()}`,
                DurationSeconds: durationSeconds,
            }),
        );
        const creds = result.Credentials;
        if (
            !creds?.AccessKeyId ||
            !creds.SecretAccessKey ||
            !creds.SessionToken
        ) {
            throw new Error("MinIO AssumeRole did not return credentials");
        }
        return {
            bucket: env.bucket,
            region: env.region,
            prefix,
            credentials: {
                accessKeyId: creds.AccessKeyId,
                secretAccessKey: creds.SecretAccessKey,
                sessionToken: creds.SessionToken,
                expiration: (
                    creds.Expiration ??
                    new Date(Date.now() + durationSeconds * 1000)
                ).toISOString(),
            },
        };
    }

    const broker = prodBrokerConfig();
    const sts = new STSClient({
        region: broker.region,
        credentials: {
            accessKeyId: broker.accessKeyId,
            secretAccessKey: broker.secretAccessKey,
        },
    });
    const result = await sts.send(
        new AssumeRoleCommand({
            RoleArn: broker.roleArn,
            RoleSessionName: `bloom-teams-${crypto.randomUUID()}`,
            DurationSeconds: durationSeconds,
            Policy: buildSessionPolicy(env.bucket, prefix, actions),
        }),
    );
    const creds = result.Credentials;
    if (!creds?.AccessKeyId || !creds.SecretAccessKey || !creds.SessionToken) {
        throw new Error("AWS STS AssumeRole did not return credentials");
    }
    return {
        bucket: env.bucket,
        region: env.region,
        prefix,
        credentials: {
            accessKeyId: creds.AccessKeyId,
            secretAccessKey: creds.SecretAccessKey,
            sessionToken: creds.SessionToken,
            expiration: (
                creds.Expiration ??
                new Date(Date.now() + durationSeconds * 1000)
            ).toISOString(),
        },
    };
};

/** Admin S3 client for server-side-only operations: HeadObject checksum/version-id
 * verification and .manifest.json backup writes. Never handed to a client. */
export const adminS3Client = (): S3Client => {
    const env = s3Env();
    const creds = adminS3Credentials();
    return new S3Client({
        endpoint: env.endpoint,
        region: env.region,
        forcePathStyle: env.forcePathStyle,
        credentials: {
            accessKeyId: creds.accessKeyId,
            secretAccessKey: creds.secretAccessKey,
        },
    });
};

export interface VerifiedUpload {
    s3VersionId: string;
    sha256Base64: string;
}

/** Base64 <-> hex helpers. S3's x-amz-checksum-sha256 attribute is base64; the
 * manifest's `sha256` field (matching the C# client, which uses
 * Convert.ToHexString/SHA256) is lowercase hex. */
export const hexToBase64 = (hex: string): string => {
    const bytes = new Uint8Array(hex.length / 2);
    for (let i = 0; i < bytes.length; i++) {
        bytes[i] = parseInt(hex.substring(i * 2, i * 2 + 2), 16);
    }
    return btoa(String.fromCharCode(...bytes));
};

/** HeadObject with ChecksumMode ENABLED to read back the stored SHA-256 attribute
 * and capture the S3 version-id created by the just-completed PUT. Returns null if
 * the object is missing or its checksum doesn't match `expectedSha256Hex`. */
export const verifyUploadedObject = async (
    client: S3Client,
    bucket: string,
    key: string,
    expectedSha256Hex: string,
): Promise<VerifiedUpload | null> => {
    try {
        const head = await client.send(
            new HeadObjectCommand({
                Bucket: bucket,
                Key: key,
                ChecksumMode: "ENABLED",
            }),
        );
        const actual = head.ChecksumSHA256;
        const expected = hexToBase64(expectedSha256Hex);
        if (!actual || actual !== expected || !head.VersionId) {
            return null;
        }
        return { s3VersionId: head.VersionId, sha256Base64: actual };
    } catch {
        // NotFound (or any other S3 error) — treat as "not verified", let the caller
        // report it as a missing/bad upload rather than propagating a 5xx.
        return null;
    }
};

/** Best-effort `.manifest.json` backup write (CONTRACTS.md S3 layout). Never
 * throws — this is a convenience backup, not the source of truth (that's the DB). */
export const writeManifestBackup = async (
    client: S3Client,
    bucket: string,
    prefix: string,
    manifest: unknown,
): Promise<void> => {
    try {
        await client.send(
            new PutObjectCommand({
                Bucket: bucket,
                Key: `${prefix}.manifest.json`,
                Body: JSON.stringify(manifest, null, 2),
                ContentType: "application/json",
            }),
        );
    } catch (err) {
        console.error("writeManifestBackup failed (non-fatal):", err);
    }
};
