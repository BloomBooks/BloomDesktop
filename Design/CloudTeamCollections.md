# Cloud Team Collections: S3 + Supabase backend

Cloud Team Collections are a second backend for Bloom's Team Collection feature. Instead of a
shared Dropbox/LAN folder, a collection is backed by an SIL-hosted **Supabase Postgres** database
(locks, membership, current-version manifests, history events) and one **S3** bucket (book and
collection-file content). It is implemented as a second subclass behind the existing
`TeamCollection` abstraction and coexists indefinitely with folder-based Team Collections; the
check-in/check-out editorial model is unchanged.

Access is gated by the existing experimental feature flag and the subscription-tier gate; for 6.5
the opt-in checkbox is additionally hidden unless the `cloudCollections` environment variable is
set (see `CollectionSettingsApi.CloudTeamCollectionOptionVisible`).

Companion docs: API surface — [CONTRACTS.md](CloudTeamCollections/CONTRACTS.md); database schema +
ER diagram — [SCHEMA.md](CloudTeamCollections/SCHEMA.md); deployment — [GOING-LIVE.md](CloudTeamCollections/GOING-LIVE.md);
implementation log — [IMPLEMENTATION.md](CloudTeamCollections/IMPLEMENTATION.md).

## Why a hosted backend

Folder-based Team Collections wrap a shared Dropbox/LAN folder. That model is hard and expensive
for users to set up, Bloom can never truly know sync status, and a third-party sync agent
underneath Bloom creates a long tail of races (conflicted-copy files, two-phase delivery, partial
downloads that look corrupt) that the code and tests spend enormous effort taming. A hosted
backend makes Bloom the authority: the database is transactional, so most of those races become
structurally impossible, and sharing needs only a Bloom (BloomLibrary) sign-in — no third-party
account.

## What it guarantees

1. **No third-party setup** for users — only a BloomLibrary sign-in.
2. **Central SIL-hosted S3**, per-collection prefixes, access brokered per operation (STS
   short-lived credentials); publishing buckets/credentials are untouched and never shared with
   this feature.
3. **Check-in/check-out preserved**; offline editing of a checked-out book always works.
4. **Real identity** = BloomLibrary Firebase account (not a self-declared email).
5. **Sharing = approved accounts**: an admin lists emails (+ role Admin/Member); an approved
   person signs in anywhere, asks "Get my Team Collections", and pulls a collection down. (Invite
   links/codes, `bloom://` deep links, and email are not implemented.)
6. **Send/Receive model**: content moves only on explicit, progress-reported transfers. Metadata
   (locks, version numbers) syncs continuously and cheaply, so the UI always knows "checked out to
   Sara" / "v12 exists, you have v10" without moving bytes.
7. **No secondary local repo copy**. Transfers stage in temp dirs and swap atomically per book —
   never new HTML over stale images.
8. **Per-file delta transfers**, SHA-256 verified, both directions.
9. **No content retention**: replaced files are not kept and there is no restore. The history
   *log* (who/when/comment/incidents) is metadata and is kept. S3 object versioning is on purely
   as a transactional safety device (a crashed Send is harmless; torn reads are impossible), with
   noncurrent versions auto-expired after ~7 days.
10. **Work is never silently lost**: whenever the repo must win over local work, that work is saved
    as a `.bloomSource` in a local **Lost & Found** folder and recorded as a server-side incident
    event admins can see.
11. **Coexists with folder/Dropbox TC** as a second backend behind the same abstraction. There is
    no auto-migration: to move a collection, un-team it (delete the link file), then enable cloud
    sharing.
12. **Subscriptions** are enforced only by the existing client-side gate; the server has no
    tier/quota logic.

## Architecture

### Components

