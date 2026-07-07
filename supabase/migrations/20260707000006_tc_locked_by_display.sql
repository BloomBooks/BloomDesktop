-- =============================================================================
-- Migration: lockedByEmail / lockedByName display fields (CONTRACTS.md v1.2, additive)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Task 05's live-testing discovery: tc.books.locked_by stores the raw auth user_id
-- (JWT `sub`), not an email or display name -- useless for a "checked out to Sara"
-- UI label. The client-side workaround (CloudTeamCollection.ResolveLockedByForDisplay)
-- can only resolve the CALLER's OWN id back to their OWN email; a teammate's lock
-- still shows as a bare id. This migration adds a server-side resolution so every
-- member sees every OTHER member's lock with a friendly identity too.
--
-- Email comes from tc.members (the approved-accounts table already keyed by
-- (collection_id, user_id) once claimed) -- authoritative and always present for
-- anyone who could possibly hold a lock (checkout_book requires membership).
-- A display *name* has no dedicated column anywhere server-side (tc.members has
-- none; the dev auth provider never sets one either) -- the best available source
-- is tc.events.by_user_name, which captures the JWT `name` claim at the moment of
-- each of that user's actions in this collection (see 20260706000003_tc_rpcs.sql's
-- `(auth.jwt() ->> 'name')` calls). We take the most recent non-null one. In dev-auth
-- mode this will normally be NULL (GoTrue sign-up carries no name claim), which is a
-- known, acceptable gap until real auth (Option A/B/C) supplies one -- exactly the
-- same "may be null, treat as no worse than today" contract as the client's own
-- lockedByFirstName/lockedBySurname fields.
--
-- tc.get_book_manifest also gains lockedBy/lockedByEmail/lockedByName (previously it
-- returned no lock information at all) so the Receive path can show "still checked
-- out to X" without a second round trip.

-- ---------------------------------------------------------------------------
-- Helper: resolve a user_id's best-known email/display name within one collection.
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
        (
            SELECT e.by_user_name
            FROM tc.events e
            WHERE e.collection_id = p_collection_id
              AND e.by_user_id     = p_user_id
              AND e.by_user_name IS NOT NULL
            ORDER BY e.id DESC
            LIMIT 1
        )
    FROM tc.members m
    WHERE m.collection_id = p_collection_id
      AND m.user_id        = p_user_id
    LIMIT 1;
$$;

COMMENT ON FUNCTION tc.resolve_member_display(uuid, text) IS
    'Best-effort resolution of a locked_by/created_by user_id to a display email '
    '(from tc.members, authoritative) and display name (from the most recent '
    'tc.events.by_user_name for that user in this collection, often NULL in dev-auth '
    'mode). Returns an all-NULL row (never an error) when p_user_id is NULL or unknown.';

GRANT EXECUTE ON FUNCTION tc.resolve_member_display(uuid, text) TO authenticated;

