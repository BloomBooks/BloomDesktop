-- =============================================================================
-- pgTAP tests: the two-phase check-in / collection-files RPCs.
--   1-2. the finish RPCs are service-role only; tc.current_caller reports the JWT caller
--   3-4. paths are NFC-normalized at start and committed under that spelling
--   5.   finish re-checks the lock and the base version (no stale overwrite)
--   6.   a rename to another live book's name is refused at start
--   7.   malformed / colliding manifests are refused at start
--   8.   collection files: NFC at start, service-role finish, idempotent retry
--   9.   the row locks that make start/finish race-safe are present
--   10.  the sweep's per-key re-check
--   11.  aborting an expired new-book check-in
--   12-13. the checkout GUID (CONTRACTS.md v1.9) at start: required for one's own lock,
--        issued when start takes a free lock or creates / resumes a new book
--   14.  an admin force-unlocks without the GUID; the old holder's check-in is refused
--   15-16. a start resume racing a finish is refused at finish (TransactionChanged)
--   17.  start taking a free lock emits a CheckOut event
--   (5 also covers the GUID at finish: it must not have changed since start, and
--   keepCheckedOut keeps it.)
-- (The edge functions call the finish RPCs with the service-role key; here the suite's
-- postgres role stands in for it and passes the caller's identity explicitly.)
-- =============================================================================
-- Run against a local Supabase stack:
--   supabase start
--   supabase test db
-- =============================================================================

BEGIN;

SELECT plan(79);

CREATE SCHEMA IF NOT EXISTS tests;

CREATE OR REPLACE FUNCTION tests.set_jwt(
    p_sub   text,
    p_email text,
    p_name  text DEFAULT NULL
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM set_config(
        'request.jwt.claims',
        json_build_object(
            'sub',            p_sub,
            'email',          p_email,
            'email_verified', true,
            'name',           p_name,
            'role',           'authenticated',
            'aud',            'authenticated'
        )::text,
        true
    );
END;
$$;

-- The stored form of a checkout GUID as the contract defines it (lowercase hex SHA-256 of
-- the lowercase GUID), computed independently of tc._checkout_guid_hash.
CREATE OR REPLACE FUNCTION tests.guid_hash(p_guid text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT encode(sha256(convert_to(lower(p_guid), 'UTF8')), 'hex')
$$;

-- A transaction's current revision, as the finish edge functions read it (with the proposal
-- they verify) and pass it to the finish RPCs as p_expected_revision.
CREATE OR REPLACE FUNCTION tests.rev(p_tx text)
RETURNS bigint
LANGUAGE sql
AS $$
    SELECT revision FROM tc.checkin_transactions WHERE id = p_tx::uuid
$$;
CREATE OR REPLACE FUNCTION tests.cfrev(p_tx text)
RETURNS bigint
LANGUAGE sql
AS $$
    SELECT revision FROM tc.collection_file_transactions WHERE id = p_tx::uuid
$$;

-- Composed vs decomposed spellings of "café.htm" (é = U+00E9, or e + U+0301).
SELECT set_config('tests.nfc', U&'caf\00E9.htm', true);
SELECT set_config('tests.nfd', U&'cafe\0301.htm', true);

-- =============================================================================
-- 1. Privileges: the finish RPCs trust the S3 version-ids they are given, so only the
--    service role (the finish edge functions) may execute them.
-- =============================================================================

SELECT ok(
    NOT has_function_privilege('authenticated', 'tc.checkin_finish_tx(uuid, text, text, text, text, boolean, jsonb, bigint)', 'EXECUTE'),
    '1a: authenticated cannot execute checkin_finish_tx'
);
SELECT ok(
    NOT has_function_privilege('anon', 'tc.checkin_finish_tx(uuid, text, text, text, text, boolean, jsonb, bigint)', 'EXECUTE'),
    '1b: anon cannot execute checkin_finish_tx'
);
SELECT ok(
    has_function_privilege('service_role', 'tc.checkin_finish_tx(uuid, text, text, text, text, boolean, jsonb, bigint)', 'EXECUTE'),
    '1c: service_role can execute checkin_finish_tx'
);
SELECT ok(
    NOT has_function_privilege('authenticated', 'tc.collection_files_finish_tx(uuid, text, text, text, jsonb, bigint)', 'EXECUTE'),
    '1d: authenticated cannot execute collection_files_finish_tx'
);
SELECT ok(
    has_function_privilege('service_role', 'tc.collection_files_finish_tx(uuid, text, text, text, jsonb, bigint)', 'EXECUTE'),
    '1e: service_role can execute collection_files_finish_tx'
);
SELECT ok(
    NOT has_function_privilege('authenticated', 'tc.stale_upload_key_state(text)', 'EXECUTE'),
    '1f: authenticated cannot execute stale_upload_key_state (sweep, service-role only)'
);

-- And a direct call as a signed-in member really is refused.
SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');
SET LOCAL ROLE authenticated;
SELECT throws_ok(
    $$SELECT tc.checkin_finish_tx('00000000-0000-0000-0000-000000000000', 'user-alice-cif', NULL, NULL, NULL, false, '[]', 1)$$,
    '42501',
    NULL,
    '1g: a member calling checkin_finish_tx directly gets permission denied'
);
SELECT throws_ok(
    $$SELECT tc.collection_files_finish_tx('00000000-0000-0000-0000-000000000000', 'user-alice-cif', NULL, NULL, '[]', 1)$$,
    '42501',
    NULL,
    '1h: a member calling collection_files_finish_tx directly gets permission denied'
);
RESET ROLE;

-- =============================================================================
-- 2. tc.current_caller: how the finish edge functions learn who is calling
-- =============================================================================

SELECT is(tc.current_caller() ->> 'userId', 'user-alice-cif', '2a: current_caller reports the JWT sub');
SELECT is(tc.current_caller() ->> 'email', 'alice-cif@example.com', '2b: current_caller reports the email');
SELECT ok(has_function_privilege('authenticated', 'tc.current_caller()', 'EXECUTE'),
    '2c: authenticated can execute current_caller');

SELECT set_config('request.jwt.claims', '{"role":"anon"}', true);
SELECT throws_ok($$SELECT tc.current_caller()$$, 'PT401', NULL,
    '2d: current_caller refuses a token with no user');

-- =============================================================================
-- Fixture: Alice (admin) and Bob (member) in one collection.
-- =============================================================================

SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');
SELECT tc.create_collection('c0000000-0000-0000-0000-00000000c401', 'Checkin Flow Collection');
SELECT tc.members_add('c0000000-0000-0000-0000-00000000c401', 'bob-cif@example.com', 'member');
SELECT tests.set_jwt('user-bob-cif', 'bob-cif@example.com', 'Bob');
SELECT tc.claim_memberships();

-- =============================================================================
-- 3. A new book whose manifest spells a path in decomposed form: start normalizes it.
-- =============================================================================

SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');

SELECT set_config('tests.start1', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c401',
    'Book One', NULL, 'cs-1', '6.5.0',
    jsonb_build_array(
        jsonb_build_object('path', current_setting('tests.nfd'), 'sha256', 'sha-cafe-1', 'size', 10),
        jsonb_build_object('path', 'images/a.png', 'sha256', 'sha-a', 'size', 20)
    ))::text, true);
SELECT set_config('tests.tx1', current_setting('tests.start1')::jsonb ->> 'transactionId', true);
SELECT set_config('tests.book1',
    (SELECT id::text FROM tc.books WHERE instance_id = 'd0000000-0000-0000-0000-00000000c401'), true);

SELECT ok(
    current_setting('tests.nfd') <> current_setting('tests.nfc'),
    '3a: sanity: the two spellings differ before normalization'
);
SELECT ok(
    (current_setting('tests.start1')::jsonb -> 'changedPaths') ? current_setting('tests.nfc')
    AND NOT ((current_setting('tests.start1')::jsonb -> 'changedPaths') ? current_setting('tests.nfd')),
    '3b: changedPaths (the keys the client uploads to) use the NFC spelling'
);
SELECT is(
    (SELECT proposed_files -> 0 ->> 'path' FROM tc.checkin_transactions WHERE id = current_setting('tests.tx1')::uuid),
    current_setting('tests.nfc'),
    '3c: the stored proposed manifest uses the NFC spelling'
);
SELECT ok(
    (SELECT current_setting('tests.nfc') = ANY(changed_paths) FROM tc.checkin_transactions WHERE id = current_setting('tests.tx1')::uuid),
    '3d: the stored changed_paths use the NFC spelling'
);
SELECT ok(
    (SELECT base_version_id IS NULL FROM tc.checkin_transactions WHERE id = current_setting('tests.tx1')::uuid),
    '3e: a new book''s transaction has no base version'
);
SELECT ok(
    (current_setting('tests.start1')::jsonb ->> 'checkoutGuid') IS NOT NULL
    AND (SELECT checkout_guid_hash FROM tc.books WHERE id = current_setting('tests.book1')::uuid)
        = tests.guid_hash(current_setting('tests.start1')::jsonb ->> 'checkoutGuid')
    AND (SELECT checkout_guid_hash FROM tc.checkin_transactions WHERE id = current_setting('tests.tx1')::uuid)
        = tests.guid_hash(current_setting('tests.start1')::jsonb ->> 'checkoutGuid'),
    '3f: a new book''s start issues a checkout GUID; the book and the transaction store its hash'
);

-- =============================================================================
-- 4. Finish (as the edge function would, with the caller passed explicitly)
-- =============================================================================

SELECT set_config('tests.fin1', tc.checkin_finish_tx(
    current_setting('tests.tx1')::uuid, 'user-alice-cif', 'Alice-CIF@example.com', 'Alice', 'first', false,
    jsonb_build_array(
        jsonb_build_object('path', current_setting('tests.nfc'), 's3VersionId', 'sv-cafe-1'),
        jsonb_build_object('path', 'images/a.png', 's3VersionId', 'sv-a-1')
    ), tests.rev(current_setting('tests.tx1')))::text, true);

SELECT is(
    (SELECT s3_version_id FROM tc.version_files
      WHERE book_id = current_setting('tests.book1')::uuid AND path = current_setting('tests.nfc')),
    'sv-cafe-1',
    '4a: the file is committed under the same NFC key it was uploaded to'
);
SELECT ok(
    (SELECT current_version_seq = 1 AND locked_by IS NULL AND checkout_guid_hash IS NULL
       FROM tc.books WHERE id = current_setting('tests.book1')::uuid),
    '4b: the first commit is seq 1 and releases the lock (and its checkout GUID)'
);
SELECT ok(
    (SELECT count(*) = 2 FROM tc.events
      WHERE book_id = current_setting('tests.book1')::uuid AND type IN (1, 2)
        AND by_user_id = 'user-alice-cif' AND by_email = 'alice-cif@example.com' AND by_user_name = 'Alice'),
    '4c: Created + CheckIn events carry the identity passed in (not the service role''s)'
);
SELECT is(
    tc.checkin_finish_tx(current_setting('tests.tx1')::uuid, 'user-alice-cif', 'alice-cif@example.com', 'Alice', 'first', false, '[]', tests.rev(current_setting('tests.tx1'))) ->> 'versionId',
    current_setting('tests.fin1')::jsonb ->> 'versionId',
    '4d: re-calling finish on a finished transaction returns the same version (idempotent)'
);
SELECT throws_ok(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-bob-cif', NULL, NULL, NULL, false, '[]', tests.rev(%1$L))$$, current_setting('tests.tx1')),
    'PT403',
    NULL,
    '4e: finish refuses a user who did not start the transaction'
);

-- =============================================================================
-- 5. Stale check-in: Alice starts from v1, loses the lock (admin force-unlock), Bob
--    commits v2 -- Alice's finish must not overwrite v2 or disturb Bob's lock.
-- =============================================================================

SELECT set_config('tests.a5',
    tc.checkout_book(current_setting('tests.book1')::uuid, 'AliceMachine') ->> 'checkoutGuid', true);
SELECT set_config('tests.tx2', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid, 'd0000000-0000-0000-0000-00000000c401',
    'Book One', NULL, 'cs-2a', '6.5.0',
    jsonb_build_array(
        jsonb_build_object('path', current_setting('tests.nfc'), 'sha256', 'sha-cafe-alice', 'size', 11),
        jsonb_build_object('path', 'images/a.png', 'sha256', 'sha-a', 'size', 20)
    ), current_setting('tests.a5')) ->> 'transactionId', true);

