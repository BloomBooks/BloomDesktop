-- =============================================================================
-- Migration: durable member display names (CONTRACTS.md v1.6, additive)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Dogfood batch 1, John's 13 Jul 2026 request: "checked out to X" (and similar) should
-- show a human-readable name with the email as a fallback, and admins should be able to
-- set that name for each team member from the Sharing panel.
--
-- Until now the only display-name source was tc.events.by_user_name — the JWT `name`
-- claim captured per action (see 20260707000006), which is normally NULL in dev-auth
-- mode and never editable. This migration adds the durable, editable source:
--
--   • tc.members.display_name (nullable text) — the canonical per-collection name.
--   • tc.resolve_member_display now prefers it, keeping the JWT-claim capture as a
--     fallback — so get_collection_state / get_changes / get_book_manifest (all of
--     which already report locked_by_name via this helper, unchanged signatures)
--     pick the new source up with no further changes.
--   • tc.members_list now returns display_name so the Sharing panel can show/edit it.
--   • tc.members_set_display_name — admin may set anyone's name; a claimed member may
--     set their own. Blank/whitespace clears back to NULL (= fall back to email).

-- ---------------------------------------------------------------------------
-- Column
-- ---------------------------------------------------------------------------
ALTER TABLE tc.members ADD COLUMN display_name text;

COMMENT ON COLUMN tc.members.display_name IS
    'Human-readable name shown in place of the email wherever the member is displayed '
    '(checkout status, history, sharing panel). NULL = none set; display falls back to '
    'email. Set via tc.members_set_display_name (admin, or the claimed member themselves).';

-- ---------------------------------------------------------------------------
-- resolve_member_display: prefer the durable column, keep the JWT-claim fallback.
-- Same signature as 20260707000006, so the current get_collection_state / get_changes /
-- get_book_manifest bodies (latest: 20260711000003) need no re-creation.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.resolve_member_display(
    p_collection_id uuid,
    p_user_id       text,
    OUT email       text,
    OUT display_name text
)
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
    SELECT
        m.email,
        COALESCE(
            m.display_name,
            (
                SELECT e.by_user_name
                FROM tc.events e
                WHERE e.collection_id = p_collection_id
                  AND e.by_user_id     = p_user_id
                  AND e.by_user_name IS NOT NULL
                ORDER BY e.id DESC
                LIMIT 1
            )
        )
    FROM tc.members m
    WHERE m.collection_id = p_collection_id
      AND m.user_id        = p_user_id
    LIMIT 1;
$$;

COMMENT ON FUNCTION tc.resolve_member_display(uuid, text) IS
    'Best-effort resolution of a locked_by/created_by user_id to a display email '
    '(from tc.members, authoritative) and display name. v1.6 (20260713000001): prefers '
    'the durable tc.members.display_name; falls back to the most recent '
    'tc.events.by_user_name JWT-claim capture (often NULL in dev-auth mode). Returns an '
    'all-NULL row (never an error) when p_user_id is NULL or unknown.';

-- ---------------------------------------------------------------------------
-- members_list: add display_name. Return type changes, so DROP + CREATE (the
-- 20260711000003 checkout_book precedent) and re-GRANT.
-- ---------------------------------------------------------------------------
DROP FUNCTION tc.members_list(uuid);

CREATE FUNCTION tc.members_list(
    p_collection_id uuid
)
RETURNS TABLE (
    id            bigint,
    email         text,
    display_name  text,
    role          tc.member_role,
    user_id       text,
    added_by      text,
    added_at      timestamptz,
    claimed_at    timestamptz
)
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
    SELECT m.id, m.email, m.display_name, m.role, m.user_id, m.added_by, m.added_at,
           m.claimed_at
    FROM tc.members m
    WHERE m.collection_id = p_collection_id
      AND tc.is_member(p_collection_id)   -- membership gate
    ORDER BY m.email
$$;

COMMENT ON FUNCTION tc.members_list(uuid) IS
    'CONTRACTS.md: members list — returns approved-accounts for the collection. '
    'Any member may call this. v1.6 (20260713000001): rows also carry display_name.';

GRANT EXECUTE ON FUNCTION tc.members_list(uuid) TO authenticated;

