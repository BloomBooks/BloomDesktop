-- =============================================================================
-- Migration: tc schema + core tables
-- Cloud Team Collections — Bloom Desktop
-- =============================================================================
-- Creates the `tc` schema and all persistent tables:
--   collections, members, books, versions, version_files,
--   collection_file_groups, collection_group_files,
--   color_palette_entries, events, checkin_transactions
--
-- Design references:
--   Design/CloudTeamCollections.md
--   Design/CloudTeamCollections/CONTRACTS.md
--   Design/CloudTeamCollections/tasks/01-server-schema.md
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Schema
-- ---------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS tc;

-- Expose tc in the search path for PostgREST (config.toml also sets extra_search_path).
-- Functions are defined in the tc schema explicitly; no implicit search-path dependency.

-- ---------------------------------------------------------------------------
-- Helper: JWT email-verified check
-- ---------------------------------------------------------------------------
-- THIS IS THE ONE PLACE that reads email-verification off the JWT.
-- Two auth shapes are supported:
--   1. Firebase ID token (real production): carries `email_verified` boolean in the JWT.
--   2. Local GoTrue (dev stack, task 11):  auto-confirm is ON so every login is confirmed;
--      GoTrue does NOT put `email_verified` in the JWT, but sets `aud = 'authenticated'`
--      and the user record is confirmed.  We treat any local-GoTrue user as verified.
--
-- Callers (claim_memberships, etc.) MUST call this function instead of reading the claim
-- directly so that a future auth change only needs to touch this one function.
CREATE OR REPLACE FUNCTION tc.jwt_email_verified()
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
  SELECT
    CASE
      -- Firebase-style: explicit boolean claim (may arrive as 'true'::text or true::bool)
      WHEN (auth.jwt() ->> 'email_verified') IS NOT NULL THEN
        (auth.jwt() ->> 'email_verified')::boolean
      -- Local GoTrue (dev): no email_verified claim; role = 'authenticated' implies confirmed
      WHEN (auth.jwt() ->> 'role') = 'authenticated' THEN
        TRUE
      ELSE
        FALSE
    END
$$;

COMMENT ON FUNCTION tc.jwt_email_verified() IS
  'The ONLY place that decides whether the caller''s email is verified. '
  'Handles both a Firebase-style email_verified JWT claim and local-GoTrue auto-confirmed '
  'users (dev stack). All callers must use this function, never the claim directly.';

-- ---------------------------------------------------------------------------
-- Helper: current user id from JWT sub claim (TEXT, not uuid)
-- ---------------------------------------------------------------------------
-- User ids are TEXT: Firebase UIDs are ~28 base64 chars; local-GoTrue issues uuid strings.
-- Both fit in TEXT without casting.
CREATE OR REPLACE FUNCTION tc.current_user_id()
RETURNS text
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
  SELECT auth.jwt() ->> 'sub'
$$;

COMMENT ON FUNCTION tc.current_user_id() IS
  'Returns the caller''s user id from the JWT sub claim as TEXT. '
  'Firebase UIDs (~28 chars) and local-GoTrue UUIDs both fit in TEXT.';

-- ---------------------------------------------------------------------------
-- Helper: current user email from JWT
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.current_user_email()
RETURNS text
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
  SELECT lower(auth.jwt() ->> 'email')
$$;

COMMENT ON FUNCTION tc.current_user_email() IS
  'Returns the caller''s email from the JWT, lowercased for case-insensitive comparison.';

