# 11 — Local dev stack (Wave 1)

**Goal**: a one-command local substitute for ALL external infrastructure — Supabase (DB, RPCs,
edge functions, auth, realtime) + MinIO standing in for S3 — so every later task, and manual
two-instance testing, needs nothing outside this machine.

**Dependencies**: none (CONTRACTS.md for shapes). Parallel-safe. Owns `server/dev/**`
(docker-compose, seed, smoke script, docs) and the auth/dev portions of `supabase/config.toml`
(coordinate with 01, which owns `supabase/migrations/**`).

## Steps
- [ ] `server/dev/docker-compose.yml`: MinIO (single-node single-drive — modern SNSD mode,
      which supports **object versioning**; data dir on the local file system, e.g.
      `server/dev/minio-data/`, git-ignored) + console; fixed dev root credentials.
- [ ] Init job/script: create the `bloom-teams-local` bucket with versioning ON (mirrors the
      production lifecycle config as far as MinIO supports; document any gaps).
- [ ] `supabase/config.toml` auth settings for dev: email/password signup enabled,
      `enable_confirmations = false` (auto-confirm ⇒ any email+password "login" just works and
      counts as verified).
- [ ] Seed script (`server/dev/seed.sql` or `supabase/seed.sql`): three standard dev users —
      `admin@dev.local`, `alice@dev.local`, `bob@dev.local` (one shared known password) — so
      tests and docs have stable identities; arbitrary new emails also work via signup.
- [ ] `server/dev/README.md`: bring-up (`supabase start`, `supabase functions serve`,
      `docker compose up`), teardown/reset, the `BLOOM_CLOUDTC_*` env vars (see below), and
      the two-instances-on-one-machine recipe (two collection folders, two dev users).
- [ ] Document the `CloudEnvironment` env-var contract consumed by task 03 (this task defines
      the names; 03 implements the C# side): `BLOOM_CLOUDTC_SUPABASE_URL`,
      `BLOOM_CLOUDTC_ANON_KEY`, `BLOOM_CLOUDTC_S3_ENDPOINT` (implies path-style + the local
      bucket), `BLOOM_CLOUDTC_AUTH_MODE` (`dev` | `real`), `BLOOM_CLOUDTC_USER` /
      `BLOOM_CLOUDTC_PASSWORD` (auto-sign-in identity for multi-instance testing).
- [ ] Edge-function dev credential mode (spec here, implemented in 02): when configured with
      MinIO instead of AWS, return the static MinIO credentials in the **identical** response
      shape as STS (`accessKeyId`/`secretAccessKey`/`sessionToken`/`expiration`) so clients
      cannot tell the difference and CONTRACTS.md stays unchanged.
- [ ] **Parity spike (do first, it de-risks everything)**: a throwaway C# console check that
      .NET `TransferUtility` against MinIO can (a) PUT with `x-amz-checksum-sha256`, (b) read
      back the stored checksum server-side, (c) capture a version-id on PUT, and (d) GET by
      (key, versionId). If any fail, record the fallback (e.g. verify sha256 via a HEAD +
      metadata convention) as a dev-mode-only deviation in server/dev/README.md.
- [ ] `server/dev/smoke.ps1` (or .sh): stack up → sign up a random user → create a bucket
      object with a version-id → call one deployed edge function → report green.

## Acceptance
- Fresh clone + Docker: `README.md` steps bring up the full stack; `smoke` script green.
- Parity spike results recorded (pass, or documented fallback).
- Seeded users can sign in via plain HTTP calls to local GoTrue and get a JWT whose claims
  satisfy `tc.jwt_email_verified()` (coordinate the helper with 01).

**Agent notes**: Sonnet. Nothing in this task touches Bloom application code. Keep everything
idempotent and resettable — E2E (09) will reuse these fixtures for per-test resets.