-- ---------------------------------------------------------------------------
-- members_set_display_name(collection_id uuid, member_id bigint, display_name text)
-- ---------------------------------------------------------------------------
-- Admin may set any member's name; a claimed member may set their own (the Sharing
-- panel is admin-edit-only for now, but the permission model anticipates a
-- set-my-own-name UI). Blank/whitespace input clears the name back to NULL.
CREATE FUNCTION tc.members_set_display_name(
    p_collection_id uuid,
    p_member_id     bigint,
    p_display_name  text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_target_user_id text;
    v_name           text;
BEGIN
    -- Membership gate first, so non-members learn nothing about member row ids.
    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    SELECT user_id INTO v_target_user_id
    FROM tc.members
    WHERE id = p_member_id AND collection_id = p_collection_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'member_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_admin(p_collection_id)
       AND (v_target_user_id IS NULL OR v_target_user_id <> tc.current_user_id()) THEN
        RAISE EXCEPTION 'admin_required' USING ERRCODE = '42501';
    END IF;

    v_name := NULLIF(btrim(p_display_name), '');
    IF char_length(v_name) > 100 THEN
        RAISE EXCEPTION 'display_name_too_long' USING ERRCODE = '22001';
    END IF;

    UPDATE tc.members
    SET    display_name = v_name
    WHERE  id            = p_member_id
      AND  collection_id = p_collection_id;
END;
$$;

COMMENT ON FUNCTION tc.members_set_display_name(uuid, bigint, text) IS
    'CONTRACTS.md v1.6: members set_display_name — admin may set any member''s display '
    'name; a claimed member may set their own. Trims; blank clears to NULL (display '
    'falls back to email). Max 100 chars.';

GRANT EXECUTE ON FUNCTION tc.members_set_display_name(uuid, bigint, text) TO authenticated;

-- ---------------------------------------------------------------------------
-- get_changes: event rows gain by_display_name (additive) so history shows the
-- CURRENT durable display name, not just the JWT-claim capture frozen into
-- by_user_name at event time. Full recreation of the 20260711000003 version with
-- only that addition (same signature, so CREATE OR REPLACE).
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.get_changes(
    p_collection_id  uuid,
    p_since_event_id bigint
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
AS $$
DECLARE
    v_events jsonb;
    v_books  jsonb;
BEGIN
    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    -- Events since cursor
    SELECT jsonb_agg(row_to_json(e)::jsonb ORDER BY e.id)
    INTO v_events
    FROM (
        SELECT
            e.id,
            e.book_id,
            e.type,
            e.by_user_id,
            e.by_user_name,
            e.by_email,
            erd.display_name AS by_display_name,
            e.book_version_seq,
            e.lock_info,
            e.book_name,
            e.group_key,
            e.message,
            e.bloom_version,
            e.occurred_at
        FROM tc.events e
        LEFT JOIN LATERAL tc.resolve_member_display(e.collection_id, e.by_user_id) erd
            ON true
        WHERE e.collection_id = p_collection_id
          AND e.id             > p_since_event_id
        ORDER BY e.id
    ) e;

    -- Touched book rows (distinct books referenced in those events)
    SELECT jsonb_agg(row_to_json(b)::jsonb)
    INTO v_books
    FROM (
        SELECT DISTINCT ON (b.id)
            b.id,
            b.instance_id,
            b.name,
            b.current_version_id,
            b.current_version_seq,
            b.current_checksum,
            b.locked_by,
            b.locked_by_machine,
            b.locked_seat,
            b.locked_at,
            b.deleted_at,
            rd.email        AS locked_by_email,
            rd.display_name AS locked_by_name
        FROM tc.books b
        JOIN tc.events e ON e.book_id = b.id
        LEFT JOIN LATERAL tc.resolve_member_display(b.collection_id, b.locked_by) rd
            ON true
        WHERE e.collection_id = p_collection_id
          AND e.id             > p_since_event_id
        ORDER BY b.id
    ) b;

    RETURN jsonb_build_object(
        'events',        COALESCE(v_events, '[]'::jsonb),
        'books',         COALESCE(v_books,  '[]'::jsonb),
        'max_event_id',  (
            SELECT max(id) FROM tc.events
            WHERE collection_id = p_collection_id
              AND id > p_since_event_id
        )
    );
END;
$$;

COMMENT ON FUNCTION tc.get_changes(uuid, bigint) IS
    'CONTRACTS.md: get_changes — events + touched book rows since the cursor. '
    'Used for polling (60s fallback) and realtime reconnect catch-up. v1.2 (20260707000006): '
    'touched book rows also carry locked_by_email/locked_by_name for display. v1.5 '
    '(20260711000003): touched book rows also carry locked_seat. v1.6 (20260713000001): '
    'event rows also carry by_display_name (the current durable display name of by_user_id).';
