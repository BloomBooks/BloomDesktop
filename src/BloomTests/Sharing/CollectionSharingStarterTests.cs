using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.History;
using Bloom.Sharing;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Sharing
{
    /// <summary>
    /// Tests of CollectionSharingStarter: the Share dialog's preview of who will have access
    /// before a collection is shared (GET sharing/state), and its Start sharing button (POST
    /// sharing/start).
    /// </summary>
    [TestFixture]
    public class CollectionSharingStarterTests
    {
        private const string kAdmin = "ruth@example.org";
        private const string kAdminName = "Ruth Nakalema";
        private static readonly string[] kAdministrators = { kAdmin, "sam@example.org" };

        private TemporaryFolder _folder;
        private DateTime _now;
        private LocalFileCollectionSharingService _service;
        private int _historyReads;

        [SetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("CollectionSharingStarterTests");
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

        // The history of a Team Collection that Ruth (its administrator, who shares it), Sam
        // (another administrator) and Amina have worked in.
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

        private CollectionSharingStarter MakeStarter(bool isTeamCollection)
        {
            return new CollectionSharingStarter(
                _service,
                isTeamCollection,
                History,
                kAdministrators,
                () => _now
            );
        }

        private string SharingFilePath =>
            Path.Combine(_folder.Path, LocalFileCollectionSharingService.kFileName);

        // The admin once, first, seen now; then Sam and Amina from the history, in their roles.
        private void AssertAreTheTeamCollectionsPeople(List<SharingMember> members)
        {
            Assert.That(
                members.Select(m => $"{m.Email} {m.Role}"),
                Is.EqualTo(
                    new[] { $"{kAdmin} Admin", "sam@example.org Admin", "amina@example.org Editor" }
                ),
                "Ruth should be there once, as the admin sharing it, not again from history"
            );
            Assert.That(members[0].Name, Is.EqualTo(kAdminName));
            Assert.That(members[0].LastSeen, Is.EqualTo(_now));
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
        public void PreviewMembers_TeamCollection_AdminAndEveryoneInHistory_SavesNothing()
        {
            var members = MakeStarter(true).PreviewMembers(kAdmin, kAdminName);

            AssertAreTheTeamCollectionsPeople(members);
            Assert.That(_service.GetRecord(), Is.Null, "a preview must not share the collection");
            Assert.That(RobustFile.Exists(SharingFilePath), Is.False, "nothing should be written");
        }

        [Test]
        public void PreviewMembers_OrdinaryCollection_JustTheAdmin_NoHistoryRead()
        {
            var members = MakeStarter(false).PreviewMembers(kAdmin, kAdminName);

            Assert.That(
                members.Select(m => $"{m.Email} {m.Role}"),
                Is.EqualTo(new[] { $"{kAdmin} Admin" })
            );
            Assert.That(_historyReads, Is.EqualTo(0), "no need to read any history");
            Assert.That(RobustFile.Exists(SharingFilePath), Is.False, "nothing should be written");
        }

        [Test]
        public void Start_TeamCollection_SharesWithWhatWasPreviewed()
        {
            var preview = MakeStarter(true).PreviewMembers(kAdmin, kAdminName);
            Assert.That(_service.GetRecord(), Is.Null, "should start unshared");

            MakeStarter(true).Start(kAdmin, kAdminName, mayManage: true);

            var members = _service.GetRecord().Members;
            AssertAreTheTeamCollectionsPeople(members);
            Assert.That(
                members.Select(m => $"{m.Email} {m.Role} {m.LastSeen}"),
                Is.EqualTo(preview.Select(m => $"{m.Email} {m.Role} {m.LastSeen}")),
                "what the admin was shown is what should be saved"
            );
        }

        [Test]
        public void Start_OrdinaryCollection_SharesWithJustTheAdmin()
        {
            MakeStarter(false).Start(kAdmin, kAdminName, mayManage: true);

            var member = _service.GetRecord().Members.Single();
            Assert.That(member.Email, Is.EqualTo(kAdmin));
            Assert.That(member.Role, Is.EqualTo(SharingRole.Admin));
            Assert.That(_historyReads, Is.EqualTo(0));
        }

        [Test]
        public void Start_NotAllowedToManage_Throws_AndStaysUnshared()
        {
            Assert.Throws<SharingNotAllowedException>(() =>
                MakeStarter(true).Start(kAdmin, kAdminName, mayManage: false)
            );
            Assert.That(_service.GetRecord(), Is.Null);
            Assert.That(RobustFile.Exists(SharingFilePath), Is.False);
        }

        [Test]
        public void Start_AlreadyShared_Throws_AndChangesNothing()
        {
            MakeStarter(true).Start(kAdmin, kAdminName, mayManage: true);
            _service.Remove(kAdmin, "amina@example.org");
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(2));

            Assert.Throws<SharingNotAllowedException>(() =>
                MakeStarter(true).Start(kAdmin, kAdminName, mayManage: true)
            );

            Assert.That(
                _service.GetRecord().Members.Count,
                Is.EqualTo(2),
                "someone the admin removed must not come back"
            );
        }
    }
}