SELECT is(
    (SELECT base_version_id::text FROM tc.checkin_transactions WHERE id = current_setting('tests.tx2')::uuid),
    current_setting('tests.fin1')::jsonb ->> 'versionId',
    '5a: with no baseVersionId sent, start records the book''s current version as the base'
);

SELECT tc.force_unlock(current_setting('tests.book1')::uuid);

SELECT tests.set_jwt('user-bob-cif', 'bob-cif@example.com', 'Bob');
SELECT set_config('tests.b5',
    tc.checkout_book(current_setting('tests.book1')::uuid, 'BobMachine') ->> 'checkoutGuid', true);
SELECT set_config('tests.tx3', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid, 'd0000000-0000-0000-0000-00000000c401',
    'Book One', (current_setting('tests.fin1')::jsonb ->> 'versionId')::uuid, 'cs-2b', '6.5.0',
    jsonb_build_array(
        jsonb_build_object('path', current_setting('tests.nfc'), 'sha256', 'sha-cafe-bob', 'size', 12),
        jsonb_build_object('path', 'images/a.png', 'sha256', 'sha-a', 'size', 20)
    ), current_setting('tests.b5')) ->> 'transactionId', true);
SELECT tc.checkin_finish_tx(current_setting('tests.tx3')::uuid, 'user-bob-cif', 'bob-cif@example.com', 'Bob', 'bob', true,
    jsonb_build_array(jsonb_build_object('path', current_setting('tests.nfc'), 's3VersionId', 'sv-cafe-bob')),
    tests.rev(current_setting('tests.tx3')));

