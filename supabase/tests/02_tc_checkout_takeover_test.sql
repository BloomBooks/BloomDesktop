-- =============================================================================
-- pgTAP tests: the checkout GUID (CONTRACTS.md v1.9/v1.10). The client that checks a book out
-- makes a random GUID, keeps it in the book folder's .checkout file and sends it to
-- checkout_book (idempotent for the same GUID); the server stores only its hash, which
-- members can read. Unlock, delete and (in
-- 04_tc_checkin_flow_test.sql) check-in by the holder require the GUID, and
-- checkout_book_takeover lets a DIFFERENT account take the lock over only by presenting it
-- (dogfood batch 1, item 9: account switch in the same local copy).
-- =============================================================================
-- Run against a local Supabase stack:
--   supabase start
--   supabase test db
-- =============================================================================

BEGIN;

SELECT plan(50);

SELECT has_function('tc', 'checkout_book_takeover', 'tc.checkout_book_takeover() exists');

-- Helper: set a fake JWT so auth.jwt() returns a known sub/email (same helper as
-- 01_tc_schema_test.sql; re-declared here since each test file runs standalone).
CREATE SCHEMA IF NOT EXISTS tests;

CREATE OR REPLACE FUNCTION tests.set_jwt(
    p_sub   text,
    p_email text,
    p_email_verified boolean DEFAULT true
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
            'email_verified', p_email_verified,
            'role',           'authenticated',
            'aud',            'authenticated'
        )::text,
        true
    );
END;
$$;

-- The hash as the contract defines it, computed here independently of tc._checkout_guid_hash:
-- lowercase hex SHA-256 of the UTF-8 bytes of the GUID's lowercase string form.
CREATE OR REPLACE FUNCTION tests.guid_hash(p_guid text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT encode(sha256(convert_to(lower(p_guid), 'UTF8')), 'hex')
$$;

-- The book's current stored hash.
CREATE OR REPLACE FUNCTION tests.stored_hash()
RETURNS text
LANGUAGE sql
AS $$
    SELECT checkout_guid_hash FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'
$$;

-- What a client does to check a book out (v1.10): make a GUID, then send it. Returns the GUID
-- when the checkout succeeded, else NULL.
CREATE OR REPLACE FUNCTION tests.checkout(p_book_id uuid, p_machine text)
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    v_guid text := gen_random_uuid()::text;
BEGIN
    IF (tc.checkout_book(p_book_id, p_machine, v_guid) ->> 'success') = 'true' THEN
        RETURN v_guid;
    END IF;
    RETURN NULL;
END;
$$;

-- =============================================================================
-- Fixture: a collection with Alice (admin) and Bob (member, claimed), and a book Alice
-- checks out on "SharedMachine". Uses the public RPCs (create_collection/members_add/
-- claim_memberships), matching 01_tc_schema_test.sql's own fixture convention.
-- =============================================================================

SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

SELECT lives_ok(
    $$SELECT tc.create_collection('c0000000-0000-0000-0000-00000000a001'::uuid, 'Takeover Test Collection')$$,
    '0a: create_collection succeeds for Alice'
);

SELECT lives_ok(
    $$SELECT tc.members_add('c0000000-0000-0000-0000-00000000a001', 'bob-tko@example.com', 'member')$$,
    '0b: Alice adds Bob as an approved member'
);

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT lives_ok(
    $$SELECT tc.claim_memberships()$$,
    '0c: Bob claims his membership'
);

SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

-- Insert a test book directly (SECURITY DEFINER helper — RLS bypassed for setup), matching
-- 01_tc_schema_test.sql section 5's own convention.
INSERT INTO tc.books (id, collection_id, instance_id, name, created_by)
VALUES (
    'b0000000-0000-0000-0000-00000000a001'::uuid,
    'c0000000-0000-0000-0000-00000000a001'::uuid,
    'b0000000-0000-0000-0000-00000000a002'::uuid,
    'Takeover Test Book',
    'user-alice-tko'
);

-- Alice's client makes a GUID (and would save it in the .checkout file) before checking out.
SELECT set_config('tests.alice_guid', gen_random_uuid()::text, true);

SELECT throws_ok(
    $$SELECT tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', NULL)$$,
    '22023', NULL,
    '0d1: checkout_book with no GUID is refused'
);

SELECT throws_ok(
    $$SELECT tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', '  ')$$,
    '22023', NULL,
    '0d2: checkout_book with a blank GUID is refused'
);

SELECT set_config('tests.checkout1',
    tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'SharedMachine',
        upper(current_setting('tests.alice_guid')))::text,
    true);

