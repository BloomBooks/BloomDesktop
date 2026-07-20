-- =============================================================================
-- Migration: support_set_admin — admin-recovery tool (service-role only)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Recovery path for the rare case where a collection's ONLY admin becomes
-- unavailable (left the org, lost their login, etc.). Normal admin management
-- (members_add / members_set_role) requires an existing admin caller, so a
-- collection with no reachable admin cannot be repaired from inside the app.
--
-- This function lets the Bloom team, using the project's SERVICE-ROLE key, grant
-- admin on a collection to a chosen email out-of-band. It is deliberately NOT
-- granted to `authenticated` (a normal signed-in user must never call it), and it
-- intentionally does NOT check is_admin -- the whole point is that there is no admin
-- left to authorize it. Idempotent: promotes an existing member to admin, or inserts
-- a new admin approval (unclaimed until that person signs in with the email and
-- claim_memberships fills their user_id). The last-admin guard only blocks removals
-- and demotions, so a promotion is never blocked.
--
-- See Design/CloudTeamCollections/GOING-LIVE.md — "Admin recovery" runbook — for how
-- to invoke it (service-role RPC call, or equivalent SQL in the Supabase SQL editor).
-- =============================================================================

CREATE OR REPLACE FUNCTION tc.support_set_admin(
    p_collection_id uuid,
    p_email         text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_email text := lower(normalize(p_email, NFC));   -- match members_add's normalization
BEGIN
    IF v_email IS NULL OR v_email = '' THEN
        RAISE EXCEPTION 'support_set_admin: email required' USING ERRCODE = '22023';
    END IF;

    UPDATE tc.members
    SET    role = 'admin'
    WHERE  collection_id = p_collection_id
      AND  email = v_email;

    IF NOT FOUND THEN
        INSERT INTO tc.members (collection_id, email, role, added_by)
        VALUES (p_collection_id, v_email, 'admin', 'support');
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.support_set_admin(uuid, text) IS
    'Admin-recovery tool: grants admin on a collection to an email, for the Bloom team to run '
    'with the SERVICE-ROLE key when a collection has lost its only reachable admin. NOT granted '
    'to authenticated; bypasses is_admin by design. Idempotent (promote existing member / insert '
    'new admin approval). See GOING-LIVE.md "Admin recovery" runbook.';

-- Lock it down: PostgreSQL grants EXECUTE to PUBLIC on new functions by default, so REVOKE that
-- and grant only to the service role. A normal signed-in (`authenticated`) caller must not be
-- able to invoke this (verified by 01_tc_schema_test.sql 11d).
REVOKE ALL ON FUNCTION tc.support_set_admin(uuid, text) FROM public;
GRANT EXECUTE ON FUNCTION tc.support_set_admin(uuid, text) TO service_role;
