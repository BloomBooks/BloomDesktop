# 06 — API endpoints (Wave 3, after 05)

**Goal**: expose cloud operations to the browser UI.

**Dependencies**: 05. **Exclusive owner of shared file `TeamCollectionApi.cs`** during this
task. Owns new `src/BloomExe/web/controllers/SharingApi.cs`.

## Steps
- [x] `SharingApi`: `sharing/members` (GET), `sharing/addApproval`, `sharing/removeApproval`,
      `sharing/setRole`, `sharing/loginState`, `sharing/login`, `sharing/logout`,
      `collections/mine` (Get-my-Team-Collections), `collections/pullDown`. Plus (matching what
      the Wave-2/UI tasks already call): `sharing/showSignIn`, `sharing/forceUnlock`,
      `sharing/history`, `sharing/historyCache`.
- [x] `TeamCollectionApi` additions (existing endpoints untouched for folder TCs): book-status
      JSON gains `localVersionSeq`/`repoVersionSeq`/`signedIn`/capability flags (additive);
      `teamCollection/receiveUpdates`; `sendAll` alias of checkInAllBooks for cloud; force-unlock
      routes through the audited RPC. Plus `teamCollection/capabilities`, `tcStatusMetadata`,
      `cloudCollectionId`, `isUserAdmin`, `createCloudTeamCollection`,
      `showCreateCloudTeamCollectionDialog` (all named by the Wave-2 UI tasks' mocks).
- [x] Websocket pushes for member-list changes and status refresh reuse existing contexts
      (`BloomWebSocketServer.SendEvent`, same as folder TCs already use).
- [x] One authorized migration: `20260707000006_tc_locked_by_display.sql`
      (`lockedByEmail`/`lockedByName` on get_collection_state/get_changes/get_book_manifest).

## Acceptance
- `SharingApiTests` + `TeamCollectionApiTests` additions (permissions, listing, status fields).
  DONE: `SharingApiTests.cs` (9 tests) + `TeamCollectionApiCloudTests.cs` (10 tests).
- Folder-TC API behavior byte-identical (existing tests untouched and green). DONE: full
  `~TeamCollection` suite 311/311; `AddCloudBookStatusFields` returns the exact same string
  instance (not even a re-parse) when the current collection isn't a `CloudTeamCollection`.

**Agent notes**: Sonnet. Thin pass-throughs to `CloudCollectionClient`; no business logic here.

## Progress log
- 7 Jul 2026 · done: full task — migration (live-verified against the local dev stack), all
  SharingApi + TeamCollectionApi endpoints, live-verification fixes to
  `CloudCollectionClient.MembersRemove/MembersSetRole` (wrong RPC param name — found by actually
  calling the deployed RPCs) and a `CloudTeamCollection` auth-initialization gap (reopening an
  existing cloud TC never called `InitializeAtStartup`), tests (112/112 Cloud, 31/31
  TeamCollectionApi, 311/311 full TeamCollection). See the task's final report (orchestrator
  merge notes) for the full endpoint list, known gaps (pullDown's untested manager=null path,
  no dedicated sign-in dialog yet, WireUpForWinforms bundle-collision finding for the UI team),
  and exact next action (UI wiring, task 07/08's "next" notes).