SELECT ok(
    (SELECT current_version_seq = 2 AND locked_by = 'user-bob-cif' FROM tc.books WHERE id = current_setting('tests.book1')::uuid),
    '5b: sanity: Bob committed v2 and kept the book checked out'
);
SELECT is(
    (SELECT checkout_guid_hash FROM tc.books WHERE id = current_setting('tests.book1')::uuid),
    tests.guid_hash(current_setting('tests.b5')),
    '5b2: keepCheckedOut keeps the checkout GUID (not rotated, not cleared)'
);

SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-alice-cif', NULL, NULL, 'stale', false, %2$L, tests.rev(%1$L))$$,
        current_setting('tests.tx2'),
        jsonb_build_array(jsonb_build_object('path', current_setting('tests.nfc'), 's3VersionId', 'sv-cafe-alice'))::text),
    '%LockHeldByOther%',
    '5c: Alice''s finish is refused while Bob holds the lock'
);
SELECT ok(
    (SELECT current_version_seq = 2 AND locked_by = 'user-bob-cif' FROM tc.books WHERE id = current_setting('tests.book1')::uuid),
    '5d: v2 and Bob''s lock are untouched'
);

-- Even once Alice holds the lock again, it is a new checkout (a new GUID), not the one her
-- transaction started under.
SELECT tc.unlock_book(current_setting('tests.book1')::uuid, current_setting('tests.b5'));
SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');
SELECT set_config('tests.a5b',
    tc.checkout_book(current_setting('tests.book1')::uuid, 'AliceMachine') ->> 'checkoutGuid', true);

SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-alice-cif', NULL, NULL, 'stale', false, %2$L, tests.rev(%1$L))$$,
        current_setting('tests.tx2'),
        jsonb_build_array(jsonb_build_object('path', current_setting('tests.nfc'), 's3VersionId', 'sv-cafe-alice'))::text),
    '%CheckoutElsewhere%',
    '5e: Alice''s finish is refused because the checkout GUID changed since her start'
);

