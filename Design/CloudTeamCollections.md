# Cloud Team Collections: S3 + Supabase design

Status: **design complete, implementation not started** (2 July 2026).
Interactive review version of this document (with diagrams and the full narrative):
https://claude.ai/code/artifact/14157962-d8e4-4c3f-afa7-34c49ed29e1a
Work breakdown: [CloudTeamCollections/IMPLEMENTATION.md](CloudTeamCollections/IMPLEMENTATION.md) ·
API contracts: [CloudTeamCollections/CONTRACTS.md](CloudTeamCollections/CONTRACTS.md)

## Motivation

Team Collections today wraps a shared Dropbox/LAN folder. Chronic problems: Dropbox is hard
and expensive for our users to set up; Bloom can never truly know sync status; and a
third-party sync agent underneath us creates a long tail of races (conflicted-copy files,
two-phase delivery, partial downloads that look corrupt) that the code and tests spend
enormous effort taming. Replacement: a central SIL-hosted backend — Supabase Postgres as the
authoritative database (locks, membership, current-version manifests, history events), one S3
bucket with a prefix per collection for book content, and sharing via an approved-accounts
model. The check-in/check-out editorial model is preserved.

## Requirements (decided)

1. Zero third-party setup for users; only a Bloom (BloomLibrary) sign-in.
2. Central SIL-hosted S3; per-collection prefixes; access brokered per-operation (STS).
3. Check-in/check-out preserved; offline editing of checked-out books always works.
4. Identity = BloomLibrary Firebase account (real auth, not self-declared email).
5. Sharing v1 = **approved accounts**: admin lists emails (+ role Admin/Member); an approved
   person signs in anywhere, asks "Get my Team Collections", pulls a collection down.
   Invite links/codes, `bloom://` deep links, landing page, Mailgun emails: all deferred to v2.
6. **Send/Receive model** (modal in v1): explicit transfers with progress; no ambient content
   copying. Metadata (locks, version numbers) syncs continuously and cheaply; the UI always
   knows "checked out to Sara" / "v12 exists, you have v10" without moving bytes.
7. No secondary local repo copy. Transfers stage in temp dirs and swap atomically per book —
   never new HTML with stale images.
8. Per-file delta transfers, SHA-256 verified, both directions.
9. **No content retention**: replaced files are not kept anywhere; no restore. The history
   *log* (who/when/comment/incidents) is kept — it is metadata. S3 object versioning stays ON
   purely as a transactional safety device (crashed Send harmless; torn reads impossible),
   with noncurrent versions auto-deleted after ~7 days.
10. Work is never silently lost: whenever the repo must win over local work, that work is
    saved as a `.bloomSource` in a local **Lost & Found** folder AND recorded as a server-side
    incident event admins can see.
11. Coexists indefinitely with folder/Dropbox TC (second backend behind the same abstraction).
    No auto-migration in v1: un-team (delete link file), then enable cloud sharing.
12. Subscriptions untouched: existing client-side gate only; the server has no tier/quota logic.
13. Experimental flag until GA (plus the existing feature gate), like the current TC.

## Architecture

