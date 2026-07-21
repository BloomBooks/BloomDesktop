-- Team Collections cloud: tables, constraints, indexes, and triggers.
CREATE TABLE IF NOT EXISTS tc.books (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    collection_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    name text NOT NULL,
    current_version_id uuid,
    current_version_seq bigint,
    current_checksum text,
    locked_by text,
    locked_by_machine text,
    locked_at timestamp with time zone,
    deleted_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by text NOT NULL,
    locked_seat text
);

COMMENT ON TABLE tc.books IS 'Authoritative book state per collection. Lock columns, soft tombstone, current-version denormalization. All state transitions go through RPCs/edge functions; no direct writes via PostgREST.';

COMMENT ON COLUMN tc.books.instance_id IS 'Client-generated UUID stable across renames. Used as the S3 prefix key (tc/{cid}/books/{instance_id}/).';

COMMENT ON COLUMN tc.books.name IS 'NFC-normalized on write by the nfc_normalize_book_name trigger. Live-name uniqueness (lower(name)) is enforced by a partial unique index.';

COMMENT ON COLUMN tc.books.deleted_at IS 'Soft tombstone: non-NULL = deleted. Tombstoned names are reusable (excluded from the live-name uniqueness index).';

COMMENT ON COLUMN tc.books.locked_seat IS 'Which local copy of the collection ("seat") holds the lock: a client-computed stable hash of the local collection folder path (never the raw path). NULL = unknown (legacy lock, or one acquired by checkin_start_tx''s take-if-free path); a NULL seat can never be taken over (fail-safe).';

CREATE TABLE IF NOT EXISTS tc.checkin_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    collection_id uuid NOT NULL,
    book_id uuid NOT NULL,
    started_by text NOT NULL,
    proposed_name text NOT NULL,
    base_version_id uuid,
    changed_paths text[] DEFAULT '{}'::text[] NOT NULL,
    client_version text,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone DEFAULT (now() + '48:00:00'::interval) NOT NULL,
    finished_at timestamp with time zone,
    aborted_at timestamp with time zone,
    status text DEFAULT 'open'::text NOT NULL,
    proposed_files jsonb DEFAULT '[]'::jsonb NOT NULL,
    checksum text,
    result_version_id uuid,
    result_seq bigint,
    CONSTRAINT checkin_transactions_status_check CHECK ((status = ANY (ARRAY['open'::text, 'finished'::text, 'aborted'::text, 'expired'::text])))
);

COMMENT ON TABLE tc.checkin_transactions IS 'Open check-in transactions (checkin-start → checkin-finish). expires_at = 48h; expired rows are reaped by a scheduled edge function. An open transaction for a new book means the book row has no current_version_id and is invisible to teammates until checkin-finish commits it.';

COMMENT ON COLUMN tc.checkin_transactions.proposed_files IS 'Full proposed manifest [{path,sha256,size}] captured at checkin-start; checkin-finish reconstructs the committed manifest from this + changed_paths + the S3 version-ids captured after upload verification.';

COMMENT ON COLUMN tc.checkin_transactions.checksum IS 'SHA-256 checksum of the full proposed manifest, supplied at checkin-start and persisted as tc.versions.checksum / tc.books.current_checksum on finish.';

COMMENT ON COLUMN tc.checkin_transactions.result_version_id IS 'Set on successful checkin-finish; makes a repeated checkin-finish call for an already-finished transaction idempotent (returns the same result).';

CREATE TABLE IF NOT EXISTS tc.collection_file_groups (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    group_key text NOT NULL,
    version bigint DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_by text NOT NULL,
    CONSTRAINT collection_file_groups_group_key_check CHECK ((group_key = ANY (ARRAY['other'::text, 'allowed-words'::text, 'sample-texts'::text])))
);

COMMENT ON TABLE tc.collection_file_groups IS 'Versioned collection-level file groups. version is bumped atomically by collection-files-finish edge function; 409 VersionConflict if expectedVersion != version.';

