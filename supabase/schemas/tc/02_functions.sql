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
    v_book     tc.books%ROWTYPE;
    v_released integer;
BEGIN
    SELECT * INTO v_book FROM tc.books WHERE id = p_book_id;

    IF NOT FOUND THEN
        RETURN;
    END IF;
    v_new_book := v_book.current_version_id IS NULL;

    -- A send-only lock (no checkout GUID) exists only for its check-in, so it goes when
    -- that check-in expires; otherwise nobody could release it without an admin.
    UPDATE tc.books b
    SET locked_by = NULL, locked_by_machine = NULL, locked_at = NULL
    WHERE b.id = p_book_id
      AND b.locked_by IS NOT NULL
      AND b.checkout_guid_hash IS NULL
      AND EXISTS (
          SELECT 1 FROM tc.checkin_transactions t
          WHERE t.book_id = p_book_id AND t.status = 'open' AND t.expires_at < now()
            AND t.started_by = b.locked_by AND t.checkout_guid_hash IS NULL
      )
      AND NOT EXISTS (
          SELECT 1 FROM tc.checkin_transactions t
          WHERE t.book_id = p_book_id AND t.started_by = b.locked_by
            AND t.status = 'open' AND t.expires_at >= now()
      );
    GET DIAGNOSTICS v_released = ROW_COUNT;

    -- A committed book's lock was visible to teammates: record its release (CheckOutReleased,
    -- type = 101, on behalf of the holder) so polling clients see it. A new book is invisible.
    IF v_released > 0 AND NOT v_new_book THEN
        INSERT INTO tc.events (collection_id, book_id, type, by_user_id, book_name, message)
        VALUES (v_book.collection_id, v_book.id, 101, v_book.locked_by, v_book.name,
                'check-in expired');
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

COMMENT ON FUNCTION tc._checkin_reap_book(p_book_id uuid) IS 'Internal: reap expired open checkin_transactions for one book. (v1.10) A send-only lock an expired check-in took (no checkout GUID) is released; a real checkout is left untouched. New, never-finished books are then deleted outright; existing books have the stale transaction marked expired.';

CREATE OR REPLACE FUNCTION tc._checkout_guid_hash(p_guid text) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
    -- NULL in, NULL out, so a missing GUID never matches a stored hash.
    SELECT encode(sha256(convert_to(lower(p_guid), 'UTF8')), 'hex')
$$;

COMMENT ON FUNCTION tc._checkout_guid_hash(p_guid text) IS 'Internal: the stored form of a checkout GUID (tc.books.checkout_guid_hash): lowercase hex SHA-256 of the UTF-8 bytes of the GUID''s lowercase string form. Clients compute the same value to compare their .checkout file with checkoutGuidHash (CONTRACTS.md v1.9).';

CREATE OR REPLACE FUNCTION tc._clear_checkout_on_unlock() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.locked_by IS NULL THEN
        NEW.checkout_guid_hash := NULL;
    ELSIF NEW.locked_by IS DISTINCT FROM OLD.locked_by
          AND NEW.checkout_guid_hash IS NOT DISTINCT FROM OLD.checkout_guid_hash
          AND NOT (OLD.locked_by IS NOT NULL
                   AND current_setting('tc.checkout_takeover', true) = 'on') THEN
        -- The lock changed hands without the new holder being issued a GUID: the old
        -- holder's GUID must not survive. The one deliberate exception is
        -- checkout_book_takeover, which hands the same GUID to the new account.
        NEW.checkout_guid_hash := NULL;
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc._clear_checkout_on_unlock() IS 'Internal: clears tc.books.checkout_guid_hash whenever locked_by is cleared (and whenever the lock changes hands without a new GUID being set, except in checkout_book_takeover, which keeps the GUID on purpose), so every unlock path (unlock_book, force_unlock, members_remove, checkin_finish_tx, future ones) stays consistent without each having to remember the column.';

CREATE OR REPLACE FUNCTION tc._normalize_proposed_files(p_files jsonb) RETURNS jsonb
    LANGUAGE plpgsql IMMUTABLE
    AS $$
DECLARE
    v_out  jsonb;
    v_bad  jsonb;
    v_dups jsonb;
BEGIN
    IF p_files IS NULL OR jsonb_typeof(p_files) <> 'array' THEN
        RAISE EXCEPTION '%', json_build_object('error', 'InvalidManifest',
            'detail', 'files must be an array')::text USING ERRCODE = 'PT400';
    END IF;

    -- Every entry needs a usable relative path, a sha256 and a non-negative size.
    SELECT jsonb_agg(e) INTO v_bad
    FROM jsonb_array_elements(p_files) e
    WHERE jsonb_typeof(e) <> 'object'
       OR jsonb_typeof(e->'path') IS DISTINCT FROM 'string'
       OR e->>'path' = ''
       OR left(e->>'path', 1) = '/'
       OR EXISTS (SELECT 1 FROM unnest(string_to_array(e->>'path', '/')) seg
                  WHERE seg IN ('', '.', '..'))
       OR jsonb_typeof(e->'sha256') IS DISTINCT FROM 'string'
       OR e->>'sha256' = ''
       OR CASE WHEN jsonb_typeof(e->'size') = 'number'
               THEN (e->>'size')::numeric < 0
                    OR (e->>'size')::numeric <> trunc((e->>'size')::numeric)
                    -- the size columns are bigint
                    OR (e->>'size')::numeric > 9223372036854775807
               ELSE true END;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION '%', json_build_object('error', 'InvalidManifest',
            'detail', 'bad file entries', 'entries', v_bad)::text USING ERRCODE = 'PT400';
    END IF;

    SELECT jsonb_agg(jsonb_build_object(
               'path',   normalize(e->>'path', NFC),
               'sha256', e->>'sha256',
               'size',   (e->>'size')::bigint
           ) ORDER BY ord)
    INTO v_out
    FROM jsonb_array_elements(p_files) WITH ORDINALITY AS a(e, ord);

    -- Two spellings of one name (or a plain duplicate) would collapse onto one S3 key.
    SELECT jsonb_agg(d.p ORDER BY d.p) INTO v_dups
    FROM (
        SELECT e->>'path' AS p
        FROM jsonb_array_elements(COALESCE(v_out, '[]'::jsonb)) e
        GROUP BY e->>'path'
        HAVING count(*) > 1
    ) d;
    IF v_dups IS NOT NULL THEN
        RAISE EXCEPTION '%', json_build_object('error', 'InvalidManifest',
            'detail', 'duplicate paths after NFC normalization', 'paths', v_dups)::text
            USING ERRCODE = 'PT400';
    END IF;

    RETURN COALESCE(v_out, '[]'::jsonb);
