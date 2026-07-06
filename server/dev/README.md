# Bloom Cloud Team Collections — Local Dev Stack

This directory contains everything needed to run **all** Cloud Team Collections
infrastructure locally — no AWS, no hosted Supabase, no internet connection required.

| Component | Technology | What it substitutes |
|-----------|-----------|---------------------|
| Postgres + RLS + RPCs | Local Supabase (`supabase start`) | Hosted Supabase project |
| Edge functions | `supabase functions serve` | Deployed edge functions |
| Auth (GoTrue) | Local Supabase (bundled) | BloomLibrary / Firebase sign-in |
| S3 object store | MinIO (Docker) | AWS S3 bucket |

---

## Prerequisites

Install all three before proceeding.

### A container runtime (required for MinIO and Supabase)

Any Docker-API-compatible runtime works. Two verified options:

**Podman Desktop (free/open-source — no licensing constraints; the verified reference
setup as of Jul 2026):**
1. Install Podman Desktop (https://podman-desktop.io or `winget install RedHat.PodmanDesktop`).
2. In its onboarding, install the **Podman** engine extension and the **Compose** extension
   (skip kubectl). Let it create and start the Podman machine (WSL2; may require a reboot).
3. The machine must be **rootful** (Supabase CLI requirement). Podman Desktop's default on
   Windows is rootful; verify with
   `podman machine inspect --format "{{.Rootful}}"` → `true`
   (fix with `podman machine stop; podman machine set --rootful; podman machine start`).
4. Podman Desktop's Docker-compatibility mode exposes `\\.\pipe\docker_engine`, so the
   Supabase CLI and docker-compose work with no `DOCKER_HOST` configuration. If tools cannot
   connect, set `DOCKER_HOST=npipe:////./pipe/podman-machine-default`.

Podman quirk to know: unlike Docker, Podman does NOT auto-create missing host directories
for bind mounts — it fails with `statfs ...: no such file or directory`. The repo commits
`.gitkeep` files in every bind-mounted directory (`server/dev/minio-data/`,
`supabase/snippets/`, `supabase/functions/`) so fresh clones just work; if you add a new
bind mount, commit its directory too.

**Docker Desktop** (`winget install Docker.DockerDesktop`): simplest, but requires a paid
subscription for organizations over 250 employees — check SIL licensing first. After
install, start it and wait for "Engine running".

### Supabase CLI

Not on winget/not supported via global npm *officially*, but the npm global install is the
path verified here and works fine:
```powershell
npm install -g supabase
```
(Or via Scoop if you have it: `scoop bucket add supabase
https://github.com/supabase/scoop-bucket.git; scoop install supabase`.)

Verify: `supabase --version`  (expect 2.x or later)

### Deno (required for `supabase functions serve` and edge-function tests)

```powershell
npm install -g deno
```
(Deno 2+ ships officially on npm. Alternatively `winget install DenoLand.Deno`.)

Verify: `deno --version`

### docker-compose (if your runtime did not bundle it)

Podman Desktop's Compose extension provides it; otherwise:
```powershell
winget install Docker.DockerCompose
```

---

## Directory layout

```
server/dev/
  docker-compose.yml        MinIO + init job
  seed.sql                  Dev users (admin/alice/bob @dev.local)
  config.auth.toml.snippet  Auth settings for orchestrator to fold into supabase/config.toml
  parity-check/             Standalone .NET console: verifies MinIO/S3 parity assumptions
  smoke.ps1                 Stack smoke-test (requires stack to be up)
  minio-data/               MinIO object data (git-ignored, created on first run)
  README.md                 This file
```

---

## Bring-up (full stack)

Run these from the **repository root** unless noted.

### Step 1 — Start Supabase (Postgres + GoTrue + PostgREST + Realtime)

```bash
supabase start
```

First run takes several minutes (pulls ~1 GB of Docker images). Subsequent starts are fast.

After it completes, note the printed values — you will need them for env vars:

```
API URL:     http://localhost:54321
DB URL:      postgresql://postgres:postgres@localhost:54322/postgres
anon key:    eyJ...   ← BLOOM_CLOUDTC_ANON_KEY
service_role key: ...
```

### Step 2 — Load dev seed users

```bash
supabase db query --file server/dev/seed.sql
```

Or if you have configured `seed_sql_path` in `supabase/config.toml` (see
`config.auth.toml.snippet`), `supabase db reset` will run it automatically.

### Step 3 — Start edge functions

```bash
supabase functions serve
```

Runs all functions under `supabase/functions/` in Deno. Keep this terminal open.

### Step 4 — Start MinIO

```bash
docker compose -f server/dev/docker-compose.yml up -d
```

On first run this also executes the `minio-init` job which:
- Creates the `bloom-teams-local` bucket.
- Enables object versioning.
- Applies a 7-day noncurrent-version expiry lifecycle rule.

The init job exits after setup. MinIO itself stays running.

Verify at http://localhost:9001 — log in with `minioadmin` / `minioadmin`.

---

## Teardown

```bash
# Stop MinIO (data preserved in server/dev/minio-data/)
docker compose -f server/dev/docker-compose.yml down

# Stop Supabase
supabase stop
```

## Full reset (wipe all local state)

```bash
docker compose -f server/dev/docker-compose.yml down
Remove-Item -Recurse -Force server/dev/minio-data   # PowerShell
# rm -rf server/dev/minio-data                      # bash

supabase db reset    # drops + recreates DB, reruns all migrations
supabase db query --file server/dev/seed.sql
docker compose -f server/dev/docker-compose.yml up -d
```

---

## Environment variables (`BLOOM_CLOUDTC_*`)

Bloom reads its Cloud TC configuration from environment variables. Set these before
launching Bloom (or in your IDE's launch profile / `.env.local`).

| Variable | Dev value | Description |
|----------|-----------|-------------|
| `BLOOM_CLOUDTC_SUPABASE_URL` | `http://localhost:54321` | Local Supabase API URL |
| `BLOOM_CLOUDTC_ANON_KEY` | *(printed by `supabase start`)* | Supabase anon/public JWT key |
| `BLOOM_CLOUDTC_S3_ENDPOINT` | `http://localhost:9000` | MinIO S3-compatible endpoint (path-style) |
| `BLOOM_CLOUDTC_S3_BUCKET` | `bloom-teams-local` | Dev bucket name |
| `BLOOM_CLOUDTC_AUTH_MODE` | `dev` | `dev` = local GoTrue email/pw; `real` = Firebase/BloomLibrary |
| `BLOOM_CLOUDTC_USER` | *(optional)* | Email to auto-sign-in as (multi-instance testing) |
| `BLOOM_CLOUDTC_PASSWORD` | *(optional)* | Password for `BLOOM_CLOUDTC_USER` |

`CloudEnvironment.cs` (task 03) is the single place that reads these variables and exposes
typed properties to the rest of the app. Do not read them directly from other code.

### S3 path-style note

MinIO requires **path-style** requests (`http://host:9000/bucket/key`). The Bloom S3 client
must configure `ForcePathStyle = true` when `BLOOM_CLOUDTC_S3_ENDPOINT` is set. Real AWS uses
virtual-hosted style; the `CloudEnvironment` flag controls which one is used.

---

## Two Bloom instances on one machine (multi-user smoke testing)

This is the Wave 3 manual smoke test — verifies check-out locking and conflict resolution
across two simultaneous users.

1. Create two separate Bloom collection folders, e.g.:
   - `C:\BloomDev\alice-collections\`
   - `C:\BloomDev\bob-collections\`

2. Launch the first Bloom instance with Alice's identity:
   ```powershell
   $env:BLOOM_CLOUDTC_USER     = "alice@dev.local"
   $env:BLOOM_CLOUDTC_PASSWORD = "BloomDev123!"
   $env:BLOOM_CLOUDTC_AUTH_MODE = "dev"
   # ... other BLOOM_CLOUDTC_* vars ...
   .\go.sh
   ```

3. Open a second PowerShell window and launch a second Bloom instance with Bob's identity:
   ```powershell
   $env:BLOOM_CLOUDTC_USER     = "bob@dev.local"
   $env:BLOOM_CLOUDTC_PASSWORD = "BloomDev123!"
   $env:BLOOM_CLOUDTC_AUTH_MODE = "dev"
   # ... same other vars ...
   .\go.sh
   ```

Both instances share the same local Supabase + MinIO stack, so they see each other's
changes in real time through the Realtime broadcast channel.

`BLOOM_CLOUDTC_USER` / `BLOOM_CLOUDTC_PASSWORD` override whatever stored credentials Bloom
would otherwise use, making it possible to run two distinct identities from the same
developer machine without logging out of anything.

---

## Dev users

Seeded by `seed.sql`. All share the password **`BloomDev123!`**.

| Email | Role | UUID |
|-------|------|------|
| `admin@dev.local` | Dev admin | `00000000-0000-0000-0000-000000000001` |
| `alice@dev.local` | Regular member | `00000000-0000-0000-0000-000000000002` |
| `bob@dev.local` | Regular member | `00000000-0000-0000-0000-000000000003` |

These have `email_verified: true` in their identity claims, satisfying the
`tc.jwt_email_verified()` RLS helper (task 01) and allowing `claim_memberships()` to work.

**Ad-hoc users**: because `enable_confirmations = false`, you can also POST any new email to
the GoTrue signup endpoint and immediately sign in — no seed change needed:

```bash
curl -s -X POST http://localhost:54321/auth/v1/signup \
  -H "apikey: $BLOOM_CLOUDTC_ANON_KEY" \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"BloomDev123!"}'
```

---

## Edge-function dev credential mode

See [DEV-CREDENTIALS.md](DEV-CREDENTIALS.md) for the full specification of how edge
functions return MinIO credentials in the STS response shape.

---

## Parity spike

See `parity-check/` — a standalone .NET console project that tests MinIO/AWS S3 parity
for the operations Bloom relies on (SHA-256 checksums, versioning, GetObjectByVersionId).

Build it (does NOT require MinIO to be running):
```powershell
dotnet build server/dev/parity-check/ParityCheck.csproj
```

Run it against a live MinIO instance:
```powershell
# Stack must be up first (Step 4 above)
dotnet run --project server/dev/parity-check/ParityCheck.csproj
```

Results are printed as PASS/FAIL per check. If any check fails, the program prints a
documented fallback strategy and exits with code 1.

---

## Smoke test

```powershell
# Stack must be fully up (steps 1–4) before running.
.\server\dev\smoke.ps1
```

The smoke script:
1. Signs up a random user via local GoTrue.
2. Puts a versioned object via the parity-check tool (or mc).
3. Calls the `download-start` edge function with a valid JWT.
4. Reports PASS/FAIL with clear error messages on any failure.

**Note**: As of initial authoring this script is authored but unrun — requires Docker Desktop
and Supabase CLI to be installed on the machine. It is ready to execute once those are
available.

---

## Orchestrator notes

### `config.auth.toml.snippet`

This file carries the `[auth]` settings that must land in `supabase/config.toml` (owned by
task 01). At merge time, fold these into `supabase/config.toml` under `[auth]` and delete
the snippet file.

The critical settings are:
```toml
[auth]
enable_signup = true
enable_confirmations = false
```

### `seed.sql` wiring

To have `supabase db reset` run this file automatically, add to `supabase/config.toml`:
```toml
[db]
seed_sql_path = "server/dev/seed.sql"
```
Alternatively, create `supabase/seed.sql` as a symlink or copy.
