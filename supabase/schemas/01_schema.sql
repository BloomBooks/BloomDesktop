-- Team Collections cloud schema: namespace + enum types.
-- Declarative source of truth (see AGENTS/CONTRACTS): edit these files, then
-- `supabase db diff -f <name>` to generate a migration. Applied in file order.
CREATE SCHEMA IF NOT EXISTS tc;

CREATE TYPE tc.member_role AS ENUM (
    'admin',
    'member'
);
