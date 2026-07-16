using System;
using System.Collections.Generic;
using System.IO;
using Bloom.History;
using Bloom.web.controllers;
using Newtonsoft.Json;
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
                ["display_name"] = "Sara S",
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
            Assert.That(result.name, Is.EqualTo("Sara S"));
        }

        [Test]
        public void ToApprovedMember_NoDisplayNameSet_NameIsNull()
        {
            // display_name is NULL until someone sets it (20260713000001 migration); the UI
            // falls back to the email. A JSON-null token must map to a real null, not "".
            var row = new JObject
            {
                ["id"] = 3,
                ["email"] = "unnamed@example.com",
                ["display_name"] = null,
                ["role"] = "member",
                ["user_id"] = "user-def",
            };

            dynamic result = SharingApi.ToApprovedMember(row);

            Assert.That(result.name, Is.Null);
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
            // The (DateTime) JToken cast (matching this codebase's existing
            // (DateTime?)row["locked_at"]-style casts elsewhere) converts to local kind, so compare
            // in UTC rather than assuming the cast preserves Z/UTC as-is.
            Assert.That(result.When.ToUniversalTime(), Is.EqualTo(when));
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
        public void ToBookHistoryEvent_DurableDisplayNamePresent_BeatsJwtNameAndEmail()
        {
            var e = new JObject
            {
                ["id"] = 44,
                ["book_id"] = "book-1",
                ["type"] = 1, // CheckIn
                ["by_user_id"] = "user-3",
                ["by_user_name"] = "JWT Name",
                ["by_email"] = "carol@example.com",
                ["by_display_name"] = "Carol the Editor",
                ["book_name"] = "My Book",
                ["message"] = null,
                ["bloom_version"] = "6.2.100",
                ["occurred_at"] = DateTime.UtcNow.ToString("O"),
            };

            var result = SharingApi.ToBookHistoryEvent(e);

            Assert.That(
                result.UserName,
                Is.EqualTo("Carol the Editor"),
                "the member's current durable display name (by_display_name, 20260713000001) "
                    + "should beat both the at-event-time JWT name claim and the email"
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

        // ------------------------------------------------------------------
        // History incremental cache (E8): fetch only NEW events past the cursor
        // and merge, instead of re-downloading the whole log every open.
        // ------------------------------------------------------------------

        private static JObject HistoryEventJson(string bookId, DateTime when, string message) =>
            new JObject
            {
                ["book_id"] = bookId,
                ["type"] = 1, // CheckIn
                ["by_user_id"] = "u",
                ["by_user_name"] = "U",
                ["by_email"] = "u@example.com",
                ["book_name"] = "B",
                ["message"] = message,
                ["bloom_version"] = "6.2",
                ["occurred_at"] = when.ToString("O"),
            };

        [Test]
        public void MergeHistory_Cursor0_ReplacesWithWholeLog()
        {
            var cached = new SharingApi.CloudHistoryCache
            {
                MaxEventId = 0,
                Events = new List<BookHistoryEvent>
                {
                    SharingApi.ToBookHistoryEvent(
                        HistoryEventJson(
                            "b1",
                            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            "stale"
                        )
                    ),
                },
            };
            var wholeLog = new JArray
            {
                HistoryEventJson(
                    "b2",
                    new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    "fresh"
                ),
            };

            var result = SharingApi.MergeHistory(cached, wholeLog, 5);

            Assert.That(result.MaxEventId, Is.EqualTo(5));
            Assert.That(
                result.Events.Count,
                Is.EqualTo(1),
                "a 0 cursor means the response is the whole log, so it replaces the cache"
            );
            Assert.That(result.Events[0].Message, Is.EqualTo("fresh"));
        }

        [Test]
        public void MergeHistory_NonZeroCursor_AppendsNewEventsNewestFirst()
        {
            var cached = new SharingApi.CloudHistoryCache
            {
                MaxEventId = 3,
                Events = new List<BookHistoryEvent>
                {
                    SharingApi.ToBookHistoryEvent(
                        HistoryEventJson(
                            "b1",
                            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            "old"
                        )
                    ),
                },
            };
            var delta = new JArray
            {
                HistoryEventJson("b2", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "new"),
            };

            var result = SharingApi.MergeHistory(cached, delta, 7);

            Assert.That(result.MaxEventId, Is.EqualTo(7));
            Assert.That(result.Events.Count, Is.EqualTo(2), "new events append to the cached ones");
            Assert.That(result.Events[0].Message, Is.EqualTo("new"), "sorted newest-first");
            Assert.That(result.Events[1].Message, Is.EqualTo("old"));
        }

        [Test]
        public void MergeHistory_NullResponseMax_KeepsExistingCursor()
        {
            var cached = new SharingApi.CloudHistoryCache
            {
                MaxEventId = 3,
                Events = new List<BookHistoryEvent>(),
            };

            var result = SharingApi.MergeHistory(cached, new JArray(), null);

            Assert.That(result.MaxEventId, Is.EqualTo(3));
            Assert.That(result.Events, Is.Empty);
        }

        [Test]
        public void LoadHistoryCache_RoundTripsNewFormat()
        {
            var path = TempCachePath();
            try
            {
                var cache = new SharingApi.CloudHistoryCache
                {
                    MaxEventId = 9,
                    Events = new List<BookHistoryEvent>
                    {
                        SharingApi.ToBookHistoryEvent(HistoryEventJson("b1", DateTime.UtcNow, "m")),
                    },
                };
                SharingApi.SaveHistoryCache(path, cache);

                var loaded = SharingApi.LoadHistoryCache(path);

                Assert.That(loaded.MaxEventId, Is.EqualTo(9));
                Assert.That(loaded.Events.Count, Is.EqualTo(1));
                Assert.That(loaded.Events[0].BookId, Is.EqualTo("b1"));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void LoadHistoryCache_OldBareArrayFormat_KeepsEventsButCursorZero()
        {
            var path = TempCachePath();
            try
            {
                // The pre-incremental on-disk format was a bare List<BookHistoryEvent>.
                var oldEvents = new List<BookHistoryEvent>
                {
                    SharingApi.ToBookHistoryEvent(HistoryEventJson("b1", DateTime.UtcNow, "m")),
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(oldEvents));

                var loaded = SharingApi.LoadHistoryCache(path);

                Assert.That(
                    loaded.MaxEventId,
                    Is.EqualTo(0),
                    "an old bare-array cache has no cursor, forcing the next fetch to be a full refetch"
                );
                Assert.That(
                    loaded.Events.Count,
                    Is.EqualTo(1),
                    "old-format events are preserved so the offline view still works after upgrade"
                );
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void LoadHistoryCache_MissingFileOrNullPath_ReturnsEmpty()
        {
            Assert.That(SharingApi.LoadHistoryCache(null).Events, Is.Empty);
            var loaded = SharingApi.LoadHistoryCache(TempCachePath()); // never written
            Assert.That(loaded.MaxEventId, Is.EqualTo(0));
            Assert.That(loaded.Events, Is.Empty);
        }

        private static string TempCachePath() =>
            Path.Combine(
                Path.GetTempPath(),
                "BloomHistoryCacheTest-" + Guid.NewGuid().ToString("N") + ".json"
            );
    }
}