-- Pretend the transaction had started under this checkout: her transaction is still based
-- on v1, which is gone.
UPDATE tc.checkin_transactions
SET checkout_guid_hash = tests.guid_hash(current_setting('tests.a5b'))
WHERE id = current_setting('tests.tx2')::uuid;

SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-alice-cif', NULL, NULL, 'stale', false, %2$L, tests.rev(%1$L))$$,
        current_setting('tests.tx2'),
        jsonb_build_array(jsonb_build_object('path', current_setting('tests.nfc'), 's3VersionId', 'sv-cafe-alice'))::text),
    '%BaseVersionSuperseded%',
    '5e2: with the same checkout, Alice''s finish is refused because the book moved on from her base version'
);
SELECT is(
    (SELECT s3_version_id FROM tc.version_files
      WHERE book_id = current_setting('tests.book1')::uuid AND path = current_setting('tests.nfc')),
    'sv-cafe-bob',
    '5f: Bob''s committed file is still the current one'
);

SELECT tc.unlock_book(current_setting('tests.book1')::uuid, current_setting('tests.a5b'));
SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-alice-cif', NULL, NULL, 'stale', false, '[]', tests.rev(%1$L))$$,
        current_setting('tests.tx2')),
    '%LockHeldByOther%',
    '5g: a finish by someone who no longer holds the lock is refused even when the lock is free'
);

-- =============================================================================
-- 6. Renaming an existing book to another live book's name is refused at start
-- =============================================================================

INSERT INTO tc.books (id, collection_id, instance_id, name, created_by)
VALUES ('b0000000-0000-0000-0000-00000000c402', 'c0000000-0000-0000-0000-00000000c401',
        'd0000000-0000-0000-0000-00000000c402', 'Book Two', 'user-alice-cif');

SELECT set_config('tests.a6',
    tc.checkout_book(current_setting('tests.book1')::uuid, 'AliceMachine') ->> 'checkoutGuid', true);
SELECT throws_like(
    format($$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', %L, 'd0000000-0000-0000-0000-00000000c401',
        'book two', NULL, 'cs-x', '6.5.0', '[]', %L)$$, current_setting('tests.book1'), current_setting('tests.a6')),
    '%NameConflict%',
    '6a: an existing book cannot be renamed (case-insensitively) to another live book''s name'
);
SELECT lives_ok(
    format($$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', %L, 'd0000000-0000-0000-0000-00000000c401',
        'Book One Renamed', NULL, 'cs-x', '6.5.0', '[]', %L)$$, current_setting('tests.book1'), current_setting('tests.a6')),
    '6b: sanity: a rename to a free name is accepted'
);

-- =============================================================================
-- 7. Malformed manifests are refused at start (PT400 InvalidManifest)
-- =============================================================================

SELECT throws_ok(
    format($$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c409',
        'Book Nine', NULL, 'cs-9', '6.5.0', %L)$$,
        jsonb_build_array(
            jsonb_build_object('path', current_setting('tests.nfc'), 'sha256', 's1', 'size', 1),
            jsonb_build_object('path', current_setting('tests.nfd'), 'sha256', 's2', 'size', 2))::text),
    'PT400',
    NULL,
    '7a: two spellings of one path (which would share one S3 key) are refused'
);
SELECT throws_like(
    $$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c409',
        'Book Nine', NULL, 'cs-9', '6.5.0', '[{"path":"../escape.htm","sha256":"s","size":1}]')$$,
    '%InvalidManifest%',
    '7b: a ".." path segment is refused'
);
SELECT throws_like(
    $$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c409',
        'Book Nine', NULL, 'cs-9', '6.5.0', '[{"path":"a.htm","size":1}]')$$,
    '%InvalidManifest%',
    '7c: an entry with no sha256 is refused'
);
SELECT throws_like(
    $$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c409',
        'Book Nine', NULL, 'cs-9', '6.5.0', '[{"path":"a.htm","sha256":"s","size":100000000000000000000}]')$$,
    '%InvalidManifest%',
    '7d: a size too big for the bigint size columns is refused (not a 500)'
);
SELECT ok(
    NOT EXISTS (SELECT 1 FROM tc.books WHERE instance_id = 'd0000000-0000-0000-0000-00000000c409'),
    '7d: a refused start creates no book row'
);

-- =============================================================================
-- 8. Collection files: NFC at start, service-role finish, idempotent retry
-- =============================================================================

SELECT set_config('tests.cfstart', tc.collection_files_start_tx(
    'c0000000-0000-0000-0000-00000000c401', 'other', 0,
    jsonb_build_array(jsonb_build_object('path', current_setting('tests.nfd'), 'sha256', 'sha-cf', 'size', 5))
    )::text, true);
SELECT set_config('tests.cftx', current_setting('tests.cfstart')::jsonb ->> 'transactionId', true);