- **Bloom desktop (C#)**: `CloudTeamCollection` — second subclass of the existing abstract
  `TeamCollection` (src/BloomExe/TeamCollection/TeamCollection.cs) — plus support classes
  (`CloudCollectionClient`, `CloudRepoCache`, `CloudBookTransfer`, `BookVersionManifest`,
  `CloudCollectionMonitor`, `CloudAuth`, `CloudJoinFlow`), a new `SharingApi`, and reuse of
  `BloomS3Client` (session credentials + TransferUtility), the WebSocket progress harness, and
  nearly all existing TC UI.
- **Supabase** (schema `tc`): tables `collections`, `members` (the approved-accounts table;
  unclaimed rows have null user_id), `books` (with the authoritative lock columns and
  `deleted_at` tombstone), `versions` (metadata row per check-in), `version_files` (the
  CURRENT manifest: path → sha256, size, s3_version_id; superseded rows pruned at commit),
  `collection_file_groups`/`collection_group_files`, `color_palette_entries` (union merge =
  `insert … on conflict do nothing`), `events` (history + realtime source + polling cursor;
  numeric types match `BookHistoryEventType`, extended with incident types), and
  `checkin_transactions`. RLS on everything; no direct writes to books/versions — state
  transitions go through RPCs/edge functions.
- **S3**: bucket per environment (`bloom-teams-production`/`-sandbox`), versioning ON,
  lifecycle = abort-multipart 7d + expire noncurrent versions ~7d. Layout:
  `tc/{collectionId}/books/{bookInstanceId}/{relativePath}` (+ `.manifest.json` current-state
  backup per book; `tc/{cid}/collectionFiles/{group}/...`). Folder keyed by instance id →
  rename is a DB row update. Existing buckets/credentials for publishing are untouched; cloud
  collections never use embedded keys.
- **Access brokering**: STS temporary credentials from edge functions, scoped by inline
  session policy (write: the one book being sent, only while its transaction is open; read:
  the collection prefix incl. `GetObjectVersion`). Uploads carry `x-amz-checksum-sha256`;
  checkin-finish verifies before commit.
- **Checkout** = conditional UPDATE (`WHERE locked_by IS NULL OR locked_by = me`) — race-free.
  Locks never auto-expire; "Force Unlock" (label decided) is a distinct audited RPC.
- **Send** = checkin-start (diff manifest → transaction + changed paths + STS creds) →
  parallel PUT changed files → checkin-finish (verify, one DB tx: version row + manifest +
  lock release + events; the commit is the atomicity point). Crash mid-Send: nothing
  committed, resumable 48h, no partial state visible ever.
- **New books** (first-class): checkin-start with `bookId:null` creates the row locked to
  caller **with no current version — invisible to teammates** until first commit. Crash → no
  phantom; expired transaction reaps. Same-name race → NameConflict → existing "name2"
  resolution; duplicate instance id → existing id-conflict flow.
- **Receive** = `get_collection_state(since)` → reuse of the existing `SyncAtStartup`
  reconciliation → download changed files by pinned (path, s3VersionId) into temp → atomic
  swap per book.
- **Realtime**: one private broadcast channel per collection driven by an events-table
  trigger; clients keep a `last_seen_event_id` cursor; `get_changes(since)` is both reconnect
  catch-up and the 60s polling fallback (polling ships first; realtime is an optimization).
- **Auth** (decision delegated to reviewing colleague; brief in the artifact, grounded in the
  BloomLibrary2 + bloom-parse-server code): recommended Option A = Supabase third-party
  Firebase auth — login page forwards the Firebase ID+refresh tokens it already holds
  (~5 lines in BloomLibrary2 `src/editor.ts`), plus one NEW small Firebase Admin cloud
  function adding the static `role:"authenticated"` claim (+ backfill; no claims infra exists
  today). Alternatives: B = exchange the legacy Parse session token (welds us to the server
  being decommissioned); C = hand-validate Firebase JWTs per the stale
  `bloom-parse-server/supabase/` docs. `CloudAuth` isolates the choice. Claiming an approval
  requires `email_verified` (all BloomLibrary accounts are verified — the existing parse
  adapter already enforces this).
- **Identity/account rules**: account email is the identity in cloud TCs (server stamps lock
  identity from the token; client sends only machine name). Sign-out/in as the same account
  is always safe. Switching accounts with unsent checked-out changes is **blocked** with
  explicit choices (Send first, or preserve `.bloomSource` + release locally); the server
  lock stays with the original account. Nothing is ever discarded implicitly.
- **History tab** (cloud TCs): reads server events (cached locally for offline display);
  the SQLite-in-book mechanism remains for folder TCs.
- **Unicode**: NFC normalization for names and paths everywhere.

## Client integration: base-class changes (all folder-backend-behavior-preserving)

1. Backend factory: `TeamCollectionLink` parses `TeamCollectionLink.txt` (folder path or
   `cloud://sil.bloom/collection/<id>`); factory replaces the three hardcoded
   `new FolderTeamCollection(...)` sites in `TeamCollectionManager`. Old Bloom versions read
   the cloud link as a folder path and land in the safe Disconnected state.
2. Lock seams: `protected virtual TryLockInRepo/UnlockInRepo` (folder keeps read-modify-write;
   cloud is one conditional RPC).
3. Status-write discipline: cloud `WriteBookStatusJsonToRepo` diff-dispatches to the narrowest
   RPC; audit the ~10 `WriteBookStatus` callers.
4. Capability flags: `SupportsVersionHistory`, `SupportsSharingUi`, `RequiresSignIn` — UI
   branches on capability, never on concrete type.

`SyncAtStartup`, clobber/conflict logic, message log, local status files, `DisconnectedTeamCollection`
are reused unchanged. `CloudRepoCache` (thread-safe, persisted snapshot + event cursor) makes
the synchronous status calls cheap and hydrates Disconnected mode.

## UI changes (summary)

Settings gains the cloud share path + a Sharing panel (approved-accounts list) replacing the
free-text admin list for cloud TCs; the cloud create dialog is sign-in → confirm immutable
name → initial Send (no folder chooser, no restart). Collection chooser gains "Get my Team
Collections". Collection tab: same status chip (now live/precise), status dialog gains
"Receive Updates" (successor of Reload Collection) and "Send All", a Share button appears, the
per-book panel carries over nearly unchanged (+ signedOut / updatesAvailable states). All new
strings via the XLF pipeline with Send/Receive terminology.

## Edge cases

The full ~135-case disposition matrix (every case in src/BloomTests/TeamCollection and the
TeamCollection.cs decision logic → impossible-now / changed / unchanged / disallowed, plus 15
new cloud-specific cases) lives in the review artifact, Level 4, and drives the test plan.
Headline: most Dropbox-era cases exist only because Dropbox is not transactional and become
structurally impossible; all offline-work-vs-moved-on-repo collisions converge on the unified
`.bloomSource` + incident-event recovery.

## Test plan (summary)

- C# unit suites (src/BloomTests/TeamCollection/Cloud/): link/factory, lock seams, cache,
  manifest, transfer, member-by-member backend tests, the ported SyncAtStartup matrix
  (asserting `.bloomSource` + incident events), monitor, auth, sharing API.
- Server: pgTAP (RLS matrix, checkout concurrency, claiming requires verified email,
  last-admin guard, cursor, tombstone/undelete, name uniqueness) + Deno tests per edge function.
- Component (vitest browser mode): SharingPanel, chooser, status panel states, create dialog.
- E2E (Playwright over CDP driving real Bloom.exe; local Supabase + MinIO/sandbox):
  E2E-1 create · E2E-2 two-instance collaboration · E2E-3 checkout contention ·
  E2E-4 forced check-in recovery (`.bloomSource` + incident) · E2E-5 approved accounts across
  two machines · E2E-6 kill-mid-Send/resume · E2E-7 un-team adoption ·
  E2E-8 Receive-during-Send coherence (mandated) · E2E-9 new-book lifecycle/phantom/name-race ·
  E2E-10 account-switch safety.
- Standing gate: the entire existing folder-TC suite passes unchanged on every branch.

## Provisioning

Supabase CLI (migrations in `supabase/` in this repo — decided location; local dev/CI via
`supabase start`) + AWS CLI/Terraform script creating the versioned bucket, lifecycle rules,
`bloom-teams-broker` role, and the assume-role-only IAM user for edge functions. Docker for
local Supabase + MinIO. Two hosted Supabase projects (production, sandbox).

## Roadmap

M0 enablers (1–2wk) → M1 create + read-only + join-by-listing + polling + modal Receive
(4–6wk) → M2 checkout + Send + force-unlock + `.bloomSource` recovery (4–6wk) → M3 sharing
panel + Get-my-Team-Collections (2–3wk) → M4 realtime + reconnect hardening (2–3wk) →
M5 adoption polish + server-fed history tab + dogfood (2–3wk). Build is orchestrated per
[CloudTeamCollections/IMPLEMENTATION.md](CloudTeamCollections/IMPLEMENTATION.md).

## Open items

- Auth option A/B/C: colleague decision pending (brief in the artifact).
- Safety-window duration: recorded as 7 days ("option one" reading — if "one day" was meant,
  transaction lifetime shrinks to ~12h; invariant: transaction lifetime < expiry floor).
