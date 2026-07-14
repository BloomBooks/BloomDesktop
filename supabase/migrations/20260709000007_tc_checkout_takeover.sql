-- =============================================================================
-- Account-switch checkout takeover (dogfood batch 1, item 9)
-- =============================================================================
-- Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md item 9 (John's decision):
-- a local collection joined under account A is reopened signed in as a DIFFERENT member
-- account B. B may edit books A left checked out on THIS machine, and the first time B's
-- Bloom needs to push a change to the server (check-in), the checkout must atomically move
-- to B -- but ONLY because A's lock was for this same machine; a lock A holds on a different
-- machine remains a genuine conflict, unchanged.
--
-- This is purely additive: tc.checkin_start_tx (20260706000004) is NOT modified. That
-- function already rejects a check-in when `locked_by <> caller` (see its "LockHeldByOther"
-- raise). Rather than loosen that gate (a real behavior/contract change to an existing,
-- already-shipped RPC, which per this task's rules is an orchestrator decision, not something
-- to do unilaterally), the C# client calls this NEW RPC first, so that by the time
-- checkin_start_tx runs, `locked_by` already equals the caller and its existing check passes
-- cleanly with zero change to its behavior.
--
-- CONTRACTS.md ADDITION FLAGGED (not applied here -- this task's rules say contract changes/
-- additions should be flagged for the orchestrator, not self-applied to CONTRACTS.md): add a
-- `checkout_book_takeover(book_id uuid, machine text) -> {success, locked_by,
-- locked_by_machine, locked_at}` row to the RPCs table, alongside checkout_book/unlock_book/
-- force_unlock.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- checkout_book_takeover(book_id uuid, machine text)
-- ---------------------------------------------------------------------------
-- Same shape and conventions as tc.checkout_book (20260706000003): race-free conditional
-- UPDATE, returns {success, locked_by, locked_by_machine, locked_at}, emits a CheckOut event
-- (type=0) on a genuine handover. Differs from checkout_book only in its WHERE clause: instead
-- of requiring the lock be free (or already the caller's), it requires the lock be held by
-- SOMEONE ELSE on the SAME machine the caller is on now. It is safe to call speculatively
-- (e.g. before every check-in) -- if the caller already holds the lock, or the book is
-- unlocked, or it's locked on a different machine, no row matches and `success` is false with
-- no event emitted; callers that only wanted "make sure a same-machine takeover happens if
-- eligible" can treat "false" here as "nothing to take over" and proceed with their own next
-- step's ordinary conflict handling (e.g. checkin_start_tx's unmodified LockHeldByOther gate
-- for a genuinely-different-machine conflict).
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
        RAISE EXCEPTION 'book_not_found' USING ERRCODE = 'P0002';
    END IF;

    v_collection := v_before.collection_id;

    IF NOT tc.is_member(v_collection) THEN
        RAISE EXCEPTION 'not_a_member' USING ERRCODE = '42501';
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

COMMENT ON FUNCTION tc.checkout_book_takeover(uuid, text) IS
    'CONTRACTS.md addition (flagged, not yet applied to CONTRACTS.md -- see Design/CloudTeamCollections '
    'orchestration item 9 report): checkout_book_takeover -- atomically reassigns a book''s lock from a '
    'DIFFERENT account to the caller, but ONLY when the existing lock is recorded for the SAME machine '
    '(account-switch behavior, batch item 9). Returns {success, locked_by, locked_by_machine, locked_at}, '
    'same shape as checkout_book. Emits a CheckOut event (type=0) only when the lock actually changed '
    'hands. Never modifies checkin_start_tx/checkin_finish_tx -- purely additive.';

GRANT EXECUTE ON FUNCTION tc.checkout_book_takeover(uuid, text) TO authenticated;