SELECT ok(
    (current_setting('tests.cfstart')::jsonb -> 'changedPaths') ? current_setting('tests.nfc'),
    '8a: collection-file changedPaths use the NFC spelling'
);
SELECT is(
    tc.collection_files_finish_tx(current_setting('tests.cftx')::uuid, 'user-alice-cif', 'alice-cif@example.com', 'Alice',
        jsonb_build_array(jsonb_build_object('path', current_setting('tests.nfc'), 's3VersionId', 'sv-cf-1')),
        tests.cfrev(current_setting('tests.cftx'))) ->> 'version',
    '1',
    '8b: finish bumps the group to version 1'
);
SELECT is(
    (SELECT gf.s3_version_id FROM tc.collection_group_files gf
       JOIN tc.collection_file_groups g ON g.id = gf.group_id
      WHERE g.collection_id = 'c0000000-0000-0000-0000-00000000c401' AND g.group_key = 'other'
        AND gf.path = current_setting('tests.nfc')),
    'sv-cf-1',
    '8c: the collection file is committed under the NFC key'
);
SELECT is(
    tc.collection_files_finish_tx(current_setting('tests.cftx')::uuid, 'user-alice-cif', 'alice-cif@example.com', 'Alice', '[]', tests.cfrev(current_setting('tests.cftx'))) ->> 'version',
    '1',
    '8d: re-calling finish returns the same version (idempotent)'
);
SELECT throws_ok(
    format($$SELECT tc.collection_files_finish_tx(%1$L, 'user-bob-cif', NULL, NULL, '[]', tests.cfrev(%1$L))$$, current_setting('tests.cftx')),
    'PT403',
    NULL,
    '8e: collection-files finish refuses a user who did not start the transaction'
);

-- =============================================================================
-- 9. Row locks that make concurrent start/finish calls safe. A real two-session race
--    cannot run in single-session pgTAP, so guard against the locks being dropped.
-- =============================================================================

SELECT ok(
    pg_get_functiondef('tc.checkin_start_tx(uuid, uuid, uuid, text, uuid, text, text, jsonb, text)'::regprocedure)
        ~ 'WHERE id = p_book_id AND collection_id = p_collection_id\s+FOR UPDATE',
    '9a: checkin_start_tx locks an existing book''s row before checking/taking the lock'
);
SELECT ok(
    pg_get_functiondef('tc.checkin_finish_tx(uuid, text, text, text, text, boolean, jsonb, bigint)'::regprocedure)
        LIKE '%FROM tc.checkin_transactions WHERE id = p_transaction_id FOR UPDATE%',
    '9b: checkin_finish_tx locks its transaction row (concurrent retries are idempotent)'
);
SELECT ok(
    pg_get_functiondef('tc.checkin_finish_tx(uuid, text, text, text, text, boolean, jsonb, bigint)'::regprocedure)
        LIKE '%FROM tc.books WHERE id = v_tx.book_id FOR UPDATE%',
    '9c: checkin_finish_tx locks the book row while re-checking lock and base version'
);
SELECT ok(
    pg_get_functiondef('tc.collection_files_finish_tx(uuid, text, text, text, jsonb, bigint)'::regprocedure)
        LIKE '%FROM tc.collection_file_transactions WHERE id = p_transaction_id FOR UPDATE%',
    '9d: collection_files_finish_tx locks its transaction row'
);

-- =============================================================================
-- 10. stale_upload_key_state: the sweep's just-before-delete re-check
-- =============================================================================

INSERT INTO tc.checkin_transactions (collection_id, book_id, started_by, proposed_name,
                                     changed_paths, status, aborted_at)
VALUES ('c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid,
        'user-alice-cif', 'Book One', ARRAY['images/a.png'], 'aborted', now());

SELECT set_config('tests.akey',
    'tc/c0000000-0000-0000-0000-00000000c401/books/d0000000-0000-0000-0000-00000000c401/images/a.png', true);

SELECT is(
    tc.stale_upload_key_state(current_setting('tests.akey')),
    '{"stillStale": true, "referencedVersionId": "sv-a-1"}'::jsonb,
    '10a: a key only a dead transaction touched is still stale, with its current referenced version'
);

INSERT INTO tc.checkin_transactions (collection_id, book_id, started_by, proposed_name,
                                     changed_paths, status)
VALUES ('c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid,
        'user-bob-cif', 'Book One', ARRAY['images/a.png'], 'open');

SELECT is(
    tc.stale_upload_key_state(current_setting('tests.akey')) ->> 'stillStale',
    'false',
    '10b: once a live transaction touches the key it is no longer stale'
);
SELECT is(
    tc.stale_upload_key_state('tc/nowhere/books/none/x') ->> 'stillStale',
    'false',
    '10c: an unknown key is not stale'
);

-- =============================================================================
-- 11. Aborting an expired check-in of a never-finished new book still succeeds
--     (abort must not reap its own target away first and then report 404)
-- =============================================================================

SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');
SELECT set_config('tests.tx11', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c411',
    'Book Eleven', NULL, 'cs-11', '6.5.0', '[]') ->> 'transactionId', true);
UPDATE tc.checkin_transactions SET expires_at = now() - INTERVAL '1 hour'
WHERE id = current_setting('tests.tx11')::uuid;

SELECT lives_ok(
    format($$SELECT tc.checkin_abort_tx(%L)$$, current_setting('tests.tx11')),
    '11a: aborting an expired new-book check-in succeeds'
);
SELECT ok(
    NOT EXISTS (SELECT 1 FROM tc.books WHERE instance_id = 'd0000000-0000-0000-0000-00000000c411'),
    '11b: and removes the never-finished book'
);

-- =============================================================================
-- 12. Existing book: starting a check-in of one's own lock needs its checkout GUID (the
--     copy that has it); taking a free lock issues a new one.
-- =============================================================================