SELECT ok(
    (current_setting('tests.checkout1')::jsonb ->> 'success') = 'true'
    AND (current_setting('tests.checkout1')::jsonb ->> 'locked_by') = 'user-alice-tko'
    AND NOT (current_setting('tests.checkout1')::jsonb ? 'checkoutGuid'),
    '0d: a checkout with a client-supplied GUID succeeds and returns no GUID'
);

SELECT is(
    tests.stored_hash(),
    tests.guid_hash(current_setting('tests.alice_guid')),
    '0e: the book row stores sha256 (lowercase hex) of the lowercase GUID (sent uppercase)'
);

-- A retry with the same GUID (the first response was lost) is the same success, changing nothing.
SELECT set_config('tests.retry',
    tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'RetryMachine',
        current_setting('tests.alice_guid'))::text,
    true);

SELECT ok(
    (current_setting('tests.retry')::jsonb ->> 'success') = 'true'
    AND NOT (current_setting('tests.retry')::jsonb ? 'locked_by_me')
    AND (current_setting('tests.retry')::jsonb ->> 'locked_by_machine') = 'SharedMachine',
    '0e1: a retry with the same GUID succeeds again, reporting the existing lock'
);

SELECT ok(
    tests.stored_hash() = tests.guid_hash(current_setting('tests.alice_guid'))
    AND (SELECT locked_by_machine FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'SharedMachine'
    AND (SELECT count(*) = 1 FROM tc.events
         WHERE book_id = 'b0000000-0000-0000-0000-00000000a001' AND type = 0),
    '0e2: the retry changes nothing and emits no second CheckOut event'
);

SELECT ok(
    has_column_privilege('authenticated', 'tc.books', 'checkout_guid_hash', 'SELECT'),
    '0f: members can SELECT tc.books.checkout_guid_hash'
);

SELECT is(
    (SELECT b ->> 'checkoutGuidHash'
       FROM jsonb_array_elements(tc.get_collection_state('c0000000-0000-0000-0000-00000000a001') -> 'books') b
      WHERE b ->> 'id' = 'b0000000-0000-0000-0000-00000000a001'),
    tests.guid_hash(current_setting('tests.alice_guid')),
    '0g: get_collection_state returns the hash as checkoutGuidHash'
);

SELECT is(
    (SELECT b ->> 'checkoutGuidHash'
       FROM jsonb_array_elements(tc.get_changes('c0000000-0000-0000-0000-00000000a001', 0) -> 'books') b
      WHERE b ->> 'id' = 'b0000000-0000-0000-0000-00000000a001'),
    tests.guid_hash(current_setting('tests.alice_guid')),
    '0h: get_changes returns the hash as checkoutGuidHash'
);

SELECT ok(
    position(current_setting('tests.alice_guid') IN tc.get_collection_state('c0000000-0000-0000-0000-00000000a001')::text) = 0
    AND position(current_setting('tests.alice_guid') IN tc.get_changes('c0000000-0000-0000-0000-00000000a001', 0)::text) = 0,
    '0i: neither get_collection_state nor get_changes contains the GUID itself'
);

-- Checked as the superuser running the tests, so every column of every row counts.
SELECT ok(
    NOT EXISTS (SELECT 1 FROM tc.books b WHERE b::text LIKE '%' || current_setting('tests.alice_guid') || '%')
    AND NOT EXISTS (SELECT 1 FROM tc.events e WHERE e::text LIKE '%' || current_setting('tests.alice_guid') || '%')
    AND NOT EXISTS (SELECT 1 FROM tc.checkin_transactions t WHERE t::text LIKE '%' || current_setting('tests.alice_guid') || '%'),
    '0j: the GUID itself is stored nowhere (books, events, checkin_transactions)'
);

-- =============================================================================
-- 1. Checking out again as the holder with a DIFFERENT GUID is refused and replaces nothing
--    (the caller is in another copy; replacing would silently orphan the copy that has the GUID).
-- =============================================================================

SELECT set_config('tests.recheckout',
    tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'OtherMachine', gen_random_uuid()::text)::text,
    true);

SELECT ok(
    (current_setting('tests.recheckout')::jsonb ->> 'success') = 'false'
    AND (current_setting('tests.recheckout')::jsonb ->> 'locked_by_me') = 'true'
    AND NOT (current_setting('tests.recheckout')::jsonb ? 'checkoutGuid'),
    '1a: re-checkout by the holder with another GUID returns success false, locked_by_me true'
);

