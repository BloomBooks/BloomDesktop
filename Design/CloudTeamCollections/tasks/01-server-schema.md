# 01 — Server schema + RPCs (Wave 1)

**Goal**: the `tc` Postgres schema, RLS, and all pure-DB RPCs, per CONTRACTS.md.

**Dependencies**: CONTRACTS.md frozen. Parallel-safe (owns `supabase/migrations/**`,
`supabase/tests/**` only).

## Steps
- [x] `supabase init` layout at repo root (`supabase/config.toml`), local dev via `supabase start`.
- [x] Migrations: `collections`, `members` (approved accounts; unique claimed user per
      collection; last-admin guard trigger), `books` (lock columns; `deleted_at`; unique
      (collection, instance_id); unique live lower(name)), `versions` (metadata),
      `version_files` (current manifest incl. s3_version_id), `collection_file_groups` +
      `collection_group_files`, `color_palette_entries`, `events` (+ indexes, realtime trigger),
      `checkin_transactions`.
- [x] RLS policies per design: member read; admin membership writes; NO direct writes to
      books/versions; realtime channel authorization.
- [x] `tc.jwt_email_verified()` SQL helper: the ONE place that reads verification off the
      token — Firebase-style `email_verified` claim OR local-GoTrue auto-confirmed users
      (dev stack, task 11). Everything else calls the helper, never the claim directly.
- [x] RPCs from CONTRACTS.md: create_collection, my_collections, claim_memberships
      (requires `tc.jwt_email_verified()`), get_collection_state, get_changes, checkout_book (conditional
      UPDATE), unlock_book, force_unlock, delete_book (lock required), undelete_book,
      rename_check, member management (remove ⇒ force-unlock + events), add_palette_colors,
      log_event.
- [x] Events: `BookHistoryEventType` numeric parity + incident types (WorkPreservedLocally…).

## Acceptance — MET
- pgTAP suite **GREEN: 42/42 (6 Jul 2026, `supabase test db` on local stack via Podman)**.
  Covers: RLS matrix; checkout concurrency (two racing calls, exactly one winner); claiming requires
  verified email; last-admin guard; get_changes cursor; tombstone/undelete; live-name uniqueness
  (tombstoned names reusable).
  Orchestrator fixes to get green: plan count corrected (60 → 42 actual assertions); RLS
  assertions now run under `SET LOCAL ROLE authenticated` — the suite's postgres superuser
  BYPASSES row security, so unswitched RLS tests pass vacuously (keep this pattern for all
  future RLS assertions). Migrations + seed also verified live: RPC round-trip
  (create_collection → my_collections) via PostgREST with Content-Profile: tc succeeded.

**Agent notes**: Sonnet. All timestamps `now()` server-side. User ids are text, not uuid —
Firebase UIDs are ~28 chars (local-GoTrue dev users are uuids; text covers both).
NFC-normalize name/path comparisons.
