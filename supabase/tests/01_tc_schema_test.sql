-- =============================================================================
-- pgTAP tests: Cloud Team Collections — tc schema, RLS, RPCs
-- =============================================================================
-- NOTE: These tests are AUTHORED but UNRUN — no Docker / Supabase CLI on this
-- machine.  Run with:
--   supabase start
--   supabase test db
-- or:
--   psql -U postgres -h localhost -p 54322 -f supabase/tests/01_tc_schema_test.sql
--
-- Requires: pgTAP extension (bundled with local Supabase), pgtap schema accessible.
-- =============================================================================

BEGIN;

-- Load pgTAP
SELECT plan(42);   -- update count when tests are added/removed

-- =============================================================================
-- 0. Sanity: schema and key tables exist
-- =============================================================================

SELECT has_schema('tc', 'tc schema exists');

SELECT has_table('tc', 'collections',           'tc.collections table exists');
SELECT has_table('tc', 'members',               'tc.members table exists');
SELECT has_table('tc', 'books',                 'tc.books table exists');
SELECT has_table('tc', 'versions',              'tc.versions table exists');
SELECT has_table('tc', 'version_files',         'tc.version_files table exists');
SELECT has_table('tc', 'collection_file_groups','tc.collection_file_groups table exists');
SELECT has_table('tc', 'collection_group_files','tc.collection_group_files table exists');
SELECT has_table('tc', 'color_palette_entries', 'tc.color_palette_entries table exists');
SELECT has_table('tc', 'events',                'tc.events table exists');
SELECT has_table('tc', 'checkin_transactions',  'tc.checkin_transactions table exists');

SELECT has_function('tc', 'jwt_email_verified',   'tc.jwt_email_verified() exists');
SELECT has_function('tc', 'create_collection',    'tc.create_collection() exists');
SELECT has_function('tc', 'claim_memberships',    'tc.claim_memberships() exists');
SELECT has_function('tc', 'checkout_book',        'tc.checkout_book() exists');

-- =============================================================================
-- Test fixture helpers
-- =============================================================================
-- Create two test users and a collection for use in subsequent tests.
-- We impersonate JWT callers via set_config so SECURITY DEFINER functions
-- can read auth.jwt().  In real Supabase these come from the auth layer.

CREATE SCHEMA IF NOT EXISTS tests;

-- Helper: set a fake JWT so auth.jwt() returns a known sub/email
CREATE OR REPLACE FUNCTION tests.set_jwt(
    p_sub   text,
    p_email text,
    p_email_verified boolean DEFAULT true
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_token text;
BEGIN
    -- Build a minimal JWT payload (no real signing needed for pgTAP in local DB;
    -- Supabase local instance accepts JWTs signed with the project's anon key,
    -- but for pgTAP we use set_config to inject the claims directly into the
    -- session so auth.jwt() returns them).
    PERFORM set_config(
        'request.jwt.claims',
        json_build_object(
            'sub',            p_sub,
            'email',          p_email,
            'email_verified', p_email_verified,
            'role',           'authenticated',
            'aud',            'authenticated'
        )::text,
        true   -- local to transaction
    );
END;
$$;

-- =============================================================================
-- 1. jwt_email_verified()
-- =============================================================================

-- 1a. Firebase-style: email_verified = true
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"firebase-uid-abc","email":"alice@example.com","email_verified":true,"role":"authenticated"}',
        true);
END;
$$;
SELECT ok(tc.jwt_email_verified(), '1a: jwt_email_verified() true for Firebase email_verified=true');

-- 1b. Firebase-style: email_verified = false
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"firebase-uid-abc","email":"alice@example.com","email_verified":false,"role":"authenticated"}',
        true);
END;
$$;
SELECT ok(NOT tc.jwt_email_verified(), '1b: jwt_email_verified() false for Firebase email_verified=false');

-- 1c. Local GoTrue: no email_verified claim, role = 'authenticated'
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"11111111-1111-1111-1111-111111111111","email":"dev@localhost","role":"authenticated"}',
        true);
END;
$$;
SELECT ok(tc.jwt_email_verified(), '1c: jwt_email_verified() true for local GoTrue (no claim, role=authenticated)');

-- =============================================================================
-- 2. create_collection + RLS: member can read their collection
-- =============================================================================

-- Set up as user Alice
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

-- Alice creates a collection
SELECT lives_ok(
    $$SELECT tc.create_collection('a0000000-0000-0000-0000-000000000001'::uuid, 'Alice Test Collection')$$,
    '2a: create_collection succeeds for authenticated user'
);

-- Alice can see her collection via RLS. Must run as the authenticated role — the suite's
-- postgres superuser bypasses RLS, which would make this assertion pass vacuously.
SET LOCAL ROLE authenticated;

SELECT ok(
    (SELECT count(*) = 1 FROM tc.collections WHERE id = 'a0000000-0000-0000-0000-000000000001'),
    '2b: Alice can SELECT her collection (RLS: is_member)'
);

