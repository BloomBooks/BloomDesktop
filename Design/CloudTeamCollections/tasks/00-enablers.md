# 00 — Enablers (Wave 0, orchestrator-led, sequential)

**Goal**: make the base classes backend-pluggable with provably zero behavior change for the
folder backend.

**Dependencies**: none. **Do not parallelize** — touches shared hot files.

**File ownership (shared, exclusive during this task)**: `src/BloomExe/TeamCollection/TeamCollection.cs`,
`TeamCollectionManager.cs`; new `TeamCollectionLink.cs`.

## Steps
- [ ] `TeamCollectionLink` class: parse/write folder-path and `cloud://sil.bloom/collection/<id>`
      forms of `TeamCollectionLink.txt`; error on both-forms-present.
- [ ] Backend factory replacing the three hardcoded `new FolderTeamCollection(...)` sites
      (manager ctor ~line 335–416, `ConnectToTeamCollection` ~500, subscription-reconnect
      handler ~260–306). Add `ConnectToCloudCollection(collectionId)` to `ITeamCollectionManager`
      (throws NotImplemented for now).
- [ ] Virtual lock seams: `protected virtual bool TryLockInRepo(bookName)` /
      `UnlockInRepo(bookName, force)`; folder overrides preserve current read-modify-write
      behavior verbatim; `AttemptLock`/`UnlockBook`/`ForceUnlock` route through them.
- [ ] Capability flags: virtual `SupportsVersionHistory` / `SupportsSharingUi` /
      `RequiresSignIn` (folder: false/false/false).
- [ ] Audit + document the ~10 `WriteBookStatus` callers (notes for task 05's diff-dispatch).
- [ ] Feature flag: cloud sharing behind the experimental-features setting (registration only;
      no UI yet).

## Acceptance
- `dotnet test` — the ENTIRE existing TeamCollection suite passes unchanged (no test edits).
- New `TeamCollectionLinkTests` (parse/write/garbage/missing/both-present).
- Manual smoke: an existing folder TC opens, checks out, checks in exactly as before.

**Agent notes**: orchestrator only. No cloud code in this task. Keep each refactor
mechanical and reviewable in isolation.
