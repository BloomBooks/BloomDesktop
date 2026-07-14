-- =============================================================================
-- Migration: Row-Level Security policies for tc schema
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- RLS principles:
--   • Members can read their own collections and related data.
--   • Admins can manage membership, force-unlock, and approve accounts.
--   • NO direct INSERT/UPDATE/DELETE on books or versions via PostgREST —
--     all state transitions go through the RPCs defined in 20260706000003_tc_rpcs.sql.
--   • The realtime channel is private: only members of a collection subscribe.
--   • Service-role (used by edge functions) bypasses RLS; all RPCs run as SECURITY DEFINER
--     to get elevated access where needed.
-- =============================================================================

-- Enable RLS on every table in the tc schema.
ALTER TABLE tc.collections           ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.members               ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.books                 ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.versions              ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.version_files         ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.collection_file_groups ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.collection_group_files ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.color_palette_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.events                ENABLE ROW LEVEL SECURITY;
ALTER TABLE tc.checkin_transactions  ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------
-- Helper: is the caller a member of a given collection?
-- ---------------------------------------------------------------------------
-- Used by multiple policies; defined as a stable SQL function so Postgres can
-- inline it into RLS policy checks without repeated subquery overhead.
CREATE OR REPLACE FUNCTION tc.is_member(p_collection_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM tc.members m
        WHERE m.collection_id = p_collection_id
          AND m.user_id        = tc.current_user_id()
    )
$$;

COMMENT ON FUNCTION tc.is_member(uuid) IS
    'Returns TRUE when the caller (JWT sub) is a claimed member of the given collection.';

-- ---------------------------------------------------------------------------
-- Helper: is the caller an admin of a given collection?
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.is_admin(p_collection_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM tc.members m
        WHERE m.collection_id = p_collection_id
          AND m.user_id        = tc.current_user_id()
          AND m.role           = 'admin'
    )
$$;

COMMENT ON FUNCTION tc.is_admin(uuid) IS
    'Returns TRUE when the caller (JWT sub) is a claimed admin of the given collection.';

-- =============================================================================
-- COLLECTIONS
-- =============================================================================

-- Members can read their own collections.
CREATE POLICY collections_select ON tc.collections
    FOR SELECT
    USING (tc.is_member(id));

-- No direct INSERT/UPDATE/DELETE via PostgREST: create_collection RPC handles creation.
-- (No INSERT/UPDATE/DELETE policies → those operations are blocked for non-service-role.)

-- =============================================================================
-- MEMBERS
-- =============================================================================

-- Members can read the member list for their collections (so they know who else is in it).
CREATE POLICY members_select ON tc.members
    FOR SELECT
    USING (tc.is_member(collection_id));

-- Admins can insert new member rows (add_member RPC runs SECURITY DEFINER, but this
-- policy is defence-in-depth in case someone calls the table directly).
CREATE POLICY members_insert ON tc.members
    FOR INSERT
    WITH CHECK (tc.is_admin(collection_id));

-- Admins can update roles; the claim_memberships RPC writes user_id (SECURITY DEFINER).
CREATE POLICY members_update ON tc.members
    FOR UPDATE
    USING (tc.is_admin(collection_id))
    WITH CHECK (tc.is_admin(collection_id));

-- Admins can remove member rows.
CREATE POLICY members_delete ON tc.members
    FOR DELETE
    USING (tc.is_admin(collection_id));

-- =============================================================================
-- BOOKS
-- =============================================================================
-- NO direct writes (INSERT/UPDATE/DELETE) allowed via PostgREST.
-- All transitions go through SECURITY DEFINER RPCs.

-- Members can read books in their collections (including tombstoned ones — needed
-- for get_collection_state delta and history display).
CREATE POLICY books_select ON tc.books
    FOR SELECT
    USING (tc.is_member(collection_id));

-- =============================================================================
-- VERSIONS
-- =============================================================================
-- Read-only for members; written only by checkin-finish edge function (service-role).

CREATE POLICY versions_select ON tc.versions
    FOR SELECT
    USING (tc.is_member(collection_id));

-- =============================================================================
-- VERSION_FILES
-- =============================================================================
-- Read-only for members (for manifest inspection); written only by checkin-finish.

CREATE POLICY version_files_select ON tc.version_files
    FOR SELECT
    USING (
        EXISTS (
            SELECT 1 FROM tc.books b
            WHERE b.id = version_files.book_id
              AND tc.is_member(b.collection_id)
        )
    );

-- =============================================================================
-- COLLECTION_FILE_GROUPS
-- =============================================================================

CREATE POLICY collection_file_groups_select ON tc.collection_file_groups
    FOR SELECT
    USING (tc.is_member(collection_id));

-- =============================================================================
-- COLLECTION_GROUP_FILES
-- =============================================================================

CREATE POLICY collection_group_files_select ON tc.collection_group_files
    FOR SELECT
    USING (
        EXISTS (
            SELECT 1 FROM tc.collection_file_groups fg
            WHERE fg.id = collection_group_files.group_id
              AND tc.is_member(fg.collection_id)
        )
    );

-- =============================================================================
-- COLOR_PALETTE_ENTRIES
-- =============================================================================

-- Members can read palette entries.
CREATE POLICY color_palette_entries_select ON tc.color_palette_entries
    FOR SELECT
    USING (tc.is_member(collection_id));

-- add_palette_colors RPC inserts via SECURITY DEFINER; direct insert requires membership.
-- (The RPC is the preferred path; this is defence-in-depth.)
CREATE POLICY color_palette_entries_insert ON tc.color_palette_entries
    FOR INSERT
    WITH CHECK (tc.is_member(collection_id));

-- =============================================================================
-- EVENTS
-- =============================================================================

-- Members can read events for their collections.
CREATE POLICY events_select ON tc.events
    FOR SELECT
    USING (tc.is_member(collection_id));

-- Members can insert their own events via log_event RPC (SECURITY DEFINER).
-- Defence-in-depth: direct INSERT requires membership.
CREATE POLICY events_insert ON tc.events
    FOR INSERT
    WITH CHECK (
        tc.is_member(collection_id)
        AND by_user_id = tc.current_user_id()
    );

-- No UPDATE or DELETE on events (immutable audit log).

-- =============================================================================
-- CHECKIN_TRANSACTIONS
-- =============================================================================

-- Users can read their own open transactions.
CREATE POLICY checkin_transactions_select ON tc.checkin_transactions
    FOR SELECT
    USING (started_by = tc.current_user_id());

-- Insertions are done by the checkin-start edge function (service-role / SECURITY DEFINER).
-- No direct write access via PostgREST.

-- =============================================================================
-- Grant usage on schema and SELECT on all tables to authenticated role
-- =============================================================================
-- PostgREST uses the `authenticated` role for JWT-carrying requests.
GRANT USAGE ON SCHEMA tc TO authenticated;
GRANT SELECT ON ALL TABLES IN SCHEMA tc TO authenticated;
GRANT INSERT ON tc.members               TO authenticated;
GRANT UPDATE ON tc.members               TO authenticated;
GRANT DELETE ON tc.members               TO authenticated;
GRANT INSERT ON tc.color_palette_entries TO authenticated;
GRANT INSERT ON tc.events                TO authenticated;

-- Sequences needed for GENERATED ALWAYS AS IDENTITY columns.
GRANT USAGE ON ALL SEQUENCES IN SCHEMA tc TO authenticated;

-- anon role gets no access (all endpoints require a JWT).
REVOKE ALL ON ALL TABLES IN SCHEMA tc FROM anon;