SELECT ok(
    tests.stored_hash() = tests.guid_hash(current_setting('tests.alice_guid'))
    AND (SELECT locked_by_machine FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'SharedMachine',
    '1b: the refused re-checkout leaves the hash (and the recorded machine) unchanged'
);

SELECT ok(
    (SELECT count(*) = 1 FROM tc.events
     WHERE book_id = 'b0000000-0000-0000-0000-00000000a001' AND type = 0),
    '1c: the refused re-checkout emits no CheckOut event'
);

-- =============================================================================
-- 2. Bob (a different account) cannot take over without the GUID
-- =============================================================================

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT ok(
    (SELECT r ->> 'success' = 'false' AND NOT (r ? 'checkoutGuid') AND NOT (r ? 'locked_by_me')
       FROM (SELECT tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'BobsMachine',
                                     current_setting('tests.alice_guid')) AS r) s),
    '2a: Bob''s checkout_book fails, even with Alice''s GUID, with no locked_by_me'
);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', NULL, 'SharedMachine')) ->> 'success' = 'false'),
    '2b: Bob cannot take over with no GUID'
);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', gen_random_uuid()::text, 'SharedMachine')) ->> 'success' = 'false'),
    '2c: Bob cannot take over with a wrong GUID'
);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001',
        (SELECT checkout_guid_hash FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
        'SharedMachine')) ->> 'success' = 'false'),
    '2d: presenting the member-readable hash instead of the GUID does not work'
);

SELECT ok(
    (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-alice-tko'
    AND tests.stored_hash() = tests.guid_hash(current_setting('tests.alice_guid')),
    '2e: the lock and its hash are unchanged after the failed attempts'
);

-- =============================================================================
-- 3. Bob CAN take over by presenting the GUID (the shared-computer scenario: account B
--    opens the local copy account A checked the book out in), and the GUID is kept.
-- =============================================================================

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001',
        current_setting('tests.alice_guid'), 'SharedMachine')) ->> 'success' = 'true'),
    '3a: Bob takes over with the GUID'
);

SELECT ok(
    (SELECT locked_by = 'user-bob-tko' AND locked_by_machine = 'SharedMachine'
       FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '3b: the lock now belongs to Bob'
);

SELECT is(
    tests.stored_hash(),
    tests.guid_hash(current_setting('tests.alice_guid')),
    '3c: takeover keeps the GUID (not rotated), so the same copy goes on working'
);

SELECT ok(
    (SELECT count(*) = 1 FROM tc.events
     WHERE book_id = 'b0000000-0000-0000-0000-00000000a001'
       AND type = 0
       AND by_user_id = 'user-bob-tko'),
    '3d: exactly one CheckOut event (type=0) recorded for Bob''s takeover'
);

SELECT is(
    current_setting('tc.checkout_takeover', true),
    'off',
    '3e: the takeover switches its keep-the-GUID flag off again afterwards'
);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001',
        current_setting('tests.alice_guid'), 'SharedMachine')) ->> 'success' = 'false')
    AND (SELECT count(*) = 1 FROM tc.events
         WHERE book_id = 'b0000000-0000-0000-0000-00000000a001' AND type = 0 AND by_user_id = 'user-bob-tko')
    AND tests.stored_hash() = tests.guid_hash(current_setting('tests.alice_guid')),
    '3f: re-calling takeover as the current holder is a no-op (no event, hash kept)'
);

SELECT tests.set_jwt('user-carol-tko', 'carol-tko@example.com', true);

-- PT403 (not 42501): checkout_book_takeover raises the schema-wide PT### passthrough codes.
SELECT throws_ok(
    format($$SELECT tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', %L, 'SharedMachine')$$,
        current_setting('tests.alice_guid')),
    'PT403',
    NULL,
    '3g: a non-member cannot take over a lock, even with the GUID (not_a_member)'
);

-- =============================================================================
-- 4. Unlock and delete by the holder require the GUID
-- =============================================================================

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT throws_like(
    $$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001', NULL)$$,
    'CheckoutElsewhere%',
    '4a: the holder cannot unlock without the GUID'
);

SELECT throws_like(
    $$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001', gen_random_uuid()::text)$$,
    'CheckoutElsewhere%',
    '4b: the holder cannot unlock with a wrong GUID'
);

