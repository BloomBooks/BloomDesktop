#!/usr/bin/env bash
# Regenerate the single initial migration for the `tc` cloud Team Collections
# schema by concatenating the declarative schema files (supabase/schemas/*.sql)
# in dependency order.
#
# WHY concatenation and not `supabase db diff`:
#   The Supabase diff tools (pg-schema-diff and migra) silently DROP every
#   `COMMENT ON` and every `GRANT EXECUTE ON FUNCTION` when generating a
#   migration from the declarative schema. That would leave the RPCs uncallable
#   by `authenticated` and the schema undocumented. Concatenation is lossless
#   and produces a migration byte-for-byte faithful to the declarative source.
#
# This is safe to run only while the schema has a SINGLE initial migration (the
# pre-launch state: local-only database, history intentionally discarded). Once
# there is a remote database whose history must be preserved, stop regenerating
# the init and instead add forward-only delta migrations (see CONTRACTS.md
# "Cloud functions" → declarative workflow).
#
# SAFETY: at go-live we commit a freeze marker (see GOING-LIVE.md Phase 2.2). Once
# that marker exists this script refuses to run — regenerating would rewrite
# already-applied history on the live database. The refusal can be deliberately
# overridden (ALLOW_INIT_REGEN=1) for the "testing went so wrong we want to wipe and
# start over" case the file is deliberately kept around for.
set -euo pipefail

cd "$(dirname "$0")/.."   # repo root
SCHEMA_DIR="supabase/schemas"
MIG_DIR="supabase/migrations"
INIT="$MIG_DIR/20260720000001_init_tc_schema.sql"
FREEZE_MARKER="supabase/.init-migration-frozen"

# Fail closed once a real database exists (marker committed at go-live).
if [ -f "$FREEZE_MARKER" ] && [ "${ALLOW_INIT_REGEN:-}" != "1" ]; then
  {
    echo "ERROR: $FREEZE_MARKER exists — a live/shared database has been deployed."
    echo "Regenerating the initial migration would rewrite already-applied history and DESTROY data."
    echo "Make schema changes as forward-only DELTA migrations instead"
    echo "  (see CONTRACTS.md → \"Database: declarative schema\")."
    echo
    echo "If you truly intend to wipe and start over (e.g. a destructive reset during early"
    echo "testing) and understand this discards all data and history, re-run with:"
    echo "    ALLOW_INIT_REGEN=1 $0"
  } >&2
  exit 1
fi

# Applied in this order; must match [db.migrations].schema_paths in config.toml.
FILES=(
  "$SCHEMA_DIR/01_schema.sql"
  "$SCHEMA_DIR/02_functions.sql"
  "$SCHEMA_DIR/03_tables.sql"
  "$SCHEMA_DIR/04_security.sql"
)

for f in "${FILES[@]}"; do
  [ -f "$f" ] || { echo "missing schema file: $f" >&2; exit 1; }
done

{
  cat <<'HDR'
-- ============================================================================
-- GENERATED FILE — do not hand-edit.
-- The `tc` cloud Team Collections schema is maintained declaratively in
-- supabase/schemas/*.sql (see [db.migrations].schema_paths in config.toml).
-- This migration is their concatenation, in dependency order, and is what
-- `supabase db reset`/`db push` actually run.
--
-- We concatenate rather than use `supabase db diff` to build this initial
-- migration because the diff tools (pg-schema-diff and migra) silently drop
-- COMMENT ON statements and every GRANT EXECUTE ON FUNCTION — which would
-- leave the RPCs uncallable and the schema undocumented. Concatenation is
-- lossless. Regenerate with: build/regen-init-migration.sh
-- ============================================================================
HDR
  for f in "${FILES[@]}"; do
    printf '\n\n-- ==== %s ====\n\n' "$(basename "$f")"
    cat "$f"
  done
} > "$INIT"

echo "Regenerated $INIT"
echo "  functions:      $(grep -ciE 'create (or replace )?function' "$INIT")"
echo "  function grants: $(grep -ciE 'grant .*on function' "$INIT")"
echo "  comments:       $(grep -ciE 'comment on' "$INIT")"
echo "Now run 'supabase db reset' to verify it applies cleanly."
