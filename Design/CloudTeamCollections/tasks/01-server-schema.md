# 01 — Server schema + RPCs (Wave 1)

**Goal**: the `tc` Postgres schema, RLS, and all pure-DB RPCs, per CONTRACTS.md.

**Dependencies**: CONTRACTS.md frozen. Parallel-safe (owns `supabase/migrations/**`,
`supabase/tests/**` only).

## Steps
- [ ] `supabase init` layout at repo root (`supabase/config.toml`), local dev via `supabase start`.
- [ ] Migrations: `collections`, `members` (approved accounts; unique claimed user per
      collection; last-admin guard trigger), `books` (lock columns; `deleted_at`; unique
      (collection, instance_id); unique live lower(name)), `versions` (metadata),
      `version_files` (current manifest incl. s3_version_id), `collection_file_groups` +
      `collection_group_files`, `color_palette_entries`, `events` (+ indexes, realtime trigger),
      `checkin_transactions`.
- [ ] RLS policies per design: member read; admin membership writes; NO direct writes to
      books/versions; realtime channel authorization.
- [ ] RPCs from CONTRACTS.md: create_collection, my_collections, claim_memberships
      (requires `email_verified`), get_collection_state, get_changes, checkout_book (conditional
      UPDATE), unlock_book, force_unlock, delete_book (lock required), undelete_book,
      rename_check, member management (remove ⇒ force-unlock + events), add_palette_colors,
      log_event.
- [ ] Events: `BookHistoryEventType` numeric parity + incident types (WorkPreservedLocally…).

## Acceptance
- pgTAP suite green under `supabase start`: RLS matrix; checkout concurrency (two racing calls,
  exactly one winner); claiming requires verified email; last-admin guard; get_changes cursor;
  tombstone/undelete; live-name uniqueness (tombstoned names reusable).

**Agent notes**: Sonnet. All timestamps `now()` server-side. Firebase UID is text (~28 chars),
not uuid. NFC-normalize name/path comparisons.
