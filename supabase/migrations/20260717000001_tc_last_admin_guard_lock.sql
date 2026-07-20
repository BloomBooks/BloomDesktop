-- =============================================================================
-- Migration: serialize the last-admin guard against concurrent admin removals
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- tc.members_last_admin_guard (20260706000001) blocks removing or demoting a
-- collection's last admin. The original counted remaining admins with a plain
-- SELECT and no lock, so it had a TOCTOU race: two concurrent transactions each
-- dropping a DIFFERENT admin would each still see the other (READ COMMITTED hides
-- the other's uncommitted change), both pass the count check, and both commit --
-- leaving the collection with ZERO admins.
--
-- This CREATE OR REPLACE adds a lock on the parent collection row before counting,
-- so admin removals/demotions on the same collection serialize: the second waits
-- for the first to commit, then its count sees the first's committed change and is
-- correctly rejected. Behavior is otherwise unchanged (same P0001 error) -- see
-- 01_tc_schema_test.sql 6a/6b (rejection) and new 11a (lock present).
-- =============================================================================

CREATE OR REPLACE FUNCTION tc.members_last_admin_guard()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    admin_count     integer;
    v_collection_id uuid    := COALESCE(OLD.collection_id, NEW.collection_id);
    -- Only removing or demoting an admin can reduce the admin count. Non-admin
    -- deletes, promotions, and unrelated column updates cannot orphan a collection.
    v_drops_admin   boolean :=
        (TG_OP = 'DELETE' AND OLD.role = 'admin')
        OR (TG_OP = 'UPDATE' AND OLD.role = 'admin' AND NEW.role = 'member');
BEGIN
    IF NOT v_drops_admin THEN
        RETURN COALESCE(NEW, OLD);
    END IF;

    -- Serialize concurrent admin drops on this collection (see migration header):
    -- lock the parent collection row so two transactions cannot each see the other's
    -- soon-to-be-gone admin and both slip through to zero admins.
    PERFORM 1 FROM tc.collections WHERE id = v_collection_id FOR UPDATE;

    SELECT count(*) INTO admin_count
    FROM tc.members
    WHERE collection_id = v_collection_id
      AND role = 'admin'
      AND id <> OLD.id;

    IF admin_count = 0 THEN
        RAISE EXCEPTION 'last_admin_guard: cannot % the last admin of collection %',
            CASE WHEN TG_OP = 'DELETE' THEN 'remove' ELSE 'demote' END, v_collection_id
            USING ERRCODE = 'P0001';
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$;

COMMENT ON FUNCTION tc.members_last_admin_guard() IS
    'Trigger function: prevents deleting or demoting the last admin of a collection. Locks the '
    'parent collection row (FOR UPDATE) before counting so concurrent admin removals/demotions '
    'serialize instead of racing to zero admins (fixed 20260717000001).';
