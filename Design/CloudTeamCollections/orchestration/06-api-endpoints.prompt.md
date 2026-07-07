# Agent prompt — task 06: API endpoints (resume-aware)

You are implementing task 06 in the MAIN working tree at c:\github\BloomDesktop (NOT a
worktree). You are the exclusive owner of the shared file `TeamCollectionApi.cs` during
this task.

**Resume check (do this FIRST):** `git status` must be clean (stop and report if not). If
branch `task/06-api-endpoints` exists, check it out and continue from the `## Progress
log` at the bottom of `Design/CloudTeamCollections/tasks/06-api-endpoints.md`. Otherwise
`git checkout -b task/06-api-endpoints cloud-collections`.

**Durability protocol (mandatory):** commit after EVERY completed step; small coherent
commits ending "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"; tick the step's
checkbox + update the `## Progress log` (`date · done · exact next action`) in the same
commit. Interruptions are expected; only commits survive.

**Anti-hang rules:** no watch modes/foreground servers (background + redirect only);
`--max-time` on curl; timeouts everywhere.

**Read first:** `Design/CloudTeamCollections/tasks/06-api-endpoints.md` (authoritative
steps); CONTRACTS.md v1.2 §Book-status JSON; the merged Cloud classes you expose
(`CloudTeamCollection`, `CloudJoinFlow`, `CloudAuth.GetLoginState`,
`CloudCollectionClient`); the END of
`Design/CloudTeamCollections/tasks/08-ui-collection-tab.md`'s Progress log — it lists the
~9 mocked endpoint names the Wave-2 UI shells already call (capabilities, tcStatusMetadata,
sendAllBooks, receiveUpdates, sharing/forceUnlock, history events, etc.); your endpoints
must match those names/shapes exactly, plus task 07's (sharing/loginState,
sharing/showSignIn, collections/mine, collections/pullDown,
teamCollection/showCreateCloudTeamCollectionDialog).

**Scope:**
- New `src/BloomExe/web/controllers/SharingApi.cs` (endpoints per the task file), thin
  pass-throughs to CloudCollectionClient/CloudJoinFlow/CloudAuth — no business logic.
- `TeamCollectionApi.cs`: additive only; book-status JSON gains
  localVersionSeq/repoVersionSeq/signedIn/capability flags; receiveUpdates; sendAll;
  force-unlock routes through the audited RPC for cloud. Folder-TC responses must stay
  BYTE-IDENTICAL (existing tests untouched and green).
- **One authorized supabase addition:** a NEW migration (never edit merged ones),
  `supabase/migrations/20260707000006_tc_locked_by_display.sql`, extending
  get_collection_state/get_changes/get_book_manifest outputs with `lockedByEmail` /
  `lockedByName` (join tc.members on locked_by = user_id) — task 05 found locked_by is the
  raw auth UUID, useless for "checked out to Sara" display. Apply with
  `supabase migration up` and live-verify. Update the C# status JSON to carry them.
- Websocket pushes for member-list changes + status refresh reuse existing contexts.

**Environment:** local stack UP (Supabase 127.0.0.1:54321, anon key via `supabase
status`; dev users admin/alice/bob@dev.local pw BloomDev123!; edge functions via
`supabase functions serve --env-file server/dev/functions.env` in background w/ redirect).

**Build caution:** Bloom.exe may be RUNNING; apphost-copy MSB3027 is benign — verify
output\Tests\Debug\AnyCPU\Bloom.dll freshness, run
`dotnet test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~Cloud"`,
`~TeamCollectionApi`, and the full `~TeamCollection` regression; report verbatim counts.

**Final report (raw data):** branch + shas; endpoint list (name → implementation status →
matched UI caller); test commands + verbatim counts; migration live-verification result;
anything the UI wiring (next step) still needs; exact next action if unfinished.
