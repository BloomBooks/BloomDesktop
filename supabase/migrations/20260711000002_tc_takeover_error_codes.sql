-- =============================================================================
-- Fix: checkout_book_takeover error codes aligned with the schema-wide convention
-- =============================================================================
-- Found by Greptile review on PR #8048: the original function (20260709000007)
-- raised `book_not_found` with ERRCODE P0002 and `not_a_member` with ERRCODE
-- 42501 -- bare-string messages with codes PostgREST does not map to HTTP
-- statuses. Every other RPC in this schema raises a JSON-object message with a
-- PT### passthrough code (PT404/PT403), which PostgREST turns into the matching
-- HTTP status and CloudCollectionClientException then maps to a typed
-- CloudErrorCode instead of Unknown. This migration re-creates the function
-- with only those two RAISE statements changed; the takeover logic itself is
-- untouched (see 20260709000007 for the full design commentary).
--
-- Convention: merged migrations are never edited in place -- fixes ship as new
-- migration files (see Design/CloudTeamCollections/IMPLEMENTATION.md).
-- =============================================================================

CREATE OR REPLACE FUNCTION tc.checkout_book_takeover(
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
    -- when that account's lock is recorded against the SAME machine the caller is on now.
    -- Never takes over a lock held on a different machine -- that stays a genuine conflict.
    UPDATE tc.books
    SET    locked_by         = v_user_id,
           locked_by_machine = p_machine,
           locked_at         = now()
    WHERE  id = p_book_id
      AND  deleted_at IS NULL
      AND  locked_by IS NOT NULL
      AND  locked_by <> v_user_id
      AND  locked_by_machine = p_machine;

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
            'locked_at',         v_row.locked_at
        );
    ELSE
        -- Nothing to take over (already ours, unlocked, or locked on a different machine).
        RETURN jsonb_build_object(
            'success',           false,
            'locked_by',         v_row.locked_by,
            'locked_by_machine', v_row.locked_by_machine,
            'locked_at',         v_row.locked_at
        );
    END IF;
END;
$$;
