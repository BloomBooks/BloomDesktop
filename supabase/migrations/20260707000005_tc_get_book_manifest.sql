-- =============================================================================
-- Migration: get_book_manifest RPC (CONTRACTS.md v1.2, additive)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Task 04's client review found that no RPC returns a book's per-file manifest
-- (path → sha256, size, s3_version_id), which the Receive path needs in order to
-- download by pinned (path, s3VersionId). get_collection_state/get_changes carry
-- only the aggregate current_version_seq/current_checksum by design (cheap
-- polling); this RPC is the explicit per-book fetch used when actual bytes are
-- about to move. (The S3 .manifest.json object is a backup of the same data,
-- not the authority.)

-- Returns: { bookId, versionId, seq, checksum, files: [{path, sha256, size, s3VersionId}] }
-- Errors:  book_not_found (P0002) — including for a never-committed (versionless)
--          book unless the caller is the one mid-Send holding its lock, mirroring
--          get_collection_state's invisibility rule; not_a_member (42501).
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

    RETURN jsonb_build_object(
        'bookId',    v_row.id,
        'versionId', v_row.current_version_id,
        'seq',       v_row.current_version_seq,
        'checksum',  v_row.current_checksum,
        'files',     v_files
    );
END;
$$;

COMMENT ON FUNCTION tc.get_book_manifest(uuid) IS
    'CONTRACTS.md v1.2: get_book_manifest — per-file current manifest for one book '
    '(path, sha256, size, s3VersionId), used by Receive to download pinned versions. '
    'Enforces the never-committed-book invisibility rule.';

GRANT EXECUTE ON FUNCTION tc.get_book_manifest(uuid) TO authenticated;