SELECT throws_like(
    $$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001',
        (SELECT checkout_guid_hash FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'))$$,
    'CheckoutElsewhere%',
    '4c: the holder cannot unlock with the hash in place of the GUID'
);

SELECT throws_like(
    $$SELECT tc.delete_book('b0000000-0000-0000-0000-00000000a001', NULL)$$,
    'CheckoutElsewhere%',
    '4d: the holder cannot delete without the GUID'
);

SELECT throws_like(
    $$SELECT tc.delete_book('b0000000-0000-0000-0000-00000000a001', gen_random_uuid()::text)$$,
    'CheckoutElsewhere%',
    '4e: the holder cannot delete with a wrong GUID'
);

SELECT ok(
    (SELECT locked_by = 'user-bob-tko' AND deleted_at IS NULL
       FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001')
    AND tests.stored_hash() = tests.guid_hash(current_setting('tests.alice_guid')),
    '4f: the refused unlocks and deletes changed nothing'
);

SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

SELECT throws_like(
    format($$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001', %L)$$,
        current_setting('tests.alice_guid')),
    'lock_not_held%',
    '4g: the GUID alone does not let a non-holder unlock'
);

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT lives_ok(
    format($$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001', %L)$$,
        upper(current_setting('tests.alice_guid'))),
    '4h: the holder can unlock with the GUID (compared case-insensitively)'
);

SELECT ok(
    (SELECT locked_by IS NULL AND checkout_guid_hash IS NULL
       FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '4i: unlocking clears the lock and the hash'
);

-- =============================================================================
-- 5. A new checkout gets a new GUID, and delete works with it
-- =============================================================================

SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

SELECT set_config('tests.alice_guid2',
    tests.checkout('b0000000-0000-0000-0000-00000000a001', 'SharedMachine'),
    true);

SELECT ok(
    current_setting('tests.alice_guid2') <> current_setting('tests.alice_guid')
    AND tests.stored_hash() = tests.guid_hash(current_setting('tests.alice_guid2')),
    '5a: a new checkout issues a new GUID and stores its hash'
);

SELECT lives_ok(
    format($$SELECT tc.delete_book('b0000000-0000-0000-0000-00000000a001', %L)$$,
        current_setting('tests.alice_guid2')),
    '5b: the holder can delete with the GUID'
);

SELECT ok(
    (SELECT deleted_at IS NOT NULL AND locked_by IS NULL AND checkout_guid_hash IS NULL
       FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '5c: delete releases the lock and clears the hash'
);

SELECT tc.undelete_book('b0000000-0000-0000-0000-00000000a001');

-- =============================================================================
-- 6. An admin can always cancel someone else's checkout without its GUID; force_unlock
--    clears the hash (books_clear_checkout_on_unlock trigger)
-- =============================================================================

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT set_config('tests.bob_guid',
    tests.checkout('b0000000-0000-0000-0000-00000000a001', 'BobsMachine'),
    true);

-- Alice (admin) never saw Bob's GUID.
SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

SELECT lives_ok(
    $$SELECT tc.force_unlock('b0000000-0000-0000-0000-00000000a001')$$,
    '6a: an admin with no GUID can force-unlock a book another member has checked out'
);

SELECT ok(
    current_setting('tests.bob_guid') IS NOT NULL
    AND (SELECT locked_by IS NULL AND checkout_guid_hash IS NULL
           FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '6b: force_unlock clears the lock and the hash'
);

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT throws_like(
    format($$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001', %L)$$,
        current_setting('tests.bob_guid')),
    'lock_not_held%',
    '6c: the force-unlocked GUID grants its old holder nothing afterwards'
);

-- =============================================================================
-- 7. members_remove clears the removed member's locks and hashes
-- =============================================================================

SELECT set_config('tests.bob_guid2',
    tests.checkout('b0000000-0000-0000-0000-00000000a001', 'BobsMachine'),
    true);

SELECT ok(
    current_setting('tests.bob_guid2') <> ''
    AND (SELECT locked_by = 'user-bob-tko'
                AND checkout_guid_hash = tests.guid_hash(current_setting('tests.bob_guid2'))
           FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '7a: Bob checks the free book out and gets a GUID'
);

SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

SELECT lives_ok(
    $$SELECT tc.members_remove('c0000000-0000-0000-0000-00000000a001',
        (SELECT id FROM tc.members
          WHERE collection_id = 'c0000000-0000-0000-0000-00000000a001' AND user_id = 'user-bob-tko'))$$,
    '7b: the admin removes Bob'
);

SELECT ok(
    (SELECT locked_by IS NULL AND checkout_guid_hash IS NULL
       FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '7c: removing the member clears his lock and its hash'
);

-- =============================================================================
-- 8. A lock that changes hands without a new GUID (outside checkout_book_takeover) loses
--    the old hash, so the previous holder's GUID cannot take it over.
-- =============================================================================

SELECT set_config('tests.alice_guid4',
    tests.checkout('b0000000-0000-0000-0000-00000000a001', 'SharedMachine'),
    true);

-- Simulate a GUID-less lock change (a different holder written without a new hash).
UPDATE tc.books SET locked_by = 'user-dave-tko' WHERE id = 'b0000000-0000-0000-0000-00000000a001';

SELECT ok(
    current_setting('tests.alice_guid4') IS NOT NULL
    AND (SELECT checkout_guid_hash IS NULL FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '8a: a change of holder that sets no new GUID clears the old hash (trigger)'
);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001',
        current_setting('tests.alice_guid4'), 'SharedMachine')) ->> 'success' = 'false')
    AND (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-dave-tko',
    '8b: a lock with no hash cannot be taken over, even with the previous holder''s GUID'
);

SELECT * FROM finish();
ROLLBACK;