-- Alice is an admin member
SELECT ok(
    (SELECT role = 'admin' FROM tc.members
     WHERE collection_id = 'a0000000-0000-0000-0000-000000000001'
       AND user_id = 'user-alice-001'),
    '2c: Alice is recorded as admin of her collection'
);

RESET ROLE;

-- =============================================================================
-- 3. RLS matrix: non-member cannot read
-- =============================================================================

-- Set up as user Bob (not a member)
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-bob-002","email":"bob@example.com","email_verified":true,"role":"authenticated","name":"Bob"}',
        true);
END;
$$;

-- RLS only applies to non-superuser roles: the suite runs as postgres, which BYPASSES
-- row security, so these direct-table assertions must run as the authenticated role
-- (the role PostgREST uses for JWT-carrying requests). RESET ROLE afterwards so later
-- fixture writes run as postgres again.
SET LOCAL ROLE authenticated;

SELECT ok(
    (SELECT count(*) = 0 FROM tc.collections WHERE id = 'a0000000-0000-0000-0000-000000000001'),
    '3a: Non-member Bob cannot SELECT Alice''s collection (RLS)'
);

SELECT ok(
    (SELECT count(*) = 0 FROM tc.books
     WHERE collection_id = 'a0000000-0000-0000-0000-000000000001'),
    '3b: Non-member Bob cannot SELECT books in Alice''s collection (RLS)'
);

SELECT ok(
    (SELECT count(*) = 0 FROM tc.events
     WHERE collection_id = 'a0000000-0000-0000-0000-000000000001'),
    '3c: Non-member Bob cannot SELECT events in Alice''s collection (RLS)'
);

RESET ROLE;

-- =============================================================================
-- 4. claim_memberships requires verified email
-- =============================================================================

-- Add Bob as an approved member (Alice adds him)
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

SELECT lives_ok(
    $$SELECT tc.members_add('a0000000-0000-0000-0000-000000000001', 'bob@example.com', 'member')$$,
    '4a: Admin Alice can add Bob as approved member'
);

-- Bob with unverified email cannot claim
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-bob-002","email":"bob@example.com","email_verified":false,"role":"authenticated"}',
        true);
END;
$$;

SELECT throws_ok(
    $$SELECT tc.claim_memberships()$$,
    '28000',    -- invalid_authorization_specification, raised by claim_memberships
    NULL,
    '4b: claim_memberships raises when email_verified=false'
);

-- Bob with verified email can claim
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-bob-002","email":"bob@example.com","email_verified":true,"role":"authenticated"}',
        true);
END;
$$;

SELECT lives_ok(
    $$SELECT tc.claim_memberships()$$,
    '4c: claim_memberships succeeds for verified Bob'
);

SELECT ok(
    (SELECT user_id = 'user-bob-002' FROM tc.members
     WHERE collection_id = 'a0000000-0000-0000-0000-000000000001'
       AND email = 'bob@example.com'),
    '4d: Bob''s user_id is filled after claiming'
);

-- =============================================================================
-- 5. checkout_book concurrency: exactly one winner
-- =============================================================================
-- Insert a test book directly (SECURITY DEFINER helper — RLS bypassed for setup).
INSERT INTO tc.books (id, collection_id, instance_id, name, created_by)
VALUES (
    'b0000000-0000-0000-0000-000000000001'::uuid,
    'a0000000-0000-0000-0000-000000000001'::uuid,
    'b0000000-0000-0000-0000-000000000002'::uuid,
    'Test Book',
    'user-alice-001'
);

-- Simulate two concurrent checkout attempts using two separate DO blocks.
-- We use advisory locks to test the conditional UPDATE serialization.
-- In practice the conditional UPDATE is atomic at READ COMMITTED; this test
-- verifies that calling checkout_book twice yields exactly one success.

-- Alice checks out
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

SELECT ok(
    (SELECT (tc.checkout_book('b0000000-0000-0000-0000-000000000001', 'AliceMachine')) ->> 'success' = 'true'),
    '5a: Alice wins the checkout race (first call)'
);

-- Bob tries to check out the same book — should fail
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-bob-002","email":"bob@example.com","email_verified":true,"role":"authenticated","name":"Bob"}',
        true);
END;
$$;

SELECT ok(
    (SELECT (tc.checkout_book('b0000000-0000-0000-0000-000000000001', 'BobMachine')) ->> 'success' = 'false'),
    '5b: Bob loses the checkout race (lock already held)'
);

-- Exactly one CheckOut event (type=0) emitted
SELECT ok(
    (SELECT count(*) = 1 FROM tc.events
     WHERE book_id = 'b0000000-0000-0000-0000-000000000001'
       AND type = 0),
    '5c: exactly one CheckOut event (type=0) emitted'
);

