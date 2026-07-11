-- =============================================================================
-- Fix: reap_expired_checkin_transactions returned the wrong count
-- =============================================================================
-- Found by Greptile review on PR #8048: the original function (20260706000004)
-- accumulated a per-book count in v_count across the loop, then immediately
-- clobbered it with `GET DIAGNOSTICS v_count = ROW_COUNT` from the
-- collection_file_transactions sweep, so it always returned only the
-- collection-file count. Diagnostic-only today (every call site PERFORMs and
-- discards the result), but the return value now matches the documented intent:
-- total items reaped across both sweeps.
--
-- Convention: merged migrations are never edited in place -- fixes ship as new
-- migration files (see Design/CloudTeamCollections/IMPLEMENTATION.md).
-- =============================================================================

CREATE OR REPLACE FUNCTION tc.reap_expired_checkin_transactions()
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.reap_expired_checkin_transactions() IS
    'Global expiry sweep for both checkin_transactions (via _checkin_reap_book) and '
    'collection_file_transactions. Returns the total number of items reaped across both '
    'sweeps. Called opportunistically at the top of checkin_start_tx/checkin_abort_tx/'
    'collection_files_start_tx; also safe to run from a scheduled job if one is ever '
    'wired up (no pg_cron dependency here).';
