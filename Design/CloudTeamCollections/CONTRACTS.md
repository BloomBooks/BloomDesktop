# Cloud Team Collections — frozen API contracts (v1)

> **Where this lives:** this copy is in the `bloom-core-supabase` repo (`Design/CloudTeamCollections/`),
> next to the backend it describes. Paths under `src/`, `Design/`, `tasks/`, `orchestration/`,
> and mentions of `IMPLEMENTATION.md` or `../CloudTeamCollections.md`, refer to the BloomDesktop
> repo (its `Design/CloudTeamCollections/` folder), where the desktop client and the project's
> design notes live.

Changes to this file require an orchestrator commit and a version-note bump here.
**Contract version: 1.10** (24 Sep 2026, BL-16531 — BREAKING for the client: the **client makes
the checkout GUID**, and check-in never issues one. A database change can commit and its response
still be lost; with a server-made GUID that stranded the book (checked out, but no copy had the
GUID to check in or unlock with). Now the client writes a new GUID to `.checkout` first, then
calls `checkout_book(book_id, machine, checkout_guid)` (a third, required argument; NULL/blank ⇒
SQLSTATE 22023 `invalid_checkout_guid`). A free book is locked with that GUID's hash; the same
caller retrying with the SAME GUID gets the same success again with nothing changed (idempotent);
the caller holding it under a different GUID (or a send-only lock, below) gets `{success: false,
locked_by_me: true}` as before. Success no longer returns `checkoutGuid`. `checkin-start` no
longer returns `checkoutGuid`: a first check-in (new book), or a check-in of a free existing book,
locks the book for the send only, with NO hash, and `checkin-finish` always releases that lock
(even with `keepCheckedOut`), as do `checkin-abort` and expiry; resuming such a send (a
never-committed new book, or an existing book under one's own send-only lock) needs no GUID. A
check-in of a book the caller has checked out still needs its GUID (else `CheckoutElsewhere`, which
is also the answer to a GUID sent for a send-only lock). `checkout_book_takeover`, `unlock_book`,
`delete_book` and `force_unlock` are unchanged. v1.9, 24 Sep 2026, BL-16531 — BREAKING for the client: the per-copy
"seat" and the v1.8 takeover token are replaced by a **checkout GUID**, which says which local
copy of a book holds its checkout, so check-in keeps working when a collection folder is moved,
renamed or copied, and a second copy can no longer silently take the checkout from the first.
`checkout_book(book_id, machine)` loses its `seat` argument, succeeds only for a free book, and
returns `checkoutGuid` (a book the caller already holds returns `{success: false, locked_by_me:
true}` and is NOT re-issued a GUID); `checkout_book_takeover(book_id, checkout_guid, machine)`
takes that GUID and keeps it; `unlock_book` and `delete_book` gain a required `checkout_guid`
argument (`CheckoutElsewhere` when it does not match); `checkin-start` takes an optional
`checkoutGuid`, returns `checkoutGuid` when it issues one, and answers `409 CheckoutElsewhere`
when the caller holds the book under a different GUID; `checkin-finish` answers `409
CheckoutElsewhere` if the checkout changed since start; book rows from `get_collection_state` /
`get_changes` carry `checkoutGuidHash` instead of `locked_seat`. `force_unlock` is unchanged: an
admin never needs the GUID. v1.9 follow-up, same day (BL-16531 review; no version bump, since no
request or response field changes): `checkin-finish` and `collection-files-finish` can answer
`409 TransactionChanged` when a concurrent start resumed (rewrote) the transaction while finish
was verifying it — nothing is committed and it is retryable like any other failed finish;
`checkin-start` taking a free lock now emits a CheckOut event, as `checkout_book` does; and
`checkin-start` obtains its S3 credentials before it takes the lock, so a credential failure can
no longer leave a checkout GUID issued that the client never received. v1.8, 23 Sep 2026, BL-16531 review fixes — BREAKING for the client:
`checkout_book` now returns a secret `checkout_token` on success, and `checkout_book_takeover`
takes that token as a new second argument and grants takeover only for it (machine/seat no
longer grant anything); `checkin-start`/`collection-files-start` NFC-normalize and validate
every path (`changedPaths` come back NFC; new `400 InvalidManifest`) and `checkin-start` now
also reports `NameConflict` for a rename of an existing book; `checkin-finish` re-checks the
lock and base version (`409 LockHeldByOther` / `BaseVersionSuperseded`); the internal finish
RPCs became service-role only. v1.7, 16 Jul 2026 — added the `get_collection_file_manifest` RPC,
additive (E9): a per-file manifest for one collection-file group so the download path fetches
only changed files pinned to their committed `s3_version_id`, mirroring `get_book_manifest`;
the data already lived in `tc.collection_group_files`, this just exposes it for reads. No
schema change. v1.6, 13 Jul 2026 — durable member display names, additive (John's
13 Jul request): `tc.members` gains an editable `display_name` column; new
`members_set_display_name(collection_id, member_id, display_name)` RPC (admin may set
anyone's, a claimed member their own; blank clears); `members_list` rows carry
`display_name`; `resolve_member_display` (hence `locked_by_name` everywhere it already
appears) prefers it over the JWT-claim event capture; `get_changes` event rows gain
`by_display_name` (the CURRENT durable name of `by_user_id`). Display rule everywhere:
name when set, else email. v1.5, 11 Jul: per-collection-copy "seat" on checkouts, additive
(bug #0, John's ruling): `checkout_book`/`checkout_book_takeover` gain an optional third
`seat` parameter (client-computed stable hash of the local collection folder path) and
return `locked_seat`; `get_collection_state`/`get_changes` book rows carry `locked_seat`;
takeover requires machine AND seat to match, and a NULL stored seat never matches
(fail-safe). v1.4, 9 Jul: added the `checkout_book_takeover` RPC, additive
(account-switch behavior, dogfood batch item 9); `checkin_start_tx`/`checkin_finish_tx`
unchanged. v1.3, 8 Jul: added the "Auth (Option A)" section: the token-receipt endpoint
BloomLibrary2's `src/editor.ts` forwards Firebase tokens to. v1.2, 7 Jul: added the
`get_book_manifest` RPC, additive; the Receive path needs a per-book file manifest and no
existing RPC carried one. v1.1, 6 Jul: two wire-format clarifications under "Postgres
RPCs"; no semantic changes.)

## Link file

`TeamCollectionLink.txt` content is either a folder path (legacy folder TC) or
`cloud://sil.bloom/collection/<collectionId>` where `<collectionId>` = the Bloom
CollectionId GUID (also the server `collections.id`).

## Auth

Bearer JWT on every request (**Option A, decided 8 Jul 2026**: Supabase third-party Firebase
auth — the JWT is the Firebase ID token itself, unmodified; `CloudAuth` isolates the client
side of this). Claims used server-side: `sub` (user id), `email`, `email_verified`. Claiming
an approval requires `email_verified = true`.

## Auth (Option A): token-receipt endpoint

The Bloom-side half of BloomLibrary2's forwarding change (`src/editor.ts`, GOING-LIVE.md
Phase 3.2, not yet written against this file's *previous* prose — this section is the
precise text to write it against). Reuses the exact conventions of the pre-existing
`external/login` endpoint (ExternalApi.cs) that the same BloomLibrary-hosted login page
already posts back to for the legacy Parse session: same host/port
(`http://127.0.0.1:{port}/bloom/api/...`, `port` is the query param the login page was opened
with — see `BloomLibraryAuthentication.LogIn`'s `login-for-editor?port=` URL), same
CORS/OPTIONS handling, same "POST it and move on" shape. It is a **separate** endpoint, not
new fields on `external/login`, because the two payloads are independent (a legacy Parse
sign-in does not imply a Cloud Team Collection one, and vice versa) and the login page may
call either or both.

**Route**: `POST /bloom/api/external/cloudLogin`

**Request body** (JSON):
```json
{ "idToken": "<firebase-id-token-jwt>", "refreshToken": "<firebase-refresh-token>" }
```
Both fields are required, non-empty strings. `idToken` is the raw Firebase ID token JWT
(the login page's own Firebase SDK session already holds this after sign-in); `refreshToken`
is its paired Firebase refresh token. Bloom derives identity (email/user id/email_verified/
expiry) **only** from decoding `idToken`'s own claims — it never trusts a separately-supplied
email or verified flag (see `FirebaseCloudAuthProvider.AcceptExternalSession` /
`SessionFromIdToken`).

**Reply**: `200` with an empty body on success (`request.PostSucceeded()`, matching
`external/login`); a non-2xx status with a plain-text error message on failure (e.g. a
malformed/unparseable token). An `OPTIONS` preflight always succeeds with an empty 200, same
as every other `external/*` endpoint.

**Side effects on success**: the same as the local-mode `sharing/login` endpoint —
`CloudAuth`'s in-memory session is replaced (persisted via `DpapiCloudTokenStore` once the
production wiring in GOING-LIVE.md Phase 3.5 selects it), the `sharing`/`loginState`
websocket event fires so any open `useSharingLoginState()` subscriber (e.g. `SignInDialog`)
re-queries and updates/closes itself, and the Bloom window is brought to the front (matching
`external/login`'s `Shell.ComeToFront()` — the user's attention is already on the browser tab
that just finished signing in).

## Cloud functions

Two categories of server-side function back the cloud client. Both are gated by the caller's
Supabase login (a JWT), but they run in different places and have different powers. Since most of
this project's programmers work in the C#/TypeScript client rather than the database backend, the
distinction in one paragraph:

- **Postgres RPCs** run *inside the database* — they are SQL / PL/pgSQL functions that Supabase's
  PostgREST layer exposes over HTTP at `/rest/v1/rpc/...`, and the Bloom client calls them
  directly. Use them for pure database reads/writes; **row-level security (RLS)** enforces
  per-user, per-collection access on every call. An RPC cannot reach outside the database — in
  particular it cannot talk to Amazon S3.
- **Edge functions** are small TypeScript programs running on a *separate* server (Supabase's edge
  runtime — **not** the database), reached at `/functions/v1/<name>`; the Bloom client calls them
  over HTTP. Use one when an operation needs something the database can't do — above all, talking
  to **AWS S3**: only edge functions hold the AWS secret key, so anything that vends S3
  credentials or verifies S3 objects *must* be an edge function. An edge function may itself call
  RPCs / database functions — e.g. `checkin-finish` does its S3 work and then calls the
  `checkin_finish_tx` database function to record the result in one atomic transaction.

Both require the user's login; only edge functions additionally hold the AWS secret. Rule of
thumb: **creds-free, single-step database work → RPC; anything touching S3 or orchestrating
multiple steps → edge function.** (Edge functions live under `supabase/functions/`; the RPCs and
everything else in the database live in the declarative schema described next.)

### Database: declarative schema

The entire `tc` database — tables, the RPCs above, transaction (`_tx`) helpers, trigger functions,
row-level-security policies, and grants — is defined **declaratively** as the source of truth in
four files, applied in this order (wired via `[db.migrations].schema_paths` in `supabase/config.toml`):

| file | contents |
|------|----------|
| `supabase/schemas/tc/01_schema.sql`   | the `tc` schema + enum types |
| `supabase/schemas/tc/02_functions.sql`| every function (RPCs, `_tx`, triggers, helpers) |
| `supabase/schemas/tc/03_tables.sql`   | tables, constraints, indexes, triggers |
| `supabase/schemas/tc/04_security.sql` | RLS enable + policies + grants |

These files are what you **edit and review**. What actually runs (`supabase db reset` locally,
`supabase db push` to a project) is a migration under `supabase/migrations/`, which is a
*generated artifact* — the concatenation of the four schema files in order, produced by
`build/regen-init-migration.sh`.

> **Why concatenation, not `supabase db diff`?** The declarative workflow normally has you run
> `supabase db diff` to *generate* a migration from the schema files. We tried it: both diff
> engines (pg-schema-diff and migra) **silently drop every `COMMENT ON` and every
> `GRANT EXECUTE ON FUNCTION`** — which would leave the RPCs uncallable by `authenticated` and the
> schema undocumented. Concatenation is lossless, so that is how the migration is built.

**Making a schema change (pre-launch — current state):** there is a single initial migration and
the only database is local, so history is disposable. Edit the relevant `schemas/tc/*.sql` file, run
`build/regen-init-migration.sh`, then `supabase db reset` to rebuild and `supabase test db` to
check. Keep `SCHEMA.md`'s diagram in sync for table changes.

**After go-live:** once a real project database exists, its history must be preserved, so you can
no longer regenerate the initial migration. Switch to **forward-only delta migrations**: edit the
`schemas/tc/*.sql` file (still the source of truth), then hand-write a small migration for the delta
(or generate one with `supabase db diff` and **re-add the `COMMENT ON` / `GRANT` lines it drops** —
see the caveat above). Never edit the already-applied initial migration.

### Postgres RPCs (PostgREST `/rest/v1/rpc/...`)

Wire-format clarifications (v1.1): (1) the implemented SQL functions prefix every parameter
with `p_`, and PostgREST matches JSON keys to parameter names — so clients send
`{"p_collection_id": ...}` etc.; the table below keeps the logical (unprefixed) names.
(2) The `tc` schema is exposed as a separate PostgREST schema: RPC calls must carry the
`Content-Profile: tc` header (reads: `Accept-Profile: tc`).

| RPC | Args → Result |
|-----|----------------|
| `create_collection(id uuid, name text)` | creates collection + caller as sole claimed admin |
| `my_collections()` | collections where caller's email is approved (claimed or not) |
| `claim_memberships()` | fills user_id on rows matching caller's verified email |
| `get_collection_state(collection_id, since_event_id?)` | full/delta snapshot: book rows (locks, current version seq + checksum), collection-file group versions, `max_event_id`. v1.9: book rows carry `checkoutGuidHash` (see "Checkout GUID" below; NULL when unlocked) |
| `get_changes(collection_id, since_event_id)` | events + touched book rows (polling/catch-up). v1.9: book rows carry `checkoutGuidHash` |
| `get_book_manifest(book_id)` | v1.2: per-file current manifest `{bookId, versionId, seq, checksum, files:[{path, sha256, size, s3VersionId}]}` for pinned-version Receive; never-committed books invisible except to their mid-Send lock holder |
| `get_collection_file_manifest(collection_id, group_key)` | v1.7: per-file current manifest `{groupKey, version, files:[{path, sha256, size, s3VersionId}]}` for one collection-file group, so the download path fetches only changed files pinned to their committed `s3_version_id` (E9); a never-written group returns `version 0` / empty `files`. Mirrors `get_book_manifest`. |
| `checkout_book(book_id, machine text, checkout_guid text)` | conditional lock of a FREE book; returns resulting status (winner's identity on failure). v1.10: `checkout_guid` is made by the client and saved in `.checkout` before the call (see "Checkout GUID" below); NULL or blank raises SQLSTATE 22023 `invalid_checkout_guid`. On success returns `{success: true, locked_by, locked_by_machine, locked_at}` (no GUID). A retry by the caller with the SAME GUID after it succeeded returns the same success, changing nothing and emitting no second event. A book the caller holds under a different GUID (another copy), or under a send-only check-in lock, returns `{success: false, locked_by_me: true, locked_by, locked_by_machine, locked_at}` and changes nothing, because replacing the GUID would orphan the copy holding the current one. Locked by someone else (or deleted): `{success: false, locked_by, locked_by_machine, locked_at}`. `machine` is for display only. |
| `checkout_book_takeover(book_id, checkout_guid text, machine text)` | v1.4/v1.9: atomically reassigns a DIFFERENT account's lock to the caller ONLY when `checkout_guid` is the lock's current checkout GUID (account switch, batch item 9: account B opening the local copy account A checked the book out in, whose `.checkout` file holds the GUID). Presenting the member-readable hash does not work. The GUID is kept (not rotated), so the same copy goes on working under the new account. `machine` is recorded with the new lock for display. Returns `{success, locked_by, locked_by_machine, locked_at}`; emits a CheckOut event only on a genuine handover; safe to call speculatively — no-ops (success:false) when unlocked, already the caller's, or the GUID is missing/wrong. |
| `unlock_book(book_id, checkout_guid text)` | release own lock (undo checkout, no content change). v1.9: needs the current checkout GUID; otherwise raises `CheckoutElsewhere: ...` (SQLSTATE P0001; HTTP 400), as it does for the hash in place of the GUID. Not the holder: `lock_not_held: ...` as before |
| `force_unlock(book_id)` | admin; audited; emits ForcedUnlock event. Never needs the checkout GUID; clears it with the lock |
| `delete_book(book_id, checkout_guid text)` | requires caller holds the lock and (v1.9) presents its checkout GUID (else `CheckoutElsewhere: ...`, SQLSTATE P0001); sets `deleted_at`; emits Deleted |
| `undelete_book(book_id)` | admin; clears tombstone (name-uniqueness enforced) |
| `rename_check(book_id, new_name)` | advisory uniqueness pre-check |
| `members: list/add/remove/set_role` | admin-only approved-accounts management; remove force-unlocks that user's checkouts (evented); last-admin guard. v1.6: list rows carry `display_name` |
| `members_set_display_name(collection_id, member_id bigint, display_name text)` | v1.6: sets the durable human-readable name shown in place of the email (member list, checkout status, history). Admin may set anyone's; a claimed member may set their own; blank/whitespace clears to NULL (display falls back to email); max 100 chars |
| `add_palette_colors(collection_id, palette, colors[])` | union merge |
| `log_event(...)` | client-originated history entries |

All timestamps server-side. All RPCs RLS-gated; books/versions accept no direct writes.

#### Checkout GUID (v1.9, v1.10)

v1.10: the client that checks a book out makes a random GUID (`Guid.NewGuid()`, lowercase "D"
form), writes it to `<bookFolder>/.checkout` (never uploaded) and only then sends it to
`checkout_book`; the server never makes or returns one. A lost response is recovered by retrying
with the same GUID (idempotent) or by comparing the book row's `checkoutGuidHash` with the hash of
the GUID on disk. Only `checkout_book` creates a checkout: the lock `checkin-start` takes on a new
or free book is a send-only lock with no hash, which ends with that check-in. The server stores
only the hash in `tc.books.checkout_guid_hash`:

    checkoutGuidHash = lowercase hex( SHA-256( UTF-8 bytes of lower(guid) ) )

(SQL: `encode(sha256(convert_to(lower(guid), 'UTF8')), 'hex')`). The hash is member-readable
(a hash of 122 random bits cannot be reversed) and comes back on every book row as
`checkoutGuidHash`; the GUID itself is stored nowhere. A copy's `.checkout` is current only when
the row is locked by the caller and `checkoutGuidHash` equals the hash of its GUID. Check-in,
`unlock_book` and `delete_book` by the holder need the GUID, and so does `checkout_book_takeover`
by another account; `force_unlock` and member removal (admin) never do, and clear it with the
lock. The hash is also cleared whenever the lock is released or passes to another account without
a new GUID (only `checkout_book_takeover` hands the same GUID on).

### Edge functions (`/functions/v1/<name>`, JWT-verified; only these hold AWS creds)

#### `checkin-start` POST
Req: `{ collectionId, bookId?, bookInstanceId, proposedName, baseVersionId?, checksum,
clientVersion, files: [{path, sha256, size}], checkoutGuid? }`
- `bookId` null ⇒ first Send of a new book: validates name/instance-id uniqueness; creates the
  row locked to caller with NO current version (invisible to teammates until first commit).
  v1.10: the lock is for the send only, with no checkout GUID (a first check-in never leaves the
  book checked out); re-calling for the same never-committed book (resume) needs only the same
  user and `bookInstanceId` (any `checkoutGuid` sent is ignored).
- v1.9/v1.10, existing book: if the caller has it checked out, `checkoutGuid` must be its current
  checkout GUID, else 409 `CheckoutElsewhere` (the caller holds it in another copy); under the
  caller's own send-only lock (an unfinished check-in that took it while free) `checkoutGuid` must
  be absent. If the book is free, start takes a send-only lock (no GUID; finish, abort and expiry
  release it) and emits a CheckOut event (type 0, as `checkout_book` does, so other clients see
  the lock via realtime/`get_changes`); if someone else holds it, 409 `LockHeldByOther` as before.
- The S3 credentials are obtained before the lock is taken; a refused start returns none.
- Existing book: `proposedName` must not equal (case-insensitively, after NFC) another live
  book's name, else 409 `NameConflict` (v1.8; previously only caught at finish).
- v1.8: every `files[].path` is NFC-normalized before anything else, and validated: it must be a
  non-empty relative path (no leading `/`, no empty, `.` or `..` segment), with a `sha256`
  string and a non-negative integer `size`; two entries whose paths are equal after
  normalization are refused. Failure ⇒ 400 `InvalidManifest` (+`detail`, and `entries` or
  `paths`). `changedPaths` are returned NFC: **the client must upload each changed file to
  `prefix + changedPath` exactly as returned**, not under its local spelling.
- The transaction records the book's current version as its base (whether or not
  `baseVersionId` was sent); `checkin-finish` refuses to commit if the book has moved on.
- Re-call with the same open transaction ⇒ refreshed credentials, same transactionId.
200: `{ transactionId, changedPaths[], s3: { bucket, region, prefix,
credentials: { accessKeyId, secretAccessKey, sessionToken, expiration } } }`
(creds scoped `tc/{cid}/books/{bookInstanceId}/*`, 1 h; v1.10: no `checkoutGuid` — a check-in
never writes `.checkout`)
Errors: 400 `InvalidManifest` · 401/403 · 409 `LockHeldByOther` (+holder) /
`CheckoutElsewhere` / `BaseVersionSuperseded` / `NameConflict` · 426 `ClientOutOfDate`.

#### `checkin-finish` POST
Req: `{ transactionId, comment?, keepCheckedOut? }`
Verifies each changed object's sha256 attribute; captures s3 version-ids; one DB tx:
version (metadata) row, current-manifest rows (superseded rows pruned), book row update,
lock release (unless keepCheckedOut), events (Created+CheckIn for a new book), writes
`.manifest.json`. 200: `{ versionId, seq }` · 409 `MissingOrBadUploads { paths[] }`
(re-upload + retry, idempotent) · 410 transaction expired.
v1.8: before committing it re-checks, under a row lock, that the caller still holds the book's
lock and that the book is still at the transaction's base version; otherwise nothing is written
and it returns 409 `LockHeldByOther` (`holder` as in checkin-start, or `null` if the lock was
released, e.g. force-unlocked) or 409 `BaseVersionSuperseded` (`currentVersionId`,
`currentVersionSeq`) — the client must Receive and re-send rather than retry. A retry of a
finish that already committed (even one racing it) returns the same `{ versionId, seq }`.
v1.9: it also refuses with 409 `CheckoutElsewhere` unless the book still has the checkout GUID
the transaction started under (checked after `LockHeldByOther`, before
`BaseVersionSuperseded`). `keepCheckedOut: true` keeps the lock AND the GUID; otherwise both are
released. v1.10: under a send-only lock (start created the book or took it while free; no GUID)
the book must still have no GUID, and the lock is always released, `keepCheckedOut` or not.
v1.9 follow-up: 409 `TransactionChanged` means a `checkin-start` for the same transaction (a
resume, which rewrites its file list) ran while this finish was verifying the uploads, so what
was verified is no longer what would be committed. Nothing is written and the transaction stays
open; treat it like any other failed finish (start again, upload the returned `changedPaths`,
finish).

*Internal (not called by the client):* the finish edge functions establish the caller from
their own JWT via the `tc.current_caller()` RPC (validated by PostgREST, so this works for a
Firebase ID token under Option A as well as a local GoTrue token), then call
`tc.checkin_finish_tx` / `tc.collection_files_finish_tx` with the **service-role key**, passing
that user id. Those two RPCs are EXECUTE-able by `service_role` only, because they trust the
S3 version-ids they are given; a member calling them directly gets `permission denied`. Each
also takes `p_expected_revision`: the transaction row's `revision` as the edge function read it
along with the file list it verified; every start resume bumps `revision`, and a mismatch is the
409 `TransactionChanged` above.

#### `checkin-abort` POST — `{ transactionId }` → 200.
Removes a never-committed new book; v1.10: releases an existing book's send-only lock (no GUID)
that the aborted check-in took; a checkout (with a GUID) is kept. An expired check-in's send-only
lock is released the same way when it is reaped.

#### `download-start` POST — `{ collectionId }` →
200 `{ s3: {...} }` read-only creds (`GetObject` + `GetObjectVersion`) scoped `tc/{cid}/*`, 1 h.

#### `collection-files-start` / `collection-files-finish` POST
`{ collectionId, groupKey: 'other'|'allowed-words'|'sample-texts', expectedVersion, files[] }`
two-phase like check-in; finish bumps the group version atomically; 409 `VersionConflict`
⇒ client pulls first (repo-wins rule). v1.8: paths are NFC-normalized/validated at start
exactly as for checkin-start (400 `InvalidManifest`; upload to the returned `changedPaths`),
and finish retries are idempotent (`{ version }` of the committed transaction). v1.9 follow-up:
finish can also answer 409 `TransactionChanged` (a concurrent start resumed the transaction
while finish was verifying it; nothing committed, retry as for any failed finish).

## Realtime

Private broadcast channel `collection:{uuid}` (events-table trigger). Message:
`{ eventId, type, bookId?, versionSeq?, byUserName, byEmail, lock?, name?, groupKey? }`.
Clients persist `last_seen_event_id`; on (re)connect always run one `get_changes` delta first.
Event `type` values = existing `BookHistoryEventType` numerics + incident extensions
(e.g. WorkPreservedLocally).

## S3 layout (bucket versioning ON; lifecycle: abort-multipart 7d, noncurrent expiry ~7d)

```
tc/{collectionId}/books/{bookInstanceId}/{relativePath}     (NFC-normalized)
tc/{collectionId}/books/{bookInstanceId}/.manifest.json     (current manifest backup)
tc/{collectionId}/collectionFiles/{group}/{relativePath}
```
Reads are ALWAYS by (path, s3VersionId) from the committed manifest — never "latest".
Invariant: check-in transaction lifetime < noncurrent-expiry floor.

## Book-status JSON (client ↔ TeamCollectionApi, additive)

Existing `IBookTeamCollectionStatus` fields unchanged; adds `localVersionSeq?`,
`repoVersionSeq?`, `signedIn`, backend capability flags.
