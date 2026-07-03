# 06 — API endpoints (Wave 3, after 05)

**Goal**: expose cloud operations to the browser UI.

**Dependencies**: 05. **Exclusive owner of shared file `TeamCollectionApi.cs`** during this
task. Owns new `src/BloomExe/web/controllers/SharingApi.cs`.

## Steps
- [ ] `SharingApi`: `sharing/members` (GET), `sharing/addApproval`, `sharing/removeApproval`,
      `sharing/setRole`, `sharing/loginState`, `sharing/login`, `sharing/logout`,
      `collections/mine` (Get-my-Team-Collections), `collections/pullDown`.
- [ ] `TeamCollectionApi` additions (existing endpoints untouched for folder TCs): book-status
      JSON gains `localVersionSeq`/`repoVersionSeq`/`signedIn`/capability flags (additive);
      `teamCollection/receiveUpdates`; `sendAll` alias of checkInAllBooks for cloud; force-unlock
      routes through the audited RPC.
- [ ] Websocket pushes for member-list changes and status refresh reuse existing contexts.

## Acceptance
- `SharingApiTests` + `TeamCollectionApiTests` additions (permissions, listing, status fields).
- Folder-TC API behavior byte-identical (existing tests untouched and green).

**Agent notes**: Sonnet. Thin pass-throughs to `CloudCollectionClient`; no business logic here.
