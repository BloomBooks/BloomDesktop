using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.History;
using Bloom.Sharing;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.Sharing
{
    [TestFixture]
    public class TeamCollectionHistoryMembersTests
    {
        private const string kAdmin = "ruth@example.org";

        private static HistoryEvent MakeEvent(string email, string name, int day)
        {
            return new HistoryEvent
            {
                UserId = email,
                UserName = name,
                When = new DateTime(2026, 8, day, 0, 0, 0, DateTimeKind.Unspecified),
                Type = BookHistoryEventType.CheckIn,
            };
        }

        [Test]
        public void Find_GroupsByEmail_NewestFirst_WithLatestName()
        {
            var events = new List<HistoryEvent>
            {
                MakeEvent("amina@example.org", "Amina", 1),
                MakeEvent("sam@example.org", "Sam", 5),
                MakeEvent("AMINA@example.org", "Amina Y", 9),
                MakeEvent("amina@example.org", null, 3),
            };

            var result = TeamCollectionHistoryMembers.Find(events, new string[0]);

            Assert.That(
                result.Select(m => m.Email),
                Is.EqualTo(new[] { "AMINA@example.org", "sam@example.org" })
            );
            Assert.That(result[0].Name, Is.EqualTo("Amina Y"));
            Assert.That(result[0].LastActivity, Is.EqualTo(new DateTime(2026, 8, 9)));
            Assert.That(result[0].LastActivity.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(result[0].Role, Is.EqualTo(SharingRole.Editor));
        }

        [Test]
        public void Find_SkipsEventsWithoutAUsableEmail()
        {
            var events = new List<HistoryEvent>
            {
                MakeEvent(null, "Old Bloom", 1),
                MakeEvent("", "Blank", 2),
                MakeEvent("not an email", "Junk", 3),
                MakeEvent("sam@example.org", "Sam", 4),
            };

            var result = TeamCollectionHistoryMembers.Find(events, null);

            Assert.That(result.Select(m => m.Email), Is.EqualTo(new[] { "sam@example.org" }));
        }

        [Test]
        public void Find_OldTeamCollectionAdministrators_AreAdmins()
        {
            var events = new List<HistoryEvent>
            {
                MakeEvent("amina@example.org", "Amina", 1),
                MakeEvent("sam@example.org", "Sam", 2),
            };

            var result = TeamCollectionHistoryMembers.Find(events, new[] { " SAM@example.org" });

            Assert.That(
                result.Single(m => m.Email == "sam@example.org").Role,
                Is.EqualTo(SharingRole.Admin)
            );
            Assert.That(
                result.Single(m => m.Email == "amina@example.org").Role,
                Is.EqualTo(SharingRole.Editor)
            );
        }

        /// <summary>
        /// Tests of StartSharingIfTeamCollection, which the Share dialog's first look at a
        /// collection's sharing (GET sharing/state, by someone who may manage it) calls.
        /// </summary>
        [TestFixture]
        public class StartSharingIfTeamCollectionTests
        {
            private TemporaryFolder _folder;
            private DateTime _now;
            private LocalFileCollectionSharingService _service;
            private int _historyReads;

            [SetUp]
            public void Setup()
            {
                _folder = new TemporaryFolder("StartSharingIfTeamCollectionTests");
                _now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
                _service = new LocalFileCollectionSharingService(
                    _folder.Path,
                    "collection-id",
                    "Bantu Readers",
                    () => _now
                );
                _historyReads = 0;
            }

            [TearDown]
            public void TearDown()
            {
                _folder.Dispose();
            }

            // The history of a Team Collection that Ruth (its administrator, who shares it),
            // Sam (another administrator) and Amina have worked in.
            private IEnumerable<HistoryEvent> History()
            {
                _historyReads++;
                return new List<HistoryEvent>
                {
                    MakeEvent("amina@example.org", "Amina", 5),
                    MakeEvent(kAdmin, "Ruth", 25),
                    MakeEvent("sam@example.org", "Sam", 20),
                };
            }

            private static readonly string[] kAdministrators = { kAdmin, "sam@example.org" };

            private void LookAtSharing(bool isTeamCollection)
            {
                TeamCollectionHistoryMembers.StartSharingIfTeamCollection(
                    _service,
                    isTeamCollection,
                    kAdmin,
                    "Ruth Nakalema",
                    History,
                    kAdministrators
                );
            }

            [Test]
            public void TeamCollection_SharesWithEveryoneInHistory_InTheirRoles()
            {
                Assert.That(_service.GetRecord(), Is.Null, "should start unshared");

                LookAtSharing(true);

                var members = _service.GetRecord().Members;
                Assert.That(
                    members.Select(m => $"{m.Email} {m.Role}"),
                    Is.EqualTo(
                        new[]
                        {
                            $"{kAdmin} Admin",
                            "sam@example.org Admin",
                            "amina@example.org Editor",
                        }
                    ),
                    "Ruth should be there once, as the admin sharing it, not again from history"
                );
                Assert.That(members[0].LastSeen, Is.EqualTo(_now));
                Assert.That(members[0].Name, Is.EqualTo("Ruth Nakalema"));
                Assert.That(
                    members[1].LastSeen,
                    Is.EqualTo(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc))
                );
                Assert.That(
                    members[2].LastSeen,
                    Is.EqualTo(new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc))
                );
                Assert.That(members.All(m => m.InvitedAt == _now && m.InvitedBy == kAdmin));
            }

            [Test]
            public void TeamCollection_AlreadyShared_ChangesNothing()
            {
                LookAtSharing(true);
                _service.Remove(kAdmin, "amina@example.org");
                Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(2));
                Assert.That(_historyReads, Is.EqualTo(1));

                LookAtSharing(true);

                Assert.That(
                    _service.GetRecord().Members.Count,
                    Is.EqualTo(2),
                    "someone the admin removed must not come back"
                );
                Assert.That(_historyReads, Is.EqualTo(1));
            }

            [Test]
            public void OrdinaryCollection_StaysUnshared()
            {
                LookAtSharing(false);

                Assert.That(_service.GetRecord(), Is.Null);
                Assert.That(_historyReads, Is.EqualTo(0), "no need to read any history");
            }
        }
    }
}
