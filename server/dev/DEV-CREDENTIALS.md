# Edge-function dev credential mode — specification

**Implemented by**: task 02 (edge functions).
**Consumed by**: task 04 (BloomS3Client), task 03 (CloudEnvironment).
**Contract version**: 1 (matches CONTRACTS.md v1).

---

## Problem

In production, the `checkin-start` and `download-start` edge functions call AWS STS
`AssumeRole` to obtain short-lived, per-book scoped credentials and return them to the
client. This requires a live AWS account, the `bloom-teams-broker` IAM role, and real STS.

In the local dev stack, there is no AWS — there is MinIO, which is S3-compatible but is
NOT wired to STS and does not support `AssumeRole`. The client code (`BloomS3Client`,
`TransferUtility`) must still receive credentials in exactly the STS response shape so that
it does not need to know whether it is talking to MinIO or AWS.

## Solution: static MinIO credentials in STS shape

When the edge function detects dev mode (i.e., the `BLOOM_CLOUDTC_AUTH_MODE` env var is
`dev`, or equivalently the `BLOOM_DEV_MODE` secret is set to `true` in the Supabase
project secrets for the local instance), it skips the STS call and returns the well-known
MinIO root credentials directly.

The response body is **identical in shape** to a real STS `AssumeRole` response:

```json
{
  "transactionId": "...",
  "changedPaths": [],
  "s3": {
    "bucket": "bloom-teams-local",
    "region": "us-east-1",
    "prefix": "tc/{collectionId}/books/{bookInstanceId}/",
    "credentials": {
      "accessKeyId":     "minioadmin",
      "secretAccessKey": "minioadmin",
      "sessionToken":    "devmode",
      "expiration":      "2099-01-01T00:00:00Z"
    }
  }
}
```

### Key points

| Field | Production (STS) | Dev (MinIO) |
|-------|-----------------|-------------|
| `accessKeyId` | STS temporary key | `minioadmin` (MinIO root) |
| `secretAccessKey` | STS temporary secret | `minioadmin` |
| `sessionToken` | STS session token | `"devmode"` (literal string) |
| `expiration` | 1 hour from now | Far future (`2099-01-01`) |
| `bucket` | `bloom-teams` (production) | `bloom-teams-local` |
| `region` | `us-east-1` (or configured) | `us-east-1` (MinIO ignores this) |
| `prefix` | Scoped path (`tc/{cid}/...`) | Same scoped path (MinIO enforces nothing, but we keep the shape) |

The `sessionToken` value of `"devmode"` is passed by the AWS SDK to MinIO as the
`X-Amz-Security-Token` header. MinIO ignores this header when the root credentials are
used, so the AWS SDK's standard credential handling works without modification.

### Client behavior

`BloomS3Client` / `TransferUtility` receives these credentials from the edge function
(via `CloudEnvironment.S3Endpoint`) and constructs an `AmazonS3Client` with:
- `ServiceURL` = `BLOOM_CLOUDTC_S3_ENDPOINT` (e.g., `http://localhost:9000`)
- `ForcePathStyle` = `true` (MinIO requires path-style; AWS uses virtual-hosted by default)
- `Credentials` = `SessionAWSCredentials(accessKeyId, secretAccessKey, sessionToken)`

The `sessionToken` field is always populated (even as `"devmode"`) so the client always
uses `SessionAWSCredentials`, which is the same type it uses in production. This keeps
the credential plumbing path identical in both environments.

### Expiration

The far-future expiration (`2099-01-01`) means dev sessions never need to be refreshed.
The client's credential-refresh logic is exercised by the production STS creds (1-hour
expiry) and is invisible to the MinIO path.

### Security

These are **local-only** credentials that exist solely within Docker on the developer's
machine. They are never transmitted outside `localhost`. The MinIO root password is a
well-known dev constant — no rotation or secrecy is required.

### Detection (how the function switches modes)

Task 02 implements the switching. The recommended approach is a Supabase secret:

```bash
# Set when initializing the local project (supabase secrets set writes to .env.local)
supabase secrets set BLOOM_DEV_MODE=true
```

The function reads `Deno.env.get("BLOOM_DEV_MODE")` at startup. If `"true"`, it uses the
static credential path. In production, this secret is simply not set (or set to `"false"`).

Alternative: infer from `SUPABASE_URL` containing `localhost` — but the explicit secret is
more robust against CI environments that happen to run locally.

---

## MinIO S3 parity: known gaps vs production AWS

These were identified during the parity spike (`parity-check/`). See that project's
output for the authoritative run results.

| Feature | AWS S3 | MinIO (SNSD) | Dev-mode deviation |
|---------|--------|-------------|-------------------|
| `x-amz-checksum-sha256` on PUT | Stored as object attribute | Stored; `GetObjectAttributes` returns it | None expected |
| `x-amz-version-id` on PUT response | Always present (versioning ON) | Present (versioning ON) | None expected |
| GET by `(key, versionId)` | Supported | Supported | None expected |
| `AssumeRole` / STS | Supported | NOT supported | Static creds (this doc) |
| IAM bucket policies / per-prefix scoping | Enforced | Not enforced (root creds) | Accept in dev — security is production-only |
| Multipart abort lifecycle | Supported | Supported | None expected |
| Noncurrent-version expiry lifecycle | Supported | Supported via `mc ilm` | None expected |

If the parity spike discovers any additional gaps, they are documented in the `PASS/FAIL`
output of `server/dev/parity-check/` and added to this table.
