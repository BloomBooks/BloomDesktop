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

## Solution: MinIO AssumeRole credentials in STS shape

> **Empirical correction (6 Jul 2026, parity spike):** an earlier draft of this spec had
> dev mode return the static root credentials with a fabricated `sessionToken: "devmode"`.
> That DOES NOT WORK: MinIO validates the `X-Amz-Security-Token` header (session tokens
> are JWTs minted by its own STS) and rejects fabricated ones with
> `The security token included in the request is invalid`. Dev mode must therefore mint
> REAL temporary credentials via MinIO's AssumeRole STS API — which is also better parity.

When the edge function detects local mode (the `BLOOM_CLOUD_LOCAL_MODE` secret set to
`true` in the Supabase project secrets for the local instance — named "local", not "dev",
because in the Bloom ecosystem "dev" means a real hosted reserved-for-testing deployment
like dev.bloomlibrary.org, which would run with this flag FALSE), it calls **MinIO's
AssumeRole endpoint** instead of AWS STS. MinIO implements the standard `AssumeRole` action on its main endpoint,
authenticated with the root credentials:

```
POST http://minio:9000/?Action=AssumeRole&Version=2011-06-15&DurationSeconds=3600
(AWS SigV4-signed with minioadmin/minioadmin)
```

MinIO returns genuine temporary credentials (`AccessKeyId`, `SecretAccessKey`,
`SessionToken`, `Expiration`) that it will accept on subsequent requests. The edge
function forwards them in the **identical shape** as the production STS response:

```json
{
  "transactionId": "...",
  "changedPaths": [],
  "s3": {
    "bucket": "bloom-teams-local",
    "region": "us-east-1",
    "prefix": "tc/{collectionId}/books/{bookInstanceId}/",
    "credentials": {
      "accessKeyId":     "<minted by MinIO AssumeRole>",
      "secretAccessKey": "<minted by MinIO AssumeRole>",
      "sessionToken":    "<real MinIO session token (JWT)>",
      "expiration":      "<now + 1h>"
    }
  }
}
```

### Key points

| Field | Production (AWS STS) | Dev (MinIO AssumeRole) |
|-------|-----------------|-------------|
| `accessKeyId` | STS temporary key | MinIO temporary key |
| `secretAccessKey` | STS temporary secret | MinIO temporary secret |
| `sessionToken` | STS session token | Real MinIO session token (validated!) |
| `expiration` | 1 hour from now | 1 hour from now |
| `bucket` | `bloom-teams` (production) | `bloom-teams-local` |
| `region` | `us-east-1` (or configured) | `us-east-1` (MinIO ignores this) |
| `prefix` | Scoped path (`tc/{cid}/...`) | Same path; NOT policy-enforced in dev (scoping is a production security measure) |

### Client behavior

`BloomS3Client` / `TransferUtility` receives these credentials from the edge function
(via `CloudEnvironment.S3Endpoint`) and constructs an `AmazonS3Client` with:
- `ServiceURL` = `BLOOM_CLOUDTC_S3_ENDPOINT` (e.g., `http://localhost:9000`)
- `ForcePathStyle` = `true` (MinIO requires path-style; AWS uses virtual-hosted by default)
- `Credentials` = `SessionAWSCredentials(accessKeyId, secretAccessKey, sessionToken)`

Because the dev token is real, the client uses `SessionAWSCredentials` in both
environments — the credential plumbing path is truly identical.

(For ad-hoc tooling that talks to MinIO directly with the root credentials — like
`parity-check` — use `BasicAWSCredentials` with NO session token.)

### Expiration

Dev credentials expire after 1 hour, same as production, so the client's refresh path
(re-calling `checkin-start` for fresh credentials) is exercised in dev too.

### Security

These are **local-only** credentials that exist solely within Docker on the developer's
machine. They are never transmitted outside `localhost`. The MinIO root password is a
well-known dev constant — no rotation or secrecy is required.

### Detection (how the function switches modes)

Task 02 implements the switching. The recommended approach is a Supabase secret:

```bash
# Set when initializing the local project (supabase secrets set writes to .env.local)
supabase secrets set BLOOM_CLOUD_LOCAL_MODE=true
```

The function reads `Deno.env.get("BLOOM_CLOUD_LOCAL_MODE")` at startup. If `"true"`, it
uses the MinIO credential path. On any HOSTED deployment — production, sandbox, or a future
"dev"-named project with real AWS endpoints — this secret is simply not set (or `"false"`).

Alternative: infer from `SUPABASE_URL` containing `localhost` — but the explicit secret is
more robust against CI environments that happen to run locally.

---

## MinIO S3 parity: known gaps vs production AWS

Parity spike run 6 Jul 2026 (Podman 5.8.3, MinIO latest, .NET AWSSDK.S3): **4/4 PASS** —
sha256 checksum PUT, server-side checksum readback, version-id capture on PUT, and GET by
`(key, versionId)` all behave as on AWS.

| Feature | AWS S3 | MinIO (SNSD) | Dev-mode deviation |
|---------|--------|-------------|-------------------|
| `x-amz-checksum-sha256` on PUT | Stored as object attribute | **VERIFIED** — stored; readable server-side | None |
| `x-amz-version-id` on PUT response | Always present (versioning ON) | **VERIFIED** present | None |
| GET by `(key, versionId)` | Supported | **VERIFIED** | None |
| `AssumeRole` / STS | Supported | Supported (MinIO's own STS; see correction above) | Session tokens are validated — fabricated tokens are REJECTED |
| IAM bucket policies / per-prefix scoping | Enforced via session policy | Not enforced in dev | Accept in dev — scoping is a production security measure |
| Multipart abort lifecycle | Supported (provision-aws sets it) | Not settable via `mc ilm rule add`; MinIO's built-in stale-upload cleanup applies | Dev-only gap, documented in docker-compose.yml |
| Noncurrent-version expiry lifecycle | Supported | **VERIFIED** via `mc ilm rule add` (7d, prefix `tc/`) | None |