-- =============================================================================
-- 6. last-admin guard
-- =============================================================================

-- Attempt to remove Alice (the only admin) should fail
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

SELECT throws_ok(
    $$SELECT tc.members_remove(
        'a0000000-0000-0000-0000-000000000001',
        (SELECT id FROM tc.members WHERE collection_id = 'a0000000-0000-0000-0000-000000000001'
          AND user_id = 'user-alice-001')
    )$$,
    'P0001',
    NULL,
    '6a: Removing the last admin raises last_admin_guard'
);

-- Demoting Alice to member should also fail
SELECT throws_ok(
    $$UPDATE tc.members
      SET role = 'member'
      WHERE collection_id = 'a0000000-0000-0000-0000-000000000001'
        AND user_id = 'user-alice-001'$$,
    'P0001',
    NULL,
    '6b: Demoting the last admin raises last_admin_guard'
);

-- =============================================================================
-- 7. get_changes cursor
-- =============================================================================

-- Log an event as Alice
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

SELECT lives_ok(
    $$SELECT tc.log_event(
        'a0000000-0000-0000-0000-000000000001',
        'b0000000-0000-0000-0000-000000000001',
        100,  -- WorkPreservedLocally
        'test incident',
        'Test Book',
        '6.5.0'
    )$$,
    '7a: log_event succeeds for a member'
);

-- get_changes with cursor = 0 returns events
SELECT ok(
    (SELECT jsonb_array_length(
        (tc.get_changes('a0000000-0000-0000-0000-000000000001', 0)) -> 'events'
    ) > 0),
    '7b: get_changes(since=0) returns at least one event'
);

-- get_changes with cursor = max returns empty
SELECT ok(
    (SELECT jsonb_array_length(
        (tc.get_changes(
            'a0000000-0000-0000-0000-000000000001',
            (SELECT max(id) FROM tc.events WHERE collection_id = 'a0000000-0000-0000-0000-000000000001')
        )) -> 'events'
    ) = 0),
    '7c: get_changes(since=max_id) returns empty events'
);

-- =============================================================================
-- 8. Tombstone / undelete
-- =============================================================================

-- Alice must hold the lock to delete (she already holds it from checkout in test 5a)
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

SELECT lives_ok(
    $$SELECT tc.delete_book('b0000000-0000-0000-0000-000000000001')$$,
    '8a: delete_book succeeds when caller holds the lock'
);

SELECT ok(
    (SELECT deleted_at IS NOT NULL FROM tc.books
     WHERE id = 'b0000000-0000-0000-0000-000000000001'),
    '8b: deleted_at is set after delete_book'
);

-- Admin (Alice) can undelete
SELECT lives_ok(
    $$SELECT tc.undelete_book('b0000000-0000-0000-0000-000000000001')$$,
    '8c: admin can undelete a tombstoned book'
);

SELECT ok(
    (SELECT deleted_at IS NULL FROM tc.books
     WHERE id = 'b0000000-0000-0000-0000-000000000001'),
    '8d: deleted_at is NULL after undelete_book'
);

-- =============================================================================
-- 9. Live-name uniqueness: tombstoned names are reusable
-- =============================================================================

-- Delete the existing book to tombstone the name 'Test Book'
DO $$
BEGIN
    PERFORM set_config('request.jwt.claims',
        '{"sub":"user-alice-001","email":"alice@example.com","email_verified":true,"role":"authenticated","name":"Alice"}',
        true);
END;
$$;

-- Re-checkout so we can delete again
SELECT tc.checkout_book('b0000000-0000-0000-0000-000000000001', 'AliceMachine');
SELECT tc.delete_book('b0000000-0000-0000-0000-000000000001');

-- Inserting a new book with the same name should succeed (tombstone excluded from index)
SELECT lives_ok(
    $$INSERT INTO tc.books (id, collection_id, instance_id, name, created_by)
      VALUES (
          'b0000000-0000-0000-0000-000000000099'::uuid,
          'a0000000-0000-0000-0000-000000000001'::uuid,
          'b0000000-0000-0000-0000-000000000098'::uuid,
          'Test Book',
          'user-alice-001'
      )$$,
    '9a: inserting a live book with tombstoned name succeeds (name reuse)'
);

-- But a second live book with the same name should fail
SELECT throws_ok(
    $$INSERT INTO tc.books (id, collection_id, instance_id, name, created_by)
      VALUES (
          'b0000000-0000-0000-0000-000000000097'::uuid,
          'a0000000-0000-0000-0000-000000000001'::uuid,
          'b0000000-0000-0000-0000-000000000096'::uuid,
          'Test Book',
          'user-alice-001'
      )$$,
    '23505',   -- unique_violation
    NULL,
    '9b: inserting a second live book with same name raises unique_violation'
);

-- =============================================================================
-- Finish
-- =============================================================================

SELECT * FROM finish();
ROLLBACK;
