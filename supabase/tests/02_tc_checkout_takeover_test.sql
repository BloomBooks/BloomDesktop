-- =============================================================================
-- pgTAP tests: tc.checkout_book_takeover (dogfood batch 1, item 9 -- account-switch
-- checkout takeover)
-- =============================================================================
-- NOTE: authored but UNRUN in this environment -- see 01_tc_schema_test.sql's header for the
-- same caveat and how to run these against a local Supabase stack:
--   supabase start
--   supabase test db
-- =============================================================================

BEGIN;

SELECT plan(13);

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
-- checked out on "SharedMachine". Uses the public RPCs (create_collection/members_add/
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

-- Alice checks the book out on SharedMachine (ordinary checkout_book, already tested
-- elsewhere -- used here purely as fixture setup).
SELECT tc.checkout_book('b0000000-0000-0000-0000-00000000a001', 'SharedMachine');

-- =============================================================================
-- 1. Bob (different account) CANNOT take over a lock held on a DIFFERENT machine
-- =============================================================================

SELECT tests.set_jwt('user-bob-tko', 'bob-tko@example.com', true);

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'BobsOwnMachine')) ->> 'success' = 'false'),
    '1a: Bob cannot take over Alice''s lock from a DIFFERENT machine'
);

SELECT ok(
    (SELECT locked_by FROM tc.books WHERE id = 'b0000000-0000-0000-0000-00000000a001') = 'user-alice-tko',
    '1b: the lock still belongs to Alice after the cross-machine attempt'
);

-- =============================================================================
-- 2. Bob (different account) CAN take over a lock held on the SAME machine
-- =============================================================================

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine')) ->> 'success' = 'true'),
    '2a: Bob takes over Alice''s same-machine lock'
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
    (SELECT count(*) = 1 FROM tc.events
     WHERE book_id = 'b0000000-0000-0000-0000-00000000a001'
       AND type = 0
       AND by_user_id = 'user-bob-tko'),
    '2d: exactly one CheckOut event (type=0) recorded for Bob''s takeover'
);

-- =============================================================================
-- 3. Calling it again for the CURRENT holder is a harmless no-op (not a new "takeover")
-- =============================================================================

SELECT ok(
    (SELECT (tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine')) ->> 'success' = 'false'),
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

SELECT throws_ok(
    $$SELECT tc.checkout_book_takeover('b0000000-0000-0000-0000-00000000a001', 'SharedMachine')$$,
    '42501',
    NULL,
    '4a: a non-member cannot take over a lock (not_a_member)'
);

SELECT * FROM finish();
ROLLBACK;