ALTER TABLE tc.collection_file_groups ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.collection_file_groups_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.collection_file_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    collection_id uuid NOT NULL,
    group_key text NOT NULL,
    started_by text NOT NULL,
    expected_version bigint NOT NULL,
    proposed_files jsonb DEFAULT '[]'::jsonb NOT NULL,
    changed_paths text[] DEFAULT '{}'::text[] NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone DEFAULT (now() + '48:00:00'::interval) NOT NULL,
    finished_at timestamp with time zone,
    aborted_at timestamp with time zone,
    status text DEFAULT 'open'::text NOT NULL,
    result_version bigint,
    CONSTRAINT collection_file_transactions_group_key_check CHECK ((group_key = ANY (ARRAY['other'::text, 'allowed-words'::text, 'sample-texts'::text]))),
    CONSTRAINT collection_file_transactions_status_check CHECK ((status = ANY (ARRAY['open'::text, 'finished'::text, 'aborted'::text, 'expired'::text])))
);

COMMENT ON TABLE tc.collection_file_transactions IS 'Open collection-files-start -> collection-files-finish two-phase commits. Mirrors tc.checkin_transactions but scoped to (collection_id, group_key) instead of a book.';

CREATE TABLE IF NOT EXISTS tc.collection_group_files (
    id bigint NOT NULL,
    group_id bigint NOT NULL,
    path text NOT NULL,
    sha256 text NOT NULL,
    size_bytes bigint NOT NULL,
    s3_version_id text NOT NULL
);

COMMENT ON TABLE tc.collection_group_files IS 'Current file manifest for each collection_file_group. Rows for the old version are replaced atomically by collection-files-finish.';

ALTER TABLE tc.collection_group_files ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.collection_group_files_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.collections (
    id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by text NOT NULL
);

COMMENT ON TABLE tc.collections IS 'One row per cloud Team Collection. id = Bloom CollectionId GUID.';

COMMENT ON COLUMN tc.collections.id IS 'The collection UUID — same value as in TeamCollectionLink.txt (cloud://sil.bloom/collection/<id>).';

COMMENT ON COLUMN tc.collections.created_by IS 'TEXT user id (Firebase UID or GoTrue UUID) of the creator.';

CREATE TABLE IF NOT EXISTS tc.color_palette_entries (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    palette text NOT NULL,
    color text NOT NULL,
    added_at timestamp with time zone DEFAULT now() NOT NULL,
    added_by text NOT NULL
);

COMMENT ON TABLE tc.color_palette_entries IS 'Color palette entries per collection. Merge is union-only: insert ... on conflict do nothing. No rows are ever deleted.';

ALTER TABLE tc.color_palette_entries ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.color_palette_entries_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.events (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    book_id uuid,
    type integer NOT NULL,
    by_user_id text NOT NULL,
    by_user_name text,
    by_email text,
    book_version_seq bigint,
    lock_info jsonb,
    book_name text,
    group_key text,
    message text,
    bloom_version text,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT events_type_check CHECK ((type = ANY (ARRAY[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 100])))
);

COMMENT ON TABLE tc.events IS 'History log, realtime broadcast source, and polling cursor. type values mirror C# BookHistoryEventType (HistoryEvent.cs): 0=CheckOut, 1=CheckIn, 2=Created, 3=Renamed, 4=Uploaded(legacy), 5=ForcedUnlock, 6=ImportSpreadsheet, 7=SyncProblem(legacy), 8=Deleted, 9=Moved. Cloud-TC incident extensions start at 100 to avoid colliding with future C# additions: 100=WorkPreservedLocally.';

ALTER TABLE tc.events ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.members (
    id bigint NOT NULL,
    collection_id uuid NOT NULL,
    email text NOT NULL,
    role tc.member_role DEFAULT 'member'::tc.member_role NOT NULL,
    user_id text,
    added_by text NOT NULL,
    added_at timestamp with time zone DEFAULT now() NOT NULL,
    claimed_at timestamp with time zone,
    display_name text
);

COMMENT ON TABLE tc.members IS 'Approved-accounts table. Unclaimed rows (user_id IS NULL) are pending until the account holder signs in and calls claim_memberships(). email is stored lowercase + NFC-normalised.';

COMMENT ON COLUMN tc.members.user_id IS 'NULL until the account holder claims the seat. TEXT covers both Firebase UIDs and local-GoTrue UUIDs.';

