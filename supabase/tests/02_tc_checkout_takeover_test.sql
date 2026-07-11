-- =============================================================================
-- pgTAP tests: tc.checkout_book_takeover (dogfood batch 1, item 9 -- account-switch
-- checkout takeover; extended by 20260711000003's per-collection-copy "seat", bug #0)
-- =============================================================================
-- Run against a local Supabase stack:
--   supabase start
--   supabase test db
-- =============================================================================

BEGIN;

SELECT plan(23);

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

-- =============================================================================
-- Fixture: a collection with Alice (admin) and Bob (member, claimed), a book Alice has
-- checked out on "SharedMachine" in her own local copy ("seat-alice-copy"). Uses the
-- public RPCs (create_collection/members_add/claim_memberships), matching
-- 01_tc_schema_test.sql's own fixture convention.
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

-- Alice checks the book out on SharedMachine, seat "seat-alice-copy" (ordinary
-- checkout_book, already tested elsewhere -- used here purely as fixture setup).
SELECT tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', 'seat-alice-copy');

SELECT ok(
    (SELECT locked_seat FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'seat-alice-copy',
    '0d: checkout_book records the caller''s seat with the lock'
);

-- =============================================================================
-- 1. Bob (different account) CANNOT take over across machines or across seats
-- =============================================================================

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'BobsOwnMachine', 'seat-alice-copy')) ->> 'success' = 'false'),
    '1a: Bob cannot take over Alice''s lock from a DIFFERENT machine'
);

SELECT ok(
    (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-alice-tko',
    '1b: the lock still belongs to Alice after the cross-machine attempt'
);

-- bug #0 (e2e-4's scenario): same machine but a DIFFERENT local copy of the collection.
SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', 'seat-bob-copy')) ->> 'success' = 'false'),
    '1c: Bob cannot take over from the SAME machine but a DIFFERENT seat (separate local copy)'
);

SELECT ok(
    (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-alice-tko',
    '1d: the lock still belongs to Alice after the wrong-seat attempt'
);

-- A pre-seat caller (p_seat defaults to NULL) can never take over.
SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine')) ->> 'success' = 'false'),
    '1e: a caller supplying no seat cannot take over'
);

-- =============================================================================
-- 2. Bob CAN take over a lock held on the SAME machine in the SAME seat (the true
--    shared-computer scenario: account B opens the exact local folder account A used)
-- =============================================================================

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', 'seat-alice-copy')) ->> 'success' = 'true'),
    '2a: Bob takes over Alice''s same-machine same-seat lock'
);

SELECT ok(
    (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-bob-tko',
    '2b: the lock now belongs to Bob'
);

SELECT ok(
    (SELECT locked_by_machine FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'SharedMachine',
    '2c: the machine is unchanged (still SharedMachine)'
);

SELECT ok(
    (SELECT locked_seat FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'seat-alice-copy',
    '2d: the seat is unchanged (still the shared local copy)'
);

SELECT ok(
    (SELECT count(*) = 1 FROM tc.events
     WHERE book_id = 'b0000000-0000-0000-0000-00000000a001'
       AND type = 0
       AND by_user_id = 'user-bob-tko'),
    '2e: exactly one CheckOut event (type=0) recorded for Bob''s takeover'
);

-- =============================================================================
-- 3. Calling it again for the CURRENT holder is a harmless no-op (not a new "takeover")
-- =============================================================================

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', 'seat-alice-copy')) ->> 'success' = 'false'),
    '3a: re-calling takeover when the caller already holds the lock reports no change'
);

SELECT ok(
    (SELECT count(*) = 1 FROM tc.events
     WHERE book_id = 'b0000000-0000-0000-0000-00000000a001'
       AND type = 0
       AND by_user_id = 'user-bob-tko'),
    '3b: no duplicate CheckOut event was emitted for the no-op re-call'
);

-- =============================================================================
-- 4. A non-member cannot take over any lock
-- =============================================================================

SELECT tests.set_jwt('user-carol-tko', 'carol-tko@example.com', true);

-- PT403 (not 42501): checkout_book_takeover raises the schema-wide PT### passthrough
-- codes as of 20260711000002.
SELECT throws_ok(
    $$SELECT tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', 'seat-alice-copy')$$,
    'PT403',
    NULL,
    '4a: a non-member cannot take over a lock (not_a_member)'
);

-- =============================================================================
-- 5. Unlock clears the seat (books_clear_seat_on_unlock trigger)
-- =============================================================================

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT lives_ok(
    $$SELECT tc.unlock_book('b0000000-0000-0000-0000-00000000a001')$$,
    '5a: the current holder can unlock'
);

SELECT ok(
    (SELECT locked_seat IS NULL FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001'),
    '5b: locked_seat is cleared with the lock (trigger)'
);

-- =============================================================================
-- 6. A lock with NO recorded seat (legacy/pre-seat, or checkin_start_tx's take-if-free
--    path) can never be taken over — fail-safe.
-- =============================================================================

SELECT tests.set_jwt('user-alice-tko', 'alice-tko@example.com', true);

SELECT ok(
    (SELECT (tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'SharedMachine')) ->> 'success' = 'true'),
    '6a: a pre-seat checkout (no seat argument) still succeeds'
);

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine', 'seat-bob-copy')) ->> 'success' = 'false'),
    '6b: a NULL stored seat never matches — takeover refused (fail-safe)'
);

SELECT ok(
    (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-alice-tko',
    '6c: the null-seat lock still belongs to Alice'
);

SELECT * FROM finish();
ROLLBACK;