END;
$$;

COMMENT ON FUNCTION tc._normalize_proposed_files(p_files jsonb) IS 'Internal: validates a proposed manifest [{path, sha256, size}] and returns it with every path NFC-normalized (order kept). Raises PT400 InvalidManifest for a non-array, an entry lacking a relative path (empty, leading "/", or an empty/"."/".." segment), sha256 or non-negative size, or two entries whose paths are equal after normalization. Used by both start RPCs so the diff, the stored transaction, changedPaths and the committed manifest agree on each path''s spelling.';

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

    -- No global reap here (checkin_start_tx and collection_files_start_tx do it): run first,
    -- it could delete this very transaction along with its never-finished book and turn a
    -- successful abort into a 404; run while holding this row's lock, two concurrent aborts
    -- could deadlock on each other's rows.


    -- FOR UPDATE, like checkin_finish_tx (which also locks this row first): abort and a
    -- concurrent finish then take turns, and whichever runs second sees the other's final
    -- status instead of overwriting a just-finished transaction with 'aborted'.
    SELECT * INTO v_tx FROM tc.checkin_transactions WHERE id = p_transaction_id FOR UPDATE;
    IF NOT FOUND THEN
        -- Nothing (any longer) to abort. Aborting a never-committed new book deletes the book,
        -- and with it this very row, so a retry after a lost response lands here and must
        -- succeed like any other repeat abort. It reveals nothing about anyone's transactions.
        RETURN;
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
    -- Existing books keep a real checkout (one with a GUID) — aborting a Send is not
    -- the same as releasing a Checkout.
    PERFORM 1 FROM tc.books WHERE id = v_tx.book_id AND current_version_id IS NULL;
    IF FOUND AND NOT EXISTS (
        SELECT 1 FROM tc.checkin_transactions
        WHERE book_id = v_tx.book_id AND status = 'open'
    ) THEN
        DELETE FROM tc.books WHERE id = v_tx.book_id AND current_version_id IS NULL;
    END IF;

    -- A send-only lock (start took the free book, with no checkout GUID) exists only for
    -- this check-in, so it goes with it; a real checkout stays.
    IF v_tx.checkout_guid_hash IS NULL THEN
        -- A committed book's lock was visible to teammates, so its release is recorded
        -- (CheckOutReleased, type = 101) for polling clients to see; a new book is invisible.
        WITH released AS (
            UPDATE tc.books
            SET locked_by = NULL, locked_by_machine = NULL, locked_at = NULL
            WHERE id = v_tx.book_id
              AND locked_by = v_user_id
              AND checkout_guid_hash IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM tc.checkin_transactions
                  WHERE book_id = v_tx.book_id AND started_by = v_user_id AND status = 'open'
              )
            RETURNING id, collection_id, name, current_version_id
        )
        INSERT INTO tc.events (collection_id, book_id, type, by_user_id, by_user_name, by_email, book_name)
        SELECT r.collection_id, r.id, 101, v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(), r.name
        FROM released r
        WHERE r.current_version_id IS NOT NULL;
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.checkin_abort_tx(p_transaction_id uuid) IS 'Internal to the checkin-abort edge function. Idempotent, including for a transaction id that no longer exists (e.g. removed with the new book a first abort rolled back): that is a no-op success, not 404. Rolls back a never-finished new book entirely; releases an existing book''s send-only lock (v1.10: taken by checkin-start with no checkout GUID); leaves a real checkout untouched.';

