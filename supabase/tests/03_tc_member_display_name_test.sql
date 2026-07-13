-- =============================================================================
-- pgTAP tests: tc.members.display_name + tc.members_set_display_name
-- (dogfood batch 1, John's 13 Jul 2026 member-display-name request;
--  migration 20260713000001)
-- =============================================================================
-- Run against a local Supabase stack:
--   supabase start
--   supabase test db
-- =============================================================================

BEGIN;

SELECT plan(24);

SELECT has_column('tc', 'members', 'display_name', 'tc.members.display_name exists');
SELECT has_function(
    'tc', 'members_set_display_name',
    'tc.members_set_display_name() exists'
);

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
-- Fixture: Alice (admin), Bob (member, claimed), Carol (approved, never claims).
-- Uses the public RPCs, matching the other test files' fixture convention.
-- =============================================================================

SELECT tests.set_jwt('user-alice-dn', 'alice-dn@example.com', true);

SELECT lives_ok(
    $$SELECT tc.create_collection('c0000000-0000-0000-0000-00000000d001'::uuid, 'Display Name Test Collection')$$,
    '0a: create_collection succeeds for Alice'
);

SELECT lives_ok(
    $$SELECT tc.members_add('c0000000-0000-0000-0000-00000000d001', 'bob-dn@example.com', 'member')$$,
    '0b: Alice adds Bob as an approved member'
);

SELECT lives_ok(
    $$SELECT tc.members_add('c0000000-0000-0000-0000-00000000d001', 'carol-dn@example.com', 'member')$$,
    '0c: Alice adds Carol (who will stay unclaimed/pending)'
);

SELECT tests.set_jwt('user-bob-dn', 'bob-dn@example.com', true);

SELECT lives_ok(
    $$SELECT tc.claim_memberships()$$,
    '0d: Bob claims his membership'
);

-- =============================================================================
-- 1. Admin can set anyone's display name (claimed or pending)
-- =============================================================================

SELECT tests.set_jwt('user-alice-dn', 'alice-dn@example.com', true);

SELECT lives_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'bob-dn@example.com'),
        '  Bob the Builder  ')$$,
    '1a: admin sets a claimed member''s display name'
);

SELECT is(
    (SELECT ml.display_name
     FROM tc.members_list('c0000000-0000-0000-0000-00000000d001') ml
     WHERE ml.email = 'bob-dn@example.com'),
    'Bob the Builder',
    '1b: members_list returns the (trimmed) display name'
);

SELECT lives_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'carol-dn@example.com'),
        'Carol C')$$,
    '1c: admin sets a PENDING (unclaimed) member''s display name'
);

-- =============================================================================
-- 2. Non-admin member: own name yes, anyone else's no
-- =============================================================================

SELECT tests.set_jwt('user-bob-dn', 'bob-dn@example.com', true);

SELECT lives_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'bob-dn@example.com'),
        'Just Bob')$$,
    '2a: a claimed non-admin member sets their OWN display name'
);

SELECT is(
    (SELECT m.display_name FROM tc.members m WHERE m.email = 'bob-dn@example.com'),
    'Just Bob',
    '2b: the self-set name stuck'
);

SELECT throws_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'alice-dn@example.com'),
        'Not Your Name')$$,
    '42501',
    'admin_required',
    '2c: a non-admin cannot set another member''s display name'
);

SELECT throws_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'carol-dn@example.com'),
        'Not Your Name Either')$$,
    '42501',
    'admin_required',
    '2d: a non-admin cannot set an unclaimed member''s display name'
);

-- Sanity: Alice's name was NOT changed by the refused 2c call.
SELECT is(
    (SELECT m.display_name FROM tc.members m WHERE m.email = 'alice-dn@example.com'),
    NULL,
    '2e: the refused call did not write anything'
);

-- =============================================================================
-- 3. Non-member is refused before learning anything
-- =============================================================================

SELECT tests.set_jwt('user-mallory-dn', 'mallory-dn@example.com', true);

SELECT throws_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        1,
        'Intruder')$$,
    '42501',
    'not_a_member',
    '3: a non-member is refused (not_a_member, not member_not_found)'
);

