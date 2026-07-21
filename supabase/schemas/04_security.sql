-- Team Collections cloud: row-level security (enable + policies) and grants.
-- Writes go through SECURITY DEFINER RPCs, so most tables expose only SELECT to
-- `authenticated`; anon gets nothing.
ALTER TABLE tc.books ENABLE ROW LEVEL SECURITY;

CREATE POLICY books_select ON tc.books FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.checkin_transactions ENABLE ROW LEVEL SECURITY;

CREATE POLICY checkin_transactions_select ON tc.checkin_transactions FOR SELECT USING ((started_by = tc.current_user_id()));

ALTER TABLE tc.collection_file_groups ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_file_groups_select ON tc.collection_file_groups FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.collection_file_transactions ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_file_transactions_select ON tc.collection_file_transactions FOR SELECT USING ((started_by = tc.current_user_id()));

ALTER TABLE tc.collection_group_files ENABLE ROW LEVEL SECURITY;

CREATE POLICY collection_group_files_select ON tc.collection_group_files FOR SELECT USING ((EXISTS ( SELECT 1
   FROM tc.collection_file_groups fg
  WHERE ((fg.id = collection_group_files.group_id) AND tc.is_member(fg.collection_id)))));

ALTER TABLE tc.collections ENABLE ROW LEVEL SECURITY;

CREATE POLICY collections_select ON tc.collections FOR SELECT USING (tc.is_member(id));

ALTER TABLE tc.color_palette_entries ENABLE ROW LEVEL SECURITY;

CREATE POLICY color_palette_entries_insert ON tc.color_palette_entries FOR INSERT WITH CHECK (tc.is_member(collection_id));

CREATE POLICY color_palette_entries_select ON tc.color_palette_entries FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.events ENABLE ROW LEVEL SECURITY;

CREATE POLICY events_insert ON tc.events FOR INSERT WITH CHECK ((tc.is_member(collection_id) AND (by_user_id = tc.current_user_id())));

CREATE POLICY events_select ON tc.events FOR SELECT USING (tc.is_member(collection_id));

ALTER TABLE tc.members ENABLE ROW LEVEL SECURITY;

CREATE POLICY members_delete ON tc.members FOR DELETE USING (tc.is_admin(collection_id));

CREATE POLICY members_insert ON tc.members FOR INSERT WITH CHECK (tc.is_admin(collection_id));

CREATE POLICY members_select ON tc.members FOR SELECT USING (tc.is_member(collection_id));

CREATE POLICY members_update ON tc.members FOR UPDATE USING (tc.is_admin(collection_id)) WITH CHECK (tc.is_admin(collection_id));

ALTER TABLE tc.version_files ENABLE ROW LEVEL SECURITY;

CREATE POLICY version_files_select ON tc.version_files FOR SELECT USING ((EXISTS ( SELECT 1
   FROM tc.books b
  WHERE ((b.id = version_files.book_id) AND tc.is_member(b.collection_id)))));

ALTER TABLE tc.versions ENABLE ROW LEVEL SECURITY;

CREATE POLICY versions_select ON tc.versions FOR SELECT USING (tc.is_member(collection_id));

GRANT USAGE ON SCHEMA tc TO authenticated;

GRANT ALL ON FUNCTION tc.add_palette_colors(p_collection_id uuid, p_palette text, p_colors text[]) TO authenticated;