CREATE OR REPLACE FUNCTION tc.checkin_finish_tx(p_transaction_id uuid, p_user_id text, p_user_email text, p_user_name text, p_comment text, p_keep_checked_out boolean, p_captured jsonb, p_expected_revision bigint) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
-- Service-role only (see 04_security.sql and the checkin-finish edge function). The
-- caller is p_user_id, which the edge function established from the caller's own JWT
-- via tc.current_caller(); auth.jwt() here is the service role and is never consulted.
DECLARE
    v_user_id     text := p_user_id;
    v_tx          tc.checkin_transactions%ROWTYPE;
    v_book        tc.books%ROWTYPE;
    v_missing     text[];
    v_final       jsonb;
    v_was_new     boolean;
    v_keep        boolean;   -- keep the lock (keepCheckedOut on a real checkout)
    v_new_seq     bigint;
    v_version_id  uuid;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;

    -- FOR UPDATE: a concurrent retry of the same finish waits here until the first
    -- commits, then sees status = 'finished' and returns that result (idempotent),
    -- instead of racing it to the same (book_id, seq).
    SELECT * INTO v_tx FROM tc.checkin_transactions WHERE id = p_transaction_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION '%', '{"error":"transaction_not_found"}' USING ERRCODE = 'PT404';
    END IF;
    IF v_tx.started_by <> v_user_id THEN
        RAISE EXCEPTION '%', '{"error":"forbidden"}' USING ERRCODE = 'PT403';
    END IF;
    -- Membership can have been revoked since start.
    IF NOT EXISTS (
        SELECT 1 FROM tc.members m
        WHERE m.collection_id = v_tx.collection_id AND m.user_id = v_user_id
    ) THEN
        RAISE EXCEPTION '%', '{"error":"not_a_member"}' USING ERRCODE = 'PT403';
    END IF;

    IF v_tx.status = 'finished' THEN
        -- Idempotent retry: return the previously-committed result unchanged.
        RETURN jsonb_build_object('versionId', v_tx.result_version_id, 'seq', v_tx.result_seq);
    END IF;

    IF v_tx.status = 'aborted' THEN
        RAISE EXCEPTION '%', '{"error":"transaction_aborted"}' USING ERRCODE = 'PT409';
    END IF;

    -- (No status update here: raising would roll it back. The expiry time alone refuses
    -- the call, and reap_expired_checkin_transactions marks the row later.)
    IF v_tx.status = 'expired' OR v_tx.expires_at < now() THEN
        RAISE EXCEPTION '%', '{"error":"TransactionExpired"}' USING ERRCODE = 'PT410';
    END IF;

    -- p_captured was verified against the proposal the edge function read at
    -- p_expected_revision. A checkin-start resume since then rewrote proposed_files and
    -- changed_paths (and bumped the revision), so those version-ids must not be committed
    -- with the new proposal's checksums. The transaction stays open for a fresh finish.
    IF v_tx.revision IS DISTINCT FROM p_expected_revision THEN
        RAISE EXCEPTION '%', '{"error":"TransactionChanged"}' USING ERRCODE = 'PT409';
    END IF;

    -- ---- Re-check, under a row lock, what start checked: the caller still holds the
    --      book's lock, and the book is still at the version this transaction was based
    --      on. Otherwise (e.g. an admin force-unlocked the book and someone else checked
    --      in meanwhile) committing would overwrite newer work and could clear the other
    --      person's lock. base_version_id is the book's current version as start saw it
    --      (NULL for a new book's first commit, which then requires the book still to
    --      have no version). ------------------------------------------------------
    SELECT * INTO v_book FROM tc.books WHERE id = v_tx.book_id FOR UPDATE;
    -- Deleted since start: committing would add a version nobody can see.
    IF v_book.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION '%', '{"error":"book_not_found"}' USING ERRCODE = 'PT404';
    END IF;
    IF v_book.locked_by IS DISTINCT FROM v_user_id THEN
        RAISE EXCEPTION '%', json_build_object(
            'error', 'LockHeldByOther',
            'holder', CASE WHEN v_book.locked_by IS NULL THEN NULL ELSE json_build_object(
                'userId', v_book.locked_by,
                'machine', v_book.locked_by_machine,
                'lockedAt', v_book.locked_at
            ) END
        )::text USING ERRCODE = 'PT409';
    END IF;
    -- The caller must also still hold the checkout that start saw: if it moved to another
    -- copy meanwhile (released and checked out again elsewhere), this copy's upload must
    -- not be committed. A send-only lock (start took a free book or created a new one)
    -- has no hash, and must still have none.
    IF v_book.checkout_guid_hash IS DISTINCT FROM v_tx.checkout_guid_hash THEN
        RAISE EXCEPTION '%', '{"error":"CheckoutElsewhere"}' USING ERRCODE = 'PT409';
    END IF;
    IF v_book.current_version_id IS DISTINCT FROM v_tx.base_version_id THEN
        RAISE EXCEPTION '%', json_build_object(
            'error', 'BaseVersionSuperseded',
            'currentVersionId', v_book.current_version_id,
            'currentVersionSeq', v_book.current_version_seq
        )::text USING ERRCODE = 'PT409';
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

    v_was_new := v_book.current_version_id IS NULL;
    -- keepCheckedOut keeps only a real checkout (one with a GUID). A send-only lock is
    -- always released: keeping it would leave the book locked with no GUID for any copy to
    -- check in, unlock or delete with.
    v_keep := p_keep_checked_out AND v_tx.checkout_guid_hash IS NOT NULL;
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
        -- keepCheckedOut keeps the checkout GUID as well; releasing the lock clears it
        -- (books_clear_checkout_on_unlock).
        locked_by = CASE WHEN v_keep THEN locked_by ELSE NULL END,
        locked_by_machine = CASE WHEN v_keep THEN locked_by_machine ELSE NULL END,
        locked_at = CASE WHEN v_keep THEN locked_at ELSE NULL END
    WHERE id = v_tx.book_id AND locked_by = v_user_id;

    IF v_was_new THEN
        INSERT INTO tc.events (collection_id, book_id, type, by_user_id, by_user_name, by_email, book_name, bloom_version)
        VALUES (v_tx.collection_id, v_tx.book_id, 2, v_user_id, p_user_name, lower(p_user_email), v_tx.proposed_name, v_tx.client_version);
    END IF;

    INSERT INTO tc.events (
        collection_id, book_id, type, by_user_id, by_user_name, by_email,
        book_version_seq, book_name, message, bloom_version
    )
    VALUES (
        v_tx.collection_id, v_tx.book_id, 1, v_user_id, p_user_name, lower(p_user_email),
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

COMMENT ON FUNCTION tc.checkin_finish_tx(p_transaction_id uuid, p_user_id text, p_user_email text, p_user_name text, p_comment text, p_keep_checked_out boolean, p_captured jsonb, p_expected_revision bigint) IS 'Internal to the checkin-finish edge function; service-role only, because it trusts p_captured (S3 version-ids the edge function verified). p_user_id/p_user_email/p_user_name identify the caller, established by the edge function from the caller''s own JWT (tc.current_caller). p_expected_revision is the transaction''s revision as the edge function read it with the proposal it verified; a different current revision (a concurrent checkin-start resume) is refused with PT409 TransactionChanged. Locks the transaction and book rows, re-checks that the caller started the transaction, is still a member, still holds the book''s lock under the same checkout GUID (v1.9: the book''s checkout_guid_hash must equal the transaction''s; v1.10: NULL for a send-only lock, which must still be NULL), and that the book is still at the transaction''s base version. Single atomic DB transaction: version row, current-manifest replacement, book update, lock release (which clears the checkout GUID; keepCheckedOut keeps both, but only for a real checkout: a send-only lock is always released), events, transaction close. Idempotent when re-called (even concurrently) on an already-finished transaction. Raises PT401/PT403/PT404/PT409(TransactionChanged, LockHeldByOther, CheckoutElsewhere, BaseVersionSuperseded, MissingOrBadUploads)/PT410(expired).';

CREATE OR REPLACE FUNCTION tc.checkin_start_tx(p_collection_id uuid, p_book_id uuid, p_book_instance_id uuid, p_proposed_name text, p_base_version_id uuid, p_checksum text, p_client_version text, p_files jsonb, p_checkout_guid text DEFAULT NULL::text) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id       text := tc.current_user_id();
    v_book          tc.books%ROWTYPE;
    v_files         jsonb;
    v_changed       text[];
    v_tx_id         uuid;
    v_existing_tx   tc.checkin_transactions%ROWTYPE;
    v_took_lock     boolean := false;   -- this call took a free existing book's lock
    v_result        jsonb;
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

    -- NFC-normalize (and validate) the proposed manifest up front, so the diff, the
    -- persisted transaction, the returned changedPaths (the S3 keys the client uploads
    -- to) and the committed manifest all use the same spelling of every path.
    v_files := tc._normalize_proposed_files(p_files);

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
            -- else: our own resumable row. A first check-in is a send, not a checkout, so
            -- the row is locked to the sender with no checkout GUID and resuming needs none.
            -- The resumed send may propose a different name from the first try (the book was
            -- renamed locally meanwhile): refuse a clash with another live book here, as for a
            -- fresh new book, rather than as a unique-index violation at finish.
            IF EXISTS (
                SELECT 1 FROM tc.books
                WHERE collection_id = p_collection_id
                  AND id <> v_book.id
                  AND deleted_at IS NULL
                  AND lower(normalize(name, NFC)) = lower(normalize(p_proposed_name, NFC))
            ) THEN
                RAISE EXCEPTION '%', json_build_object('error', 'NameConflict')::text
                    USING ERRCODE = 'PT409';
            END IF;
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

            -- Locked to the sender for the duration of the send, with no checkout GUID;
            -- checkin_finish_tx releases it.
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
        -- Lock order is transaction row, then book row, the same as checkin_finish_tx and
        -- checkin_abort_tx, so a re-sent start racing this user's own finish waits instead
        -- of deadlocking. So first lock any open transaction of ours on this book (the
        -- resume below updates it).
        PERFORM 1 FROM tc.checkin_transactions
        WHERE book_id = p_book_id AND started_by = v_user_id AND status = 'open'
        FOR UPDATE;

        -- FOR UPDATE: hold the row while checking and taking the lock, so a concurrent
        -- checkout either finishes first (and we report LockHeldByOther below) or waits.
        SELECT * INTO v_book FROM tc.books
        WHERE id = p_book_id AND collection_id = p_collection_id
        FOR UPDATE;

        -- A deleted book (tombstone) is not there to check in to: finishing would add a
        -- version nobody can see.
        IF NOT FOUND OR v_book.deleted_at IS NOT NULL THEN
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

        -- Our own lock: only the copy holding the current checkout GUID may check in. The
        -- caller holds the book in another copy (moved, duplicated, another computer's)
        -- otherwise. A send-only lock (an unfinished check-in of ours that took the book
        -- while it was free) has no GUID and is resumed by sending none (a NULL hash
        -- matches a NULL hash); sending a GUID for it means that .checkout is obsolete.
        IF v_book.locked_by = v_user_id
           AND tc._checkout_guid_hash(p_checkout_guid) IS DISTINCT FROM v_book.checkout_guid_hash THEN
            RAISE EXCEPTION '%', '{"error":"CheckoutElsewhere"}' USING ERRCODE = 'PT409';
        END IF;

        IF p_base_version_id IS NOT NULL
           AND v_book.current_version_id IS DISTINCT FROM p_base_version_id THEN
            RAISE EXCEPTION '%', json_build_object(
                'error', 'BaseVersionSuperseded',
                'currentVersionId', v_book.current_version_id,
                'currentVersionSeq', v_book.current_version_seq
            )::text USING ERRCODE = 'PT409';
        END IF;

        -- A rename must not collide with another live book (same rule as the new-book
        -- path); catching it here gives the structured NameConflict instead of a unique
        -- index violation at finish, after the upload.
        IF EXISTS (
            SELECT 1 FROM tc.books
            WHERE collection_id = p_collection_id
              AND id <> v_book.id
              AND deleted_at IS NULL
              AND lower(normalize(name, NFC)) = lower(normalize(p_proposed_name, NFC))
        ) THEN
            RAISE EXCEPTION '%', json_build_object('error', 'NameConflict')::text
                USING ERRCODE = 'PT409';
        END IF;

        -- Take the lock if free; no-op if already ours (lock ACQUISITION here, not just
        -- verification, is a deliberate reading of "membership + lock checks" — see
        -- orchestration report for the alternative interpretation considered). A free lock
        -- taken here is for the send only: it gets no checkout GUID, and checkin_finish_tx
        -- releases it. Our own lock keeps its GUID.
        v_took_lock := v_book.locked_by IS NULL;
        UPDATE tc.books
        SET locked_by = v_user_id, locked_at = now()
        WHERE id = p_book_id AND (locked_by IS NULL OR locked_by = v_user_id)
        RETURNING * INTO v_book;

        IF NOT FOUND THEN
            -- Cannot happen while we hold the row lock, but never fall through with an
            -- all-NULL v_book (that surfaced as a NOT NULL violation, not a conflict).
            RAISE EXCEPTION '%', json_build_object(
                'error', 'LockHeldByOther',
                'holder', json_build_object(
                    'userId', (SELECT locked_by FROM tc.books WHERE id = p_book_id),
                    'machine', (SELECT locked_by_machine FROM tc.books WHERE id = p_book_id),
                    'lockedAt', (SELECT locked_at FROM tc.books WHERE id = p_book_id)
                )
            )::text USING ERRCODE = 'PT409';
        END IF;

        IF v_took_lock THEN
            -- We just took a free lock: record the CheckOut event (type = 0) exactly as
            -- checkout_book does, so other clients' get_changes/realtime pick up the new
            -- lock state. (A new book gets no such event: it stays invisible until commit.)
            INSERT INTO tc.events (
                collection_id, book_id, type,
                by_user_id, by_user_name, by_email, book_name
            )
            SELECT
                v_book.collection_id, v_book.id, 0,
                v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(),
                v_book.name;
        END IF;
    END IF;

    -- ---- Diff proposed manifest vs current --------------------------------
    SELECT COALESCE(array_agg(f.path), '{}') INTO v_changed
    FROM jsonb_to_recordset(v_files) AS f(path text, sha256 text, size bigint)
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

    -- base_version_id records the book's current version as of NOW (which equals
    -- p_base_version_id whenever the client sent one, having passed the check above):
    -- checkin_finish_tx refuses to commit if the book has moved on from it.
    IF FOUND THEN
        -- Bumping revision tells a checkin-finish that read the old proposal (and verified
        -- S3 against it) that it changed underneath it (TransactionChanged). status = 'open'
        -- is re-checked here because a concurrent finish may have closed the row since the
        -- SELECT above; then a fresh transaction is opened instead.
        UPDATE tc.checkin_transactions
        SET proposed_name = p_proposed_name,
            base_version_id = v_book.current_version_id,
            checksum = p_checksum,
            client_version = p_client_version,
            proposed_files = v_files,
            changed_paths = v_changed,
            checkout_guid_hash = v_book.checkout_guid_hash,
            expires_at = now() + INTERVAL '48 hours',
            revision = revision + 1
        WHERE id = v_existing_tx.id AND status = 'open'
        RETURNING id INTO v_tx_id;
    END IF;
    IF v_tx_id IS NULL THEN
        INSERT INTO tc.checkin_transactions (
            collection_id, book_id, started_by, proposed_name, base_version_id,
            changed_paths, client_version, proposed_files, checksum, checkout_guid_hash
        )
        VALUES (
            p_collection_id, v_book.id, v_user_id, p_proposed_name, v_book.current_version_id,
            v_changed, p_client_version, v_files, p_checksum, v_book.checkout_guid_hash
        )
        RETURNING id INTO v_tx_id;
    END IF;

    -- Check-in never issues a checkout GUID: the client makes its own, for checkout_book.
    v_result := jsonb_build_object(
        'transactionId', v_tx_id,
        'bookId', v_book.id,
        'changedPaths', to_jsonb(v_changed)
    );
    RETURN v_result;
END;
$$;

COMMENT ON FUNCTION tc.checkin_start_tx(p_collection_id uuid, p_book_id uuid, p_book_instance_id uuid, p_proposed_name text, p_base_version_id uuid, p_checksum text, p_client_version text, p_files jsonb, p_checkout_guid text) IS 'Internal to the checkin-start edge function. NFC-normalizes and validates the proposed manifest (PT400 InvalidManifest), then handles membership/lock/base-version/name checks (the existing-book path holds the book row FOR UPDATE), the new-book path, manifest diffing, and open-transaction resume. v1.10: never issues a checkout GUID. A book already locked by the caller needs p_checkout_guid to match its checkout_guid_hash (else PT409 CheckoutElsewhere; a send-only lock, with no hash, matches only no GUID); creating a new book or taking a free lock of an existing one locks it for the send only, with no hash, and checkin_finish_tx releases it; resuming one''s own never-committed new book needs only the same user and instance id. Taking a free lock of an existing book emits a CheckOut event (type=0), as checkout_book does. Records the book''s current version and checkout_guid_hash in the transaction for checkin_finish_tx to re-check; resuming an open transaction bumps its revision (checkin_finish_tx then refuses a finish that verified the older proposal). Raises PT400/PT401/PT403/PT404/PT409/PT426 per CONTRACTS.md checkin-start error list.';

CREATE OR REPLACE FUNCTION tc.checkout_book(p_book_id uuid, p_machine text, p_checkout_guid text) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id     text;
    v_collection  uuid;
    v_updated     integer;   -- row count from the conditional UPDATE (0 or 1)
    v_row         tc.books%ROWTYPE;
    v_guid_hash   text;
BEGIN
    v_user_id := tc.current_user_id();

    -- The client makes the checkout GUID and saves it in its .checkout file BEFORE asking,
    -- so a checkout whose response is lost can still be recognized (and retried) as its own.
    IF p_checkout_guid IS NULL OR btrim(p_checkout_guid) = '' THEN
        RAISE EXCEPTION 'invalid_checkout_guid: a checkout GUID is required' USING ERRCODE = '22023';
    END IF;
    v_guid_hash := tc._checkout_guid_hash(p_checkout_guid);

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

    -- Race-free conditional UPDATE. Only a FREE book can be checked out: a book the caller
    -- already holds under a different GUID keeps it, because replacing it would silently
    -- orphan the copy holding the current one (the caller may be in another copy of the
    -- collection). Only the GUID's hash is stored.
    UPDATE tc.books
    SET    locked_by          = v_user_id,
           locked_by_machine  = p_machine,
           locked_at          = now(),
           checkout_guid_hash = v_guid_hash
    WHERE  id = p_book_id
      AND  deleted_at IS NULL
      AND  locked_by IS NULL;

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
            'locked_at',         v_row.locked_at
        );
    ELSIF v_row.locked_by = v_user_id
          AND v_row.deleted_at IS NULL
          AND v_row.checkout_guid_hash = v_guid_hash THEN
        -- A retry of a checkout that already succeeded (its response was lost): the same
        -- success, with nothing changed and no second CheckOut event.
        RETURN jsonb_build_object(
            'success',           true,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_at',         v_row.locked_at
        );
    ELSIF v_row.locked_by = v_user_id THEN
        -- Already checked out to the caller under another GUID (in another copy, or a
        -- send-only check-in lock with no GUID): nothing changes.
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by_me',      true,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_at',         v_row.locked_at
        );
    ELSE
        -- Lock held by someone else (or the book is deleted)
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_at',         v_row.locked_at
        );
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.checkout_book(p_book_id uuid, p_machine text, p_checkout_guid text) IS 'CONTRACTS.md: checkout_book — conditional lock (race-free UPDATE WHERE locked_by IS NULL). v1.10: the client supplies the checkout GUID (non-empty, else 22023 invalid_checkout_guid), having saved it in its .checkout file first; only its hash (tc.books.checkout_guid_hash) is stored and the GUID is never returned. A free book is locked with that hash. A book already locked by the caller with the SAME hash succeeds again with no change and no event (idempotent retry after a lost response). A book locked by the caller with a different (or no) hash returns {success: false, locked_by_me: true} and keeps it (replacing it would orphan the copy holding it). Returns {success, locked_by, locked_by_machine, locked_at, locked_by_me (only when the caller''s under another GUID)}. Emits CheckOut event (type=0) only when it takes a free lock.';

