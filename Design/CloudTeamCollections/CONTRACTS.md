# Cloud Team Collections — frozen API contracts (v1)

Changes to this file require an orchestrator commit and a version-note bump here.
**Contract version: 1.6** (13 Jul 2026 — durable member display names, additive (John's
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

**Side effects on success**: the same as the dev-mode `sharing/login` endpoint —
`CloudAuth`'s in-memory session is replaced (persisted via `DpapiCloudTokenStore` once the
production wiring in GOING-LIVE.md Phase 3.5 selects it), the `sharing`/`loginState`
websocket event fires so any open `useSharingLoginState()` subscriber (e.g. `SignInDialog`)
re-queries and updates/closes itself, and the Bloom window is brought to the front (matching
`external/login`'s `Shell.ComeToFront()` — the user's attention is already on the browser tab
that just finished signing in).

## Postgres RPCs (PostgREST `/rest/v1/rpc/...`)

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
| `get_collection_state(collection_id, since_event_id?)` | full/delta snapshot: book rows (locks, current version seq + checksum), collection-file group versions, `max_event_id` |
| `get_changes(collection_id, since_event_id)` | events + touched book rows (polling/catch-up) |
| `get_book_manifest(book_id)` | v1.2: per-file current manifest `{bookId, versionId, seq, checksum, files:[{path, sha256, size, s3VersionId}]}` for pinned-version Receive; never-committed books invisible except to their mid-Send lock holder |
| `checkout_book(book_id, machine text, seat text?)` | conditional lock; returns resulting status (winner's identity on failure). v1.5: also records the caller's `seat` — a stable hash of the local collection folder path identifying WHICH local copy took the lock (never the raw path); returns `locked_seat`. |
| `checkout_book_takeover(book_id, machine text, seat text?)` | v1.4/v1.5: atomically reassigns another account's lock to the caller ONLY when the existing lock is recorded for the same machine AND the same seat (account-switch, batch item 9 + bug #0: two local copies on one computer are two seats); a NULL stored seat never matches (fail-safe). Returns `{success, locked_by, locked_by_machine, locked_seat, locked_at}` (same shape as checkout_book); emits a CheckOut event only on a genuine handover; safe to call speculatively — no-ops (success:false) when unlocked, already the caller's, locked on a different machine, or locked in a different/unknown seat. Note: `machine` and `seat` are client-asserted, consistent with checkout_book's existing trust model. |
| `unlock_book(book_id)` | release own lock (undo checkout, no content change) |
| `force_unlock(book_id)` | admin; audited; emits ForcedUnlock event |
| `delete_book(book_id)` | requires caller holds the lock; sets `deleted_at`; emits Deleted |
| `undelete_book(book_id)` | admin; clears tombstone (name-uniqueness enforced) |
| `rename_check(book_id, new_name)` | advisory uniqueness pre-check |
| `members: list/add/remove/set_role` | admin-only approved-accounts management; remove force-unlocks that user's checkouts (evented); last-admin guard. v1.6: list rows carry `display_name` |
| `members_set_display_name(collection_id, member_id bigint, display_name text)` | v1.6: sets the durable human-readable name shown in place of the email (member list, checkout status, history). Admin may set anyone's; a claimed member may set their own; blank/whitespace clears to NULL (display falls back to email); max 100 chars |
| `add_palette_colors(collection_id, palette, colors[])` | union merge |
| `log_event(...)` | client-originated history entries |

All timestamps server-side. All RPCs RLS-gated; books/versions accept no direct writes.

## Edge functions (`/functions/v1/<name>`, JWT-verified; only these hold AWS creds)

### `checkin-start` POST
Req: `{ collectionId, bookId?, bookInstanceId, proposedName, baseVersionId?, checksum,
clientVersion, files: [{path, sha256, size}] }`
- `bookId` null ⇒ first Send of a new book: validates name/instance-id uniqueness; creates the
  row locked to caller with NO current version (invisible to teammates until first commit).
- Re-call with the same open transaction ⇒ refreshed credentials, same transactionId.
200: `{ transactionId, changedPaths[], s3: { bucket, region, prefix,
credentials: { accessKeyId, secretAccessKey, sessionToken, expiration } } }`
(creds scoped `tc/{cid}/books/{bookInstanceId}/*`, 1 h)
Errors: 401/403 · 409 `LockHeldByOther` (+holder) / `BaseVersionSuperseded` / `NameConflict`
· 426 `ClientOutOfDate`.

### `checkin-finish` POST
Req: `{ transactionId, comment?, keepCheckedOut? }`
Verifies each changed object's sha256 attribute; captures s3 version-ids; one DB tx:
version (metadata) row, current-manifest rows (superseded rows pruned), book row update,
lock release (unless keepCheckedOut), events (Created+CheckIn for a new book), writes
`.manifest.json`. 200: `{ versionId, seq }` · 409 `MissingOrBadUploads { paths[] }`
(re-upload + retry, idempotent) · 410 transaction expired.

### `checkin-abort` POST — `{ transactionId }` → 200.

### `download-start` POST — `{ collectionId }` →
200 `{ s3: {...} }` read-only creds (`GetObject` + `GetObjectVersion`) scoped `tc/{cid}/*`, 1 h.

### `collection-files-start` / `collection-files-finish` POST
`{ collectionId, groupKey: 'other'|'allowed-words'|'sample-texts', expectedVersion, files[] }`
two-phase like check-in; finish bumps the group version atomically; 409 `VersionConflict`
⇒ client pulls first (repo-wins rule).

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
