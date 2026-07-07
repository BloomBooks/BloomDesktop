using System;
using Bloom.History;
using Bloom.web.controllers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for SharingApi's pure wire-shape mapping/resolution logic (the parts that don't
    /// require a live Supabase server): mapping RPC snake_case rows to the camelCase shapes
    /// sharingApi.ts (task 07) already committed to, and resolving an email to a member row id
    /// for the members_remove/members_set_role RPC-param fix. Endpoints that dispatch to a live
    /// CloudCollectionClient are exercised instead (with a fake REST executor) by the Cloud test
    /// suite's own established pattern -- SharingApi itself adds no additional Cloud-backend
    /// business logic beyond this mapping, per its own file's design.
    /// </summary>
    [TestFixture]
    public class SharingApiTests
    {
        [Test]
        public void ToApprovedMember_ClaimedAdmin_MapsAllFields()
        {
            var row = new JObject
            {
                ["id"] = 1,
                ["email"] = "sara@example.com",
                ["role"] = "admin",
                ["user_id"] = "user-abc",
                ["added_by"] = "user-xyz",
                ["added_at"] = "2026-07-01T00:00:00Z",
                ["claimed_at"] = "2026-07-02T00:00:00Z",
            };

            dynamic result = SharingApi.ToApprovedMember(row);

            Assert.That(result.email, Is.EqualTo("sara@example.com"));
            Assert.That(result.role, Is.EqualTo("admin"));
            Assert.That(result.claimed, Is.True);
            Assert.That(result.name, Is.Null); // no display-name source server-side; see comment
        }

        [Test]
        public void ToApprovedMember_UnclaimedInvitation_ClaimedIsFalse()
        {
            var row = new JObject
            {
                ["id"] = 2,
                ["email"] = "pending@example.com",
                ["role"] = "member",
                ["user_id"] = null,
                ["added_by"] = "user-xyz",
                ["added_at"] = "2026-07-01T00:00:00Z",
                ["claimed_at"] = null,
            };

            dynamic result = SharingApi.ToApprovedMember(row);

            Assert.That(result.email, Is.EqualTo("pending@example.com"));
            Assert.That(
                result.claimed,
                Is.False,
                "An invitation with no user_id yet has not been claimed"
            );
        }

        [Test]
        public void ToCollectionSummary_MapsSnakeCaseMyRoleToCamelCaseRole()
        {
            // Exact shape live-verified against the local dev stack's my_collections() RPC.
            var row = new JObject
            {
                ["id"] = "11111111-1111-1111-1111-111111111111",
                ["name"] = "My Cloud Collection",
                ["created_at"] = "2026-07-01T00:00:00Z",
                ["created_by"] = "user-1",
                ["my_role"] = "admin",
                ["is_claimed"] = true,
            };

            dynamic result = SharingApi.ToCollectionSummary(row);

            Assert.That(result.collectionId, Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That(result.name, Is.EqualTo("My Cloud Collection"));
            Assert.That(
                result.role,
                Is.EqualTo("admin"),
                "role must come from my_role, not a non-existent 'role' key"
            );
        }

        [Test]
        public void ToBookHistoryEvent_MapsToSameShapeFolderTeamCollectionsUse()
        {
            var when = new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);
            var e = new JObject
            {
                ["id"] = 42,
                ["book_id"] = "book-1",
                ["type"] = 5, // ForcedUnlock
                ["by_user_id"] = "user-1",
                ["by_user_name"] = "Sara",
                ["by_email"] = "sara@example.com",
                ["book_version_seq"] = 3,
                ["lock_info"] = null,
                ["book_name"] = "My Book",
                ["group_key"] = null,
                ["message"] = "Admin force-unlocked",
                ["bloom_version"] = "6.2.100",
                ["occurred_at"] = when.ToString("O"),
            };

            var result = SharingApi.ToBookHistoryEvent(e);

            Assert.That(result.BookId, Is.EqualTo("book-1"));
            Assert.That(result.Title, Is.EqualTo("My Book"));
            Assert.That(result.Message, Is.EqualTo("Admin force-unlocked"));
            Assert.That(result.Type, Is.EqualTo(BookHistoryEventType.ForcedUnlock));
            Assert.That(result.UserId, Is.EqualTo("user-1"));
            Assert.That(result.UserName, Is.EqualTo("Sara"));
            Assert.That(result.When, Is.EqualTo(when));
            Assert.That(result.BloomVersion, Is.EqualTo("6.2.100"));
        }

        [Test]
        public void ToBookHistoryEvent_NoDisplayName_FallsBackToEmail()
        {
            var e = new JObject
            {
                ["id"] = 43,
                ["book_id"] = "book-1",
                ["type"] = 0, // CheckOut
                ["by_user_id"] = "user-2",
                ["by_user_name"] = null,
                ["by_email"] = "bob@example.com",
                ["book_name"] = "My Book",
                ["message"] = null,
                ["bloom_version"] = "6.2.100",
                ["occurred_at"] = DateTime.UtcNow.ToString("O"),
            };

            var result = SharingApi.ToBookHistoryEvent(e);

            Assert.That(
                result.UserName,
                Is.EqualTo("bob@example.com"),
                "when by_user_name is null (no JWT name claim, common in dev-auth mode), UserName "
                    + "should fall back to the always-present by_email rather than showing nothing"
            );
        }

        [Test]
        public void ResolveMemberIdFromList_KnownEmail_ReturnsRowId()
        {
            var members = new JArray(
                new JObject
                {
                    ["id"] = 17,
                    ["email"] = "admin@dev.local",
                    ["role"] = "admin",
                },
                new JObject
                {
                    ["id"] = 18,
                    ["email"] = "alice@dev.local",
                    ["role"] = "member",
                }
            );

            var id = SharingApi.ResolveMemberIdFromList(members, "collection-1", "alice@dev.local");

            Assert.That(id, Is.EqualTo(18));
        }

        [Test]
        public void ResolveMemberIdFromList_EmailIsCaseInsensitive()
        {
            var members = new JArray(
                new JObject
                {
                    ["id"] = 18,
                    ["email"] = "alice@dev.local",
                    ["role"] = "member",
                }
            );

            var id = SharingApi.ResolveMemberIdFromList(members, "collection-1", "ALICE@DEV.LOCAL");

            Assert.That(id, Is.EqualTo(18));
        }

        [Test]
        public void ResolveMemberIdFromList_UnknownEmail_Throws()
        {
            var members = new JArray(
                new JObject
                {
                    ["id"] = 17,
                    ["email"] = "admin@dev.local",
                    ["role"] = "admin",
                }
            );

            Assert.Throws<ApplicationException>(
                () =>
                    SharingApi.ResolveMemberIdFromList(
                        members,
                        "collection-1",
                        "not-a-member@dev.local"
                    ),
                "removeApproval/setRole for someone who isn't an approved member is a genuine "
                    + "caller error and should fail loudly, not silently no-op"
            );
        }
    }
}