CREATE OR REPLACE FUNCTION tc.checkout_book_takeover(p_book_id uuid, p_checkout_guid text, p_machine text) RETURNS jsonb
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
    -- when the caller presents that lock's checkout GUID. The GUID was returned only to
    -- the account that checked the book out, which saved it in the book folder's .checkout
    -- file, so presenting it proves the caller is working in THAT local copy (the
    -- shared-computer, same-local-folder scenario of bug #0) — something no other member
    -- can fake: members can read only the GUID's hash, and the hash is not accepted here.
    -- p_machine is just recorded for display.
    --
    -- The GUID is NOT rotated: the copy that presented it keeps working under the new
    -- account. tc.checkout_takeover tells books_clear_checkout_on_unlock that this change
    -- of holder keeps the hash on purpose; it is switched off again straight after.
    PERFORM set_config('tc.checkout_takeover', 'on', true);
    UPDATE tc.books
    SET    locked_by          = v_user_id,
           locked_by_machine  = p_machine,
           locked_at          = now()
    WHERE  id = p_book_id
      AND  deleted_at IS NULL
      AND  locked_by IS NOT NULL
      AND  locked_by <> v_user_id
      AND  checkout_guid_hash IS NOT NULL
      AND  p_checkout_guid IS NOT NULL
      AND  checkout_guid_hash = tc._checkout_guid_hash(p_checkout_guid);

    GET DIAGNOSTICS v_updated = ROW_COUNT;
    PERFORM set_config('tc.checkout_takeover', 'off', true);

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
            'locked_at',         v_row.locked_at
        );
    ELSE
        -- Nothing to take over (already ours, unlocked, or the GUID is missing/wrong).
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_at',         v_row.locked_at
        );
    END IF;