-- ---------------------------------------------------------------------------
-- collections
-- ---------------------------------------------------------------------------
-- One row per cloud Team Collection.  The id is the Bloom CollectionId GUID
-- (same value in TeamCollectionLink.txt: cloud://sil.bloom/collection/<id>).
CREATE TABLE tc.collections (
    id          uuid        PRIMARY KEY,
    name        text        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  text        NOT NULL  -- user_id (TEXT; see note above)
);

COMMENT ON TABLE tc.collections IS
    'One row per cloud Team Collection. id = Bloom CollectionId GUID.';
COMMENT ON COLUMN tc.collections.id IS
    'The collection UUID — same value as in TeamCollectionLink.txt '
    '(cloud://sil.bloom/collection/<id>).';
COMMENT ON COLUMN tc.collections.created_by IS
    'TEXT user id (Firebase UID or GoTrue UUID) of the creator.';

-- ---------------------------------------------------------------------------
-- members  (approved-accounts table)
-- ---------------------------------------------------------------------------
-- Each row is one approved email ↔ collection binding.
-- user_id is NULL until the account holder claims the seat (see claim_memberships RPC).
-- Constraints:
--   • A claimed user may appear at most once per collection (UNIQUE WHERE user_id IS NOT NULL).
--   • At least one admin must remain (enforced by last-admin guard trigger).
CREATE TYPE tc.member_role AS ENUM ('admin', 'member');

CREATE TABLE tc.members (
    id             bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    collection_id  uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    email          text        NOT NULL,   -- the approved email (lowercase, NFC-normalized on insert)
    role           tc.member_role NOT NULL DEFAULT 'member',
    user_id        text,                   -- NULL until claimed; TEXT (Firebase UID / GoTrue UUID)
    added_by       text        NOT NULL,   -- user_id of the admin who added this row
    added_at       timestamptz NOT NULL DEFAULT now(),
    claimed_at     timestamptz,

    -- Unique approved email per collection (case-insensitive; enforced via lower() on insert)
    CONSTRAINT members_collection_email_uq UNIQUE (collection_id, email),

    -- A claimed user appears at most once per collection
    CONSTRAINT members_claimed_user_uq UNIQUE NULLS NOT DISTINCT (collection_id, user_id)
);

COMMENT ON TABLE tc.members IS
    'Approved-accounts table. Unclaimed rows (user_id IS NULL) are pending until the '
    'account holder signs in and calls claim_memberships(). '
    'email is stored lowercase + NFC-normalised.';
COMMENT ON COLUMN tc.members.user_id IS
    'NULL until the account holder claims the seat. TEXT covers both Firebase UIDs and '
    'local-GoTrue UUIDs.';

CREATE INDEX members_collection_id_idx ON tc.members(collection_id);
CREATE INDEX members_user_id_idx       ON tc.members(user_id) WHERE user_id IS NOT NULL;
CREATE INDEX members_email_idx         ON tc.members(lower(email));

-- ---------------------------------------------------------------------------
-- Trigger: last-admin guard on members
-- ---------------------------------------------------------------------------
-- Prevents the last admin of a collection from being removed or demoted to member.
CREATE OR REPLACE FUNCTION tc.members_last_admin_guard()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    admin_count integer;
BEGIN
    -- Fires on DELETE or UPDATE (role change to 'member' / user_id removal)
    -- Determine the collection_id being affected
    IF TG_OP = 'DELETE' THEN
        -- Count remaining admins after this hypothetical delete
        SELECT count(*) INTO admin_count
        FROM tc.members
        WHERE collection_id = OLD.collection_id
          AND role = 'admin'
          AND id != OLD.id;

        IF admin_count = 0 AND OLD.role = 'admin' THEN
            RAISE EXCEPTION 'last_admin_guard: cannot remove the last admin of collection %',
                OLD.collection_id
                USING ERRCODE = 'P0001';
        END IF;
    ELSIF TG_OP = 'UPDATE' THEN
        -- Fires when role changes from admin → member
        IF OLD.role = 'admin' AND NEW.role = 'member' THEN
            SELECT count(*) INTO admin_count
            FROM tc.members
            WHERE collection_id = OLD.collection_id
              AND role = 'admin'
              AND id != OLD.id;

            IF admin_count = 0 THEN
                RAISE EXCEPTION 'last_admin_guard: cannot demote the last admin of collection %',
                    OLD.collection_id
                    USING ERRCODE = 'P0001';
            END IF;
        END IF;
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$;

COMMENT ON FUNCTION tc.members_last_admin_guard() IS
    'Trigger function: prevents deleting or demoting the last admin of a collection.';

CREATE TRIGGER members_last_admin_guard_tg
    BEFORE DELETE OR UPDATE ON tc.members
    FOR EACH ROW EXECUTE FUNCTION tc.members_last_admin_guard();

-- ---------------------------------------------------------------------------
-- books
-- ---------------------------------------------------------------------------
-- Authoritative server-side state for each book in a collection.
-- Lock columns: locked_by (TEXT user_id), locked_by_machine, locked_at.
-- deleted_at: soft tombstone (set by delete_book, cleared by undelete_book).
-- instance_id: client-generated UUID (stable across renames; keyed in S3).
--
-- Uniqueness constraints:
--   1. (collection_id, instance_id) — one S3 prefix per book.
--   2. Live lower(NFC-normalized name) per collection (tombstoned names reusable).
CREATE TABLE tc.books (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    collection_id       uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    instance_id         uuid        NOT NULL,
    name                text        NOT NULL,   -- NFC-normalized on insert/update via trigger
    current_version_id  uuid,                   -- FK to tc.versions, set after first checkin-finish
    current_version_seq bigint,                 -- denormalised seq for quick status reads
    current_checksum    text,                   -- SHA-256 of the current version manifest
    locked_by           text,                   -- TEXT user_id; NULL = not checked out
    locked_by_machine   text,                   -- machine label provided by the client
    locked_at           timestamptz,
    deleted_at          timestamptz,            -- tombstone; NULL = live
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          text        NOT NULL,

    -- Physical uniqueness: one S3 prefix per book in a collection
    CONSTRAINT books_collection_instance_uq UNIQUE (collection_id, instance_id)
);

COMMENT ON TABLE tc.books IS
    'Authoritative book state per collection. Lock columns, soft tombstone, '
    'current-version denormalization. All state transitions go through RPCs/edge functions; '
    'no direct writes via PostgREST.';
COMMENT ON COLUMN tc.books.instance_id IS
    'Client-generated UUID stable across renames. Used as the S3 prefix key '
    '(tc/{cid}/books/{instance_id}/).';
COMMENT ON COLUMN tc.books.name IS
    'NFC-normalized on write by the nfc_normalize_book_name trigger. '
    'Live-name uniqueness (lower(name)) is enforced by a partial unique index.';
COMMENT ON COLUMN tc.books.deleted_at IS
    'Soft tombstone: non-NULL = deleted. Tombstoned names are reusable (excluded from the '
    'live-name uniqueness index).';

CREATE INDEX books_collection_id_idx        ON tc.books(collection_id);
CREATE INDEX books_locked_by_idx            ON tc.books(locked_by) WHERE locked_by IS NOT NULL;
CREATE INDEX books_instance_id_idx          ON tc.books(instance_id);

-- Partial unique index: enforce lower(NFC-normalized name) uniqueness among live books.
-- Tombstoned (deleted_at IS NOT NULL) names are excluded → reusable after deletion.
CREATE UNIQUE INDEX books_live_name_uq
    ON tc.books (collection_id, lower(normalize(name, NFC)))
    WHERE deleted_at IS NULL;

-- ---------------------------------------------------------------------------
-- Trigger: NFC-normalize book names on insert/update
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION tc.nfc_normalize_book_name()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.name := normalize(NEW.name, NFC);
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.nfc_normalize_book_name() IS
    'Trigger: NFC-normalize the book name before every insert or update.';

CREATE TRIGGER books_nfc_normalize_name_tg
    BEFORE INSERT OR UPDATE OF name ON tc.books
    FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_book_name();

-- ---------------------------------------------------------------------------
-- versions  (metadata per check-in)
-- ---------------------------------------------------------------------------
CREATE TABLE tc.versions (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    book_id         uuid        NOT NULL REFERENCES tc.books(id) ON DELETE CASCADE,
    collection_id   uuid        NOT NULL,   -- denormalised for fast queries
    seq             bigint      NOT NULL,   -- monotonically increasing per book
    checksum        text        NOT NULL,   -- SHA-256 of the manifest
    comment         text,
    created_by      text        NOT NULL,   -- user_id
    created_at      timestamptz NOT NULL DEFAULT now(),
    client_version  text,                   -- Bloom version string

    CONSTRAINT versions_book_seq_uq UNIQUE (book_id, seq)
);

COMMENT ON TABLE tc.versions IS
    'One metadata row per successful check-in (checkin-finish edge function). '
    'seq is monotonically increasing per book.';

CREATE INDEX versions_book_id_idx       ON tc.versions(book_id);
CREATE INDEX versions_collection_id_idx ON tc.versions(collection_id);

-- ---------------------------------------------------------------------------
-- version_files  (CURRENT manifest: path → sha256 + s3_version_id)
-- ---------------------------------------------------------------------------
-- Rows here are the live manifest for the current version of each book.
-- Superseded rows are pruned by checkin-finish when a new version is committed.
-- s3_version_id is the S3 object version captured at upload time; reads always use
-- (path, s3_version_id) — never "latest" — for transactional safety.
CREATE TABLE tc.version_files (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    book_id         uuid        NOT NULL REFERENCES tc.books(id) ON DELETE CASCADE,
    version_id      uuid        NOT NULL REFERENCES tc.versions(id) ON DELETE CASCADE,
    path            text        NOT NULL,   -- relative path within the book; NFC-normalized
    sha256          text        NOT NULL,
    size_bytes      bigint      NOT NULL,
    s3_version_id   text        NOT NULL,   -- S3 object version id captured at PUT time

    CONSTRAINT version_files_book_path_uq UNIQUE (book_id, path)
);

COMMENT ON TABLE tc.version_files IS
    'Current manifest for each book: path → sha256, size, s3_version_id. '
    'Superseded rows are pruned at checkin-finish. Reads always use (path, s3_version_id).';

CREATE INDEX version_files_book_id_idx    ON tc.version_files(book_id);
CREATE INDEX version_files_version_id_idx ON tc.version_files(version_id);

-- Trigger: NFC-normalize version_files.path on insert
CREATE OR REPLACE FUNCTION tc.nfc_normalize_path()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.path := normalize(NEW.path, NFC);
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.nfc_normalize_path() IS
    'Trigger: NFC-normalize the file path before every insert or update.';

CREATE TRIGGER version_files_nfc_normalize_path_tg
    BEFORE INSERT OR UPDATE OF path ON tc.version_files
    FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_path();

-- ---------------------------------------------------------------------------
-- collection_file_groups  (versioned blobs: allowed-words, sample-texts, other)
-- ---------------------------------------------------------------------------
CREATE TABLE tc.collection_file_groups (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    collection_id   uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    group_key       text        NOT NULL
                    CHECK (group_key IN ('other', 'allowed-words', 'sample-texts')),
    version         bigint      NOT NULL DEFAULT 0,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      text        NOT NULL,

    CONSTRAINT collection_file_groups_uq UNIQUE (collection_id, group_key)
);

COMMENT ON TABLE tc.collection_file_groups IS
    'Versioned collection-level file groups. version is bumped atomically by '
    'collection-files-finish edge function; 409 VersionConflict if expectedVersion != version.';

CREATE INDEX collection_file_groups_collection_id_idx ON tc.collection_file_groups(collection_id);

-- ---------------------------------------------------------------------------
-- collection_group_files  (manifest per group)
-- ---------------------------------------------------------------------------
CREATE TABLE tc.collection_group_files (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    group_id        bigint      NOT NULL REFERENCES tc.collection_file_groups(id) ON DELETE CASCADE,
    path            text        NOT NULL,   -- NFC-normalized
    sha256          text        NOT NULL,
    size_bytes      bigint      NOT NULL,
    s3_version_id   text        NOT NULL,

    CONSTRAINT collection_group_files_group_path_uq UNIQUE (group_id, path)
);

COMMENT ON TABLE tc.collection_group_files IS
    'Current file manifest for each collection_file_group. '
    'Rows for the old version are replaced atomically by collection-files-finish.';

CREATE INDEX collection_group_files_group_id_idx ON tc.collection_group_files(group_id);

CREATE TRIGGER collection_group_files_nfc_normalize_path_tg
    BEFORE INSERT OR UPDATE OF path ON tc.collection_group_files
    FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_path();

-- ---------------------------------------------------------------------------
-- color_palette_entries  (union-merge; no deletes)
-- ---------------------------------------------------------------------------
CREATE TABLE tc.color_palette_entries (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    collection_id   uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    palette         text        NOT NULL,   -- named palette (e.g. 'cover-colors')
    color           text        NOT NULL,   -- hex or CSS color string
    added_at        timestamptz NOT NULL DEFAULT now(),
    added_by        text        NOT NULL,

    CONSTRAINT color_palette_entries_uq UNIQUE (collection_id, palette, color)
);

COMMENT ON TABLE tc.color_palette_entries IS
    'Color palette entries per collection. Merge is union-only: '
    'insert ... on conflict do nothing. No rows are ever deleted.';

CREATE INDEX color_palette_entries_collection_id_idx ON tc.color_palette_entries(collection_id);

-- ---------------------------------------------------------------------------
-- events  (history log + realtime source + polling cursor)
-- ---------------------------------------------------------------------------
-- Numeric type values MUST match the C# BookHistoryEventType enum
-- (src/BloomExe/History/HistoryEvent.cs) which stores the integer in SQLite.
-- The enum is:
--   CheckOut          = 0
--   CheckIn           = 1
--   Created           = 2
--   Renamed           = 3
--   Uploaded          = 4   (legacy; not used in cloud TC)
--   ForcedUnlock      = 5
--   ImportSpreadsheet = 6   (client-side only; may appear in log_event)
--   SyncProblem       = 7   (legacy; not used in cloud TC)
--   Deleted           = 8
--   Moved             = 9
--
-- Cloud-TC-specific incident types start at 100 to avoid collision with any future
-- additions to BookHistoryEventType (which must be added at the end of that enum):
--   WorkPreservedLocally = 100  (Lost & Found: local work saved as .bloomSource)
--   (future incidents: 101, 102, ...)
--
-- The check constraint below lists all valid values; update it when extending.
CREATE TABLE tc.events (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    collection_id   uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    book_id         uuid        REFERENCES tc.books(id) ON DELETE SET NULL,
    type            integer     NOT NULL
                    CHECK (type IN (
                        -- C# BookHistoryEventType (must stay in sync with HistoryEvent.cs)
                        0,   -- CheckOut
                        1,   -- CheckIn
                        2,   -- Created
                        3,   -- Renamed
                        4,   -- Uploaded (legacy)
                        5,   -- ForcedUnlock
                        6,   -- ImportSpreadsheet
                        7,   -- SyncProblem (legacy)
                        8,   -- Deleted
                        9,   -- Moved
                        -- Cloud-TC incident extensions (start at 100, never collide with C# enum)
                        100  -- WorkPreservedLocally
                    )),
    by_user_id      text        NOT NULL,
    by_user_name    text,
    by_email        text,
    book_version_seq bigint,
    lock_info       jsonb,      -- JSON {locked_by, machine} snapshot for ForcedUnlock events
    book_name       text,       -- denormalized for display (name at event time)
    group_key       text,       -- for collection-files events
    message         text,       -- check-in comment / incident detail
    bloom_version   text,
    occurred_at     timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE tc.events IS
    'History log, realtime broadcast source, and polling cursor. '
    'type values mirror C# BookHistoryEventType (HistoryEvent.cs): 0=CheckOut, 1=CheckIn, '
    '2=Created, 3=Renamed, 4=Uploaded(legacy), 5=ForcedUnlock, 6=ImportSpreadsheet, '
    '7=SyncProblem(legacy), 8=Deleted, 9=Moved. '
    'Cloud-TC incident extensions start at 100 to avoid colliding with future C# additions: '
    '100=WorkPreservedLocally.';

CREATE INDEX events_collection_id_idx ON tc.events(collection_id);
CREATE INDEX events_book_id_idx       ON tc.events(book_id) WHERE book_id IS NOT NULL;
-- Cursor index: get_changes(since_event_id) uses id > since_event_id
CREATE INDEX events_collection_cursor_idx ON tc.events(collection_id, id);

-- ---------------------------------------------------------------------------
-- Realtime broadcast trigger on events
-- ---------------------------------------------------------------------------
-- Broadcasts a lightweight message on the private channel collection:{uuid} whenever a
-- new event row is inserted.  Clients receive the metadata; large payloads (book content)
-- are never pushed — clients call get_changes(since) to fetch details.
--
-- TODO(realtime, wave 4): pg_notify does NOT reach Supabase Realtime websockets. When the
-- realtime optimization lands (polling ships first — CloudCollectionMonitor), replace this
-- with realtime.send(payload, event, topic, private), topic 'collection:' || collection_id
-- per CONTRACTS.md §Realtime, wrapped in an EXCEPTION guard so an events INSERT never fails
-- in environments without the realtime schema (e.g. bare-Postgres pgTAP CI).
CREATE OR REPLACE FUNCTION tc.events_realtime_broadcast()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify(
        'realtime:' || NEW.collection_id::text,
        json_build_object(
            'eventId',     NEW.id,
            'type',        NEW.type,
            'bookId',      NEW.book_id,
            'versionSeq',  NEW.book_version_seq,
            'byUserName',  NEW.by_user_name,
            'byEmail',     NEW.by_email,
            'lock',        NEW.lock_info,
            'name',        NEW.book_name,
            'groupKey',    NEW.group_key
        )::text
    );
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION tc.events_realtime_broadcast() IS
    'Broadcasts a realtime notification on channel realtime:{collection_id} for every new '
    'event row. The message shape matches CONTRACTS.md §Realtime.';

CREATE TRIGGER events_realtime_broadcast_tg
    AFTER INSERT ON tc.events
    FOR EACH ROW EXECUTE FUNCTION tc.events_realtime_broadcast();

-- ---------------------------------------------------------------------------
-- checkin_transactions  (open send transactions; reaped on expiry)
-- ---------------------------------------------------------------------------
-- Tracks in-flight checkin-start → checkin-finish two-phase commits.
-- An open transaction means a new book row may exist with no current_version_id (invisible).
-- Expiry is 48 hours (per design); expired rows should be reaped by a scheduled function
-- (edge function task 02 / pg_cron).
CREATE TABLE tc.checkin_transactions (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    collection_id   uuid        NOT NULL REFERENCES tc.collections(id) ON DELETE CASCADE,
    book_id         uuid        NOT NULL REFERENCES tc.books(id) ON DELETE CASCADE,
    started_by      text        NOT NULL,   -- user_id
    proposed_name   text        NOT NULL,
    base_version_id uuid        REFERENCES tc.versions(id),
    changed_paths   text[]      NOT NULL DEFAULT '{}',
    client_version  text,
    started_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL DEFAULT (now() + INTERVAL '48 hours'),
    finished_at     timestamptz,            -- set on checkin-finish (success or abort)
    aborted_at      timestamptz,
    status          text        NOT NULL DEFAULT 'open'
                    CHECK (status IN ('open', 'finished', 'aborted', 'expired'))
);

COMMENT ON TABLE tc.checkin_transactions IS
    'Open check-in transactions (checkin-start → checkin-finish). '
    'expires_at = 48h; expired rows are reaped by a scheduled edge function. '
    'An open transaction for a new book means the book row has no current_version_id '
    'and is invisible to teammates until checkin-finish commits it.';

CREATE INDEX checkin_transactions_book_id_idx        ON tc.checkin_transactions(book_id);
CREATE INDEX checkin_transactions_started_by_idx     ON tc.checkin_transactions(started_by);
CREATE INDEX checkin_transactions_expires_at_idx     ON tc.checkin_transactions(expires_at)
    WHERE status = 'open';
