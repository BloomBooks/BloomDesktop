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


-- ==== 01_schema.sql ====

-- Team Collections cloud schema: namespace + enum types.
-- Declarative source of truth (see AGENTS/CONTRACTS): edit these files, then
-- `supabase db diff -f <name>` to generate a migration. Applied in file order.
CREATE SCHEMA IF NOT EXISTS tc;

CREATE TYPE tc.member_role AS ENUM (
    'admin',
    'member'
);


-- ==== 02_functions.sql ====

-- Team Collections cloud: all functions (RPCs, transaction `_tx` helpers,
-- trigger functions, and internal helpers). Created before the tables they
-- reference, so body validation is deferred here (Postgres late-binds plpgsql;
-- this SET covers the LANGUAGE sql functions too).
set check_function_bodies = false;
CREATE OR REPLACE FUNCTION tc._checkin_reap_book(p_book_id uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_new_book boolean;
BEGIN
    SELECT (current_version_id IS NULL) INTO v_new_book
    FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF v_new_book THEN
        -- Deleting the book cascades its (expired, still-open) transactions.
        DELETE FROM tc.books
        WHERE id = p_book_id
          AND current_version_id IS NULL
          AND EXISTS (
              SELECT 1 FROM tc.checkin_transactions t
              WHERE t.book_id = p_book_id AND t.status = 'open' AND t.expires_at < now()
          )
          -- never reap while ANOTHER still-live open transaction exists
          AND NOT EXISTS (
              SELECT 1 FROM tc.checkin_transactions t
              WHERE t.book_id = p_book_id AND t.status = 'open' AND t.expires_at >= now()
          );
    ELSE
        UPDATE tc.checkin_transactions
        SET status = 'expired'
        WHERE book_id = p_book_id AND status = 'open' AND expires_at < now();
    END IF;
END;
$$;

COMMENT ON FUNCTION tc._checkin_reap_book(p_book_id uuid) IS 'Internal: reap expired open checkin_transactions for one book. New, never-finished books are deleted outright; existing books just have the stale transaction marked expired (lock is left untouched).';

CREATE OR REPLACE FUNCTION tc._clear_seat_on_unlock() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.locked_by IS NULL THEN
        NEW.locked_seat := NULL;
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc._clear_seat_on_unlock() IS 'Internal: clears tc.books.locked_seat whenever locked_by is cleared, so every unlock path (unlock_book, force_unlock, checkin_finish_tx, future ones) stays seat-consistent without each having to remember the column.';

CREATE OR REPLACE FUNCTION tc.add_palette_colors(p_collection_id uuid, p_palette text, p_colors text[]) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.add_palette_colors(p_collection_id uuid, p_palette text, p_colors text[]) IS 'CONTRACTS.md: add_palette_colors — union merge; insert-on-conflict-do-nothing. Any member may call.';

CREATE OR REPLACE FUNCTION tc.checkin_abort_tx(p_transaction_id uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id text := tc.current_user_id();
    v_tx      tc.checkin_transactions%ROWTYPE;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;

    SELECT * INTO v_tx FROM tc.checkin_transactions WHERE id = p_transaction_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION '%', '{"error":"transaction_not_found"}' USING ERRCODE = 'PT404';
    END IF;
    IF v_tx.started_by <> v_user_id THEN
        RAISE EXCEPTION '%', '{"error":"forbidden"}' USING ERRCODE = 'PT403';
    END IF;

    IF v_tx.status = 'aborted' THEN
        RETURN; -- idempotent
    END IF;
    IF v_tx.status = 'finished' THEN
        RAISE EXCEPTION '%', '{"error":"already_finished"}' USING ERRCODE = 'PT409';
    END IF;

    UPDATE tc.checkin_transactions SET status = 'aborted', aborted_at = now()
    WHERE id = p_transaction_id;

    -- Roll back a never-finished new book entirely (fully invisible, as designed).
    -- Existing books keep whatever lock they had — aborting a Send is not the same
    -- as releasing a Checkout.
    PERFORM 1 FROM tc.books WHERE id = v_tx.book_id AND current_version_id IS NULL;
    IF FOUND AND NOT EXISTS (
        SELECT 1 FROM tc.checkin_transactions
        WHERE book_id = v_tx.book_id AND status = 'open'
    ) THEN
        DELETE FROM tc.books WHERE id = v_tx.book_id AND current_version_id IS NULL;
    END IF;

    PERFORM tc.reap_expired_checkin_transactions();
END;
$$;

COMMENT ON FUNCTION tc.checkin_abort_tx(p_transaction_id uuid) IS 'Internal to the checkin-abort edge function. Idempotent. Rolls back a never-finished new book entirely; leaves an existing book''s lock untouched.';

CREATE OR REPLACE FUNCTION tc.checkin_finish_tx(p_transaction_id uuid, p_comment text, p_keep_checked_out boolean, p_captured jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id     text := tc.current_user_id();
    v_tx          tc.checkin_transactions%ROWTYPE;
    v_missing     text[];
    v_final       jsonb;
    v_was_new     boolean;
    v_new_seq     bigint;
    v_version_id  uuid;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;

    SELECT * INTO v_tx FROM tc.checkin_transactions WHERE id = p_transaction_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION '%', '{"error":"transaction_not_found"}' USING ERRCODE = 'PT404';
    END IF;
    IF v_tx.started_by <> v_user_id THEN
        RAISE EXCEPTION '%', '{"error":"forbidden"}' USING ERRCODE = 'PT403';
    END IF;

    IF v_tx.status = 'finished' THEN
        -- Idempotent retry: return the previously-committed result unchanged.
        RETURN jsonb_build_object('versionId', v_tx.result_version_id, 'seq', v_tx.result_seq);
    END IF;

    IF v_tx.status = 'aborted' THEN
        RAISE EXCEPTION '%', '{"error":"transaction_aborted"}' USING ERRCODE = 'PT409';
    END IF;

    IF v_tx.status = 'expired' OR v_tx.expires_at < now() THEN
        UPDATE tc.checkin_transactions SET status = 'expired'
        WHERE id = p_transaction_id AND status = 'open';
        RAISE EXCEPTION '%', '{"error":"TransactionExpired"}' USING ERRCODE = 'PT410';
    END IF;

    -- ---- Verify every changed path was captured (uploaded + checksum-verified
    --      by the edge function before calling us) -------------------------
    SELECT COALESCE(array_agg(cp), '{}') INTO v_missing
    FROM unnest(v_tx.changed_paths) cp
    WHERE NOT EXISTS (
        SELECT 1 FROM jsonb_to_recordset(p_captured) AS c(path text, "s3VersionId" text)
        WHERE c.path = cp AND c."s3VersionId" IS NOT NULL
    );

    IF array_length(v_missing, 1) > 0 THEN
        -- Transaction stays OPEN so the client can re-upload and retry.
        RAISE EXCEPTION '%', json_build_object(
            'error', 'MissingOrBadUploads', 'paths', to_jsonb(v_missing)
        )::text USING ERRCODE = 'PT409';
    END IF;

    -- ---- Build the final manifest: proposed_files, with s3_version_id from
    --      p_captured for changed paths and from the CURRENT manifest for
    --      everything else. -------------------------------------------------
    SELECT jsonb_agg(jsonb_build_object(
               'path', f.path,
               'sha256', f.sha256,
               'size', f.size,
               's3VersionId', COALESCE(
                   (SELECT c."s3VersionId" FROM jsonb_to_recordset(p_captured) AS c(path text, "s3VersionId" text)
                    WHERE c.path = f.path),
                   (SELECT vf.s3_version_id FROM tc.version_files vf
                    WHERE vf.book_id = v_tx.book_id AND vf.path = f.path)
               )
           ))
    INTO v_final
    FROM jsonb_to_recordset(v_tx.proposed_files) AS f(path text, sha256 text, size bigint);

    IF EXISTS (
        SELECT 1 FROM jsonb_array_elements(COALESCE(v_final, '[]'::jsonb)) e
        WHERE e->>'s3VersionId' IS NULL
    ) THEN
        -- Defensive: a path was neither captured now nor present in the prior manifest.
        RAISE EXCEPTION '%', json_build_object('error', 'MissingOrBadUploads',
            'paths', (SELECT jsonb_agg(e->>'path') FROM jsonb_array_elements(v_final) e
                      WHERE e->>'s3VersionId' IS NULL))::text
            USING ERRCODE = 'PT409';
    END IF;

    v_was_new := (SELECT current_version_id FROM tc.books WHERE id = v_tx.book_id) IS NULL;
    v_new_seq := COALESCE((SELECT max(seq) FROM tc.versions WHERE book_id = v_tx.book_id), 0) + 1;

    INSERT INTO tc.versions (book_id, collection_id, seq, checksum, comment, created_by, client_version)
    VALUES (v_tx.book_id, v_tx.collection_id, v_new_seq, v_tx.checksum, p_comment, v_user_id, v_tx.client_version)
    RETURNING id INTO v_version_id;

    DELETE FROM tc.version_files WHERE book_id = v_tx.book_id;

    INSERT INTO tc.version_files (book_id, version_id, path, sha256, size_bytes, s3_version_id)
    SELECT v_tx.book_id, v_version_id, e->>'path', e->>'sha256', (e->>'size')::bigint, e->>'s3VersionId'
    FROM jsonb_array_elements(v_final) e;

    UPDATE tc.books
    SET current_version_id = v_version_id,
        current_version_seq = v_new_seq,
        current_checksum = v_tx.checksum,
        name = v_tx.proposed_name,
        locked_by = CASE WHEN p_keep_checked_out THEN locked_by ELSE NULL END,
        locked_by_machine = CASE WHEN p_keep_checked_out THEN locked_by_machine ELSE NULL END,
        locked_at = CASE WHEN p_keep_checked_out THEN locked_at ELSE NULL END
    WHERE id = v_tx.book_id;

    IF v_was_new THEN
        INSERT INTO tc.events (collection_id, book_id, type, by_user_id, by_user_name, by_email, book_name, bloom_version)
        VALUES (v_tx.collection_id, v_tx.book_id, 2, v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(), v_tx.proposed_name, v_tx.client_version);
    END IF;

    INSERT INTO tc.events (
        collection_id, book_id, type, by_user_id, by_user_name, by_email,
        book_version_seq, book_name, message, bloom_version
    )
    VALUES (
        v_tx.collection_id, v_tx.book_id, 1, v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
        v_new_seq, v_tx.proposed_name, p_comment, v_tx.client_version
    );

    UPDATE tc.checkin_transactions
    SET status = 'finished', finished_at = now(), result_version_id = v_version_id, result_seq = v_new_seq
    WHERE id = p_transaction_id;

    -- 'manifest' is NOT part of the CONTRACTS.md response ({versionId, seq} only) — it
    -- is extra data for the edge function's own use (writing .manifest.json to S3);
    -- the edge function must not forward it to the client.
    RETURN jsonb_build_object('versionId', v_version_id, 'seq', v_new_seq, 'manifest', v_final);
END;
$$;

COMMENT ON FUNCTION tc.checkin_finish_tx(p_transaction_id uuid, p_comment text, p_keep_checked_out boolean, p_captured jsonb) IS 'Internal to the checkin-finish edge function. Single atomic DB transaction: version row, current-manifest replacement, book update, lock release, events, transaction close. Idempotent when re-called on an already-finished transaction. Raises PT401/PT403/PT404/PT409(MissingOrBadUploads)/PT410(expired).';

CREATE OR REPLACE FUNCTION tc.checkin_start_tx(p_collection_id uuid, p_book_id uuid, p_book_instance_id uuid, p_proposed_name text, p_base_version_id uuid, p_checksum text, p_client_version text, p_files jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id       text := tc.current_user_id();
    v_book          tc.books%ROWTYPE;
    v_changed       text[];
    v_tx_id         uuid;
    v_existing_tx   tc.checkin_transactions%ROWTYPE;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;

    IF NOT tc.is_client_version_supported(p_client_version) THEN
        RAISE EXCEPTION '%', json_build_object(
            'error', 'ClientOutOfDate',
            'minVersion', tc.min_supported_client_version()
        )::text USING ERRCODE = 'PT426';
    END IF;

    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION '%', '{"error":"not_a_member"}' USING ERRCODE = 'PT403';
    END IF;

    PERFORM tc.reap_expired_checkin_transactions();

    IF p_book_id IS NULL THEN
        -- ---- New-book path (or resume of our own not-yet-committed new-book Send) --
        -- CONTRACTS.md's checkin-start response never exposes `bookId` (that's the
        -- whole point of "invisible until commit") so a client resuming an
        -- interrupted new-book Send has no id to pass back — it re-calls with
        -- bookId=null and the SAME bookInstanceId, which is the only identity it has.
        -- We must therefore recognize "a row already exists with this instance_id,
        -- but it's OUR OWN never-committed, still-locked-to-us row" as a resume, not
        -- a conflict — otherwise resume is unreachable (every retry would trip the
        -- instance-id uniqueness check below against the row created by try #1).
        SELECT * INTO v_book FROM tc.books
        WHERE collection_id = p_collection_id AND instance_id = p_book_instance_id;

        IF FOUND THEN
            IF v_book.current_version_id IS NOT NULL OR v_book.locked_by IS DISTINCT FROM v_user_id THEN
                -- Committed already, or in-flight under someone else's Send: genuine conflict.
                RAISE EXCEPTION '%', json_build_object('error', 'NameConflict',
                    'detail', 'instance_id already in use')::text
                    USING ERRCODE = 'PT409';
            END IF;
            -- else: fall through with v_book already set to our own resumable row.
        ELSE
            IF EXISTS (
                SELECT 1 FROM tc.books
                WHERE collection_id = p_collection_id
                  AND deleted_at IS NULL
                  AND lower(normalize(name, NFC)) = lower(normalize(p_proposed_name, NFC))
            ) THEN
                RAISE EXCEPTION '%', json_build_object('error', 'NameConflict')::text
                    USING ERRCODE = 'PT409';
            END IF;

            INSERT INTO tc.books (
                collection_id, instance_id, name, locked_by, locked_at, created_by
            )
            VALUES (
                p_collection_id, p_book_instance_id, p_proposed_name, v_user_id, now(), v_user_id
            )
            RETURNING * INTO v_book;
        END IF;
    ELSE
        -- ---- Existing-book path ---------------------------------------------
        SELECT * INTO v_book FROM tc.books
        WHERE id = p_book_id AND collection_id = p_collection_id;

        IF NOT FOUND THEN
            RAISE EXCEPTION '%', '{"error":"book_not_found"}' USING ERRCODE = 'PT404';
        END IF;

        IF v_book.locked_by IS NOT NULL AND v_book.locked_by <> v_user_id THEN
            RAISE EXCEPTION '%', json_build_object(
                'error', 'LockHeldByOther',
                'holder', json_build_object(
                    'userId', v_book.locked_by,
                    'machine', v_book.locked_by_machine,
                    'lockedAt', v_book.locked_at
                )
            )::text USING ERRCODE = 'PT409';
        END IF;

        IF p_base_version_id IS NOT NULL
           AND v_book.current_version_id IS DISTINCT FROM p_base_version_id THEN
            RAISE EXCEPTION '%', json_build_object(
                'error', 'BaseVersionSuperseded',
                'currentVersionId', v_book.current_version_id,
                'currentVersionSeq', v_book.current_version_seq
            )::text USING ERRCODE = 'PT409';
        END IF;

        -- Take the lock if free; no-op if already ours (lock ACQUISITION here, not just
        -- verification, is a deliberate reading of "membership + lock checks" — see
        -- orchestration report for the alternative interpretation considered).
        UPDATE tc.books
        SET locked_by = v_user_id, locked_at = now()
        WHERE id = p_book_id AND (locked_by IS NULL OR locked_by = v_user_id)
        RETURNING * INTO v_book;
    END IF;

    -- ---- Diff proposed manifest vs current --------------------------------
    SELECT COALESCE(array_agg(f.path), '{}') INTO v_changed
    FROM jsonb_to_recordset(p_files) AS f(path text, sha256 text, size bigint)
    WHERE NOT EXISTS (
        SELECT 1 FROM tc.version_files vf
        WHERE vf.book_id = v_book.id
          AND vf.path = f.path
          AND vf.sha256 = f.sha256
          AND vf.size_bytes = f.size
    );

    -- ---- Resume an already-open transaction for this (book, caller) -------
    SELECT * INTO v_existing_tx
    FROM tc.checkin_transactions
    WHERE book_id = v_book.id AND started_by = v_user_id AND status = 'open';

    IF FOUND THEN
        UPDATE tc.checkin_transactions
        SET proposed_name = p_proposed_name,
            base_version_id = p_base_version_id,
            checksum = p_checksum,
            client_version = p_client_version,
            proposed_files = p_files,
            changed_paths = v_changed,
            expires_at = now() + INTERVAL '48 hours'
        WHERE id = v_existing_tx.id
        RETURNING id INTO v_tx_id;
    ELSE
        INSERT INTO tc.checkin_transactions (
            collection_id, book_id, started_by, proposed_name, base_version_id,
            changed_paths, client_version, proposed_files, checksum
        )
        VALUES (
            p_collection_id, v_book.id, v_user_id, p_proposed_name, p_base_version_id,
            v_changed, p_client_version, p_files, p_checksum
        )
        RETURNING id INTO v_tx_id;
    END IF;

    RETURN jsonb_build_object(
        'transactionId', v_tx_id,
        'bookId', v_book.id,
        'changedPaths', to_jsonb(v_changed)
    );
END;
$$;

COMMENT ON FUNCTION tc.checkin_start_tx(p_collection_id uuid, p_book_id uuid, p_book_instance_id uuid, p_proposed_name text, p_base_version_id uuid, p_checksum text, p_client_version text, p_files jsonb) IS 'Internal to the checkin-start edge function. Handles membership/lock/base-version checks, the new-book path, manifest diffing, and open-transaction resume. Raises PT401/PT403/PT404/PT409/PT426 per CONTRACTS.md checkin-start error list.';

CREATE OR REPLACE FUNCTION tc.checkout_book(p_book_id uuid, p_machine text, p_seat text DEFAULT NULL::text) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.checkout_book(p_book_id uuid, p_machine text, p_seat text) IS 'CONTRACTS.md: checkout_book — conditional lock (race-free UPDATE WHERE locked_by IS NULL OR locked_by = me). v1.5: also records the caller''s seat (local-copy id) with the lock. Returns {success, locked_by, locked_by_machine, locked_seat, locked_at}. Emits CheckOut event (type=0) on success.';

CREATE OR REPLACE FUNCTION tc.checkout_book_takeover(p_book_id uuid, p_machine text, p_seat text DEFAULT NULL::text) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.checkout_book_takeover(p_book_id uuid, p_machine text, p_seat text) IS 'CONTRACTS.md v1.5: checkout_book_takeover — atomically reassigns a book''s lock from a DIFFERENT account to the caller, but ONLY when the existing lock is recorded for the SAME machine AND the SAME seat (local collection copy) — bug #0: two local copies on one computer are two seats; a NULL stored seat never matches (fail-safe). Returns {success, locked_by, locked_by_machine, locked_seat, locked_at}. Emits a CheckOut event (type=0) only when the lock actually changed hands.';

CREATE OR REPLACE FUNCTION tc.claim_memberships() RETURNS TABLE(collection_id uuid, role tc.member_role)
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.claim_memberships() IS 'CONTRACTS.md: claim_memberships — fills user_id on rows matching the caller''s verified email. Requires tc.jwt_email_verified().';

CREATE OR REPLACE FUNCTION tc.collection_files_finish_tx(p_transaction_id uuid, p_captured jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id  text := tc.current_user_id();
    v_tx       tc.collection_file_transactions%ROWTYPE;
    v_group    tc.collection_file_groups%ROWTYPE;
    v_missing  text[];
    v_final    jsonb;
    v_new_ver  bigint;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;

    SELECT * INTO v_tx FROM tc.collection_file_transactions WHERE id = p_transaction_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION '%', '{"error":"transaction_not_found"}' USING ERRCODE = 'PT404';
    END IF;
    IF v_tx.started_by <> v_user_id THEN
        RAISE EXCEPTION '%', '{"error":"forbidden"}' USING ERRCODE = 'PT403';
    END IF;

    IF v_tx.status = 'finished' THEN
        RETURN jsonb_build_object('version', v_tx.result_version);
    END IF;
    IF v_tx.status = 'aborted' THEN
        RAISE EXCEPTION '%', '{"error":"transaction_aborted"}' USING ERRCODE = 'PT409';
    END IF;
    IF v_tx.status = 'expired' OR v_tx.expires_at < now() THEN
        UPDATE tc.collection_file_transactions SET status = 'expired'
        WHERE id = p_transaction_id AND status = 'open';
        RAISE EXCEPTION '%', '{"error":"TransactionExpired"}' USING ERRCODE = 'PT410';
    END IF;

    SELECT * INTO v_group FROM tc.collection_file_groups
    WHERE collection_id = v_tx.collection_id AND group_key = v_tx.group_key;

    IF v_group.version <> v_tx.expected_version THEN
        UPDATE tc.collection_file_transactions SET status = 'aborted', aborted_at = now()
        WHERE id = p_transaction_id;
        RAISE EXCEPTION '%', json_build_object(
            'error', 'VersionConflict', 'currentVersion', v_group.version
        )::text USING ERRCODE = 'PT409';
    END IF;

    SELECT COALESCE(array_agg(cp), '{}') INTO v_missing
    FROM unnest(v_tx.changed_paths) cp
    WHERE NOT EXISTS (
        SELECT 1 FROM jsonb_to_recordset(p_captured) AS c(path text, "s3VersionId" text)
        WHERE c.path = cp AND c."s3VersionId" IS NOT NULL
    );

    IF array_length(v_missing, 1) > 0 THEN
        RAISE EXCEPTION '%', json_build_object(
            'error', 'MissingOrBadUploads', 'paths', to_jsonb(v_missing)
        )::text USING ERRCODE = 'PT409';
    END IF;

    SELECT jsonb_agg(jsonb_build_object(
               'path', f.path,
               'sha256', f.sha256,
               'size', f.size,
               's3VersionId', COALESCE(
                   (SELECT c."s3VersionId" FROM jsonb_to_recordset(p_captured) AS c(path text, "s3VersionId" text)
                    WHERE c.path = f.path),
                   (SELECT gf.s3_version_id FROM tc.collection_group_files gf
                    WHERE gf.group_id = v_group.id AND gf.path = f.path)
               )
           ))
    INTO v_final
    FROM jsonb_to_recordset(v_tx.proposed_files) AS f(path text, sha256 text, size bigint);

    v_new_ver := v_tx.expected_version + 1;

    UPDATE tc.collection_file_groups
    SET version = v_new_ver, updated_at = now(), updated_by = v_user_id
    WHERE id = v_group.id AND version = v_tx.expected_version;

    IF NOT FOUND THEN
        UPDATE tc.collection_file_transactions SET status = 'aborted', aborted_at = now()
        WHERE id = p_transaction_id;
        RAISE EXCEPTION '%', json_build_object(
            'error', 'VersionConflict', 'currentVersion', v_group.version
        )::text USING ERRCODE = 'PT409';
    END IF;

    DELETE FROM tc.collection_group_files WHERE group_id = v_group.id;

    INSERT INTO tc.collection_group_files (group_id, path, sha256, size_bytes, s3_version_id)
    SELECT v_group.id, e->>'path', e->>'sha256', (e->>'size')::bigint, e->>'s3VersionId'
    FROM jsonb_array_elements(v_final) e;

    INSERT INTO tc.events (collection_id, type, by_user_id, by_user_name, by_email, group_key)
    VALUES (v_tx.collection_id, 1, v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(), v_tx.group_key);

    UPDATE tc.collection_file_transactions
    SET status = 'finished', finished_at = now(), result_version = v_new_ver
    WHERE id = p_transaction_id;

    -- 'manifest' is extra data for the edge function only (not part of the
    -- CONTRACTS.md {version} response) — used to write the .manifest.json backup.
    RETURN jsonb_build_object('version', v_new_ver, 'manifest', v_final);
END;
$$;

COMMENT ON FUNCTION tc.collection_files_finish_tx(p_transaction_id uuid, p_captured jsonb) IS 'Internal to the collection-files-finish edge function. Re-checks the optimistic version at finish time too (repo-wins rule); PT409 VersionConflict aborts the transaction so a stale retry cannot succeed later.';

CREATE OR REPLACE FUNCTION tc.collection_files_start_tx(p_collection_id uuid, p_group_key text, p_expected_version bigint, p_files jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id  text := tc.current_user_id();
    v_group    tc.collection_file_groups%ROWTYPE;
    v_changed  text[];
    v_tx_id    uuid;
    v_existing tc.collection_file_transactions%ROWTYPE;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;
    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION '%', '{"error":"not_a_member"}' USING ERRCODE = 'PT403';
    END IF;

    PERFORM tc.reap_expired_checkin_transactions();

    INSERT INTO tc.collection_file_groups (collection_id, group_key, version, updated_by)
    VALUES (p_collection_id, p_group_key, 0, v_user_id)
    ON CONFLICT (collection_id, group_key) DO NOTHING;

    SELECT * INTO v_group FROM tc.collection_file_groups
    WHERE collection_id = p_collection_id AND group_key = p_group_key;

    IF v_group.version <> p_expected_version THEN
        RAISE EXCEPTION '%', json_build_object(
            'error', 'VersionConflict', 'currentVersion', v_group.version
        )::text USING ERRCODE = 'PT409';
    END IF;

    SELECT COALESCE(array_agg(f.path), '{}') INTO v_changed
    FROM jsonb_to_recordset(p_files) AS f(path text, sha256 text, size bigint)
    WHERE NOT EXISTS (
        SELECT 1 FROM tc.collection_group_files gf
        WHERE gf.group_id = v_group.id
          AND gf.path = f.path
          AND gf.sha256 = f.sha256
          AND gf.size_bytes = f.size
    );

    SELECT * INTO v_existing FROM tc.collection_file_transactions
    WHERE collection_id = p_collection_id AND group_key = p_group_key
      AND started_by = v_user_id AND status = 'open';

    IF FOUND THEN
        UPDATE tc.collection_file_transactions
        SET expected_version = p_expected_version,
            proposed_files = p_files,
            changed_paths = v_changed,
            expires_at = now() + INTERVAL '48 hours'
        WHERE id = v_existing.id
        RETURNING id INTO v_tx_id;
    ELSE
        INSERT INTO tc.collection_file_transactions (
            collection_id, group_key, started_by, expected_version, proposed_files, changed_paths
        )
        VALUES (
            p_collection_id, p_group_key, v_user_id, p_expected_version, p_files, v_changed
        )
        RETURNING id INTO v_tx_id;
    END IF;

    RETURN jsonb_build_object('transactionId', v_tx_id, 'changedPaths', to_jsonb(v_changed));
END;
$$;

COMMENT ON FUNCTION tc.collection_files_start_tx(p_collection_id uuid, p_group_key text, p_expected_version bigint, p_files jsonb) IS 'Internal to the collection-files-start edge function. Optimistic-version gate (PT409 VersionConflict) + manifest diff + transaction open/resume.';

CREATE OR REPLACE FUNCTION tc.create_collection(p_id uuid, p_name text) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.create_collection(p_id uuid, p_name text) IS 'CONTRACTS.md: create_collection — creates collection + caller as sole claimed admin.';

CREATE OR REPLACE FUNCTION tc.current_user_email() RETURNS text
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
  SELECT lower(auth.jwt() ->> 'email')
$$;

COMMENT ON FUNCTION tc.current_user_email() IS 'Returns the caller''s email from the JWT, lowercased for case-insensitive comparison.';

CREATE OR REPLACE FUNCTION tc.current_user_id() RETURNS text
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
  SELECT auth.jwt() ->> 'sub'
$$;

COMMENT ON FUNCTION tc.current_user_id() IS 'Returns the caller''s user id from the JWT sub claim as TEXT. Firebase UIDs (~28 chars) and local-GoTrue UUIDs both fit in TEXT.';

CREATE OR REPLACE FUNCTION tc.delete_book(p_book_id uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.delete_book(p_book_id uuid) IS 'CONTRACTS.md: delete_book — requires caller holds the lock; sets deleted_at tombstone; emits Deleted (type=8). Lock is released on deletion.';

CREATE OR REPLACE FUNCTION tc.download_start_check(p_collection_id uuid) RETURNS void
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    AS $$
BEGIN
    IF tc.current_user_id() IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;
    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION '%', '{"error":"not_a_member"}' USING ERRCODE = 'PT403';
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.download_start_check(p_collection_id uuid) IS 'Internal to the download-start edge function: membership gate only. PT403 not_a_member if the caller is not a member of the collection.';

CREATE OR REPLACE FUNCTION tc.events_realtime_broadcast() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    PERFORM pg_notify(
        'realtime:' || NEW.collection_id::text,
        json_build_object(
            'eventId',     NEW.id,
            'type',        NEW.type,
            'bookId',      NEW.book_id,
            'versionSeq',  NEW.book_version_seq,
            'byUserName',  NEW.by_user_name,
            'byEmail',     NEW.by_email,
            'lock',        NEW.lock_info,
            'name',        NEW.book_name,
            'groupKey',    NEW.group_key
        )::text
    );
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.events_realtime_broadcast() IS 'Broadcasts a realtime notification on channel realtime:{collection_id} for every new event row. The message shape matches CONTRACTS.md §Realtime.';

CREATE OR REPLACE FUNCTION tc.force_unlock(p_book_id uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.force_unlock(p_book_id uuid) IS 'CONTRACTS.md: force_unlock — admin-only; releases any lock; emits ForcedUnlock (type=5).';

CREATE OR REPLACE FUNCTION tc.get_book_manifest(p_book_id uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
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

COMMENT ON FUNCTION tc.get_book_manifest(p_book_id uuid) IS 'CONTRACTS.md v1.2: get_book_manifest — per-file current manifest for one book (path, sha256, size, s3VersionId), used by Receive to download pinned versions. Enforces the never-committed-book invisibility rule. v1.2 (20260707000006): also reports lockedBy/lockedByEmail/lockedByName so Receive can show "still checked out to X" without a second round trip.';

CREATE OR REPLACE FUNCTION tc.get_changes(p_collection_id uuid, p_since_event_id bigint) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
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

COMMENT ON FUNCTION tc.get_changes(p_collection_id uuid, p_since_event_id bigint) IS 'CONTRACTS.md: get_changes — events + touched book rows since the cursor. Used for polling (60s fallback) and realtime reconnect catch-up. v1.2 (20260707000006): touched book rows also carry locked_by_email/locked_by_name for display. v1.5 (20260711000003): touched book rows also carry locked_seat. v1.6 (20260713000001): event rows also carry by_display_name (the current durable display name of by_user_id).';

CREATE OR REPLACE FUNCTION tc.get_collection_file_manifest(p_collection_id uuid, p_group_key text) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    AS $$
DECLARE
    v_group_id bigint;
    v_version  bigint;
    v_files    jsonb;
BEGIN
    IF NOT tc.is_member(p_collection_id) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
    END IF;

    SELECT id, version INTO v_group_id, v_version
    FROM tc.collection_file_groups
    WHERE collection_id = p_collection_id AND group_key = p_group_key;

    IF NOT FOUND THEN
        RETURN jsonb_build_object(
            'groupKey', p_group_key,
            'version',  0,
            'files',    '[]'::jsonb
        );
    END IF;

    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'path',        gf.path,
                'sha256',      gf.sha256,
                'size',        gf.size_bytes,
                's3VersionId', gf.s3_version_id
            )
            ORDER BY gf.path
        ),
        '[]'::jsonb
    )
    INTO v_files
    FROM tc.collection_group_files gf
    WHERE gf.group_id = v_group_id;

    RETURN jsonb_build_object(
        'groupKey', p_group_key,
        'version',  v_version,
        'files',    v_files
    );
END;
$$;

COMMENT ON FUNCTION tc.get_collection_file_manifest(p_collection_id uuid, p_group_key text) IS 'E9: per-file current manifest for one collection-file group (path, sha256, size, s3VersionId) from tc.collection_group_files, used by the download path to fetch only changed files pinned to their committed s3_version_id. Mirrors get_book_manifest; a never-written group returns version 0 / empty files.';

CREATE OR REPLACE FUNCTION tc.get_collection_state(p_collection_id uuid, p_since_event_id bigint DEFAULT NULL::bigint) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
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

COMMENT ON FUNCTION tc.get_collection_state(p_collection_id uuid, p_since_event_id bigint) IS 'CONTRACTS.md: get_collection_state — full/delta snapshot of book rows + group versions + max_event_id. since_event_id = NULL → full; otherwise delta. v1.2 (20260707000006): book rows also carry locked_by_email/locked_by_name for display. v1.5 (20260711000003): book rows also carry locked_seat.';

CREATE OR REPLACE FUNCTION tc.is_admin(p_collection_id uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
    SELECT EXISTS (
        SELECT 1
        FROM tc.members m
        WHERE m.collection_id = p_collection_id
          AND m.user_id        = tc.current_user_id()
          AND m.role           = 'admin'
    )
$$;

COMMENT ON FUNCTION tc.is_admin(p_collection_id uuid) IS 'Returns TRUE when the caller (JWT sub) is a claimed admin of the given collection.';

CREATE OR REPLACE FUNCTION tc.is_client_version_supported(p_client_version text) RETURNS boolean
    LANGUAGE plpgsql IMMUTABLE
    AS $_$
DECLARE
    v_min   int[];
    v_cur   int[];
    v_floor text := tc.min_supported_client_version();
BEGIN
    IF v_floor IS NULL OR v_floor = '0.0.0' THEN
        RETURN true; -- floor disabled
    END IF;
    IF p_client_version IS NULL OR p_client_version !~ '^[0-9]+(\.[0-9]+)*$' THEN
        RETURN false; -- unparsable version, floor is enabled ⇒ reject
    END IF;

    SELECT array_agg(x::int) INTO v_min FROM unnest(string_to_array(v_floor, '.')) x;
    SELECT array_agg(x::int) INTO v_cur FROM unnest(string_to_array(p_client_version, '.')) x;

    FOR i IN 1 .. greatest(array_length(v_min, 1), array_length(v_cur, 1)) LOOP
        IF COALESCE(v_cur[i], 0) > COALESCE(v_min[i], 0) THEN
            RETURN true;
        ELSIF COALESCE(v_cur[i], 0) < COALESCE(v_min[i], 0) THEN
            RETURN false;
        END IF;
    END LOOP;
    RETURN true; -- equal
END;
$_$;

COMMENT ON FUNCTION tc.is_client_version_supported(p_client_version text) IS 'Dotted-integer version compare against tc.min_supported_client_version(). Used to raise ClientOutOfDate (426) in checkin_start_tx.';

CREATE OR REPLACE FUNCTION tc.is_member(p_collection_id uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
    SELECT EXISTS (
        SELECT 1
        FROM tc.members m
        WHERE m.collection_id = p_collection_id
          AND m.user_id        = tc.current_user_id()
    )
$$;

COMMENT ON FUNCTION tc.is_member(p_collection_id uuid) IS 'Returns TRUE when the caller (JWT sub) is a claimed member of the given collection.';

CREATE OR REPLACE FUNCTION tc.jwt_email_verified() RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
  SELECT
    CASE
      -- Firebase-style: explicit boolean claim (may arrive as 'true'::text or true::bool)
      WHEN (auth.jwt() ->> 'email_verified') IS NOT NULL THEN
        (auth.jwt() ->> 'email_verified')::boolean
      -- Local GoTrue (dev): no email_verified claim; role = 'authenticated' implies confirmed
      WHEN (auth.jwt() ->> 'role') = 'authenticated' THEN
        TRUE
      ELSE
        FALSE
    END
$$;

COMMENT ON FUNCTION tc.jwt_email_verified() IS 'The ONLY place that decides whether the caller''s email is verified. Handles both a Firebase-style email_verified JWT claim and local-GoTrue auto-confirmed users (dev stack). All callers must use this function, never the claim directly.';

CREATE OR REPLACE FUNCTION tc.list_stale_upload_garbage() RETURNS TABLE(transaction_kind text, transaction_id uuid, s3_key text, referenced_version_id text)
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
    -- Book check-in uploads: tc/{collectionId}/books/{bookInstanceId}/{path}
    SELECT
        'book'::text,
        t.id,
        'tc/' || t.collection_id::text || '/books/' || b.instance_id::text || '/' || p.path,
        (SELECT vf.s3_version_id
           FROM tc.version_files vf
          WHERE vf.book_id = t.book_id AND vf.path = p.path)
    FROM tc.checkin_transactions t
    JOIN tc.books b ON b.id = t.book_id
    CROSS JOIN LATERAL unnest(t.changed_paths) AS p(path)
    WHERE (t.status = 'aborted' OR (t.status <> 'finished' AND t.expires_at < now()))
      AND NOT EXISTS (
          SELECT 1 FROM tc.checkin_transactions live
          WHERE live.book_id = t.book_id
            AND live.status = 'open'
            AND live.expires_at >= now()
            AND p.path = ANY(live.changed_paths)
      )

    UNION ALL

    -- Collection-file group uploads: tc/{collectionId}/collectionFiles/{group}/{path}
    SELECT
        'collection_file'::text,
        t.id,
        'tc/' || t.collection_id::text || '/collectionFiles/' || t.group_key || '/' || p.path,
        (SELECT gf.s3_version_id
           FROM tc.collection_group_files gf
           JOIN tc.collection_file_groups g ON g.id = gf.group_id
          WHERE g.collection_id = t.collection_id
            AND g.group_key = t.group_key
            AND gf.path = p.path)
    FROM tc.collection_file_transactions t
    CROSS JOIN LATERAL unnest(t.changed_paths) AS p(path)
    WHERE (t.status = 'aborted' OR (t.status <> 'finished' AND t.expires_at < now()))
      AND NOT EXISTS (
          SELECT 1 FROM tc.collection_file_transactions live
          WHERE live.collection_id = t.collection_id
            AND live.group_key = t.group_key
            AND live.status = 'open'
            AND live.expires_at >= now()
            AND p.path = ANY(live.changed_paths)
      );
$$;

COMMENT ON FUNCTION tc.list_stale_upload_garbage() IS 'Worklist for the sweep-stale-uploads edge function: per-file S3 keys touched by DEAD (aborted/expired) check-in transactions, with the currently-referenced s3_version_id as the delete-newer-than watermark (NULL = nothing references the key). Excludes paths a live transaction is still uploading. service-role only. See GOING-LIVE.md "Orphaned-upload sweep".';

CREATE OR REPLACE FUNCTION tc.log_event(p_collection_id uuid, p_book_id uuid DEFAULT NULL::uuid, p_type integer DEFAULT NULL::integer, p_message text DEFAULT NULL::text, p_book_name text DEFAULT NULL::text, p_bloom_version text DEFAULT NULL::text) RETURNS bigint
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.log_event(p_collection_id uuid, p_book_id uuid, p_type integer, p_message text, p_book_name text, p_bloom_version text) IS 'CONTRACTS.md: log_event — client-originated history entries (e.g. WorkPreservedLocally incident events). Returns the new event id.';

CREATE OR REPLACE FUNCTION tc.members_add(p_collection_id uuid, p_email text, p_role tc.member_role DEFAULT 'member'::tc.member_role) RETURNS bigint
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.members_add(p_collection_id uuid, p_email text, p_role tc.member_role) IS 'CONTRACTS.md: members add — admin-only; adds an approved-account email. Idempotent (on conflict do nothing).';

CREATE OR REPLACE FUNCTION tc.members_last_admin_guard() RETURNS trigger
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

COMMENT ON FUNCTION tc.members_last_admin_guard() IS 'Trigger function: prevents deleting or demoting the last admin of a collection. Locks the parent collection row (FOR UPDATE) before counting so concurrent admin removals/demotions serialize instead of racing to zero admins (fixed 20260717000001).';

CREATE OR REPLACE FUNCTION tc.members_list(p_collection_id uuid) RETURNS TABLE(id bigint, email text, display_name text, role tc.member_role, user_id text, added_by text, added_at timestamp with time zone, claimed_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
    SELECT m.id, m.email, m.display_name, m.role, m.user_id, m.added_by, m.added_at,
           m.claimed_at
    FROM tc.members m
    WHERE m.collection_id = p_collection_id
      AND tc.is_member(p_collection_id)   -- membership gate
    ORDER BY m.email
$$;

COMMENT ON FUNCTION tc.members_list(p_collection_id uuid) IS 'CONTRACTS.md: members list — returns approved-accounts for the collection. Any member may call this. v1.6 (20260713000001): rows also carry display_name.';

CREATE OR REPLACE FUNCTION tc.members_remove(p_collection_id uuid, p_member_id bigint) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.members_remove(p_collection_id uuid, p_member_id bigint) IS 'CONTRACTS.md: members remove — admin-only; force-unlocks any books held by the removed member (emits ForcedUnlock events). Last-admin guard trigger fires on DELETE.';

CREATE OR REPLACE FUNCTION tc.members_set_display_name(p_collection_id uuid, p_member_id bigint, p_display_name text) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.members_set_display_name(p_collection_id uuid, p_member_id bigint, p_display_name text) IS 'CONTRACTS.md v1.6: members set_display_name — admin may set any member''s display name; a claimed member may set their own. Trims; blank clears to NULL (display falls back to email). Max 100 chars.';

CREATE OR REPLACE FUNCTION tc.members_set_role(p_collection_id uuid, p_member_id bigint, p_new_role tc.member_role) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.members_set_role(p_collection_id uuid, p_member_id bigint, p_new_role tc.member_role) IS 'CONTRACTS.md: members set_role — admin-only; last-admin guard trigger fires on demotion.';

CREATE OR REPLACE FUNCTION tc.min_supported_client_version() RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
    SELECT '0.0.0'::text
$$;

COMMENT ON FUNCTION tc.min_supported_client_version() IS 'Floor Bloom client version for cloud check-in operations. Bump via CREATE OR REPLACE FUNCTION when a breaking client-side protocol change ships. ClientOutOfDate (426) is raised when the caller''s clientVersion sorts below this.';

CREATE OR REPLACE FUNCTION tc.my_collections() RETURNS TABLE(id uuid, name text, created_at timestamp with time zone, created_by text, my_role tc.member_role, is_claimed boolean)
    LANGUAGE sql STABLE SECURITY DEFINER
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

COMMENT ON FUNCTION tc.my_collections() IS 'CONTRACTS.md: my_collections — returns collections where the caller''s email is approved (claimed or not).';

CREATE OR REPLACE FUNCTION tc.nfc_normalize_book_name() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.name := normalize(NEW.name, NFC);
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.nfc_normalize_book_name() IS 'Trigger: NFC-normalize the book name before every insert or update.';

CREATE OR REPLACE FUNCTION tc.nfc_normalize_path() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.path := normalize(NEW.path, NFC);
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.nfc_normalize_path() IS 'Trigger: NFC-normalize the file path before every insert or update.';

CREATE OR REPLACE FUNCTION tc.reap_expired_checkin_transactions() RETURNS integer
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_book_id uuid;
    v_count   integer := 0;
    v_updated integer;
BEGIN
    FOR v_book_id IN
        SELECT DISTINCT book_id FROM tc.checkin_transactions
        WHERE status = 'open' AND expires_at < now()
    LOOP
        PERFORM tc._checkin_reap_book(v_book_id);
        v_count := v_count + 1;
    END LOOP;

    UPDATE tc.collection_file_transactions
    SET status = 'expired'
    WHERE status = 'open' AND expires_at < now();
    GET DIAGNOSTICS v_updated = ROW_COUNT;
    v_count := v_count + v_updated;

    RETURN v_count;
END;
$$;

COMMENT ON FUNCTION tc.reap_expired_checkin_transactions() IS 'Global expiry sweep for both checkin_transactions (via _checkin_reap_book) and collection_file_transactions. Returns the total number of items reaped across both sweeps. Called opportunistically at the top of checkin_start_tx/checkin_abort_tx/collection_files_start_tx; also safe to run from a scheduled job if one is ever wired up (no pg_cron dependency here).';

CREATE OR REPLACE FUNCTION tc.rename_check(p_book_id uuid, p_new_name text) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
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

COMMENT ON FUNCTION tc.rename_check(p_book_id uuid, p_new_name text) IS 'CONTRACTS.md: rename_check — advisory live-name uniqueness pre-check. Returns {available, conflict}. Does NOT perform the rename.';

CREATE OR REPLACE FUNCTION tc.resolve_member_display(p_collection_id uuid, p_user_id text, OUT email text, OUT display_name text) RETURNS record
    LANGUAGE sql STABLE SECURITY DEFINER
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

COMMENT ON FUNCTION tc.resolve_member_display(p_collection_id uuid, p_user_id text, OUT email text, OUT display_name text) IS 'Best-effort resolution of a locked_by/created_by user_id to a display email (from tc.members, authoritative) and display name. v1.6 (20260713000001): prefers the durable tc.members.display_name; falls back to the most recent tc.events.by_user_name JWT-claim capture (often NULL in dev-auth mode). Returns an all-NULL row (never an error) when p_user_id is NULL or unknown.';

CREATE OR REPLACE FUNCTION tc.support_set_admin(p_collection_id uuid, p_email text) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.support_set_admin(p_collection_id uuid, p_email text) IS 'Admin-recovery tool: grants admin on a collection to an email, for the Bloom team to run with the SERVICE-ROLE key when a collection has lost its only reachable admin. NOT granted to authenticated; bypasses is_admin by design. Idempotent (promote existing member / insert new admin approval). See GOING-LIVE.md "Admin recovery" runbook.';

CREATE OR REPLACE FUNCTION tc.undelete_book(p_book_id uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.undelete_book(p_book_id uuid) IS 'CONTRACTS.md: undelete_book — admin-only; clears tombstone; enforces live-name uniqueness (raises name_conflict if another live book uses the same name).';

CREATE OR REPLACE FUNCTION tc.unlock_book(p_book_id uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
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

COMMENT ON FUNCTION tc.unlock_book(p_book_id uuid) IS 'CONTRACTS.md: unlock_book — release own lock (undo checkout, no content change). Only the lock holder may call this; use force_unlock for admin override.';


-- ==== 03_tables.sql ====

-- Team Collections cloud: tables, constraints, indexes, and triggers.
CREATE TABLE IF NOT EXISTS tc.books (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    collection_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    name text NOT NULL,
    current_version_id uuid,
    current_version_seq bigint,
    current_checksum text,
    locked_by text,
    locked_by_machine text,
    locked_at timestamp with time zone,
    deleted_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by text NOT NULL,
    locked_seat text
);

COMMENT ON TABLE tc.books IS 'Authoritative book state per collection. Lock columns, soft tombstone, current-version denormalization. All state transitions go through RPCs/edge functions; no direct writes via PostgREST.';

COMMENT ON COLUMN tc.books.instance_id IS 'Client-generated UUID stable across renames. Used as the S3 prefix key (tc/{cid}/books/{instance_id}/).';

COMMENT ON COLUMN tc.books.name IS 'NFC-normalized on write by the nfc_normalize_book_name trigger. Live-name uniqueness (lower(name)) is enforced by a partial unique index.';

COMMENT ON COLUMN tc.books.deleted_at IS 'Soft tombstone: non-NULL = deleted. Tombstoned names are reusable (excluded from the live-name uniqueness index).';

COMMENT ON COLUMN tc.books.locked_seat IS 'Which local copy of the collection ("seat") holds the lock: a client-computed stable hash of the local collection folder path (never the raw path). NULL = unknown (legacy lock, or one acquired by checkin_start_tx''s take-if-free path); a NULL seat can never be taken over (fail-safe).';

CREATE TABLE IF NOT EXISTS tc.checkin_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    collection_id uuid NOT NULL,
    book_id uuid NOT NULL,
    started_by text NOT NULL,
    proposed_name text NOT NULL,
    base_version_id uuid,
    changed_paths text[] DEFAULT '{}'::text[] NOT NULL,
    client_version text,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone DEFAULT (now() + '48:00:00'::interval) NOT NULL,
    finished_at timestamp with time zone,
    aborted_at timestamp with time zone,
    status text DEFAULT 'open'::text NOT NULL,
    proposed_files jsonb DEFAULT '[]'::jsonb NOT NULL,
    checksum text,
    result_version_id uuid,
    result_seq bigint,
    CONSTRAINT checkin_transactions_status_check CHECK ((status = ANY (ARRAY['open'::text, 'finished'::text, 'aborted'::text, 'expired'::text])))
);

COMMENT ON TABLE tc.checkin_transactions IS 'Open check-in transactions (checkin-start → checkin-finish). expires_at = 48h; expired rows are reaped by a scheduled edge function. An open transaction for a new book means the book row has no current_version_id and is invisible to teammates until checkin-finish commits it.';

COMMENT ON COLUMN tc.checkin_transactions.proposed_files IS 'Full proposed manifest [{path,sha256,size}] captured at checkin-start; checkin-finish reconstructs the committed manifest from this + changed_paths + the S3 version-ids captured after upload verification.';

COMMENT ON COLUMN tc.checkin_transactions.checksum IS 'SHA-256 checksum of the full proposed manifest, supplied at checkin-start and persisted as tc.versions.checksum / tc.books.current_checksum on finish.';

COMMENT ON COLUMN tc.checkin_transactions.result_version_id IS 'Set on successful checkin-finish; makes a repeated checkin-finish call for an already-finished transaction idempotent (returns the same result).';

CREATE TABLE IF NOT EXISTS tc.collection_file_groups (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    group_key text NOT NULL,
    version bigint DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_by text NOT NULL,
    CONSTRAINT collection_file_groups_group_key_check CHECK ((group_key = ANY (ARRAY['other'::text, 'allowed-words'::text, 'sample-texts'::text])))
);

COMMENT ON TABLE tc.collection_file_groups IS 'Versioned collection-level file groups. version is bumped atomically by collection-files-finish edge function; 409 VersionConflict if expectedVersion != version.';

ALTER TABLE tc.collection_file_groups ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.collection_file_groups_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.collection_file_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    collection_id uuid NOT NULL,
    group_key text NOT NULL,
    started_by text NOT NULL,
    expected_version bigint NOT NULL,
    proposed_files jsonb DEFAULT '[]'::jsonb NOT NULL,
    changed_paths text[] DEFAULT '{}'::text[] NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone DEFAULT (now() + '48:00:00'::interval) NOT NULL,
    finished_at timestamp with time zone,
    aborted_at timestamp with time zone,
    status text DEFAULT 'open'::text NOT NULL,
    result_version bigint,
    CONSTRAINT collection_file_transactions_group_key_check CHECK ((group_key = ANY (ARRAY['other'::text, 'allowed-words'::text, 'sample-texts'::text]))),
    CONSTRAINT collection_file_transactions_status_check CHECK ((status = ANY (ARRAY['open'::text, 'finished'::text, 'aborted'::text, 'expired'::text])))
);

COMMENT ON TABLE tc.collection_file_transactions IS 'Open collection-files-start -> collection-files-finish two-phase commits. Mirrors tc.checkin_transactions but scoped to (collection_id, group_key) instead of a book.';

CREATE TABLE IF NOT EXISTS tc.collection_group_files (
    id bigint NOT NULL,
    group_id bigint NOT NULL,
    path text NOT NULL,
    sha256 text NOT NULL,
    size_bytes bigint NOT NULL,
    s3_version_id text NOT NULL
);

COMMENT ON TABLE tc.collection_group_files IS 'Current file manifest for each collection_file_group. Rows for the old version are replaced atomically by collection-files-finish.';

ALTER TABLE tc.collection_group_files ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.collection_group_files_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.collections (
    id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by text NOT NULL
);

COMMENT ON TABLE tc.collections IS 'One row per cloud Team Collection. id = Bloom CollectionId GUID.';

COMMENT ON COLUMN tc.collections.id IS 'The collection UUID — same value as in TeamCollectionLink.txt (cloud://sil.bloom/collection/<id>).';

COMMENT ON COLUMN tc.collections.created_by IS 'TEXT user id (Firebase UID or GoTrue UUID) of the creator.';

CREATE TABLE IF NOT EXISTS tc.color_palette_entries (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    palette text NOT NULL,
    color text NOT NULL,
    added_at timestamp with time zone DEFAULT now() NOT NULL,
    added_by text NOT NULL
);

COMMENT ON TABLE tc.color_palette_entries IS 'Color palette entries per collection. Merge is union-only: insert ... on conflict do nothing. No rows are ever deleted.';

ALTER TABLE tc.color_palette_entries ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.color_palette_entries_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.events (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    book_id uuid,
    type integer NOT NULL,
    by_user_id text NOT NULL,
    by_user_name text,
    by_email text,
    book_version_seq bigint,
    lock_info jsonb,
    book_name text,
    group_key text,
    message text,
    bloom_version text,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT events_type_check CHECK ((type = ANY (ARRAY[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 100])))
);

COMMENT ON TABLE tc.events IS 'History log, realtime broadcast source, and polling cursor. type values mirror C# BookHistoryEventType (HistoryEvent.cs): 0=CheckOut, 1=CheckIn, 2=Created, 3=Renamed, 4=Uploaded(legacy), 5=ForcedUnlock, 6=ImportSpreadsheet, 7=SyncProblem(legacy), 8=Deleted, 9=Moved. Cloud-TC incident extensions start at 100 to avoid colliding with future C# additions: 100=WorkPreservedLocally.';

ALTER TABLE tc.events ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.members (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    email text NOT NULL,
    role tc.member_role DEFAULT 'member'::tc.member_role NOT NULL,
    user_id text,
    added_by text NOT NULL,
    added_at timestamp with time zone DEFAULT now() NOT NULL,
    claimed_at timestamp with time zone,
    display_name text
);

COMMENT ON TABLE tc.members IS 'Approved-accounts table. Unclaimed rows (user_id IS NULL) are pending until the account holder signs in and calls claim_memberships(). email is stored lowercase + NFC-normalised.';

COMMENT ON COLUMN tc.members.user_id IS 'NULL until the account holder claims the seat. TEXT covers both Firebase UIDs and local-GoTrue UUIDs.';

COMMENT ON COLUMN tc.members.display_name IS 'Human-readable name shown in place of the email wherever the member is displayed (checkout status, history, sharing panel). NULL = none set; display falls back to email. Set via tc.members_set_display_name (admin, or the claimed member themselves).';

ALTER TABLE tc.members ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.members_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.version_files (
    id bigint NOT NULL,
    book_id uuid NOT NULL,
    version_id uuid NOT NULL,
    path text NOT NULL,
    sha256 text NOT NULL,
    size_bytes bigint NOT NULL,
    s3_version_id text NOT NULL
);

COMMENT ON TABLE tc.version_files IS 'Current manifest for each book: path → sha256, size, s3_version_id. Superseded rows are pruned at checkin-finish. Reads always use (path, s3_version_id).';

ALTER TABLE tc.version_files ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.version_files_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.versions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    book_id uuid NOT NULL,
    collection_id uuid NOT NULL,
    seq bigint NOT NULL,
    checksum text NOT NULL,
    comment text,
    created_by text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    client_version text
);

COMMENT ON TABLE tc.versions IS 'One metadata row per successful check-in (checkin-finish edge function). seq is monotonically increasing per book.';

ALTER TABLE ONLY tc.books
    ADD CONSTRAINT books_collection_instance_uq UNIQUE (collection_id, instance_id);

ALTER TABLE ONLY tc.books
    ADD CONSTRAINT books_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collection_file_groups
    ADD CONSTRAINT collection_file_groups_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collection_file_groups
    ADD CONSTRAINT collection_file_groups_uq UNIQUE (collection_id, group_key);

ALTER TABLE ONLY tc.collection_file_transactions
    ADD CONSTRAINT collection_file_transactions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collection_group_files
    ADD CONSTRAINT collection_group_files_group_path_uq UNIQUE (group_id, path);

ALTER TABLE ONLY tc.collection_group_files
    ADD CONSTRAINT collection_group_files_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collections
    ADD CONSTRAINT collections_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.color_palette_entries
    ADD CONSTRAINT color_palette_entries_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.color_palette_entries
    ADD CONSTRAINT color_palette_entries_uq UNIQUE (collection_id, palette, color);

ALTER TABLE ONLY tc.events
    ADD CONSTRAINT events_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_claimed_user_uq UNIQUE (collection_id, user_id);

COMMENT ON CONSTRAINT members_claimed_user_uq ON tc.members IS 'A claimed user appears at most once per collection. NULL user_id rows (pending invitations) do not collide (default NULLS DISTINCT; fixed 20260713000002 — the original NULLS NOT DISTINCT allowed only one pending invitation per collection).';

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_collection_email_uq UNIQUE (collection_id, email);

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_book_path_uq UNIQUE (book_id, path);

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.versions
    ADD CONSTRAINT versions_book_seq_uq UNIQUE (book_id, seq);

ALTER TABLE ONLY tc.versions
    ADD CONSTRAINT versions_pkey PRIMARY KEY (id);

CREATE INDEX books_collection_id_idx ON tc.books USING btree (collection_id);

CREATE INDEX books_instance_id_idx ON tc.books USING btree (instance_id);

CREATE UNIQUE INDEX books_live_name_uq ON tc.books USING btree (collection_id, lower(NORMALIZE(name, NFC))) WHERE (deleted_at IS NULL);

CREATE INDEX books_locked_by_idx ON tc.books USING btree (locked_by) WHERE (locked_by IS NOT NULL);

CREATE INDEX checkin_transactions_book_id_idx ON tc.checkin_transactions USING btree (book_id);

CREATE INDEX checkin_transactions_expires_at_idx ON tc.checkin_transactions USING btree (expires_at) WHERE (status = 'open'::text);

CREATE INDEX checkin_transactions_started_by_idx ON tc.checkin_transactions USING btree (started_by);

CREATE INDEX collection_file_groups_collection_id_idx ON tc.collection_file_groups USING btree (collection_id);

CREATE INDEX collection_file_transactions_expires_at_idx ON tc.collection_file_transactions USING btree (expires_at) WHERE (status = 'open'::text);

CREATE INDEX collection_file_transactions_scope_idx ON tc.collection_file_transactions USING btree (collection_id, group_key, started_by) WHERE (status = 'open'::text);

CREATE INDEX collection_group_files_group_id_idx ON tc.collection_group_files USING btree (group_id);

CREATE INDEX color_palette_entries_collection_id_idx ON tc.color_palette_entries USING btree (collection_id);

CREATE INDEX events_book_id_idx ON tc.events USING btree (book_id) WHERE (book_id IS NOT NULL);

CREATE INDEX events_collection_cursor_idx ON tc.events USING btree (collection_id, id);

CREATE INDEX events_collection_id_idx ON tc.events USING btree (collection_id);

CREATE INDEX members_collection_id_idx ON tc.members USING btree (collection_id);

CREATE INDEX members_email_idx ON tc.members USING btree (lower(email));

CREATE INDEX members_user_id_idx ON tc.members USING btree (user_id) WHERE (user_id IS NOT NULL);

CREATE INDEX version_files_book_id_idx ON tc.version_files USING btree (book_id);

CREATE INDEX version_files_version_id_idx ON tc.version_files USING btree (version_id);

CREATE INDEX versions_book_id_idx ON tc.versions USING btree (book_id);

CREATE INDEX versions_collection_id_idx ON tc.versions USING btree (collection_id);

CREATE OR REPLACE TRIGGER books_clear_seat_on_unlock BEFORE UPDATE ON tc.books FOR EACH ROW EXECUTE FUNCTION tc._clear_seat_on_unlock();

CREATE OR REPLACE TRIGGER books_nfc_normalize_name_tg BEFORE INSERT OR UPDATE OF name ON tc.books FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_book_name();

CREATE OR REPLACE TRIGGER collection_group_files_nfc_normalize_path_tg BEFORE INSERT OR UPDATE OF path ON tc.collection_group_files FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_path();

CREATE OR REPLACE TRIGGER events_realtime_broadcast_tg AFTER INSERT ON tc.events FOR EACH ROW EXECUTE FUNCTION tc.events_realtime_broadcast();

CREATE OR REPLACE TRIGGER members_last_admin_guard_tg BEFORE DELETE OR UPDATE ON tc.members FOR EACH ROW EXECUTE FUNCTION tc.members_last_admin_guard();

CREATE OR REPLACE TRIGGER version_files_nfc_normalize_path_tg BEFORE INSERT OR UPDATE OF path ON tc.version_files FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_path();

ALTER TABLE ONLY tc.books
    ADD CONSTRAINT books_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_base_version_id_fkey FOREIGN KEY (base_version_id) REFERENCES tc.versions(id);

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_result_version_id_fkey FOREIGN KEY (result_version_id) REFERENCES tc.versions(id);

ALTER TABLE ONLY tc.collection_file_groups
    ADD CONSTRAINT collection_file_groups_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.collection_file_transactions
    ADD CONSTRAINT collection_file_transactions_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.collection_group_files
    ADD CONSTRAINT collection_group_files_group_id_fkey FOREIGN KEY (group_id) REFERENCES tc.collection_file_groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.color_palette_entries
    ADD CONSTRAINT color_palette_entries_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.events
    ADD CONSTRAINT events_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE SET NULL;

ALTER TABLE ONLY tc.events
    ADD CONSTRAINT events_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_version_id_fkey FOREIGN KEY (version_id) REFERENCES tc.versions(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.versions
    ADD CONSTRAINT versions_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE CASCADE;


-- ==== 04_security.sql ====

-- Team Collections cloud: row-level security (enable + policies) and grants.
-- Writes go through SECURITY DEFINER RPCs, so most tables expose only SELECT to
-- `authenticated`; anon gets nothing.
ALTER TABLE tc.books ENABLE ROW LEVEL SECURITY;

CREATE POLICY books_select ON tc.books FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.checkin_transactions ENABLE ROW LEVEL SECURITY;

CREATE POLICY checkin_transactions_select ON tc.checkin_transactions FOR SELECT USING ((started_by = tc.current_user_id()));

ALTER TABLE tc.collection_file_groups ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_file_groups_select ON tc.collection_file_groups FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.collection_file_transactions ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_file_transactions_select ON tc.collection_file_transactions FOR SELECT USING ((started_by = tc.current_user_id()));

ALTER TABLE tc.collection_group_files ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_group_files_select ON tc.collection_group_files FOR SELECT USING ((EXISTS ( SELECT 1
   FROM tc.collection_file_groups fg
  WHERE ((fg.id = collection_group_files.group_id) AND tc.is_member(fg.collection_id)))));

ALTER TABLE tc.collections ENABLE ROW LEVEL SECURITY;

CREATE POLICY collections_select ON tc.collections FOR SELECT USING (tc.is_member(id));

ALTER TABLE tc.color_palette_entries ENABLE ROW LEVEL SECURITY;

CREATE POLICY color_palette_entries_insert ON tc.color_palette_entries FOR INSERT WITH CHECK (tc.is_member(collection_id));

CREATE POLICY color_palette_entries_select ON tc.color_palette_entries FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.events ENABLE ROW LEVEL SECURITY;

CREATE POLICY events_insert ON tc.events FOR INSERT WITH CHECK ((tc.is_member(collection_id) AND (by_user_id = tc.current_user_id())));

CREATE POLICY events_select ON tc.events FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.members ENABLE ROW LEVEL SECURITY;

CREATE POLICY members_delete ON tc.members FOR DELETE USING (tc.is_admin(collection_id));

CREATE POLICY members_insert ON tc.members FOR INSERT WITH CHECK (tc.is_admin(collection_id));

CREATE POLICY members_select ON tc.members FOR SELECT USING (tc.is_member(collection_id));

CREATE POLICY members_update ON tc.members FOR UPDATE USING (tc.is_admin(collection_id)) WITH CHECK (tc.is_admin(collection_id));

ALTER TABLE tc.version_files ENABLE ROW LEVEL SECURITY;

CREATE POLICY version_files_select ON tc.version_files FOR SELECT USING ((EXISTS ( SELECT 1
   FROM tc.books b
  WHERE ((b.id = version_files.book_id) AND tc.is_member(b.collection_id)))));

ALTER TABLE tc.versions ENABLE ROW LEVEL SECURITY;

CREATE POLICY versions_select ON tc.versions FOR SELECT USING (tc.is_member(collection_id));

GRANT USAGE ON SCHEMA tc TO authenticated;

GRANT ALL ON FUNCTION tc.add_palette_colors(p_collection_id uuid, p_palette text, p_colors text[]) TO authenticated;

GRANT ALL ON FUNCTION tc.checkin_abort_tx(p_transaction_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.checkin_finish_tx(p_transaction_id uuid, p_comment text, p_keep_checked_out boolean, p_captured jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.checkin_start_tx(p_collection_id uuid, p_book_id uuid, p_book_instance_id uuid, p_proposed_name text, p_base_version_id uuid, p_checksum text, p_client_version text, p_files jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.checkout_book(p_book_id uuid, p_machine text, p_seat text) TO authenticated;

GRANT ALL ON FUNCTION tc.checkout_book_takeover(p_book_id uuid, p_machine text, p_seat text) TO authenticated;

GRANT ALL ON FUNCTION tc.claim_memberships() TO authenticated;

GRANT ALL ON FUNCTION tc.collection_files_finish_tx(p_transaction_id uuid, p_captured jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.collection_files_start_tx(p_collection_id uuid, p_group_key text, p_expected_version bigint, p_files jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.create_collection(p_id uuid, p_name text) TO authenticated;

GRANT ALL ON FUNCTION tc.delete_book(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.download_start_check(p_collection_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.force_unlock(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.get_book_manifest(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.get_changes(p_collection_id uuid, p_since_event_id bigint) TO authenticated;

GRANT ALL ON FUNCTION tc.get_collection_file_manifest(p_collection_id uuid, p_group_key text) TO authenticated;

GRANT ALL ON FUNCTION tc.get_collection_state(p_collection_id uuid, p_since_event_id bigint) TO authenticated;

REVOKE ALL ON FUNCTION tc.list_stale_upload_garbage() FROM PUBLIC;
GRANT ALL ON FUNCTION tc.list_stale_upload_garbage() TO service_role;

GRANT ALL ON FUNCTION tc.log_event(p_collection_id uuid, p_book_id uuid, p_type integer, p_message text, p_book_name text, p_bloom_version text) TO authenticated;

GRANT ALL ON FUNCTION tc.members_add(p_collection_id uuid, p_email text, p_role tc.member_role) TO authenticated;

GRANT ALL ON FUNCTION tc.members_list(p_collection_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.members_remove(p_collection_id uuid, p_member_id bigint) TO authenticated;

GRANT ALL ON FUNCTION tc.members_set_display_name(p_collection_id uuid, p_member_id bigint, p_display_name text) TO authenticated;

GRANT ALL ON FUNCTION tc.members_set_role(p_collection_id uuid, p_member_id bigint, p_new_role tc.member_role) TO authenticated;

GRANT ALL ON FUNCTION tc.my_collections() TO authenticated;

GRANT ALL ON FUNCTION tc.reap_expired_checkin_transactions() TO authenticated;

GRANT ALL ON FUNCTION tc.rename_check(p_book_id uuid, p_new_name text) TO authenticated;

GRANT ALL ON FUNCTION tc.resolve_member_display(p_collection_id uuid, p_user_id text, OUT email text, OUT display_name text) TO authenticated;

REVOKE ALL ON FUNCTION tc.support_set_admin(p_collection_id uuid, p_email text) FROM PUBLIC;
GRANT ALL ON FUNCTION tc.support_set_admin(p_collection_id uuid, p_email text) TO service_role;

GRANT ALL ON FUNCTION tc.undelete_book(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.unlock_book(p_book_id uuid) TO authenticated;

GRANT SELECT ON TABLE tc.books TO authenticated;

GRANT SELECT ON TABLE tc.checkin_transactions TO authenticated;

GRANT SELECT ON TABLE tc.collection_file_groups TO authenticated;

GRANT USAGE ON SEQUENCE tc.collection_file_groups_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.collection_file_transactions TO authenticated;

GRANT SELECT ON TABLE tc.collection_group_files TO authenticated;

GRANT USAGE ON SEQUENCE tc.collection_group_files_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.collections TO authenticated;

GRANT SELECT,INSERT ON TABLE tc.color_palette_entries TO authenticated;

GRANT USAGE ON SEQUENCE tc.color_palette_entries_id_seq TO authenticated;

GRANT SELECT,INSERT ON TABLE tc.events TO authenticated;

GRANT USAGE ON SEQUENCE tc.events_id_seq TO authenticated;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE tc.members TO authenticated;

GRANT USAGE ON SEQUENCE tc.members_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.version_files TO authenticated;

GRANT USAGE ON SEQUENCE tc.version_files_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.versions TO authenticated;

-- Defense in depth: anon holds no privileges anywhere in tc.
REVOKE ALL ON ALL TABLES IN SCHEMA tc FROM anon;
