-- =============================================================================
-- Migration: check-in / collection-files transaction functions (task 02)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- These functions implement the atomic-DB-transaction half of the two-phase
-- check-in / collection-files protocols described in CONTRACTS.md. They are
-- called ONLY by the edge functions in supabase/functions/** (checkin-start,
-- checkin-finish, checkin-abort, collection-files-start, collection-files-finish),
-- forwarding the CALLING USER'S OWN JWT (not a service-role key) so that
-- tc.current_user_id()/tc.current_user_email() resolve correctly. They are
-- SECURITY DEFINER (like every other RPC in this schema) so they can write to
-- tables that have no direct-write RLS policy for `authenticated`.
--
-- They are NOT part of the public wire contract in CONTRACTS.md — that document
-- governs the HTTP shape of the edge functions, not these internal helpers. A
-- client should never call these directly; nothing stops it (same trust model as
-- every other RPC here: re-validate everything internally), but there is no
-- reason to.
--
-- HTTP-status passthrough convention: PostgREST maps an ERRCODE of the form
-- 'PT###' directly to HTTP status ###. We RAISE EXCEPTION '%', <json text>
-- USING ERRCODE = 'PT409' (etc.) so the edge function can forward the same
-- status code and JSON.parse() the exception message for structured fields
-- (e.g. the lock holder's identity). See _shared/rpc.ts on the edge-function side.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Extra checkin_transactions columns needed to make checkin-finish stateless
-- (it receives only { transactionId, comment?, keepCheckedOut? } per CONTRACTS.md —
-- no files list — so the full proposed manifest and its checksum must be
-- persisted at checkin-start time).
-- ---------------------------------------------------------------------------
ALTER TABLE tc.checkin_transactions
    ADD COLUMN IF NOT EXISTS proposed_files    jsonb   NOT NULL DEFAULT '[]'::jsonb,
    ADD COLUMN IF NOT EXISTS checksum          text,
    ADD COLUMN IF NOT EXISTS result_version_id uuid REFERENCES tc.versions(id),
    ADD COLUMN IF NOT EXISTS result_seq        bigint;

COMMENT ON COLUMN tc.checkin_transactions.proposed_files IS
    'Full proposed manifest [{path,sha256,size}] captured at checkin-start; '
    'checkin-finish reconstructs the committed manifest from this + changed_paths + '
    'the S3 version-ids captured after upload verification.';
COMMENT ON COLUMN tc.checkin_transactions.checksum IS
    'SHA-256 checksum of the full proposed manifest, supplied at checkin-start and '
    'persisted as tc.versions.checksum / tc.books.current_checksum on finish.';
COMMENT ON COLUMN tc.checkin_transactions.result_version_id IS
    'Set on successful checkin-finish; makes a repeated checkin-finish call for an '
    'already-finished transaction idempotent (returns the same result).';

-- ---------------------------------------------------------------------------
-- collection_file_transactions  (two-phase commit for collection-files-start/finish)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tc.collection_file_transactions (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    collection_id     uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    group_key         text        NOT NULL
                      CHECK (group_key IN ('other', 'allowed-words', 'sample-texts')),
    started_by        text        NOT NULL,
    expected_version   bigint      NOT NULL,
    proposed_files    jsonb       NOT NULL DEFAULT '[]'::jsonb,
    changed_paths     text[]      NOT NULL DEFAULT '{}',
    started_at        timestamptz NOT NULL DEFAULT now(),
    expires_at        timestamptz NOT NULL DEFAULT (now() + INTERVAL '48 hours'),
    finished_at       timestamptz,
    aborted_at        timestamptz,
    status            text        NOT NULL DEFAULT 'open'
                      CHECK (status IN ('open', 'finished', 'aborted', 'expired')),
    result_version    bigint
);

COMMENT ON TABLE tc.collection_file_transactions IS
    'Open collection-files-start -> collection-files-finish two-phase commits. '
    'Mirrors tc.checkin_transactions but scoped to (collection_id, group_key) instead '
    'of a book.';

CREATE INDEX IF NOT EXISTS collection_file_transactions_scope_idx
    ON tc.collection_file_transactions(collection_id, group_key, started_by)
    WHERE status = 'open';
CREATE INDEX IF NOT EXISTS collection_file_transactions_expires_at_idx
    ON tc.collection_file_transactions(expires_at) WHERE status = 'open';

ALTER TABLE tc.collection_file_transactions ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_file_transactions_select ON tc.collection_file_transactions
    FOR SELECT
    USING (started_by = tc.current_user_id());

-- ---------------------------------------------------------------------------
-- Minimum supported client version (ClientOutOfDate / 426 handling)
-- ---------------------------------------------------------------------------
-- A tiny "constant function" so operators can bump the floor by CREATE OR REPLACE
-- without a migration. Version strings compare as dotted integer tuples
-- ('1.2.3' < '1.10.0'); a NULL/unparsable client_version is treated as out of date
-- only if the floor is above '0.0.0' (keeps existing behaviour permissive by default).
CREATE OR REPLACE FUNCTION tc.min_supported_client_version()
RETURNS text
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT '0.0.0'::text
$$;

COMMENT ON FUNCTION tc.min_supported_client_version() IS
    'Floor Bloom client version for cloud check-in operations. Bump via '
    'CREATE OR REPLACE FUNCTION when a breaking client-side protocol change ships. '
    'ClientOutOfDate (426) is raised when the caller''s clientVersion sorts below this.';

CREATE OR REPLACE FUNCTION tc.is_client_version_supported(p_client_version text)
RETURNS boolean
LANGUAGE plpgsql
IMMUTABLE
AS $$
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
$$;

COMMENT ON FUNCTION tc.is_client_version_supported(text) IS
    'Dotted-integer version compare against tc.min_supported_client_version(). '
    'Used to raise ClientOutOfDate (426) in checkin_start_tx.';

-- ---------------------------------------------------------------------------
-- download_start_check(collection_id uuid)
-- ---------------------------------------------------------------------------
-- All the DB-side work download-start needs: confirm membership. Raises PT403
-- if not a member; returns void on success.
CREATE OR REPLACE FUNCTION tc.download_start_check(
    p_collection_id uuid
)
RETURNS void
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.download_start_check(uuid) IS
    'Internal to the download-start edge function: membership gate only. '
    'PT403 not_a_member if the caller is not a member of the collection.';

-- ---------------------------------------------------------------------------
-- _checkin_reap_book(book_id uuid) — internal helper
-- ---------------------------------------------------------------------------
-- Reaps expired OPEN transactions tied to a single book. A brand-new book that
-- was never finished (current_version_id IS NULL) is deleted outright (fully
-- invisible, as if the Send had never started); an existing book's stale
-- transaction is just marked 'expired' — its lock is left alone (expiry of a
-- check-in attempt should not silently release a checkout the user still holds).
CREATE OR REPLACE FUNCTION tc._checkin_reap_book(p_book_id uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc._checkin_reap_book(uuid) IS
    'Internal: reap expired open checkin_transactions for one book. New, '
    'never-finished books are deleted outright; existing books just have the '
    'stale transaction marked expired (lock is left untouched).';

-- ---------------------------------------------------------------------------
-- reap_expired_checkin_transactions() — global sweep
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.reap_expired_checkin_transactions()
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_book_id uuid;
    v_count   integer := 0;
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
    GET DIAGNOSTICS v_count = ROW_COUNT;

    RETURN v_count;
END;
$$;

COMMENT ON FUNCTION tc.reap_expired_checkin_transactions() IS
    'Global expiry sweep for both checkin_transactions (via _checkin_reap_book) and '
    'collection_file_transactions. Called opportunistically at the top of '
    'checkin_start_tx/checkin_abort_tx/collection_files_start_tx; also safe to run '
    'from a scheduled job if one is ever wired up (no pg_cron dependency here).';

-- ---------------------------------------------------------------------------
-- checkin_start_tx(...)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.checkin_start_tx(
    p_collection_id   uuid,
    p_book_id         uuid,      -- NULL ⇒ new book
    p_book_instance_id uuid,
    p_proposed_name   text,
    p_base_version_id uuid,
    p_checksum        text,
    p_client_version  text,
    p_files           jsonb      -- [{path, sha256, size}], full proposed manifest
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.checkin_start_tx(uuid, uuid, uuid, text, uuid, text, text, jsonb) IS
    'Internal to the checkin-start edge function. Handles membership/lock/base-version '
    'checks, the new-book path, manifest diffing, and open-transaction resume. '
    'Raises PT401/PT403/PT404/PT409/PT426 per CONTRACTS.md checkin-start error list.';

-- ---------------------------------------------------------------------------
-- checkin_finish_tx(...)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.checkin_finish_tx(
    p_transaction_id  uuid,
    p_comment         text,
    p_keep_checked_out boolean,
    p_captured        jsonb    -- [{path, s3VersionId}] for every path in changed_paths
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.checkin_finish_tx(uuid, text, boolean, jsonb) IS
    'Internal to the checkin-finish edge function. Single atomic DB transaction: '
    'version row, current-manifest replacement, book update, lock release, events, '
    'transaction close. Idempotent when re-called on an already-finished transaction. '
    'Raises PT401/PT403/PT404/PT409(MissingOrBadUploads)/PT410(expired).';

-- ---------------------------------------------------------------------------
-- checkin_abort_tx(transaction_id uuid)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.checkin_abort_tx(
    p_transaction_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.checkin_abort_tx(uuid) IS
    'Internal to the checkin-abort edge function. Idempotent. Rolls back a never-'
    'finished new book entirely; leaves an existing book''s lock untouched.';

-- ---------------------------------------------------------------------------
-- collection_files_start_tx(...)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.collection_files_start_tx(
    p_collection_id   uuid,
    p_group_key       text,
    p_expected_version bigint,
    p_files           jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.collection_files_start_tx(uuid, text, bigint, jsonb) IS
    'Internal to the collection-files-start edge function. Optimistic-version gate '
    '(PT409 VersionConflict) + manifest diff + transaction open/resume.';

-- ---------------------------------------------------------------------------
-- collection_files_finish_tx(...)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.collection_files_finish_tx(
    p_transaction_id uuid,
    p_captured       jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.collection_files_finish_tx(uuid, jsonb) IS
    'Internal to the collection-files-finish edge function. Re-checks the optimistic '
    'version at finish time too (repo-wins rule); PT409 VersionConflict aborts the '
    'transaction so a stale retry cannot succeed later.';

-- ---------------------------------------------------------------------------
-- Grants — same pattern as 20260706000003_tc_rpcs.sql (SECURITY DEFINER + grant
-- to `authenticated`; internal re-validation makes this safe to call directly).
-- ---------------------------------------------------------------------------
GRANT EXECUTE ON FUNCTION tc.download_start_check(uuid)                    TO authenticated;
GRANT EXECUTE ON FUNCTION tc.checkin_start_tx(uuid, uuid, uuid, text, uuid, text, text, jsonb) TO authenticated;
GRANT EXECUTE ON FUNCTION tc.checkin_finish_tx(uuid, text, boolean, jsonb)  TO authenticated;
GRANT EXECUTE ON FUNCTION tc.checkin_abort_tx(uuid)                         TO authenticated;
GRANT EXECUTE ON FUNCTION tc.collection_files_start_tx(uuid, text, bigint, jsonb)  TO authenticated;
GRANT EXECUTE ON FUNCTION tc.collection_files_finish_tx(uuid, jsonb)        TO authenticated;
GRANT EXECUTE ON FUNCTION tc.reap_expired_checkin_transactions()            TO authenticated;

GRANT SELECT ON tc.collection_file_transactions TO authenticated;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA tc TO authenticated;
