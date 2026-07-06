-- =============================================================================
-- Migration: Postgres RPCs (PostgREST /rest/v1/rpc/...)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- All RPCs defined here are SECURITY DEFINER so they can bypass RLS where needed
-- (e.g. writing books/versions, reading members to check admin status).
-- They re-check membership / admin status internally before any mutation.
--
-- RPC signatures must match CONTRACTS.md exactly.
-- All timestamps server-side via now().
-- User ids are TEXT (Firebase UIDs ~28 chars; local-GoTrue UUIDs; both fit in TEXT).
-- =============================================================================

-- ---------------------------------------------------------------------------
-- create_collection(id uuid, name text)
-- ---------------------------------------------------------------------------
-- Creates a collection + the caller as the sole claimed admin.
-- Requires the caller to be authenticated (email_verified is NOT required for
-- collection creation — just a valid JWT).
CREATE OR REPLACE FUNCTION tc.create_collection(
    p_id    uuid,
    p_name  text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id text;
    v_email   text;
BEGIN
    v_user_id := tc.current_user_id();
    v_email   := tc.current_user_email();

    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'unauthenticated' USING ERRCODE = '28000';
    END IF;

    -- Insert collection
    INSERT INTO tc.collections (id, name, created_by)
    VALUES (p_id, normalize(p_name, NFC), v_user_id);

    -- Insert caller as sole claimed admin
    INSERT INTO tc.members (collection_id, email, role, user_id, added_by, claimed_at)
    VALUES (p_id, lower(v_email), 'admin', v_user_id, v_user_id, now());
END;
$$;

COMMENT ON FUNCTION tc.create_collection(uuid, text) IS
    'CONTRACTS.md: create_collection — creates collection + caller as sole claimed admin.';

-- ---------------------------------------------------------------------------
-- my_collections()
-- ---------------------------------------------------------------------------
-- Returns collections where the caller's email is in the approved list
-- (claimed or unclaimed — email match; used in the "Get my Team Collections" flow).
CREATE OR REPLACE FUNCTION tc.my_collections()
RETURNS TABLE (
    id          uuid,
    name        text,
    created_at  timestamptz,
    created_by  text,
    my_role     tc.member_role,
    is_claimed  boolean
)
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
    SELECT
        c.id,
        c.name,
        c.created_at,
        c.created_by,
        m.role      AS my_role,
        (m.user_id IS NOT NULL) AS is_claimed
    FROM tc.collections c
    JOIN tc.members m
        ON m.collection_id = c.id
       AND lower(m.email)  = tc.current_user_email()
    ORDER BY c.name
$$;

COMMENT ON FUNCTION tc.my_collections() IS
    'CONTRACTS.md: my_collections — returns collections where the caller''s email is approved '
    '(claimed or not).';

-- ---------------------------------------------------------------------------
-- claim_memberships()
-- ---------------------------------------------------------------------------
-- Fills user_id on rows matching the caller's verified email.
-- REQUIRES email_verified = true (tc.jwt_email_verified()).
CREATE OR REPLACE FUNCTION tc.claim_memberships()
RETURNS TABLE (
    collection_id  uuid,
    role           tc.member_role
)
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id text;
    v_email   text;
BEGIN
    IF NOT tc.jwt_email_verified() THEN
        RAISE EXCEPTION 'email_not_verified: claiming memberships requires a verified email'
            USING ERRCODE = '28000';
    END IF;

    v_user_id := tc.current_user_id();
    v_email   := tc.current_user_email();

    -- Fill user_id on unclaimed matching rows
    UPDATE tc.members m
    SET    user_id    = v_user_id,
           claimed_at = now()
    WHERE  lower(m.email) = v_email
      AND  m.user_id IS NULL;

    -- Return the now-claimed memberships
    RETURN QUERY
        SELECT m.collection_id, m.role
        FROM   tc.members m
        WHERE  m.user_id = v_user_id;
END;
$$;

COMMENT ON FUNCTION tc.claim_memberships() IS
    'CONTRACTS.md: claim_memberships — fills user_id on rows matching the caller''s verified '
    'email. Requires tc.jwt_email_verified().';

-- ---------------------------------------------------------------------------
-- get_collection_state(collection_id uuid, since_event_id bigint)
-- ---------------------------------------------------------------------------
-- Full or delta snapshot: book rows (locks, current version seq + checksum),
-- collection-file group versions, max_event_id.
-- If since_event_id IS NULL → full snapshot.
-- If since_event_id is provided → delta (only books touched since that event).
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
        -- Full snapshot: all live books
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
                b.created_by
            FROM tc.books b
            WHERE b.collection_id = p_collection_id
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
                b.created_by
            FROM tc.books b
            JOIN tc.events e ON e.book_id = b.id
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
    'max_event_id. since_event_id = NULL → full; otherwise delta.';

-- ---------------------------------------------------------------------------
-- get_changes(collection_id uuid, since_event_id bigint)
-- ---------------------------------------------------------------------------
-- Returns events + touched book rows since the cursor (for polling / reconnect catch-up).
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
            b.deleted_at
        FROM tc.books b
        JOIN tc.events e ON e.book_id = b.id
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
    'Used for polling (60s fallback) and realtime reconnect catch-up.';

-- ---------------------------------------------------------------------------
-- checkout_book(book_id uuid, machine text)
-- ---------------------------------------------------------------------------
-- Conditional lock: race-free UPDATE … WHERE locked_by IS NULL OR locked_by = me.
-- Returns the resulting lock status: {success, locked_by, locked_by_machine, locked_at}.
CREATE OR REPLACE FUNCTION tc.checkout_book(
    p_book_id  uuid,
    p_machine  text
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id     text;
    v_collection  uuid;
    v_updated     boolean;
    v_row         tc.books%ROWTYPE;
BEGIN
    v_user_id := tc.current_user_id();

    -- Get book + membership check
    SELECT b.collection_id INTO v_collection
    FROM tc.books b
    WHERE b.id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_member(v_collection) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    -- Race-free conditional UPDATE
    UPDATE tc.books
    SET    locked_by         = v_user_id,
           locked_by_machine = p_machine,
           locked_at         = now()
    WHERE  id = p_book_id
      AND  deleted_at IS NULL
      AND  (locked_by IS NULL OR locked_by = v_user_id);

    GET DIAGNOSTICS v_updated = ROW_COUNT;

    -- Fetch resulting row
    SELECT * INTO v_row FROM tc.books WHERE id = p_book_id;

    IF v_updated > 0 THEN
        -- Emit CheckOut event (type = 0)
        INSERT INTO tc.events (
            collection_id, book_id, type,
            by_user_id, by_user_name, by_email, book_name
        )
        SELECT
            v_row.collection_id, p_book_id, 0,
            v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
            v_row.name;

        RETURN jsonb_build_object(
            'success',           true,
            'locked_by',         v_user_id,
            'locked_by_machine', p_machine,
            'locked_at',         now()
        );
    ELSE
        -- Lock held by someone else
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_at',         v_row.locked_at
        );
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.checkout_book(uuid, text) IS
    'CONTRACTS.md: checkout_book — conditional lock (race-free UPDATE WHERE locked_by IS NULL '
    'OR locked_by = me). Returns {success, locked_by, locked_by_machine, locked_at}. '
    'Emits CheckOut event (type=0) on success.';

-- ---------------------------------------------------------------------------
-- unlock_book(book_id uuid)
-- ---------------------------------------------------------------------------
-- Release the caller's own lock (undo checkout, no content change, no event needed —
-- caller never committed content so no history entry required).
-- Only the lock holder can unlock; no admin override here (use force_unlock).
CREATE OR REPLACE FUNCTION tc.unlock_book(
    p_book_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id    text;
    v_collection uuid;
    v_locked_by  text;
BEGIN
    v_user_id := tc.current_user_id();

    SELECT b.collection_id, b.locked_by INTO v_collection, v_locked_by
    FROM tc.books b
    WHERE b.id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_member(v_collection) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    IF v_locked_by IS DISTINCT FROM v_user_id THEN
        RAISE EXCEPTION 'lock_not_held: book is not locked by you' USING ERRCODE = 'P0001';
    END IF;

    UPDATE tc.books
    SET    locked_by         = NULL,
           locked_by_machine = NULL,
           locked_at         = NULL
    WHERE  id = p_book_id;
END;
$$;

COMMENT ON FUNCTION tc.unlock_book(uuid) IS
    'CONTRACTS.md: unlock_book — release own lock (undo checkout, no content change). '
    'Only the lock holder may call this; use force_unlock for admin override.';

-- ---------------------------------------------------------------------------
-- force_unlock(book_id uuid)
-- ---------------------------------------------------------------------------
-- Admin-only; releases any lock; emits ForcedUnlock event (type=5).
CREATE OR REPLACE FUNCTION tc.force_unlock(
    p_book_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id    text;
    v_collection uuid;
    v_row        tc.books%ROWTYPE;
BEGIN
    v_user_id := tc.current_user_id();

    SELECT * INTO v_row FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_admin(v_row.collection_id) THEN
        RAISE EXCEPTION 'admin_required' USING ERRCODE = '42501';
    END IF;

    -- Snapshot the lock state for the audit event before clearing it
    INSERT INTO tc.events (
        collection_id, book_id, type,
        by_user_id, by_user_name, by_email,
        lock_info, book_name
    )
    VALUES (
        v_row.collection_id, p_book_id, 5, -- ForcedUnlock
        v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
        jsonb_build_object(
            'locked_by',  v_row.locked_by,
            'machine',    v_row.locked_by_machine,
            'locked_at',  v_row.locked_at
        ),
        v_row.name
    );

    UPDATE tc.books
    SET    locked_by         = NULL,
           locked_by_machine = NULL,
           locked_at         = NULL
    WHERE  id = p_book_id;
END;
$$;

COMMENT ON FUNCTION tc.force_unlock(uuid) IS
    'CONTRACTS.md: force_unlock — admin-only; releases any lock; emits ForcedUnlock (type=5).';

-- ---------------------------------------------------------------------------
-- delete_book(book_id uuid)
-- ---------------------------------------------------------------------------
-- Requires the caller holds the lock (editorial workflow: check out, then delete).
-- Sets deleted_at tombstone; emits Deleted event (type=8).
CREATE OR REPLACE FUNCTION tc.delete_book(
    p_book_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id text;
    v_row     tc.books%ROWTYPE;
BEGIN
    v_user_id := tc.current_user_id();

    SELECT * INTO v_row FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_member(v_row.collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    IF v_row.locked_by IS DISTINCT FROM v_user_id THEN
        RAISE EXCEPTION 'lock_required: caller must hold the lock to delete a book'
            USING ERRCODE = 'P0001';
    END IF;

    IF v_row.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION 'already_deleted' USING ERRCODE = 'P0001';
    END IF;

    UPDATE tc.books
    SET    deleted_at        = now(),
           locked_by         = NULL,
           locked_by_machine = NULL,
           locked_at         = NULL
    WHERE  id = p_book_id;

    INSERT INTO tc.events (
        collection_id, book_id, type,
        by_user_id, by_user_name, by_email, book_name
    )
    VALUES (
        v_row.collection_id, p_book_id, 8, -- Deleted
        v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
        v_row.name
    );
END;
$$;

COMMENT ON FUNCTION tc.delete_book(uuid) IS
    'CONTRACTS.md: delete_book — requires caller holds the lock; sets deleted_at tombstone; '
    'emits Deleted (type=8). Lock is released on deletion.';

-- ---------------------------------------------------------------------------
-- undelete_book(book_id uuid)
-- ---------------------------------------------------------------------------
-- Admin-only; clears tombstone.  Name-uniqueness is re-enforced: if another live book
-- already uses the same lower(NFC(name)), the call fails with a name_conflict error.
CREATE OR REPLACE FUNCTION tc.undelete_book(
    p_book_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id text;
    v_row     tc.books%ROWTYPE;
    v_conflict_count integer;
BEGIN
    v_user_id := tc.current_user_id();

    SELECT * INTO v_row FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_admin(v_row.collection_id) THEN
        RAISE EXCEPTION 'admin_required' USING ERRCODE = '42501';
    END IF;

    IF v_row.deleted_at IS NULL THEN
        RAISE EXCEPTION 'not_deleted: book is not tombstoned' USING ERRCODE = 'P0001';
    END IF;

    -- Check live-name uniqueness before restoring
    SELECT count(*) INTO v_conflict_count
    FROM tc.books
    WHERE collection_id = v_row.collection_id
      AND id            != p_book_id
      AND deleted_at    IS NULL
      AND lower(normalize(name, NFC)) = lower(normalize(v_row.name, NFC));

    IF v_conflict_count > 0 THEN
        RAISE EXCEPTION 'name_conflict: a live book already uses this name'
            USING ERRCODE = 'P0001';
    END IF;

    UPDATE tc.books
    SET deleted_at = NULL
    WHERE id = p_book_id;

    -- Log the undelete as a Created event to make it visible in history
    INSERT INTO tc.events (
        collection_id, book_id, type,
        by_user_id, by_user_name, by_email, book_name, message
    )
    VALUES (
        v_row.collection_id, p_book_id, 2, -- Created (reuse; undelete restores the book)
        v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
        v_row.name, 'undeleted'
    );
END;
$$;

COMMENT ON FUNCTION tc.undelete_book(uuid) IS
    'CONTRACTS.md: undelete_book — admin-only; clears tombstone; enforces live-name '
    'uniqueness (raises name_conflict if another live book uses the same name).';

-- ---------------------------------------------------------------------------
-- rename_check(book_id uuid, new_name text)
-- ---------------------------------------------------------------------------
-- Advisory uniqueness pre-check (returns whether the name is available).
-- Does NOT perform the rename — that happens via checkin-finish.
CREATE OR REPLACE FUNCTION tc.rename_check(
    p_book_id  uuid,
    p_new_name text
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
AS $$
DECLARE
    v_collection uuid;
    v_conflict   boolean;
BEGIN
    SELECT b.collection_id INTO v_collection
    FROM tc.books b WHERE b.id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    IF NOT tc.is_member(v_collection) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM tc.books
        WHERE collection_id = v_collection
          AND id             != p_book_id
          AND deleted_at     IS NULL
          AND lower(normalize(name, NFC)) = lower(normalize(p_new_name, NFC))
    ) INTO v_conflict;

    RETURN jsonb_build_object(
        'available', NOT v_conflict,
        'conflict',  v_conflict
    );
END;
$$;

COMMENT ON FUNCTION tc.rename_check(uuid, text) IS
    'CONTRACTS.md: rename_check — advisory live-name uniqueness pre-check. '
    'Returns {available, conflict}. Does NOT perform the rename.';

-- ---------------------------------------------------------------------------
-- members_list(collection_id uuid)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.members_list(
    p_collection_id uuid
)
RETURNS TABLE (
    id            bigint,
    email         text,
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
    SELECT m.id, m.email, m.role, m.user_id, m.added_by, m.added_at, m.claimed_at
    FROM tc.members m
    WHERE m.collection_id = p_collection_id
      AND tc.is_member(p_collection_id)   -- membership gate
    ORDER BY m.email
$$;

COMMENT ON FUNCTION tc.members_list(uuid) IS
    'CONTRACTS.md: members list — returns approved-accounts for the collection. '
    'Any member may call this.';

-- ---------------------------------------------------------------------------
-- members_add(collection_id uuid, email text, role tc.member_role)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.members_add(
    p_collection_id uuid,
    p_email         text,
    p_role          tc.member_role DEFAULT 'member'
)
RETURNS bigint
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id text;
    v_new_id  bigint;
BEGIN
    v_user_id := tc.current_user_id();

    IF NOT tc.is_admin(p_collection_id) THEN
        RAISE EXCEPTION 'admin_required' USING ERRCODE = '42501';
    END IF;

    INSERT INTO tc.members (collection_id, email, role, added_by)
    VALUES (p_collection_id, lower(normalize(p_email, NFC)), p_role, v_user_id)
    ON CONFLICT (collection_id, email) DO NOTHING
    RETURNING id INTO v_new_id;

    RETURN v_new_id;
END;
$$;

COMMENT ON FUNCTION tc.members_add(uuid, text, tc.member_role) IS
    'CONTRACTS.md: members add — admin-only; adds an approved-account email. '
    'Idempotent (on conflict do nothing).';

-- ---------------------------------------------------------------------------
-- members_remove(collection_id uuid, member_id bigint)
-- ---------------------------------------------------------------------------
-- Admin-only; removing a member force-unlocks any books they hold and emits events.
CREATE OR REPLACE FUNCTION tc.members_remove(
    p_collection_id uuid,
    p_member_id     bigint
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_caller_id      text;
    v_target_user_id text;
    v_book           record;
BEGIN
    v_caller_id := tc.current_user_id();

    IF NOT tc.is_admin(p_collection_id) THEN
        RAISE EXCEPTION 'admin_required' USING ERRCODE = '42501';
    END IF;

    SELECT user_id INTO v_target_user_id
    FROM tc.members
    WHERE id = p_member_id AND collection_id = p_collection_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'member_not_found' USING ERRCODE = 'P0002';
    END IF;

    -- Force-unlock all books held by this user and emit ForcedUnlock events
    FOR v_book IN
        SELECT b.id, b.name, b.locked_by_machine, b.locked_at
        FROM tc.books b
        WHERE b.collection_id = p_collection_id
          AND b.locked_by     = v_target_user_id
    LOOP
        INSERT INTO tc.events (
            collection_id, book_id, type,
            by_user_id, by_user_name, by_email,
            lock_info, book_name, message
        )
        VALUES (
            p_collection_id, v_book.id, 5, -- ForcedUnlock
            v_caller_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
            jsonb_build_object(
                'locked_by', v_target_user_id,
                'machine',   v_book.locked_by_machine,
                'locked_at', v_book.locked_at
            ),
            v_book.name,
            'lock released due to member removal'
        );

        UPDATE tc.books
        SET    locked_by         = NULL,
               locked_by_machine = NULL,
               locked_at         = NULL
        WHERE  id = v_book.id;
    END LOOP;

    -- Delete the member row (last-admin guard trigger will fire here if applicable)
    DELETE FROM tc.members WHERE id = p_member_id;
END;
$$;

COMMENT ON FUNCTION tc.members_remove(uuid, bigint) IS
    'CONTRACTS.md: members remove — admin-only; force-unlocks any books held by the removed '
    'member (emits ForcedUnlock events). Last-admin guard trigger fires on DELETE.';

-- ---------------------------------------------------------------------------
-- members_set_role(collection_id uuid, member_id bigint, new_role tc.member_role)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.members_set_role(
    p_collection_id uuid,
    p_member_id     bigint,
    p_new_role      tc.member_role
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    IF NOT tc.is_admin(p_collection_id) THEN
        RAISE EXCEPTION 'admin_required' USING ERRCODE = '42501';
    END IF;

    -- The last-admin guard trigger will raise if this demotes the last admin.
    UPDATE tc.members
    SET    role = p_new_role
    WHERE  id              = p_member_id
      AND  collection_id   = p_collection_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'member_not_found' USING ERRCODE = 'P0002';
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.members_set_role(uuid, bigint, tc.member_role) IS
    'CONTRACTS.md: members set_role — admin-only; last-admin guard trigger fires on demotion.';

-- ---------------------------------------------------------------------------
-- add_palette_colors(collection_id uuid, palette text, colors text[])
-- ---------------------------------------------------------------------------
-- Union merge: insert-on-conflict-do-nothing.
CREATE OR REPLACE FUNCTION tc.add_palette_colors(
    p_collection_id uuid,
    p_palette       text,
    p_colors        text[]
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id text;
    v_color   text;
BEGIN
    v_user_id := tc.current_user_id();

    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    FOREACH v_color IN ARRAY p_colors LOOP
        INSERT INTO tc.color_palette_entries (collection_id, palette, color, added_by)
        VALUES (p_collection_id, p_palette, v_color, v_user_id)
        ON CONFLICT (collection_id, palette, color) DO NOTHING;
    END LOOP;
END;
$$;

COMMENT ON FUNCTION tc.add_palette_colors(uuid, text, text[]) IS
    'CONTRACTS.md: add_palette_colors — union merge; insert-on-conflict-do-nothing. '
    'Any member may call.';

-- ---------------------------------------------------------------------------
-- log_event(collection_id uuid, book_id uuid, type integer, message text,
--           book_name text, bloom_version text)
-- ---------------------------------------------------------------------------
-- Client-originated history entries (e.g. WorkPreservedLocally incidents).
CREATE OR REPLACE FUNCTION tc.log_event(
    p_collection_id  uuid,
    p_book_id        uuid DEFAULT NULL,
    p_type           integer DEFAULT NULL,
    p_message        text DEFAULT NULL,
    p_book_name      text DEFAULT NULL,
    p_bloom_version  text DEFAULT NULL
)
RETURNS bigint
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id  text;
    v_event_id bigint;
BEGIN
    v_user_id := tc.current_user_id();

    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    -- type must be a valid event type value (the check constraint on tc.events will catch
    -- invalid values, but we give a friendlier error here).
    IF p_type IS NULL THEN
        RAISE EXCEPTION 'event_type_required' USING ERRCODE = '22023';
    END IF;

    INSERT INTO tc.events (
        collection_id, book_id, type,
        by_user_id, by_user_name, by_email,
        book_name, message, bloom_version
    )
    VALUES (
        p_collection_id, p_book_id, p_type,
        v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
        p_book_name, p_message, p_bloom_version
    )
    RETURNING id INTO v_event_id;

    RETURN v_event_id;
END;
$$;

COMMENT ON FUNCTION tc.log_event(uuid, uuid, integer, text, text, text) IS
    'CONTRACTS.md: log_event — client-originated history entries (e.g. WorkPreservedLocally '
    'incident events). Returns the new event id.';

-- ---------------------------------------------------------------------------
-- Grant EXECUTE on all RPCs to authenticated role
-- ---------------------------------------------------------------------------
GRANT EXECUTE ON FUNCTION tc.create_collection(uuid, text)              TO authenticated;
GRANT EXECUTE ON FUNCTION tc.my_collections()                           TO authenticated;
GRANT EXECUTE ON FUNCTION tc.claim_memberships()                        TO authenticated;
GRANT EXECUTE ON FUNCTION tc.get_collection_state(uuid, bigint)         TO authenticated;
GRANT EXECUTE ON FUNCTION tc.get_changes(uuid, bigint)                  TO authenticated;
GRANT EXECUTE ON FUNCTION tc.checkout_book(uuid, text)                  TO authenticated;
GRANT EXECUTE ON FUNCTION tc.unlock_book(uuid)                          TO authenticated;
GRANT EXECUTE ON FUNCTION tc.force_unlock(uuid)                         TO authenticated;
GRANT EXECUTE ON FUNCTION tc.delete_book(uuid)                          TO authenticated;
GRANT EXECUTE ON FUNCTION tc.undelete_book(uuid)                        TO authenticated;
GRANT EXECUTE ON FUNCTION tc.rename_check(uuid, text)                   TO authenticated;
GRANT EXECUTE ON FUNCTION tc.members_list(uuid)                         TO authenticated;
GRANT EXECUTE ON FUNCTION tc.members_add(uuid, text, tc.member_role)    TO authenticated;
GRANT EXECUTE ON FUNCTION tc.members_remove(uuid, bigint)               TO authenticated;
GRANT EXECUTE ON FUNCTION tc.members_set_role(uuid, bigint, tc.member_role) TO authenticated;
GRANT EXECUTE ON FUNCTION tc.add_palette_colors(uuid, text, text[])     TO authenticated;
GRANT EXECUTE ON FUNCTION tc.log_event(uuid, uuid, integer, text, text, text) TO authenticated;