END;
$$;

COMMENT ON FUNCTION tc.checkout_book_takeover(p_book_id uuid, p_checkout_guid text, p_machine text) IS 'CONTRACTS.md v1.9: checkout_book_takeover — atomically reassigns a book''s lock from a DIFFERENT account to the caller, but ONLY when the caller presents the lock''s current checkout GUID (kept in the local copy''s .checkout file); only its hash is stored and compared, and presenting the hash does not work. The GUID is kept (not rotated), so the same copy goes on checking in under the new account. p_machine is recorded with the new lock for display but grants nothing. Returns {success, locked_by, locked_by_machine, locked_at}. Emits a CheckOut event (type=0) only when the lock actually changed hands.';

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

CREATE OR REPLACE FUNCTION tc.collection_files_finish_tx(p_transaction_id uuid, p_user_id text, p_user_email text, p_user_name text, p_captured jsonb, p_expected_revision bigint) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
-- Service-role only; p_user_id is the caller, as in checkin_finish_tx.
DECLARE
    v_user_id  text := p_user_id;
    v_tx       tc.collection_file_transactions%ROWTYPE;
    v_group    tc.collection_file_groups%ROWTYPE;
    v_missing  text[];
    v_final    jsonb;
    v_new_ver  bigint;
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;

    -- FOR UPDATE so a concurrent retry waits and then takes the 'finished' branch.
    SELECT * INTO v_tx FROM tc.collection_file_transactions WHERE id = p_transaction_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION '%', '{"error":"transaction_not_found"}' USING ERRCODE = 'PT404';
    END IF;
    IF v_tx.started_by <> v_user_id THEN
        RAISE EXCEPTION '%', '{"error":"forbidden"}' USING ERRCODE = 'PT403';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM tc.members m
        WHERE m.collection_id = v_tx.collection_id AND m.user_id = v_user_id
    ) THEN
        RAISE EXCEPTION '%', '{"error":"not_a_member"}' USING ERRCODE = 'PT403';
    END IF;

    IF v_tx.status = 'finished' THEN
        RETURN jsonb_build_object('version', v_tx.result_version);
    END IF;
    IF v_tx.status = 'aborted' THEN
        RAISE EXCEPTION '%', '{"error":"transaction_aborted"}' USING ERRCODE = 'PT409';
    END IF;
    -- (No status update here: raising would roll it back. The expiry time alone refuses
    -- the call, and the reaper marks the row later.)
    IF v_tx.status = 'expired' OR v_tx.expires_at < now() THEN
        RAISE EXCEPTION '%', '{"error":"TransactionExpired"}' USING ERRCODE = 'PT410';
    END IF;
    -- Same guard as checkin_finish_tx: p_captured was verified against the proposal read
    -- at p_expected_revision, not one a concurrent collection-files-start resume wrote since.
    IF v_tx.revision IS DISTINCT FROM p_expected_revision THEN
        RAISE EXCEPTION '%', '{"error":"TransactionChanged"}' USING ERRCODE = 'PT409';
    END IF;

    SELECT * INTO v_group FROM tc.collection_file_groups
    WHERE collection_id = v_tx.collection_id AND group_key = v_tx.group_key
    FOR UPDATE;

    IF v_group.version <> v_tx.expected_version THEN
        -- The transaction stays open (an update here would be rolled back by the raise);
        -- the caller's next collection-files-start resumes it with the new version.
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
        -- The transaction stays open (an update here would be rolled back by the raise);
        -- the caller's next collection-files-start resumes it with the new version.
        RAISE EXCEPTION '%', json_build_object(
            'error', 'VersionConflict', 'currentVersion', v_group.version
        )::text USING ERRCODE = 'PT409';
    END IF;

    DELETE FROM tc.collection_group_files WHERE group_id = v_group.id;

    INSERT INTO tc.collection_group_files (group_id, path, sha256, size_bytes, s3_version_id)
    SELECT v_group.id, e->>'path', e->>'sha256', (e->>'size')::bigint, e->>'s3VersionId'
    FROM jsonb_array_elements(v_final) e;

    INSERT INTO tc.events (collection_id, type, by_user_id, by_user_name, by_email, group_key)
    VALUES (v_tx.collection_id, 1, v_user_id, p_user_name, lower(p_user_email), v_tx.group_key);

    UPDATE tc.collection_file_transactions
    SET status = 'finished', finished_at = now(), result_version = v_new_ver
    WHERE id = p_transaction_id;

    -- 'manifest' is extra data for the edge function only (not part of the
    -- CONTRACTS.md {version} response) — used to write the .manifest.json backup.
    RETURN jsonb_build_object('version', v_new_ver, 'manifest', v_final);
