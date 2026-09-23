using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.History;
using Bloom.Sharing;
using NUnit.Framework;

namespace BloomTests.Sharing
{
    [TestFixture]
    public class SharingSuggestionsTests
    {
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

            var result = SharingSuggestions.Find(events, new string[0], null);

            Assert.That(
                result.Select(s => s.Email),
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

            var result = SharingSuggestions.Find(events, null, null);

            Assert.That(result.Select(s => s.Email), Is.EqualTo(new[] { "sam@example.org" }));
        }

        [Test]
        public void Find_OldTeamCollectionAdministrators_SuggestedAsAdmins()
        {
            var events = new List<HistoryEvent>
            {
                MakeEvent("amina@example.org", "Amina", 1),
                MakeEvent("sam@example.org", "Sam", 2),
            };

            var result = SharingSuggestions.Find(events, new[] { " SAM@example.org" }, null);

            Assert.That(
                result.Single(s => s.Email == "sam@example.org").Role,
                Is.EqualTo(SharingRole.Admin)
            );
            Assert.That(
                result.Single(s => s.Email == "amina@example.org").Role,
                Is.EqualTo(SharingRole.Editor)
            );
        }

        [Test]
        public void Find_LeavesOutMembersAndDismissed()
        {
            var events = new List<HistoryEvent>
            {
                MakeEvent("ruth@example.org", "Ruth", 1),
                MakeEvent("amina@example.org", "Amina", 2),
                MakeEvent("sam@example.org", "Sam", 3),
            };
            var record = new CollectionSharingRecord
            {
                Members = new List<SharingMember>
                {
                    new SharingMember { Email = "Ruth@example.org", Role = SharingRole.Admin },
                },
                DismissedSuggestions = new List<string> { "SAM@example.org" },
            };

            var result = SharingSuggestions.Find(events, new string[0], record);

            Assert.That(result.Select(s => s.Email), Is.EqualTo(new[] { "amina@example.org" }));
        }
    }
}