COMMENT ON COLUMN tc.members.display_name IS 'Human-readable name shown in place of the email wherever the member is displayed (checkout status, history, sharing panel). NULL = none set; display falls back to email. Set via tc.members_set_display_name (admin, or the claimed member themselves).';

ALTER TABLE tc.members ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.members_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.version_files (
    id bigint NOT NULL,
    book_id uuid NOT NULL,
    version_id uuid NOT NULL,
    path text NOT NULL,
    sha256 text NOT NULL,
    size_bytes bigint NOT NULL,
    s3_version_id text NOT NULL
);

COMMENT ON TABLE tc.version_files IS 'Current manifest for each book: path → sha256, size, s3_version_id. Superseded rows are pruned at checkin-finish. Reads always use (path, s3_version_id).';

ALTER TABLE tc.version_files ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME tc.version_files_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE IF NOT EXISTS tc.versions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    book_id uuid NOT NULL,
    collection_id uuid NOT NULL,
    seq bigint NOT NULL,
    checksum text NOT NULL,
    comment text,
    created_by text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    client_version text
);

COMMENT ON TABLE tc.versions IS 'One metadata row per successful check-in (checkin-finish edge function). seq is monotonically increasing per book.';

ALTER TABLE ONLY tc.books
    ADD CONSTRAINT books_collection_instance_uq UNIQUE (collection_id, instance_id);

ALTER TABLE ONLY tc.books
    ADD CONSTRAINT books_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collection_file_groups
    ADD CONSTRAINT collection_file_groups_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collection_file_groups
    ADD CONSTRAINT collection_file_groups_uq UNIQUE (collection_id, group_key);

ALTER TABLE ONLY tc.collection_file_transactions
    ADD CONSTRAINT collection_file_transactions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collection_group_files
    ADD CONSTRAINT collection_group_files_group_path_uq UNIQUE (group_id, path);

ALTER TABLE ONLY tc.collection_group_files
    ADD CONSTRAINT collection_group_files_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.collections
    ADD CONSTRAINT collections_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.color_palette_entries
    ADD CONSTRAINT color_palette_entries_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.color_palette_entries
    ADD CONSTRAINT color_palette_entries_uq UNIQUE (collection_id, palette, color);

ALTER TABLE ONLY tc.events
    ADD CONSTRAINT events_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_claimed_user_uq UNIQUE (collection_id, user_id);

COMMENT ON CONSTRAINT members_claimed_user_uq ON tc.members IS 'A claimed user appears at most once per collection. NULL user_id rows (pending invitations) do not collide (default NULLS DISTINCT; fixed 20260713000002 — the original NULLS NOT DISTINCT allowed only one pending invitation per collection).';

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_collection_email_uq UNIQUE (collection_id, email);

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_book_path_uq UNIQUE (book_id, path);

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_pkey PRIMARY KEY (id);

ALTER TABLE ONLY tc.versions
    ADD CONSTRAINT versions_book_seq_uq UNIQUE (book_id, seq);

ALTER TABLE ONLY tc.versions
    ADD CONSTRAINT versions_pkey PRIMARY KEY (id);

CREATE INDEX books_collection_id_idx ON tc.books USING btree (collection_id);

CREATE INDEX books_instance_id_idx ON tc.books USING btree (instance_id);

CREATE UNIQUE INDEX books_live_name_uq ON tc.books USING btree (collection_id, lower(NORMALIZE(name, NFC))) WHERE (deleted_at IS NULL);

CREATE INDEX books_locked_by_idx ON tc.books USING btree (locked_by) WHERE (locked_by IS NOT NULL);

CREATE INDEX checkin_transactions_book_id_idx ON tc.checkin_transactions USING btree (book_id);

CREATE INDEX checkin_transactions_expires_at_idx ON tc.checkin_transactions USING btree (expires_at) WHERE (status = 'open'::text);

CREATE INDEX checkin_transactions_started_by_idx ON tc.checkin_transactions USING btree (started_by);

CREATE INDEX collection_file_groups_collection_id_idx ON tc.collection_file_groups USING btree (collection_id);

