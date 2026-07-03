# 10 — Adoption path + polish (Wave 4)

**Goal**: the manual folder-TC → cloud path is smooth, documented, and clean.

**Dependencies**: waves 0–3. Touches: cloud-create flow (cleanup step), docs.

## Steps
- [ ] Enabling cloud on a formerly-folder-TC collection cleans stale artifacts: per-book
      `TeamCollection.status`, `lastCollectionFileSyncData.txt`, `log.txt`; simultaneous
      folder-link + cloud-link = error with fix instructions.
- [ ] Members' existing local copies reconcile by checksum on first Receive (verify the
      first-time-join merge path).
- [ ] User documentation: the un-team + enable + invite-team walkthrough (docs site), incl.
      "everyone check in first".
- [ ] Localization sweep of all new strings (xlf-strings skill rules).
- [ ] Analytics review: create/join/send (bytes uploaded vs skipped)/receive/force-unlock/
      incident events flowing with Backend="Cloud".
- [ ] Dogfood with a real team; triage findings.

## Acceptance
- E2E-7 green; a real Dropbox-TC collection migrated by hand following only the docs.

**Agent notes**: Sonnet + Haiku (strings/docs). Nothing here changes protocol or schema.
