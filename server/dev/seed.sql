-- Bloom Cloud Team Collections — dev seed users
-- This file seeds three stable developer identities into the local Supabase stack.
-- The local GoTrue instance has enable_confirmations = false (see config.auth.toml.snippet),
-- so these users are immediately confirmed and can sign in.
--
-- Shared password for all three dev users: BloomDev123!
-- (Hash below is bcrypt for that password, computed with GoTrue's default cost factor 10.)
--
-- These rows follow the schema GoTrue uses internally in the auth schema.
-- References:
--   https://github.com/supabase/auth/blob/main/internal/models/user.go
--   Standard Supabase seed patterns: insert into auth.users, then auth.identities.
--
-- To re-seed after a wipe: supabase db reset  (which replays migrations then runs seed.sql).
-- To add ad-hoc users at runtime: POST http://localhost:54321/auth/v1/signup  (no seed needed).
--
-- WARNING: These credentials are for local development only. NEVER use in production.

BEGIN;

-- ---------------------------------------------------------------------------
-- Helper: ensure idempotency (re-running seed.sql does not fail).
-- We delete existing rows for our well-known UUIDs then re-insert.
-- ---------------------------------------------------------------------------

-- Fixed, stable UUIDs so task-09 E2E fixtures can reference them by ID.
DO $$
DECLARE
    admin_id  uuid := '00000000-0000-0000-0000-000000000001';
    alice_id  uuid := '00000000-0000-0000-0000-000000000002';
    bob_id    uuid := '00000000-0000-0000-0000-000000000003';
BEGIN
    DELETE FROM auth.identities WHERE user_id IN (admin_id, alice_id, bob_id);
    DELETE FROM auth.users    WHERE id       IN (admin_id, alice_id, bob_id);
END $$;

-- ---------------------------------------------------------------------------
-- auth.users — one row per identity
-- encrypted_password: bcrypt(cost=10) of "BloomDev123!"
-- email_confirmed_at set to a fixed past timestamp = "already confirmed"
-- raw_app_meta_data provider = 'email' (standard GoTrue email/password flow)
-- ---------------------------------------------------------------------------

INSERT INTO auth.users (
    id,
    instance_id,
    aud,
    role,
    email,
    encrypted_password,
    email_confirmed_at,
    recovery_sent_at,
    last_sign_in_at,
    raw_app_meta_data,
    raw_user_meta_data,
    is_super_admin,
    created_at,
    updated_at,
    confirmation_token,
    email_change,
    email_change_token_new,
    recovery_token
)
VALUES
    -- admin@dev.local
    (
        '00000000-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'admin@dev.local',
        '$2a$10$PW4QR5Zj5/C4VRqWV.K2pePQnPbhMqR1GG0d6f2hAvUfJfJYqCgEu',
        '2026-01-01 00:00:00+00',
        NULL,
        NULL,
        '{"provider": "email", "providers": ["email"]}',
        '{"full_name": "Dev Admin"}',
        FALSE,
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00',
        '',
        '',
        '',
        ''
    ),
    -- alice@dev.local
    (
        '00000000-0000-0000-0000-000000000002',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'alice@dev.local',
        '$2a$10$PW4QR5Zj5/C4VRqWV.K2pePQnPbhMqR1GG0d6f2hAvUfJfJYqCgEu',
        '2026-01-01 00:00:00+00',
        NULL,
        NULL,
        '{"provider": "email", "providers": ["email"]}',
        '{"full_name": "Alice Dev"}',
        FALSE,
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00',
        '',
        '',
        '',
        ''
    ),
    -- bob@dev.local
    (
        '00000000-0000-0000-0000-000000000003',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'bob@dev.local',
        '$2a$10$PW4QR5Zj5/C4VRqWV.K2pePQnPbhMqR1GG0d6f2hAvUfJfJYqCgEu',
        '2026-01-01 00:00:00+00',
        NULL,
        NULL,
        '{"provider": "email", "providers": ["email"]}',
        '{"full_name": "Bob Dev"}',
        FALSE,
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00',
        '',
        '',
        '',
        ''
    );

-- ---------------------------------------------------------------------------
-- auth.identities — links the user row to a specific login provider.
-- provider_id = email address (GoTrue convention for the 'email' provider).
-- identity_data carries the claims that GoTrue will embed in the JWT:
--   sub, email, email_verified.
-- email_verified: true — satisfies tc.jwt_email_verified() in every RLS policy
--   (task 01 implements that helper; it reads this claim off the JWT).
-- ---------------------------------------------------------------------------

INSERT INTO auth.identities (
    id,
    user_id,
    provider_id,
    identity_data,
    provider,
    last_sign_in_at,
    created_at,
    updated_at
)
VALUES
    (
        '00000000-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000001',
        'admin@dev.local',
        '{"sub": "00000000-0000-0000-0000-000000000001", "email": "admin@dev.local", "email_verified": true}',
        'email',
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00'
    ),
    (
        '00000000-0000-0000-0000-000000000002',
        '00000000-0000-0000-0000-000000000002',
        'alice@dev.local',
        '{"sub": "00000000-0000-0000-0000-000000000002", "email": "alice@dev.local", "email_verified": true}',
        'email',
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00'
    ),
    (
        '00000000-0000-0000-0000-000000000003',
        '00000000-0000-0000-0000-000000000003',
        'bob@dev.local',
        '{"sub": "00000000-0000-0000-0000-000000000003", "email": "bob@dev.local", "email_verified": true}',
        'email',
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00',
        '2026-01-01 00:00:00+00'
    );

COMMIT;
