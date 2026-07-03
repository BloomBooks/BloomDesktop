# 08 — UI: collection tab (Wave 2 shells → Wave 3 wiring)

**Goal**: status button, status/history dialog, Share button, per-book panel states.

**Dependencies**: shells in Wave 2 (mocked); wiring after 06. **Exclusive owner of**
`TeamCollectionButton.tsx`, `TeamCollectionDialog.tsx`, `TeamCollectionBookStatusPanel.tsx`,
`statusPanelCommon.tsx`, `CollectionHistoryTable.tsx` during its waves.

## Steps
- [ ] Status button: same chip/colors, driven by live metadata ("Updates available (3 books)").
- [ ] Status dialog: "Receive Updates" primary action (Reload remains only for applied
      collection-settings changes); "Send All"; message log unchanged.
- [ ] Share button beside the status button → SharingPanel (admin manage / member read-only).
- [ ] Per-book panel: keep Check out/Check in + note field + avatars; add signedOut (with
      Sign-in action), updatesAvailable, offline-disabled-with-reason states; check-in progress
      = modal Send; "Force Unlock (Administrator Only)" wired to the audited RPC.
- [ ] Book thumbnails: holder-avatar overlay unchanged; subtle "newer version exists" marker.
- [ ] History tab: server events feed for cloud TCs (incl. incident entries), local cache for
      offline; folder TCs unchanged.

## Acceptance
- Component tests: panel state matrix (incl. new states), status-button states, history
  rendering incl. incidents.
- `yarn lint` clean; folder-TC UI behavior unchanged.

**Agent notes**: Sonnet. `StatusPanelState` additions must stay in sync with the C# status
JSON (CONTRACTS.md, book-status section).