SELECT ok(
    (SELECT locked_by = 'user-alice-cif' AND checkout_guid_hash = tests.guid_hash(current_setting('tests.a6'))
       FROM tc.books WHERE id = current_setting('tests.book1')::uuid),
    '12-sanity: Alice still holds book one under the GUID from section 6'
);

SELECT throws_like(
    format($$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', %L, 'd0000000-0000-0000-0000-00000000c401',
        'Book One Renamed', NULL, 'cs-12', '6.5.0', '[]')$$, current_setting('tests.book1')),
    '%CheckoutElsewhere%',
    '12a: without the GUID, start refuses the holder (CheckoutElsewhere)'
);
SELECT throws_like(
    format($$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', %L, 'd0000000-0000-0000-0000-00000000c401',
        'Book One Renamed', NULL, 'cs-12', '6.5.0', '[]', %L)$$,
        current_setting('tests.book1'), gen_random_uuid()::text),
    '%CheckoutElsewhere%',
    '12b: with a wrong GUID, start refuses the holder (CheckoutElsewhere)'
);

SELECT set_config('tests.s12', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid, 'd0000000-0000-0000-0000-00000000c401',
    'Book One Renamed', NULL, 'cs-12', '6.5.0', '[]', current_setting('tests.a6'))::text, true);
SELECT ok(
    (current_setting('tests.s12')::jsonb ? 'transactionId')
    AND NOT (current_setting('tests.s12')::jsonb ? 'checkoutGuid')
    AND (SELECT checkout_guid_hash FROM tc.books WHERE id = current_setting('tests.book1')::uuid)
        = tests.guid_hash(current_setting('tests.a6')),
    '12c: with the right GUID, start succeeds, issues no new GUID and keeps the hash'
);

-- Take-if-free: release the lock, then start with no GUID.
SELECT tc.unlock_book(current_setting('tests.book1')::uuid, current_setting('tests.a6'));
SELECT set_config('tests.s12e', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid, 'd0000000-0000-0000-0000-00000000c401',
    'Book One Renamed', NULL, 'cs-12', '6.5.0', '[]')::text, true);
SELECT ok(
    (current_setting('tests.s12e')::jsonb ->> 'checkoutGuid') IS NOT NULL
    AND (SELECT locked_by = 'user-alice-cif'
                AND checkout_guid_hash = tests.guid_hash(current_setting('tests.s12e')::jsonb ->> 'checkoutGuid')
           FROM tc.books WHERE id = current_setting('tests.book1')::uuid)
    AND (SELECT checkout_guid_hash FROM tc.checkin_transactions
          WHERE id = (current_setting('tests.s12e')::jsonb ->> 'transactionId')::uuid)
        = tests.guid_hash(current_setting('tests.s12e')::jsonb ->> 'checkoutGuid'),
    '12d: start on a free book takes the lock and issues a GUID (stored on the book and the transaction)'
);

-- =============================================================================
-- 13. New book: start issues a GUID, and resuming the never-committed book needs it (or
--     re-issues one when the row has none).
-- =============================================================================

SELECT set_config('tests.s13', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c413',
    'Book Thirteen', NULL, 'cs-13', '6.5.0', '[]')::text, true);
SELECT ok(
    (current_setting('tests.s13')::jsonb ->> 'checkoutGuid') IS NOT NULL
    AND (SELECT checkout_guid_hash FROM tc.books WHERE instance_id = 'd0000000-0000-0000-0000-00000000c413')
        = tests.guid_hash(current_setting('tests.s13')::jsonb ->> 'checkoutGuid'),
    '13a: a new book''s start issues a GUID'
);

SELECT throws_like(
    $$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c413',
        'Book Thirteen', NULL, 'cs-13', '6.5.0', '[]')$$,
    '%CheckoutElsewhere%',
    '13b: resuming one''s own new book without its GUID is refused (CheckoutElsewhere)'
);

SELECT ok(
    (SELECT r ->> 'transactionId' = current_setting('tests.s13')::jsonb ->> 'transactionId'
            AND NOT (r ? 'checkoutGuid')
       FROM (SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c413',
                 'Book Thirteen', NULL, 'cs-13', '6.5.0', '[]',
                 current_setting('tests.s13')::jsonb ->> 'checkoutGuid') AS r) s),
    '13c: resuming with the GUID continues the same transaction and issues no new GUID'
);

-- A never-committed row with no hash at all: resuming re-issues one.
UPDATE tc.books SET checkout_guid_hash = NULL WHERE instance_id = 'd0000000-0000-0000-0000-00000000c413';
SELECT set_config('tests.s13d', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c413',
    'Book Thirteen', NULL, 'cs-13', '6.5.0', '[]')::text, true);
SELECT ok(
    (current_setting('tests.s13d')::jsonb ->> 'checkoutGuid') IS NOT NULL
    AND (current_setting('tests.s13d')::jsonb ->> 'checkoutGuid') <> (current_setting('tests.s13')::jsonb ->> 'checkoutGuid')
    AND (current_setting('tests.s13d')::jsonb ->> 'transactionId') = (current_setting('tests.s13')::jsonb ->> 'transactionId')
    AND (SELECT checkout_guid_hash FROM tc.books WHERE instance_id = 'd0000000-0000-0000-0000-00000000c413')
        = tests.guid_hash(current_setting('tests.s13d')::jsonb ->> 'checkoutGuid')
    AND (SELECT checkout_guid_hash FROM tc.checkin_transactions
          WHERE id = (current_setting('tests.s13d')::jsonb ->> 'transactionId')::uuid)
        = tests.guid_hash(current_setting('tests.s13d')::jsonb ->> 'checkoutGuid'),
    '13d: resuming a new book whose row has no hash issues a new GUID for the same transaction'
);

