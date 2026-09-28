using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.History;
using Bloom.Sharing;
using NUnit.Framework;

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
    }
}