-- =============================================================================
-- 4. Blank clears to NULL; unknown member id; over-long name
-- =============================================================================

SELECT tests.set_jwt('user-alice-dn', 'alice-dn@example.com', true);

SELECT lives_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'bob-dn@example.com'),
        '   ')$$,
    '4a: blank/whitespace input is accepted'
);

SELECT is(
    (SELECT m.display_name FROM tc.members m WHERE m.email = 'bob-dn@example.com'),
    NULL,
    '4b: ...and clears the display name back to NULL'
);

SELECT throws_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        999999999,
        'Nobody')$$,
    'P0002',
    'member_not_found',
    '4c: unknown member id raises member_not_found'
);

SELECT throws_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'bob-dn@example.com'),
        repeat('x', 101))$$,
    '22001',
    'display_name_too_long',
    '4d: a 101-char name raises display_name_too_long'
);

-- =============================================================================
-- 5. resolve_member_display precedence: durable column beats the JWT-claim
--    event capture; the event capture remains the fallback.
-- =============================================================================

-- A historical event carrying the JWT name claim for Bob (direct insert for setup,
-- matching the other files' fixture convention).
INSERT INTO tc.events (collection_id, type, by_user_id, by_user_name, by_email)
VALUES (
    'c0000000-0000-0000-0000-00000000d001'::uuid,
    1, -- CheckIn
    'user-bob-dn',
    'JWT Bob',
    'bob-dn@example.com'
);

-- Bob's display_name is NULL right now (cleared in 4a/4b) → fallback applies.
SELECT is(
    (SELECT rd.display_name
     FROM tc.resolve_member_display(
         'c0000000-0000-0000-0000-00000000d001'::uuid, 'user-bob-dn') rd),
    'JWT Bob',
    '5a: with no durable name, the latest event by_user_name is the fallback'
);

SELECT lives_ok(
    $$SELECT tc.members_set_display_name(
        'c0000000-0000-0000-0000-00000000d001',
        (SELECT id FROM tc.members WHERE email = 'bob-dn@example.com'),
        'Durable Bob')$$,
    '5b: admin sets the durable name again'
);

SELECT is(
    (SELECT rd.display_name
     FROM tc.resolve_member_display(
         'c0000000-0000-0000-0000-00000000d001'::uuid, 'user-bob-dn') rd),
    'Durable Bob',
    '5c: the durable column now beats the event fallback'
);

-- ...and the whole pipeline: a book Bob has checked out reports his durable name as
-- locked_by_name in get_collection_state (queried as Bob himself, since a
-- never-committed book is only visible to its locker).
INSERT INTO tc.books (id, collection_id, instance_id, name, created_by, locked_by,
                      locked_by_machine, locked_at)
VALUES (
    'b0000000-0000-0000-0000-00000000d001'::uuid,
    'c0000000-0000-0000-0000-00000000d001'::uuid,
    'b0000000-0000-0000-0000-00000000d002'::uuid,
    'Display Name Test Book',
    'user-bob-dn',
    'user-bob-dn',
    'BobsMachine',
    now()
);

SELECT tests.set_jwt('user-bob-dn', 'bob-dn@example.com', true);

SELECT is(
    (SELECT b ->> 'locked_by_name'
     FROM jsonb_array_elements(
         tc.get_collection_state('c0000000-0000-0000-0000-00000000d001'::uuid) -> 'books'
     ) b
     WHERE b ->> 'name' = 'Display Name Test Book'),
    'Durable Bob',
    '5d: get_collection_state book rows carry the durable name as locked_by_name'
);

-- ...and history: get_changes event rows report the CURRENT durable name as
-- by_display_name, alongside the frozen at-event-time by_user_name ('JWT Bob').
SELECT is(
    (SELECT e ->> 'by_display_name'
     FROM jsonb_array_elements(
         tc.get_changes('c0000000-0000-0000-0000-00000000d001'::uuid, 0) -> 'events'
     ) e
     WHERE e ->> 'by_user_id' = 'user-bob-dn'
     LIMIT 1),
    'Durable Bob',
    '5e: get_changes event rows carry the current durable name as by_display_name'
);

SELECT * FROM finish();

ROLLBACK;