-- ---------------------------------------------------------------------------
-- get_collection_state: add locked_by_email / locked_by_name to each book row.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.get_collection_state(
    p_collection_id  uuid,
    p_since_event_id bigint DEFAULT NULL
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
AS $$
DECLARE
    v_max_event_id bigint;
    v_books        jsonb;
    v_groups       jsonb;
BEGIN
    -- Verify membership
    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    -- Max event id for the cursor
    SELECT max(id) INTO v_max_event_id
    FROM tc.events
    WHERE collection_id = p_collection_id;

    -- Books: full or delta
    IF p_since_event_id IS NULL THEN
        -- Full snapshot: all live books.
        -- (task 02 fix, live-integration spike 6 Jul 2026): a book with
        -- current_version_id IS NULL has never had a first checkin-finish — per
        -- CONTRACTS.md's checkin-start spec it is "invisible to teammates until
        -- first commit". The delta branch below gets this for free (a never-
        -- committed book has no events yet to join against), but this full-snapshot
        -- branch queried tc.books directly and leaked it to every member. Exclude
        -- it unless the caller is the one who has it locked (i.e. is themselves
        -- mid-Send) — they should still see their own in-flight new book.
        SELECT jsonb_agg(row_to_json(b)::jsonb)
        INTO v_books
        FROM (
            SELECT
                b.id,
                b.instance_id,
                b.name,
                b.current_version_id,
                b.current_version_seq,
                b.current_checksum,
                b.locked_by,
                b.locked_by_machine,
                b.locked_at,
                b.deleted_at,
                b.created_at,
                b.created_by,
                rd.email        AS locked_by_email,
                rd.display_name AS locked_by_name
            FROM tc.books b
            LEFT JOIN LATERAL tc.resolve_member_display(b.collection_id, b.locked_by) rd
                ON true
            WHERE b.collection_id = p_collection_id
              AND (b.current_version_id IS NOT NULL OR b.locked_by = tc.current_user_id())
            ORDER BY lower(b.name)
        ) b;
    ELSE
        -- Delta: only books that have an event since since_event_id
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
                b.locked_at,
                b.deleted_at,
                b.created_at,
                b.created_by,
                rd.email        AS locked_by_email,
                rd.display_name AS locked_by_name
            FROM tc.books b
            JOIN tc.events e ON e.book_id = b.id
            LEFT JOIN LATERAL tc.resolve_member_display(b.collection_id, b.locked_by) rd
                ON true
            WHERE b.collection_id = p_collection_id
              AND e.id             > p_since_event_id
            ORDER BY b.id
        ) b;
    END IF;

    -- Collection file group versions
    SELECT jsonb_agg(row_to_json(g)::jsonb)
    INTO v_groups
    FROM (
        SELECT group_key, version, updated_at
        FROM tc.collection_file_groups
        WHERE collection_id = p_collection_id
        ORDER BY group_key
    ) g;

    RETURN jsonb_build_object(
        'books',        COALESCE(v_books,  '[]'::jsonb),
        'groups',       COALESCE(v_groups, '[]'::jsonb),
        'max_event_id', v_max_event_id
    );
END;
$$;

COMMENT ON FUNCTION tc.get_collection_state(uuid, bigint) IS
    'CONTRACTS.md: get_collection_state — full/delta snapshot of book rows + group versions + '
    'max_event_id. since_event_id = NULL → full; otherwise delta. v1.2 (20260707000006): book '
    'rows also carry locked_by_email/locked_by_name for display.';

-- ---------------------------------------------------------------------------
-- get_changes: add locked_by_email / locked_by_name to each touched book row.
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
            e.book_version_seq,
            e.lock_info,
            e.book_name,
            e.group_key,
            e.message,
            e.bloom_version,
            e.occurred_at
        FROM tc.events e
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
    'touched book rows also carry locked_by_email/locked_by_name for display.';

-- ---------------------------------------------------------------------------
-- get_book_manifest: add lockedBy / lockedByEmail / lockedByName.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.get_book_manifest(
    p_book_id uuid
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
AS $$
DECLARE
    v_row   tc.books%ROWTYPE;
    v_files jsonb;
    v_email text;
    v_name  text;
BEGIN
    SELECT * INTO v_row FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_member(v_row.collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    -- Never-committed books are invisible to everyone except their mid-Send owner
    -- (same rule as get_collection_state's full snapshot).
    IF v_row.current_version_id IS NULL
       AND v_row.locked_by IS DISTINCT FROM tc.current_user_id() THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'path',        vf.path,
                'sha256',      vf.sha256,
                'size',        vf.size_bytes,
                's3VersionId', vf.s3_version_id
            )
            ORDER BY vf.path
        ),
        '[]'::jsonb
    )
    INTO v_files
    FROM tc.version_files vf
    WHERE vf.book_id = p_book_id;

    SELECT rd.email, rd.display_name
    INTO v_email, v_name
    FROM tc.resolve_member_display(v_row.collection_id, v_row.locked_by) rd;

    RETURN jsonb_build_object(
        'bookId',        v_row.id,
        'versionId',     v_row.current_version_id,
        'seq',           v_row.current_version_seq,
        'checksum',      v_row.current_checksum,
        'files',         v_files,
        'lockedBy',      v_row.locked_by,
        'lockedByEmail', v_email,
        'lockedByName',  v_name
    );
END;
$$;

COMMENT ON FUNCTION tc.get_book_manifest(uuid) IS
    'CONTRACTS.md v1.2: get_book_manifest — per-file current manifest for one book '
    '(path, sha256, size, s3VersionId), used by Receive to download pinned versions. '
    'Enforces the never-committed-book invisibility rule. v1.2 (20260707000006): also '
    'reports lockedBy/lockedByEmail/lockedByName so Receive can show "still checked out '
    'to X" without a second round trip.';

GRANT EXECUTE ON FUNCTION tc.get_book_manifest(uuid) TO authenticated;
