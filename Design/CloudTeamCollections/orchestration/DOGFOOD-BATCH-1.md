# Dogfood batch 1 (9 Jul 2026) — restartable work plan

John's bug/improvement list from first real dogfooding, plus decisions already made.
This file is the durable state for the batch: the orchestrator ticks checkboxes and
updates each item's `Status:` line as work proceeds (same protocol as RESUME.md — commit
after every completed step, progress state lives in git, never only in a conversation).

**To restart after any interruption:** start a fresh Claude Code session in this repo and
say: **"Resume the dogfood batch per
Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md."** The resumer reads each
item's Status line, secures any uncommitted `task/*` or worktree work as WIP commits, and
continues with the next unchecked step. General orchestration rules (review-before-merge,
C# test filter, dev-stack bring-up, environment quirks) are in RESUME.md and apply here.

**Testing protocol for this batch (agreed with John 9 Jul):** per item, run only the 1–2
E2E specs covering the touched area (each spec independently wipes + reseeds the dev DB,
so subsets are trustworthy); add `e2e-1-create-share` whenever a change touches startup or
localizable strings. One full-matrix run before pushing the finished batch. E2E runs are
serialized (shared dev DB + real Bloom launches); code work may proceed during a run.
Bloom windows go to John's spare screen via machine-level `BLOOM_E2E_SCREEN=1`.

**Decision already made (9 Jul, John):** remote-checkin application is FULLY AUTOMATIC —
when the poll notices a checkin for a book not checked out here, apply it to the local
book folder immediately and refresh the preview if that book is selected. The Sync button
(renamed from Reload) forces an immediate pass; no updates-available nag for the common
case.

## Work order

Chosen order: quick wins first, then the propagation cluster (items 4+5 are one work
item), then the two larger UI features. Items 1–3 are independent of everything else.

### 1. "Bloom is busy" missing localization  `[quick]`
Status: NOT STARTED
- [ ] Find the source of the "Bloom is busy" message (grep C# + TS; likely surfaced during
      cloud operations); determine whether the string is needed at all.
- [ ] If needed: give it a proper l10n id + XLF entry per `.github/skills/xlf-strings/SKILL.md`
      (en only; NEVER `--` inside `<note>` — crashes every launch).
- [ ] Verify: `e2e-1-create-share` (launch gate for XLF changes).

### 2. Poll immediately on book selection  `[quick]`
Status: NOT STARTED
- [ ] When a book is selected in a cloud TC, immediately refresh its checkout status from
      the server (targeted status fetch or PollNow — decide by cost; the monitor's poll
      loop already exists: CloudCollectionMonitor).
- [ ] Guard: no spam when rapidly changing selection (coalesce/in-flight check).
- [ ] Unit test if seam allows; verify with `e2e-2-collaboration-loop`.

### 3. Center the checkin-progress dialog in the status panel  `[quick]`
Status: NOT STARTED
- [ ] The cloud checkin ("Send") progress dialog (BrowserProgressDialog, launched from the
      TC status panel path) currently appears elsewhere; position it centered over the TC
      status panel area.
- [ ] Verify visually via a manual/scripted launch + `e2e-2-collaboration-loop` (which
      exercises checkin).

### 4+5. Automatic remote-update application + in-place Sync (one work item)  `[medium]`
Status: NOT STARTED
Observed bug: after a remote checkin, the other instance updated lock state (avatar +
status panel) but the TC button showed no "updates available" and the preview did not
refresh; book folder content update unverified.
- [ ] Diagnose: what does CloudCollectionMonitor do with a remote checkin today — does it
      copy the new version to the local folder at all, or only update status? (Compare
      folder-TC behavior: queued message → Reload button.)
- [ ] Implement fully-automatic application (see decision above): poll notices checkin →
      download/apply book to local folder → notify UI → refresh preview if selected.
- [ ] SAFETY (John, 9 Jul): the partially-downloaded intermediate state must never be
      user-reachable. Stage the download into a temp folder and swap it in atomically
      (never write file-by-file into the live book folder); while the swap/apply is in
      flight, the book is "busy" — checkout, edit, publish, delete, and rename must be
      blocked or safely queued. Selecting the book mid-apply shows a transient state, not
      half-updated content.
- [ ] Rename "Reload" to "Sync" for cloud TCs and make it an in-place update: apply all
      pending remote changes WITHOUT closing/reopening the collection (folder TCs keep
      their existing reload semantics unless trivially unifiable).
- [ ] Keep the reload-requiring paths (settings changes etc.) working — not everything can
      be in-place; distinguish the cases.
- [ ] XLF for the new "Sync" label (skill rules apply).
- [ ] Verify: `e2e-2-collaboration-loop` + `e2e-8-receive-during-send`; e2e-1 for the XLF.
- [ ] Update tests/specs that assert on the old Reload wording/behavior.

### 6. Join-card integration in the collection chooser  `[medium]`
Status: NOT STARTED
- [ ] In the collection chooser dialog, remove the separate "team collections to join"
      list; instead add extra cards to the MAIN collection list for collections the user
      is invited to (server membership exists) but has no local copy of.
- [ ] NO join card when the user already joined + has a local copy. DO show a join card
      when a same-named local collection exists that is NOT a TC (existing join-conflict
      code handles the actual join).
- [ ] Join cards do not count against the MRU-list card limit.
- [ ] Omit any card info not available for an unjoined TC (thumbnail, languages, …).
- [ ] Verify: `join-auto-open` + `e2e-1-create-share`; vitest for the card-list logic.

### 7. Progressive join: open the collection before all books download  `[large]`
Status: NOT STARTED
- [ ] On join, fetch collection settings + book list (titles) first, open the collection
      immediately; books not yet downloaded show a placeholder icon suggesting an
      in-progress download.
- [ ] Background-download books; swap each placeholder for the real icon as its download
      completes.
- [ ] Selecting a not-yet-downloaded book bumps it to the front of the download queue;
      status panel shows a "downloading" message until it arrives.
- [ ] Same SAFETY rule as item 4+5: a book appears in the collection only as placeholder
      (no dangerous actions possible) or fully downloaded — never as a half-populated
      folder the user can act on. Temp-folder staging + atomic swap.
- [ ] Handle interruption: Bloom closed mid-join resumes/completes downloads on next open
      (SyncAtStartup should already fetch missing books — verify).
- [ ] Verify: `join-auto-open` + `e2e-9-new-book-lifecycle`; consider a new spec for the
      placeholder/priority behavior if cheap.

## Also queued from dogfooding (not in John's list, orchestrator-flagged)
- Administrators field shows the REGISTRATION email (john_thomson@sil.org) instead of the
  signed-in account email for cloud TCs (`ConnectToCloudCollection` sets
  `Settings.Administrators = new[] { CurrentUser }`) — cosmetic identity-model
  inconsistency, fix opportunistically with item 4+5 or 6.

## Progress log
(orchestrator appends: date · what was just completed · EXACT next action)
- 9 Jul 2026 · Batch plan created; full-matrix baseline run in progress (validates
  checkin-comment fix + 5s poll live) · Next: item 1 ("Bloom is busy" l10n) code work
  while the matrix runs.