-- =============================================================================
-- 14. An admin can always cancel someone else's checkout without its GUID; the old
--     holder's in-flight check-in is then refused.
-- =============================================================================

SELECT tests.set_jwt('user-bob-cif', 'bob-cif@example.com', 'Bob');
SELECT set_config('tests.b14',
    tc.checkout_book('b0000000-0000-0000-0000-00000000c402', 'BobMachine') ->> 'checkoutGuid', true);
SELECT set_config('tests.tx14', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', 'b0000000-0000-0000-0000-00000000c402', 'd0000000-0000-0000-0000-00000000c402',
    'Book Two', NULL, 'cs-14', '6.5.0',
    jsonb_build_array(jsonb_build_object('path', 'two.htm', 'sha256', 'sha-two', 'size', 3)),
    current_setting('tests.b14')) ->> 'transactionId', true);

-- Alice (admin) has never seen Bob's GUID.
SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');
SELECT lives_ok(
    $$SELECT tc.force_unlock('b0000000-0000-0000-0000-00000000c402')$$,
    '14a: an admin with no GUID can force-unlock a book another member has checked out'
);
SELECT ok(
    current_setting('tests.tx14', true) IS NOT NULL
    AND (SELECT locked_by IS NULL AND checkout_guid_hash IS NULL
           FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000c402'),
    '14b: force_unlock clears the lock and the checkout GUID hash'
);
SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-bob-cif', NULL, NULL, 'stale', false, %2$L, tests.rev(%1$L))$$,
        current_setting('tests.tx14'),
        jsonb_build_array(jsonb_build_object('path', 'two.htm', 's3VersionId', 'sv-two'))::text),
    '%LockHeldByOther%',
    '14c: the old holder''s check-in under the now-stale GUID is refused'
);
-- =============================================================================
-- 15. A checkin-start resume between the finish edge function's read of the transaction
--     and the finish RPC: the finish is refused (TransactionChanged), since the uploads
--     it verified belong to the older proposal.
-- =============================================================================

SELECT set_config('tests.tx15', current_setting('tests.s12e')::jsonb ->> 'transactionId', true);
-- What checkin-finish read (with the proposal it went on to verify against S3).
SELECT set_config('tests.rev15', tests.rev(current_setting('tests.tx15'))::text, true);
SELECT set_config('tests.s15', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', current_setting('tests.book1')::uuid, 'd0000000-0000-0000-0000-00000000c401',
    'Book One Renamed', NULL, 'cs-15', '6.5.0',
    jsonb_build_array(jsonb_build_object('path', 'fifteen.htm', 'sha256', 'sha-15-new', 'size', 15)),
    current_setting('tests.s12e')::jsonb ->> 'checkoutGuid')::text, true);

SELECT ok(
    (current_setting('tests.s15')::jsonb ->> 'transactionId') = current_setting('tests.tx15')
    AND tests.rev(current_setting('tests.tx15')) = current_setting('tests.rev15')::bigint + 1,
    '15a: sanity: the concurrent start resumed the same transaction and bumped its revision'
);
SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%L, 'user-alice-cif', NULL, NULL, 'raced', false, %L, %s)$$,
        current_setting('tests.tx15'),
        jsonb_build_array(jsonb_build_object('path', 'fifteen.htm', 's3VersionId', 'sv-15-old'))::text,
        current_setting('tests.rev15')),
    '%TransactionChanged%',
    '15b: a finish carrying the revision it verified before the resume is refused (TransactionChanged)'
);
SELECT ok(
    (SELECT status = 'open' FROM tc.checkin_transactions WHERE id = current_setting('tests.tx15')::uuid)
    AND (SELECT current_version_seq = 2 AND locked_by = 'user-alice-cif'
           FROM tc.books WHERE id = current_setting('tests.book1')::uuid),
    '15c: the refused finish commits nothing and leaves the transaction open'
);
SELECT is(
    tc.checkin_finish_tx(current_setting('tests.tx15')::uuid, 'user-alice-cif', NULL, NULL, 'retried', false,
        jsonb_build_array(jsonb_build_object('path', 'fifteen.htm', 's3VersionId', 'sv-15-new')),
        tests.rev(current_setting('tests.tx15'))) ->> 'seq',
    '3',
    '15d: a finish that verified the current revision commits'
);

-- =============================================================================
-- 16. The same race for collection files.
-- =============================================================================

SELECT set_config('tests.cf16', tc.collection_files_start_tx(
    'c0000000-0000-0000-0000-00000000c401', 'sample-texts', 0,
    jsonb_build_array(jsonb_build_object('path', 's.txt', 'sha256', 'sha-s-old', 'size', 1))
    ) ->> 'transactionId', true);
SELECT is(tests.cfrev(current_setting('tests.cf16')), 1::bigint,
    '16a: a new collection-files transaction starts at revision 1');
