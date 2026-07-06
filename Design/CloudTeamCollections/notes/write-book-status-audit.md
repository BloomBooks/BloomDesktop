# WriteBookStatus caller audit (task 00 prerequisite for task 05)

`TeamCollection.WriteBookStatus(bookName, status)` writes a `BookStatus` to **both** the repo
(via the abstract `WriteBookStatusJsonToRepo`) **and** the local status file.  The cloud backend
will need `WriteBookStatusJsonToRepo` to diff-dispatch to the narrowest RPC rather than always
writing the full JSON blob.

All callers are in `TeamCollection.cs` (abstract base class).  None of the existing callers are
in `FolderTeamCollection.cs` — that class only overrides `WriteBookStatusJsonToRepo`.

---

## Caller inventory

### 1. `ForgetChangesCheckin` (line ~271)

```csharp
status = status.WithLockedBy(null);
WriteBookStatus(finalBookName, status);
```

**What it writes**: Clears the lock (releases checkout) after abandoning local changes; the
checksum is unchanged (it came from `GetLocalStatus` before restore).
**Clears lock**: Yes — `WithLockedBy(null)`.
**Cloud dispatch**: → `unlock_book` RPC (no content change).

---

### 2. `AttemptLock` — routed through `TryLockInRepo` (line ~717, post-task-00)

```csharp
TryLockInRepo(bookName, status);   // status already has lockedBy set
```

**What it writes**: Sets `lockedBy`, `lockedByFirstName`, `lockedBySurname`, `lockedWhere`,
`lockedWhen` on an otherwise-unchanged status.
**Clears lock**: No — sets it.
**Cloud dispatch**: → `checkout_book` RPC (conditional lock).

---

### 3. `UnlockBook` — routed through `UnlockInRepo(force:false)` (line ~697, post-task-00)

```csharp
WriteBookStatus(bookName, GetStatus(bookName).WithLockedBy(null));
```

**What it writes**: Clears the lock; all other fields unchanged.
**Clears lock**: Yes.
**Cloud dispatch**: → `unlock_book` RPC.

---

### 4. `ForceUnlock` — routed through `UnlockInRepo(force:true)` (line ~729, post-task-00)

```csharp
WriteBookStatus(bookName, GetStatus(bookName).WithLockedBy(null));
```

**What it writes**: Force-clears the lock (admin operation); all other fields unchanged.
**Clears lock**: Yes.
**Cloud dispatch**: → `force_unlock` RPC (audited; emits ForcedUnlock event).

---

### 5. `SyncAtStartup` — restore checkout (line ~2467)

```csharp
WriteBookStatus(bookName, localStatus);
```

**Context**: `localAndRepoChecksumsMatch && repoStatus.lockedBy == null`.  Someone started a
checkout remotely then changed their mind.  We restore our checkout in the repo.
**What it writes**: Restores the full local status (lock + checksum) to repo.
**Clears lock**: No — re-asserts our lock.
**Cloud dispatch**: → `checkout_book` RPC (re-assert our existing checkout).

---

### 6. `SyncAtStartup` — accept remote lock, no local edits (line ~2503)

```csharp
WriteBookStatus(bookName, repoStatus);
```

**Context**: `localAndRepoChecksumsMatch`, repo shows a different lock holder; local has no
edits.  We accept the repo's lock state.
**What it writes**: Overwrites local status with repo status (changes lock owner).
**Clears lock**: No (may set or change lock owner).
**Cloud dispatch**: Local-only write — repo already has the correct state.  Only
`WriteLocalStatus` should be called; no repo RPC needed.

---

### 7. `SyncAtStartup` — update checksum after repo change, no local edits (line ~2527–2530)

```csharp
WriteBookStatus(bookName, localStatus.WithChecksum(repoStatus.checksum));
```

**Context**: Book changed in repo; local had no edits; we keep our local checkout but update
the checksum to match the newly-downloaded repo version.
**What it writes**: Updates the checksum in both repo and local status; lock unchanged.
**Clears lock**: No.
**Cloud dispatch**: Local-only write — the repo already has the new checksum (it IS the source
of truth).  Only `WriteLocalStatus` should be called; no repo RPC.

---

## Summary table

| # | Call site | State written | Lock change | Cloud RPC |
|---|-----------|---------------|-------------|-----------|
| 1 | `ForgetChangesCheckin` | restore from repo + clear lock | clears | `unlock_book` |
| 2 | `TryLockInRepo` (`AttemptLock`) | set lock fields | sets | `checkout_book` |
| 3 | `UnlockInRepo(force:false)` (`UnlockBook`) | clear lock | clears | `unlock_book` |
| 4 | `UnlockInRepo(force:true)` (`ForceUnlock`) | force-clear lock | clears | `force_unlock` |
| 5 | `SyncAtStartup` — restore checkout | full local status | sets | `checkout_book` |
| 6 | `SyncAtStartup` — accept remote lock | full repo status | changes owner | local-only |
| 7 | `SyncAtStartup` — update checksum | checksum only | none | local-only |

## Design note for task 05

Callers 6 and 7 write to the repo despite the cloud already holding the authoritative state.
For the cloud backend `WriteBookStatus` should be split so the repo half (`WriteBookStatusJsonToRepo`)
becomes a no-op (or a thin diff) when the cloud is already up-to-date.  The cleanest approach is
to override `WriteBookStatus` in `CloudTeamCollection` and route each caller through the
narrowest available RPC, falling back to a local-only write for cases 6 and 7.

`ForgetChangesCheckin` (caller 1) currently reads the local status before restore; after the
cloud book-copy lands in task 02+, this caller will need to signal an `unlock_book` RPC rather
than going through the full `WriteBookStatus` path.
