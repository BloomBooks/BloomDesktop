-- =============================================================================
-- Migration: allow multiple PENDING invitations per collection (constraint fix)
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Found by 03_tc_member_display_name_test.sql's fixture (13 Jul 2026): the original
-- schema declared
--
--     CONSTRAINT members_claimed_user_uq UNIQUE NULLS NOT DISTINCT (collection_id, user_id)
--
-- NULLS NOT DISTINCT makes two unclaimed rows (user_id IS NULL) collide, so a second
-- invitation raises 23505 from members_add before the first invitee claims — i.e. a
-- collection could only ever have ONE pending invitation at a time. That contradicts
-- the schema's own documented intent ("A claimed user may appear at most once per
-- collection (UNIQUE WHERE user_id IS NOT NULL)"); dogfooding never tripped it only
-- because invites happened to alternate with claims. members_add's ON CONFLICT clause
-- targets the (collection_id, email) constraint, so this error escaped it unhandled.
--
-- Fix: same constraint with default NULLS DISTINCT semantics — claimed users stay
-- unique per collection, unclaimed rows don't collide.
ALTER TABLE tc.members DROP CONSTRAINT members_claimed_user_uq;
ALTER TABLE tc.members ADD CONSTRAINT members_claimed_user_uq
    UNIQUE (collection_id, user_id);

COMMENT ON CONSTRAINT members_claimed_user_uq ON tc.members IS
    'A claimed user appears at most once per collection. NULL user_id rows (pending '
    'invitations) do not collide (default NULLS DISTINCT; fixed 20260713000002 — the '
    'original NULLS NOT DISTINCT allowed only one pending invitation per collection).';