CREATE INDEX collection_file_transactions_expires_at_idx ON tc.collection_file_transactions USING btree (expires_at) WHERE (status = 'open'::text);

CREATE INDEX collection_file_transactions_scope_idx ON tc.collection_file_transactions USING btree (collection_id, group_key, started_by) WHERE (status = 'open'::text);

CREATE INDEX collection_group_files_group_id_idx ON tc.collection_group_files USING btree (group_id);

CREATE INDEX color_palette_entries_collection_id_idx ON tc.color_palette_entries USING btree (collection_id);

CREATE INDEX events_book_id_idx ON tc.events USING btree (book_id) WHERE (book_id IS NOT NULL);

CREATE INDEX events_collection_cursor_idx ON tc.events USING btree (collection_id, id);

CREATE INDEX events_collection_id_idx ON tc.events USING btree (collection_id);

CREATE INDEX members_collection_id_idx ON tc.members USING btree (collection_id);

CREATE INDEX members_email_idx ON tc.members USING btree (lower(email));

CREATE INDEX members_user_id_idx ON tc.members USING btree (user_id) WHERE (user_id IS NOT NULL);

CREATE INDEX version_files_book_id_idx ON tc.version_files USING btree (book_id);

CREATE INDEX version_files_version_id_idx ON tc.version_files USING btree (version_id);

CREATE INDEX versions_book_id_idx ON tc.versions USING btree (book_id);

CREATE INDEX versions_collection_id_idx ON tc.versions USING btree (collection_id);

CREATE OR REPLACE TRIGGER books_clear_seat_on_unlock BEFORE UPDATE ON tc.books FOR EACH ROW EXECUTE FUNCTION tc._clear_seat_on_unlock();

CREATE OR REPLACE TRIGGER books_nfc_normalize_name_tg BEFORE INSERT OR UPDATE OF name ON tc.books FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_book_name();

CREATE OR REPLACE TRIGGER collection_group_files_nfc_normalize_path_tg BEFORE INSERT OR UPDATE OF path ON tc.collection_group_files FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_path();

CREATE OR REPLACE TRIGGER events_realtime_broadcast_tg AFTER INSERT ON tc.events FOR EACH ROW EXECUTE FUNCTION tc.events_realtime_broadcast();

CREATE OR REPLACE TRIGGER members_last_admin_guard_tg BEFORE DELETE OR UPDATE ON tc.members FOR EACH ROW EXECUTE FUNCTION tc.members_last_admin_guard();

CREATE OR REPLACE TRIGGER version_files_nfc_normalize_path_tg BEFORE INSERT OR UPDATE OF path ON tc.version_files FOR EACH ROW EXECUTE FUNCTION tc.nfc_normalize_path();

ALTER TABLE ONLY tc.books
    ADD CONSTRAINT books_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_base_version_id_fkey FOREIGN KEY (base_version_id) REFERENCES tc.versions(id);

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.checkin_transactions
    ADD CONSTRAINT checkin_transactions_result_version_id_fkey FOREIGN KEY (result_version_id) REFERENCES tc.versions(id);

ALTER TABLE ONLY tc.collection_file_groups
    ADD CONSTRAINT collection_file_groups_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.collection_file_transactions
    ADD CONSTRAINT collection_file_transactions_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.collection_group_files
    ADD CONSTRAINT collection_group_files_group_id_fkey FOREIGN KEY (group_id) REFERENCES tc.collection_file_groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.color_palette_entries
    ADD CONSTRAINT color_palette_entries_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.events
    ADD CONSTRAINT events_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE SET NULL;

ALTER TABLE ONLY tc.events
    ADD CONSTRAINT events_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.members
    ADD CONSTRAINT members_collection_id_fkey FOREIGN KEY (collection_id) REFERENCES tc.collections(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.version_files
    ADD CONSTRAINT version_files_version_id_fkey FOREIGN KEY (version_id) REFERENCES tc.versions(id) ON DELETE CASCADE;

ALTER TABLE ONLY tc.versions
    ADD CONSTRAINT versions_book_id_fkey FOREIGN KEY (book_id) REFERENCES tc.books(id) ON DELETE CASCADE;
