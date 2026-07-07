# Cloud Team Collections — frozen API contracts (v1)

Changes to this file require an orchestrator commit and a version-note bump here.
**Contract version: 1.2** (7 Jul 2026 — added the `get_book_manifest` RPC, additive; the
Receive path needs a per-book file manifest and no existing RPC carried one. v1.1, 6 Jul:
two wire-format clarifications under "Postgres RPCs"; no semantic changes.)

## Link file

`TeamCollectionLink.txt` content is either a folder path (legacy folder TC) or
`cloud://sil.bloom/collection/<collectionId>` where `<collectionId>` = the Bloom
CollectionId GUID (also the server `collections.id`).

## Auth

Bearer JWT on every request (mechanism per pending Option A/B/C decision; `CloudAuth`
isolates it). Claims used server-side: `sub` (user id), `email`, `email_verified`.
Claiming an approval requires `email_verified = true`.

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
| `checkout_book(book_id, machine text)` | conditional lock; returns resulting status (winner's identity on failure) |
| `unlock_book(book_id)` | release own lock (undo checkout, no content change) |
| `force_unlock(book_id)` | admin; audited; emits ForcedUnlock event |
| `delete_book(book_id)` | requires caller holds the lock; sets `deleted_at`; emits Deleted |
| `undelete_book(book_id)` | admin; clears tombstone (name-uniqueness enforced) |
| `rename_check(book_id, new_name)` | advisory uniqueness pre-check |
| `members: list/add/remove/set_role` | admin-only approved-accounts management; remove force-unlocks that user's checkouts (evented); last-admin guard |
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
