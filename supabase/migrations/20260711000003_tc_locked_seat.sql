-- =============================================================================
-- Per-collection-copy "seat" on checkouts (dogfood batch 1, bug #0 — John's decision)
-- =============================================================================
-- Item 9's same-machine takeover (20260709000007) gated only on the machine name, so
-- ANY same-machine account could take over ANY same-machine lock — including across two
-- separate local copies of the collection on one computer (two "seats"), which is what
-- e2e-4 simulates and what a shared lab machine would really be. John's ruling
-- (11 Jul 2026, recorded in orchestration/DOGFOOD-BATCH-1.md): editing/takeover of a
-- checkout is only legitimate where the book is checked out — in THAT local copy of the
-- collection. So the lock record now carries a seat id (client-computed stable hash of
-- the local collection folder path — never the raw path, for privacy), and takeover
-- requires machine AND seat to match. A lock with no recorded seat (legacy row, or a
-- lock acquired via checkin_start_tx's take-if-free path, which has no seat parameter)
-- REFUSES takeover — fail-safe.
--
-- Seat lifecycle: set by checkout_book/checkout_book_takeover; cleared automatically by
-- a BEFORE UPDATE trigger whenever locked_by is cleared (covers unlock_book,
-- force_unlock, checkin_finish_tx's release, and any future unlock path without
-- recreating them here).
--
-- CONTRACTS.md v1.5: checkout_book/checkout_book_takeover gain an optional `seat`
-- parameter and return `locked_seat`; get_collection_state/get_changes book rows carry
-- `locked_seat`. All additive.
-- =============================================================================

ALTER TABLE tc.books ADD COLUMN locked_seat text;

COMMENT ON COLUMN tc.books.locked_seat IS
    'Which local copy of the collection ("seat") holds the lock: a client-computed '
    'stable hash of the local collection folder path (never the raw path). NULL = '
    'unknown (legacy lock, or one acquired by checkin_start_tx''s take-if-free path); '
    'a NULL seat can never be taken over (fail-safe).';

-- ---------------------------------------------------------------------------
-- Trigger: locked_seat can never outlive locked_by.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc._clear_seat_on_unlock()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.locked_by IS NULL THEN
        NEW.locked_seat := NULL;
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc._clear_seat_on_unlock() IS
    'Internal: clears tc.books.locked_seat whenever locked_by is cleared, so every '
    'unlock path (unlock_book, force_unlock, checkin_finish_tx, future ones) stays '
    'seat-consistent without each having to remember the column.';

CREATE TRIGGER books_clear_seat_on_unlock
    BEFORE UPDATE ON tc.books
    FOR EACH ROW
    EXECUTE FUNCTION tc._clear_seat_on_unlock();

-- ---------------------------------------------------------------------------
-- checkout_book(book_id, machine, seat) — records the caller's seat with the lock.
-- ---------------------------------------------------------------------------
-- DROP + CREATE (not OR REPLACE) because the signature gains a parameter. The new
-- parameter has a DEFAULT so pre-seat callers (and pgTAP's 2-arg calls) keep working;
-- they simply record an unknown (NULL) seat.
DROP FUNCTION tc.checkout_book(uuid, text);

CREATE FUNCTION tc.checkout_book(
    p_book_id  uuid,
    p_machine  text,
    p_seat     text DEFAULT NULL
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id     text;
    v_collection  uuid;
    v_updated     integer;   -- row count from the conditional UPDATE (0 or 1)
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
           locked_seat       = p_seat,
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
            'locked_seat',       p_seat,
            'locked_at',         now()
        );
    ELSE
        -- Lock held by someone else
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_seat',       v_row.locked_seat,
            'locked_at',         v_row.locked_at
        );
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.checkout_book(uuid, text, text) IS
    'CONTRACTS.md: checkout_book — conditional lock (race-free UPDATE WHERE locked_by IS NULL '
    'OR locked_by = me). v1.5: also records the caller''s seat (local-copy id) with the lock. '
    'Returns {success, locked_by, locked_by_machine, locked_seat, locked_at}. '
    'Emits CheckOut event (type=0) on success.';

GRANT EXECUTE ON FUNCTION tc.checkout_book(uuid, text, text) TO authenticated;

-- ---------------------------------------------------------------------------
-- checkout_book_takeover(book_id, machine, seat) — takeover now requires the seat too.
-- ---------------------------------------------------------------------------
DROP FUNCTION tc.checkout_book_takeover(uuid, text);

CREATE FUNCTION tc.checkout_book_takeover(
    p_book_id  uuid,
    p_machine  text,
    p_seat     text DEFAULT NULL
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id     text;
    v_collection  uuid;
    v_before      tc.books%ROWTYPE;
    v_updated     integer;   -- row count from the conditional UPDATE (0 or 1)
    v_row         tc.books%ROWTYPE;
BEGIN
    v_user_id := tc.current_user_id();

    SELECT * INTO v_before FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION '%', '{"error":"book_not_found"}' USING ERRCODE = 'PT404';
    END IF;

    v_collection := v_before.collection_id;

    IF NOT tc.is_member(v_collection) THEN
        RAISE EXCEPTION '%', '{"error":"not_a_member"}' USING ERRCODE = 'PT403';
    END IF;

    -- Race-free conditional UPDATE: only takes the lock from a DIFFERENT account, and only
    -- when that account's lock is recorded against the SAME machine AND the SAME seat
    -- (local collection copy) the caller is on now (bug #0, John's ruling: takeover is the
    -- shared-computer, same-local-folder scenario — two folders on one machine are two
    -- seats and remain a genuine conflict). A NULL stored seat (legacy lock, or one taken
    -- by checkin_start_tx's take-if-free path) never matches — fail-safe. A NULL p_seat
    -- (pre-seat caller) likewise can never take over.
    UPDATE tc.books
    SET    locked_by         = v_user_id,
           locked_by_machine = p_machine,
           locked_seat       = p_seat,
           locked_at         = now()
    WHERE  id = p_book_id
      AND  deleted_at IS NULL
      AND  locked_by IS NOT NULL
      AND  locked_by <> v_user_id
      AND  locked_by_machine = p_machine
      AND  locked_seat IS NOT NULL
      AND  locked_seat = p_seat;

    GET DIAGNOSTICS v_updated = ROW_COUNT;

    -- Fetch resulting row
    SELECT * INTO v_row FROM tc.books WHERE id = p_book_id;

    IF v_updated > 0 THEN
        -- Emit CheckOut event (type = 0) -- same event type an ordinary checkout_book success
        -- emits, since from the audit trail's point of view this genuinely is B checking the
        -- book out; the preceding history already shows A's own checkout, so the handoff reads
        -- naturally without needing a new event-type constant shared across client/server.
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
            'locked_seat',       p_seat,
            'locked_at',         v_row.locked_at
        );
    ELSE
        -- Nothing to take over (already ours, unlocked, locked on a different machine, or
        -- locked in a different/unknown seat).
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_seat',       v_row.locked_seat,
            'locked_at',         v_row.locked_at
        );
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.checkout_book_takeover(uuid, text, text) IS
    'CONTRACTS.md v1.5: checkout_book_takeover — atomically reassigns a book''s lock from a '
    'DIFFERENT account to the caller, but ONLY when the existing lock is recorded for the SAME '
    'machine AND the SAME seat (local collection copy) — bug #0: two local copies on one '
    'computer are two seats; a NULL stored seat never matches (fail-safe). Returns '
    '{success, locked_by, locked_by_machine, locked_seat, locked_at}. Emits a CheckOut event '
    '(type=0) only when the lock actually changed hands.';

GRANT EXECUTE ON FUNCTION tc.checkout_book_takeover(uuid, text, text) TO authenticated;

-- ---------------------------------------------------------------------------
-- get_collection_state / get_changes: expose locked_seat on book rows (additive).
-- ---------------------------------------------------------------------------
-- Full recreations of the 20260707000006 versions with locked_seat added to each
-- book-row SELECT; no other change.
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
        -- Full snapshot: all live books, minus never-committed books invisible to
        -- everyone but their own mid-Send lock holder (see 20260707000006 for history).
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
                b.locked_seat,
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
                b.locked_seat,
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
    'rows also carry locked_by_email/locked_by_name for display. v1.5 (20260711000003): book '
    'rows also carry locked_seat.';

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
    '(20260711000003): touched book rows also carry locked_seat.';