GRANT ALL ON FUNCTION tc.checkin_abort_tx(p_transaction_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.checkin_finish_tx(p_transaction_id uuid, p_comment text, p_keep_checked_out boolean, p_captured jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.checkin_start_tx(p_collection_id uuid, p_book_id uuid, p_book_instance_id uuid, p_proposed_name text, p_base_version_id uuid, p_checksum text, p_client_version text, p_files jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.checkout_book(p_book_id uuid, p_machine text, p_seat text) TO authenticated;

GRANT ALL ON FUNCTION tc.checkout_book_takeover(p_book_id uuid, p_machine text, p_seat text) TO authenticated;

GRANT ALL ON FUNCTION tc.claim_memberships() TO authenticated;

GRANT ALL ON FUNCTION tc.collection_files_finish_tx(p_transaction_id uuid, p_captured jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.collection_files_start_tx(p_collection_id uuid, p_group_key text, p_expected_version bigint, p_files jsonb) TO authenticated;

GRANT ALL ON FUNCTION tc.create_collection(p_id uuid, p_name text) TO authenticated;

GRANT ALL ON FUNCTION tc.delete_book(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.download_start_check(p_collection_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.force_unlock(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.get_book_manifest(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.get_changes(p_collection_id uuid, p_since_event_id bigint) TO authenticated;

GRANT ALL ON FUNCTION tc.get_collection_file_manifest(p_collection_id uuid, p_group_key text) TO authenticated;

GRANT ALL ON FUNCTION tc.get_collection_state(p_collection_id uuid, p_since_event_id bigint) TO authenticated;

REVOKE ALL ON FUNCTION tc.list_stale_upload_garbage() FROM PUBLIC;
GRANT ALL ON FUNCTION tc.list_stale_upload_garbage() TO service_role;

GRANT ALL ON FUNCTION tc.log_event(p_collection_id uuid, p_book_id uuid, p_type integer, p_message text, p_book_name text, p_bloom_version text) TO authenticated;

GRANT ALL ON FUNCTION tc.members_add(p_collection_id uuid, p_email text, p_role tc.member_role) TO authenticated;

GRANT ALL ON FUNCTION tc.members_list(p_collection_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.members_remove(p_collection_id uuid, p_member_id bigint) TO authenticated;

GRANT ALL ON FUNCTION tc.members_set_display_name(p_collection_id uuid, p_member_id bigint, p_display_name text) TO authenticated;

GRANT ALL ON FUNCTION tc.members_set_role(p_collection_id uuid, p_member_id bigint, p_new_role tc.member_role) TO authenticated;

GRANT ALL ON FUNCTION tc.my_collections() TO authenticated;

GRANT ALL ON FUNCTION tc.reap_expired_checkin_transactions() TO authenticated;

GRANT ALL ON FUNCTION tc.rename_check(p_book_id uuid, p_new_name text) TO authenticated;

GRANT ALL ON FUNCTION tc.resolve_member_display(p_collection_id uuid, p_user_id text, OUT email text, OUT display_name text) TO authenticated;

REVOKE ALL ON FUNCTION tc.support_set_admin(p_collection_id uuid, p_email text) FROM PUBLIC;
GRANT ALL ON FUNCTION tc.support_set_admin(p_collection_id uuid, p_email text) TO service_role;

GRANT ALL ON FUNCTION tc.undelete_book(p_book_id uuid) TO authenticated;

GRANT ALL ON FUNCTION tc.unlock_book(p_book_id uuid) TO authenticated;

GRANT SELECT ON TABLE tc.books TO authenticated;

GRANT SELECT ON TABLE tc.checkin_transactions TO authenticated;

GRANT SELECT ON TABLE tc.collection_file_groups TO authenticated;

GRANT USAGE ON SEQUENCE tc.collection_file_groups_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.collection_file_transactions TO authenticated;

GRANT SELECT ON TABLE tc.collection_group_files TO authenticated;

GRANT USAGE ON SEQUENCE tc.collection_group_files_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.collections TO authenticated;

GRANT SELECT,INSERT ON TABLE tc.color_palette_entries TO authenticated;

GRANT USAGE ON SEQUENCE tc.color_palette_entries_id_seq TO authenticated;

GRANT SELECT,INSERT ON TABLE tc.events TO authenticated;

GRANT USAGE ON SEQUENCE tc.events_id_seq TO authenticated;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE tc.members TO authenticated;

GRANT USAGE ON SEQUENCE tc.members_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.version_files TO authenticated;

GRANT USAGE ON SEQUENCE tc.version_files_id_seq TO authenticated;

GRANT SELECT ON TABLE tc.versions TO authenticated;

-- Defense in depth: anon holds no privileges anywhere in tc.
REVOKE ALL ON ALL TABLES IN SCHEMA tc FROM anon;