SELECT is(
    tc.collection_files_start_tx(
        'c0000000-0000-0000-0000-00000000c401', 'sample-texts', 0,
        jsonb_build_array(jsonb_build_object('path', 's.txt', 'sha256', 'sha-s-new', 'size', 2))
    ) ->> 'transactionId',
    current_setting('tests.cf16'),
    '16b: sanity: a second start resumes the same transaction'
);
SELECT throws_like(
    format($$SELECT tc.collection_files_finish_tx(%L, 'user-alice-cif', NULL, NULL, %L, 1)$$,
        current_setting('tests.cf16'),
        jsonb_build_array(jsonb_build_object('path', 's.txt', 's3VersionId', 'sv-s-old'))::text),
    '%TransactionChanged%',
    '16c: a collection-files finish carrying the pre-resume revision is refused (TransactionChanged)'
);
SELECT is(
    tc.collection_files_finish_tx(current_setting('tests.cf16')::uuid, 'user-alice-cif', NULL, NULL,
        jsonb_build_array(jsonb_build_object('path', 's.txt', 's3VersionId', 'sv-s-new')),
        tests.cfrev(current_setting('tests.cf16'))) ->> 'version',
    '1',
    '16d: with the current revision (2) the collection-files finish commits'
);

-- =============================================================================
-- 17. checkin-start taking a free lock records a CheckOut event, like checkout_book, so
--     other clients polling get_changes see the new lock.
-- =============================================================================

SELECT ok(
    (SELECT locked_by IS NULL FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000c402'),
    '17-sanity: book two is free (force-unlocked in section 14)'
);
SELECT set_config('tests.ev17', (SELECT COALESCE(max(id), 0) FROM tc.events)::text, true);
SELECT set_config('tests.s17', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', 'b0000000-0000-0000-0000-00000000c402', 'd0000000-0000-0000-0000-00000000c402',
    'Book Two', NULL, 'cs-17', '6.5.0', '[]')::text, true);

SELECT ok(
    (SELECT count(*) = 1 FROM tc.events
      WHERE id > current_setting('tests.ev17')::bigint
        AND collection_id = 'c0000000-0000-0000-0000-00000000c401'
        AND book_id = 'b0000000-0000-0000-0000-00000000c402'
        AND type = 0 AND by_user_id = 'user-alice-cif' AND by_email = 'alice-cif@example.com'
        AND by_user_name = 'Alice' AND book_name = 'Book Two')
    AND (SELECT count(*) = 1 FROM tc.events WHERE id > current_setting('tests.ev17')::bigint),
    '17a: taking the free lock emitted exactly one CheckOut event, with the caller''s identity'
);
SELECT ok(
    (SELECT (c -> 'events') @> '[{"type": 0, "book_id": "b0000000-0000-0000-0000-00000000c402"}]'::jsonb
            AND (c -> 'books') @> jsonb_build_array(jsonb_build_object(
                'id', 'b0000000-0000-0000-0000-00000000c402',
                'locked_by', 'user-alice-cif',
                'checkoutGuidHash', tests.guid_hash(current_setting('tests.s17')::jsonb ->> 'checkoutGuid')))
       FROM (SELECT tc.get_changes('c0000000-0000-0000-0000-00000000c401',
                                   current_setting('tests.ev17')::bigint) AS c) s),
    '17b: get_changes reports the event and the book''s new lock'
);
SELECT tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', 'b0000000-0000-0000-0000-00000000c402', 'd0000000-0000-0000-0000-00000000c402',
    'Book Two', NULL, 'cs-17', '6.5.0', '[]', current_setting('tests.s17')::jsonb ->> 'checkoutGuid');
SELECT ok(
    (SELECT count(*) = 1 FROM tc.events WHERE id > current_setting('tests.ev17')::bigint),
    '17c: starting again under one''s own lock records no further CheckOut event'
);
-- =============================================================================
-- 18. A deleted book (tombstone) can't be checked in to
-- =============================================================================

UPDATE tc.books SET deleted_at = now(), locked_by = NULL, locked_at = NULL
WHERE id = current_setting('tests.book1')::uuid;
SELECT tests.set_jwt('user-alice-cif', 'alice-cif@example.com', 'Alice');
SELECT throws_like(
    format($$SELECT tc.checkin_start_tx('c0000000-0000-0000-0000-00000000c401', %L, 'd0000000-0000-0000-0000-00000000c401',
        'Book One', NULL, 'cs-x', '6.5.0', '[]')$$, current_setting('tests.book1')),
    '%book_not_found%',
    '18a: check-in start refuses a deleted book instead of taking its lock'
);
-- ...and a book deleted after its check-in started can't be finished either.
SELECT set_config('tests.tx18', tc.checkin_start_tx(
    'c0000000-0000-0000-0000-00000000c401', NULL, 'd0000000-0000-0000-0000-00000000c418',
    'Book Eighteen', NULL, 'cs-18', '6.5.0', '[]') ->> 'transactionId', true);
UPDATE tc.books SET deleted_at = now()
WHERE instance_id = 'd0000000-0000-0000-0000-00000000c418';
SELECT throws_like(
    format($$SELECT tc.checkin_finish_tx(%1$L, 'user-alice-cif', 'alice-cif@example.com', 'Alice', 'eighteen', false, '[]', tests.rev(%1$L))$$,
        current_setting('tests.tx18')),
    '%book_not_found%',
    '18b: finish refuses a book deleted after start (no invisible version)'
);
SELECT * FROM finish();ROLLBACK;
