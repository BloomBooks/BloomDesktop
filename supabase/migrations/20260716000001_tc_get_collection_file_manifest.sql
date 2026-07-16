-- =============================================================================
-- Migration: get_collection_file_manifest RPC (CONTRACTS.md, additive)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- E9: collection-file group downloads used to LIST the S3 prefix and re-fetch
-- every object every time, with no per-file change detection and no consistent
-- snapshot (a mid-write listing could read an uncommitted mix). The committed
-- per-file manifest already exists durably in tc.collection_group_files
-- (path, sha256, size_bytes, s3_version_id — replaced atomically by
-- collection-files-finish); it just wasn't exposed for reads. This RPC exposes
-- it, mirroring get_book_manifest, so the client downloads only files whose
-- sha256 changed, pinned to the committed s3_version_id (a consistent snapshot,
-- same guarantee book content already has).
--
-- Returns: { groupKey, version, files: [{path, sha256, size, s3VersionId}] }
--   A group that has never been written (no row in tc.collection_file_groups)
--   returns { version: 0, files: [] } rather than erroring — "nothing to
--   download" is a normal state for the allowed-words/sample-texts groups most
--   collections never populate.
-- Errors:  not_a_member (42501).
CREATE OR REPLACE FUNCTION tc.get_collection_file_manifest(
    p_collection_id uuid,
    p_group_key     text
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.get_collection_file_manifest(uuid, text) IS
    'E9: per-file current manifest for one collection-file group '
    '(path, sha256, size, s3VersionId) from tc.collection_group_files, used by the '
    'download path to fetch only changed files pinned to their committed s3_version_id. '
    'Mirrors get_book_manifest; a never-written group returns version 0 / empty files.';

GRANT EXECUTE ON FUNCTION tc.get_collection_file_manifest(uuid, text) TO authenticated;
