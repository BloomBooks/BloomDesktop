-- =============================================================================
-- Migration: list_stale_upload_garbage — worklist for the orphaned-upload sweep
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Supports the sweep-stale-uploads edge function (see GOING-LIVE.md "Orphaned-upload
-- sweep"). Context: a check-in uploads changed files to S3 (creating new object
-- versions) BEFORE it commits. If the upload succeeds but the commit does not, the
-- garbage upload becomes the S3 "current" version while the still-referenced
-- committed version is demoted to "noncurrent" -- so the NoncurrentVersionExpiration
-- lifecycle rule would eventually delete the version we still need, and never touch
-- the garbage (which is current). A periodic sweep repairs this; this function is its
-- reference-aware worklist.
--
-- For every file touched by a DEAD check-in transaction (explicitly aborted, or open
-- past its 48h expiry) it returns the S3 key plus the version-id the CURRENT manifest
-- references (NULL if the file is not in any current manifest -- e.g. a brand-new file,
-- or a book whose first commit never landed). The sweep then deletes every S3 version
-- of that key NEWER than the referenced one (all of them when NULL), which are by
-- definition uncommitted.
--
-- Crucially it EXCLUDES any path a still-LIVE transaction (open, not yet expired) is
-- currently uploading, so the sweep can never race a legitimate in-flight check-in.
--
-- Operational, cross-collection function: SECURITY DEFINER (sees all collections) and
-- granted only to service_role -- never callable by `authenticated`.
-- =============================================================================

CREATE OR REPLACE FUNCTION tc.list_stale_upload_garbage()
RETURNS TABLE (
    transaction_kind      text,
    transaction_id        uuid,
    s3_key                text,
    referenced_version_id text
)
LANGUAGE sql
STABLE
SECURITY DEFINER
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

COMMENT ON FUNCTION tc.list_stale_upload_garbage() IS
    'Worklist for the sweep-stale-uploads edge function: per-file S3 keys touched by DEAD '
    '(aborted/expired) check-in transactions, with the currently-referenced s3_version_id as the '
    'delete-newer-than watermark (NULL = nothing references the key). Excludes paths a live '
    'transaction is still uploading. service-role only. See GOING-LIVE.md "Orphaned-upload sweep".';

-- Operational function: never callable by a normal signed-in user.
REVOKE ALL ON FUNCTION tc.list_stale_upload_garbage() FROM public;
GRANT EXECUTE ON FUNCTION tc.list_stale_upload_garbage() TO service_role;