END;
$$;

COMMENT ON FUNCTION tc.collection_files_finish_tx(p_transaction_id uuid, p_user_id text, p_user_email text, p_user_name text, p_captured jsonb, p_expected_revision bigint) IS 'Internal to the collection-files-finish edge function; service-role only (trusts p_captured), with the caller passed as p_user_id/p_user_email/p_user_name as for checkin_finish_tx. Locks the transaction and group rows, so concurrent retries are idempotent. Refuses with PT409 TransactionChanged when the transaction''s revision is no longer p_expected_revision (a concurrent collection-files-start resume rewrote the proposal the edge function verified). Re-checks the optimistic version at finish time too (repo-wins rule); PT409 VersionConflict leaves the transaction open: a stale retry still fails the same check, and the caller''s next collection-files-start resumes it with the new version.';

CREATE OR REPLACE FUNCTION tc.collection_files_start_tx(p_collection_id uuid, p_group_key text, p_expected_version bigint, p_files jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id  text := tc.current_user_id();
    v_group    tc.collection_file_groups%ROWTYPE;
    v_files    jsonb;
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

    -- Same NFC normalization/validation as checkin_start_tx, for the same reason.
    v_files := tc._normalize_proposed_files(p_files);

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
    FROM jsonb_to_recordset(v_files) AS f(path text, sha256 text, size bigint)
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
        -- As in checkin_start_tx: the revision bump makes a finish that verified the old
        -- proposal refuse (TransactionChanged), and a row a concurrent finish closed since
        -- the SELECT above is left alone in favor of a fresh transaction.
        UPDATE tc.collection_file_transactions
        SET expected_version = p_expected_version,
            proposed_files = v_files,
            changed_paths = v_changed,
            expires_at = now() + INTERVAL '48 hours',
            revision = revision + 1
        WHERE id = v_existing.id AND status = 'open'
        RETURNING id INTO v_tx_id;
    END IF;
    IF v_tx_id IS NULL THEN
        INSERT INTO tc.collection_file_transactions (
            collection_id, group_key, started_by, expected_version, proposed_files, changed_paths
        )
        VALUES (
            p_collection_id, p_group_key, v_user_id, p_expected_version, v_files, v_changed
        )
        RETURNING id INTO v_tx_id;
    END IF;

    RETURN jsonb_build_object('transactionId', v_tx_id, 'changedPaths', to_jsonb(v_changed));
END;
$$;

COMMENT ON FUNCTION tc.collection_files_start_tx(p_collection_id uuid, p_group_key text, p_expected_version bigint, p_files jsonb) IS 'Internal to the collection-files-start edge function. NFC-normalizes/validates the proposed manifest (PT400 InvalidManifest), then optimistic-version gate (PT409 VersionConflict) + manifest diff + transaction open/resume (a resume bumps the transaction''s revision; see collection_files_finish_tx).';

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

CREATE OR REPLACE FUNCTION tc.current_caller() RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    AS $$
BEGIN
    IF tc.current_user_id() IS NULL THEN
        RAISE EXCEPTION '%', '{"error":"unauthenticated"}' USING ERRCODE = 'PT401';
    END IF;
    RETURN jsonb_build_object(
        'userId', tc.current_user_id(),
        'email',  tc.current_user_email(),
        'name',   auth.jwt() ->> 'name'
    );
END;
$$;

COMMENT ON FUNCTION tc.current_caller() IS 'Returns {userId, email, name} from the caller''s own (PostgREST-validated) JWT. The finish edge functions call it with the caller''s token to establish who is calling, then pass that identity to the service-role-only finish RPCs. Works the same for a Firebase ID token (third-party auth) and a local GoTrue token.';

CREATE OR REPLACE FUNCTION tc.delete_book(p_book_id uuid, p_checkout_guid text) RETURNS void
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

    IF v_row.checkout_guid_hash IS NULL
       OR tc._checkout_guid_hash(p_checkout_guid) IS DISTINCT FROM v_row.checkout_guid_hash THEN
        RAISE EXCEPTION 'CheckoutElsewhere: this book is checked out to you in another copy'
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

COMMENT ON FUNCTION tc.delete_book(p_book_id uuid, p_checkout_guid text) IS 'CONTRACTS.md: delete_book — requires caller holds the lock and (v1.9) presents its checkout GUID (else CheckoutElsewhere); sets deleted_at tombstone; emits Deleted (type=8). Lock is released on deletion.';

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
    -- Supabase Realtime's broadcast-from-database: realtime.send stores the message in
    -- realtime.messages, and the Realtime server delivers it to subscribers of the private
    -- channel collection:{collection_id} whom the realtime.messages RLS policy lets read it
    -- (04_security.sql). realtime.send only warns if it cannot deliver, so a Realtime problem
    -- never blocks the check-in that logged the event. Where the Realtime schema is absent
    -- (a database started without the Realtime service, as the pgTAP job does) there is
    -- nothing to send to; clients catch up with get_changes either way.
    IF to_regprocedure('realtime.send(jsonb,text,text,boolean)') IS NOT NULL THEN
        PERFORM realtime.send(
            jsonb_build_object(
                'eventId',     NEW.id,
                'type',        NEW.type,
                'bookId',      NEW.book_id,
                'versionSeq',  NEW.book_version_seq,
                'byUserName',  NEW.by_user_name,
                'byEmail',     NEW.by_email,
                'lock',        NEW.lock_info,
                'name',        NEW.book_name,
                'groupKey',    NEW.group_key
            ),
            'tc_event',
            'collection:' || NEW.collection_id::text,
            true
        );
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.events_realtime_broadcast() IS 'Broadcasts every new event row with realtime.send (Supabase Realtime broadcast from the database) as event "tc_event" on the PRIVATE channel collection:{collection_id}, in the message shape of CONTRACTS.md §Realtime (realtime.send adds its own "id" key). A no-op where the realtime schema is not installed; delivery failures are only warnings.';

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
            b.checkout_guid_hash AS "checkoutGuidHash",
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

COMMENT ON FUNCTION tc.get_changes(p_collection_id uuid, p_since_event_id bigint) IS 'CONTRACTS.md: get_changes — events + touched book rows since the cursor. Used for polling (60s fallback) and realtime reconnect catch-up. v1.2 (20260707000006): touched book rows also carry locked_by_email/locked_by_name for display. v1.6 (20260713000001): event rows also carry by_display_name (the current durable display name of by_user_id). v1.9: touched book rows also carry checkoutGuidHash (tc.books.checkout_guid_hash).';

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
                b.checkout_guid_hash AS "checkoutGuidHash",
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
                b.checkout_guid_hash AS "checkoutGuidHash",
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

COMMENT ON FUNCTION tc.get_collection_state(p_collection_id uuid, p_since_event_id bigint) IS 'CONTRACTS.md: get_collection_state — full/delta snapshot of book rows + group versions + max_event_id. since_event_id = NULL → full; otherwise delta. v1.2 (20260707000006): book rows also carry locked_by_email/locked_by_name for display. v1.9: book rows also carry checkoutGuidHash (tc.books.checkout_guid_hash, so a client can tell whether its .checkout file is current).';

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

CREATE OR REPLACE FUNCTION tc.list_stale_upload_keys(p_after_key text, p_limit integer) RETURNS TABLE(transaction_kind text, s3_key text, referenced_version_id text)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    AS $$
BEGIN
    -- Every page must fit in one PostgREST response (max_rows 1000), or it would be
    -- truncated silently and the cursor would skip the missing rows.
    IF p_limit IS NULL OR p_limit < 1 OR p_limit > 1000 THEN
        RAISE EXCEPTION 'invalid_limit: p_limit must be between 1 and 1000' USING ERRCODE = '22023';
    END IF;
    -- One row per key (a key appears once per dead transaction that touched it), keyset-paged
    -- in byte order ("C" collation, so the order and the > comparison agree on every server).
    RETURN QUERY
    SELECT g.kind, g.k::text, min(g.ref)
    FROM (
        SELECT l.transaction_kind AS kind, l.s3_key COLLATE "C" AS k,
               l.referenced_version_id AS ref
        FROM tc.list_stale_upload_garbage() l
    ) g
    WHERE p_after_key IS NULL OR g.k > (p_after_key COLLATE "C")
    GROUP BY g.kind, g.k
    ORDER BY g.k
    LIMIT p_limit;
END;
$$;

COMMENT ON FUNCTION tc.list_stale_upload_keys(p_after_key text, p_limit integer) IS 'Paged worklist for the sweep-stale-uploads edge function: the distinct S3 keys of tc.list_stale_upload_garbage (with their kind and currently-referenced version), in "C"-collation key order, after p_after_key (NULL = from the start), at most p_limit (1..1000, else 22023 invalid_limit). The sweep passes the last key of each page as the next cursor and reads every page per run, so keys whose dead transaction rows remain after their garbage is deleted never stop it reaching later ones. service-role only.';

CREATE OR REPLACE FUNCTION tc.stale_upload_key_state(p_s3_key text) RETURNS jsonb
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
    -- Re-evaluates list_stale_upload_garbage for ONE key, as of now: still listed means no
    -- live transaction touches it; referencedVersionId is what the manifest references now.
    SELECT jsonb_build_object(
        'stillStale', count(*) > 0,
        'referencedVersionId', min(g.referenced_version_id)
    )
    FROM tc.list_stale_upload_garbage() g
    WHERE g.s3_key = p_s3_key
$$;

COMMENT ON FUNCTION tc.stale_upload_key_state(p_s3_key text) IS 'For the sweep-stale-uploads edge function, immediately before it deletes versions of one key: {stillStale, referencedVersionId} re-read now. The sweep skips the key unless it is still stale (no live transaction touches it) and still references the same version it planned against, so a check-in that committed or started after the worklist snapshot never loses its upload. service-role only.';

COMMENT ON FUNCTION tc.list_stale_upload_garbage() IS 'The whole, unpaged worklist behind the sweep-stale-uploads edge function (which reads it a page at a time through tc.list_stale_upload_keys, and one key at a time through tc.stale_upload_key_state): per-file S3 keys touched by DEAD (aborted/expired) check-in transactions, with the currently-referenced s3_version_id as the delete-newer-than watermark (NULL = nothing references the key). Excludes paths a live transaction is still uploading. service-role only. See GOING-LIVE.md "Orphaned-upload sweep".';

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

COMMENT ON FUNCTION tc.reap_expired_checkin_transactions() IS 'Global expiry sweep for both checkin_transactions (via _checkin_reap_book) and collection_file_transactions. Returns the total number of items reaped across both sweeps. Called opportunistically at the top of checkin_start_tx and collection_files_start_tx; also safe to run from a scheduled job if one is ever wired up (no pg_cron dependency here).';

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

CREATE OR REPLACE FUNCTION tc.unlock_book(p_book_id uuid, p_checkout_guid text) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    AS $$
DECLARE
    v_user_id    text;
    v_collection uuid;
    v_locked_by  text;
    v_guid_hash  text;
BEGIN
    v_user_id := tc.current_user_id();

    SELECT b.collection_id, b.locked_by, b.checkout_guid_hash
    INTO v_collection, v_locked_by, v_guid_hash
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

    -- Only the copy holding the checkout GUID may undo the checkout; another copy of the
    -- same user's would otherwise discard that copy's checkout behind its back.
    IF v_guid_hash IS NULL
       OR tc._checkout_guid_hash(p_checkout_guid) IS DISTINCT FROM v_guid_hash THEN
        RAISE EXCEPTION 'CheckoutElsewhere: this book is checked out to you in another copy'
            USING ERRCODE = 'P0001';
    END IF;

    UPDATE tc.books
    SET    locked_by         = NULL,
           locked_by_machine = NULL,
           locked_at         = NULL
    WHERE  id = p_book_id;

    -- CheckOutReleased (type = 101): get_changes only returns books named by newer events,
    -- so without one, polling teammates would go on seeing the book checked out.
    INSERT INTO tc.events (collection_id, book_id, type, by_user_id, by_user_name, by_email, book_name)
    SELECT b.collection_id, b.id, 101, v_user_id, (auth.jwt() ->> 'name'), tc.current_user_email(), b.name
    FROM tc.books b WHERE b.id = p_book_id;
END;
$$;

COMMENT ON FUNCTION tc.unlock_book(p_book_id uuid, p_checkout_guid text) IS 'CONTRACTS.md: unlock_book — release own lock (undo checkout, no content change). Only the lock holder may call this, and (v1.9) only with the current checkout GUID (else CheckoutElsewhere); use force_unlock for admin override. Releasing the lock clears the GUID. Emits CheckOutReleased (type=101) so polling clients see the book unlocked.';