- **Bloom desktop (C#)** — `CloudTeamCollection`, a subclass of the abstract `TeamCollection`
  ([src/BloomExe/TeamCollection/TeamCollection.cs](../src/BloomExe/TeamCollection/TeamCollection.cs)),
  plus support classes: `CloudCollectionClient` (RPC/edge calls), `CloudRepoCache` (thread-safe,
  disk-persisted snapshot of server state + event cursor), `CloudBookTransfer`/`BookVersionManifest`
  (delta upload/download), `CloudCollectionMonitor` (polling/realtime), `CloudAuth`
  (+ `FirebaseCloudAuthProvider` / `LocalCloudAuthProvider`), and `CloudJoinFlow`. A `SharingApi`
  serves the membership/sign-in UI. Existing infrastructure is reused: the S3 client seam, the
  WebSocket progress harness, and nearly all Team Collection UI.
- **Supabase** (Postgres schema `tc`) — maintained declaratively in `supabase/schemas/` (see
  CONTRACTS.md → "Database: declarative schema"). Tables: `collections`, `members` (the
  approved-accounts list; unclaimed rows have a null `user_id`), `books` (authoritative lock
  columns + a `deleted_at` tombstone), `versions` (one metadata row per check-in), `version_files`
  (the current manifest: path → sha256, size, s3_version_id; superseded rows pruned at commit),
  `collection_file_groups`/`collection_group_files`, `color_palette_entries`, `events` (history +
  realtime source + polling cursor), and the ephemeral `checkin_transactions` /
  `collection_file_transactions`. RLS is on everywhere; clients never write `books`/`versions`
  directly — every state transition goes through an RLS-gated RPC or an edge function. The full
  ER diagram is in [SCHEMA.md](CloudTeamCollections/SCHEMA.md).
- **S3** — one bucket per environment (`bloom-teams-production` / `-sandbox`; MinIO locally),
  versioning on, lifecycle = abort-multipart 7d + expire-noncurrent ~7d. Layout:
  `tc/{collectionId}/books/{bookInstanceId}/{relativePath}` (book folders keyed by instance id, so
  a rename is just a DB row update) and `tc/{collectionId}/collectionFiles/{group}/...`. A
  `.manifest.json` current-state backup is written per book.

### Access brokering

Edge functions vend STS short-lived credentials scoped by an inline session policy: write access
to only the one book being sent and only while its transaction is open; read access to the
collection prefix (including `GetObjectVersion`). Uploads carry `x-amz-checksum-sha256`;
checkin-finish verifies every object against its proposed hash before committing.

### Editorial flow

- **Checkout** is a race-free conditional UPDATE (`WHERE locked_by IS NULL OR locked_by = me`).
  Locks never auto-expire; **Force Unlock** is a distinct, audited RPC. A checkout also records the
  caller's *seat* (a stable hash of the local collection-folder path) so that a takeover is only
  allowed from the same machine **and** seat.
- **Send** = `checkin-start` (diff the manifest → open a transaction with the changed paths + STS
  credentials) → parallel PUT of changed files → `checkin-finish` (verify uploads, then one DB
  transaction: new version row + manifest + lock release + events). The commit is the single
  atomicity point; a crash mid-Send commits nothing, is resumable for 48h, and never leaves a
  partial state visible.
- **New books** are first-class: `checkin-start` with `bookId:null` creates the book row locked to
  the caller **with no current version**, so it is invisible to teammates until its first commit. A
  crash leaves no phantom (the expired transaction is reaped); a same-name or duplicate-instance-id
  race resolves through the existing conflict flows.
- **Receive** = `get_collection_state(since)` → the existing `SyncAtStartup` reconciliation →
  download changed files by pinned `(path, s3VersionId)` into a temp dir → atomic swap per book.

### Sync

Metadata sync is continuous and cheap. Each client keeps a `last_seen_event_id` cursor;
`get_changes(since)` serves both reconnect catch-up and a 60s polling fallback. A per-collection
private realtime broadcast channel (driven by an events-table trigger) is an optimization on top
of polling, not a dependency.

### Auth & identity

Sign-in uses **Supabase third-party Firebase auth**: the BloomLibrary login page forwards the
Firebase ID + refresh tokens it already holds, and a small Firebase Admin function adds the static
`role:"authenticated"` claim. `CloudAuth` isolates the provider so the rest of the client is
auth-agnostic: `FirebaseCloudAuthProvider` for hosted environments, `LocalCloudAuthProvider`
(local GoTrue, any email/password) for the on-machine local stack. Claiming an approval requires a
verified email (all BloomLibrary accounts are verified).

The account email is the identity in a cloud TC: the server stamps the lock owner from the token
(the client sends only a machine name). Signing out and back in as the same account is always
safe. Switching to a *different* account while a checked-out book has unsent changes is blocked
with explicit choices (Send first, or preserve a `.bloomSource` and release locally); the server
lock stays with the original account, and nothing is discarded implicitly.

### History & Unicode

For cloud TCs the History tab reads server events (cached locally for offline display); the
SQLite-in-book history mechanism remains for folder TCs. Names and paths are NFC-normalized
everywhere.

## Client integration (behind the `TeamCollection` abstraction)

- **Backend factory**: `TeamCollectionLink` parses `TeamCollectionLink.txt` (a folder path, or
  `cloud://sil.bloom/collection/<id>`) and the factory chooses the subclass. An old Bloom reads a
  cloud link as a folder path and lands in the safe Disconnected state.
- **Lock seams**: `TryLockInRepo`/`UnlockInRepo` are virtual — folder keeps read-modify-write,
  cloud is one conditional RPC.
- **Status-write discipline**: cloud `WriteBookStatusJsonToRepo` dispatches each change to the
  narrowest RPC rather than rewriting a whole status blob.
- **Capability flags** (`SupportsVersionHistory`, `SupportsSharingUi`, `RequiresSignIn`): UI
  branches on capability, never on the concrete backend type.

`SyncAtStartup`, the clobber/conflict logic, the message log, local status files, and
`DisconnectedTeamCollection` are reused unchanged. `CloudRepoCache` makes the synchronous status
calls cheap and serves Disconnected mode from its last snapshot when offline.

## UI

Settings gains a Sharing panel (the approved-accounts list, replacing the free-text admin list for
cloud TCs). The cloud create dialog is sign-in → confirm the immutable name → initial Send (no
folder chooser, no restart). The collection chooser gains "Get my Team Collections". On the
collection tab the status chip is now live and precise, the status dialog gains "Receive Updates"
and "Send All", and a Share button appears; the per-book panel carries over with added
signed-out / updates-available states. All strings go through the XLF pipeline with Send/Receive
terminology.

## Testing

- **C# unit** (`src/BloomTests/TeamCollection/Cloud/`): link/factory, lock seams, cache, manifest,
  transfer, auth, sharing API, and member-by-member backend tests including the ported
  `SyncAtStartup` matrix (asserting `.bloomSource` + incident events).
- **Server**: pgTAP (`supabase/tests/`) covering the RLS matrix, checkout concurrency,
  verified-email claiming, the last-admin guard, the event cursor, tombstone/undelete, name
  uniqueness, and the orphaned-upload sweep worklist; plus Deno tests per edge function.
- **Component** (vitest): SharingPanel, chooser, status panel states, create dialog.
- **E2E** (Playwright over CDP driving a real `Bloom.exe` against the local stack): create,
  two-instance collaboration, checkout contention, forced check-in recovery, approved accounts
  across two machines, kill-mid-Send/resume, un-team adoption, Receive-during-Send coherence,
  new-book lifecycle, and account-switch safety.
- **Standing gate**: the entire existing folder-TC suite passes unchanged.

## Environments & provisioning

Three environments, distinguished by configuration (`BLOOM_CLOUDTC_*` env vars), never by code:

- **local** — the on-machine emulation: local Supabase (`supabase start`) + MinIO, set up per
  [server/dev/README.md](../server/dev/README.md). Auth is `CloudAuthMode.Local`.
- **dev / sandbox** — a hosted Supabase project + real AWS S3, reserved for testing.
- **production** — the hosted production project + bucket.

("local" always means the on-machine emulation; "dev"/"sandbox" always mean the hosted test cloud.)
The database is applied from `supabase/migrations/` (a single migration generated from the
declarative `supabase/schemas/`); provisioning the AWS buckets, lifecycle rules, broker role, and
the assume-role-only IAM user for edge functions, and scheduling the orphaned-upload sweep, are
covered in [GOING-LIVE.md](CloudTeamCollections/GOING-LIVE.md).

## Key decisions

- **Auth = Supabase third-party Firebase (Option A).** Chosen over exchanging the legacy Parse
  session token (welds us to a server being decommissioned) and over hand-validating Firebase JWTs
  (stale, fragile). `CloudAuth` isolates the choice.
- **Sharing v1 = approved accounts.** Invite links/codes, deep links, and email are deferred.
- **No content retention.** Only the current version's files exist in S3; older versions are a
  short-lived transactional safety net, not a restore feature.
- **No auto-migration** between folder and cloud backends; they coexist indefinitely.
- **Send/Receive is explicit**; only metadata syncs ambiently.

Remaining go-live work — including a planned split of the review into a `bloom-core-supabase` PR
and a `bloom-desktop` PR — is tracked in [GOING-LIVE.md](CloudTeamCollections/GOING-LIVE.md).
