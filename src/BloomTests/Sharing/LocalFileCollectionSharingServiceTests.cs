using System;
using System.IO;
using System.Linq;
using Bloom.Sharing;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Sharing
{
    [TestFixture]
    public class LocalFileCollectionSharingServiceTests
    {
        private const string kAdmin = "ruth@example.org";
        private TemporaryFolder _folder;
        private DateTime _now;
        private LocalFileCollectionSharingService _service;

        [SetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("LocalFileCollectionSharingServiceTests");
            _now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            _service = MakeService();
        }

        [TearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        // A fresh service on the same folder, so tests can check what was saved to the file
        // rather than what one instance remembers.
        private LocalFileCollectionSharingService MakeService()
        {
            return new LocalFileCollectionSharingService(
                _folder.Path,
                "collection-id",
                "Bantu Readers",
                () => _now
            );
        }

        // Invite one person, the way the dialog's invite row does.
        private void Invite(string byEmail, string email, SharingRole role)
        {
            _service.Invite(
                byEmail,
                new[]
                {
                    new SharingInvitation { Email = email, Role = role },
                }
            );
        }

        private void StartSharingAsRuth()
        {
            _service.StartSharing(
                kAdmin,
                "Ruth Nakalema",
                new SharingInvitation[0],
                new TeamCollectionHistoryMember[0]
            );
        }

        // Someone a Team Collection's history shows last working in it on the given day.
        private static TeamCollectionHistoryMember HistoryMember(
            string email,
            SharingRole role,
            int day
        )
        {
            return new TeamCollectionHistoryMember
            {
                Email = email,
                Name = email.Split('@')[0],
                Role = role,
                LastActivity = new DateTime(2026, 8, day, 0, 0, 0, DateTimeKind.Utc),
            };
        }

        [Test]
        public void GetRecord_NotShared_ReturnsNull()
        {
            Assert.That(_service.GetRecord(), Is.Null);
            Assert.That(
                RobustFile.Exists(
                    Path.Combine(_folder.Path, LocalFileCollectionSharingService.kFileName)
                ),
                Is.False
            );
        }

        [Test]
        public void StartSharing_MakesStarterTheOnlyAdmin_SeenNow()
        {
            StartSharingAsRuth();

            var record = MakeService().GetRecord();
            Assert.That(record, Is.Not.Null, "starting to share should have saved a record");
            Assert.That(record.CollectionId, Is.EqualTo("collection-id"));
            Assert.That(record.CollectionName, Is.EqualTo("Bantu Readers"));
            Assert.That(record.CreatedAt, Is.EqualTo(_now));
            var member = record.Members.Single();
            Assert.That(member.Email, Is.EqualTo(kAdmin));
            Assert.That(member.Name, Is.EqualTo("Ruth Nakalema"));
            Assert.That(member.Role, Is.EqualTo(SharingRole.Admin));
            Assert.That(member.InvitedAt, Is.EqualTo(_now));
            Assert.That(member.LastSeen, Is.EqualTo(_now));
        }

        [Test]
        public void StartSharing_AlreadyShared_Throws()
        {
            StartSharingAsRuth();
            Assert.Throws<SharingNotAllowedException>(() =>
                _service.StartSharing(
                    "someone@example.org",
                    "Someone",
                    new SharingInvitation[0],
                    new TeamCollectionHistoryMember[0]
                )
            );
        }

        [Test]
        public void StartSharing_WithInvitations_SavesThemTogether_NotYetSeen()
        {
            _service.StartSharing(
                kAdmin,
                "Ruth Nakalema",
                new[]
                {
                    new SharingInvitation
                    {
                        Email = "amina@example.org",
                        Role = SharingRole.Editor,
                    },
                },
                new TeamCollectionHistoryMember[0]
            );
            var members = MakeService().GetRecord().Members;
            Assert.That(
                members.Select(m => m.Email),
                Is.EqualTo(new[] { kAdmin, "amina@example.org" })
            );
            Assert.That(members[1].InvitedAt, Is.EqualTo(_now));
            Assert.That(members[1].LastSeen, Is.Null, "an invitation is not a visit");
        }

        [Test]
        public void StartSharing_WithHistoryMembers_AddsThemInTheirRoles_LastSeenAtTheirLastActivity()
        {
            _service.StartSharing(
                kAdmin,
                "Ruth Nakalema",
                new SharingInvitation[0],
                new[]
                {
                    HistoryMember("sam@example.org", SharingRole.Admin, 20),
                    HistoryMember("amina@example.org", SharingRole.Editor, 5),
                }
            );

            var members = MakeService().GetRecord().Members;
            Assert.That(
                members.Select(m => $"{m.Email} {m.Role}"),
                Is.EqualTo(
                    new[] { $"{kAdmin} Admin", "sam@example.org Admin", "amina@example.org Editor" }
                )
            );
            var amina = members[2];
            Assert.That(amina.Name, Is.EqualTo("amina"));
            Assert.That(amina.InvitedAt, Is.EqualTo(_now));
            Assert.That(amina.InvitedBy, Is.EqualTo(kAdmin));
            Assert.That(
                amina.LastSeen,
                Is.EqualTo(new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc))
            );
        }

        [Test]
        public void StartSharing_AdminInHistory_IsNotAddedTwice()
        {
            _service.StartSharing(
                kAdmin,
                "Ruth Nakalema",
                new SharingInvitation[0],
                new[]
                {
                    HistoryMember(" RUTH@example.org", SharingRole.Editor, 3),
                    HistoryMember("amina@example.org", SharingRole.Editor, 5),
                }
            );

            var members = MakeService().GetRecord().Members;
            Assert.That(
                members.Select(m => m.Email),
                Is.EqualTo(new[] { kAdmin, "amina@example.org" })
            );
            Assert.That(members[0].Role, Is.EqualTo(SharingRole.Admin));
            Assert.That(members[0].LastSeen, Is.EqualTo(_now), "the admin is here now");
        }

        [Test]
        public void StartSharing_BadInvitation_LeavesCollectionUnshared()
        {
            // Inviting yourself is bad: you already have access as the new admin.
            Assert.Throws<SharingNotAllowedException>(() =>
                _service.StartSharing(
                    kAdmin,
                    "Ruth Nakalema",
                    new[]
                    {
                        new SharingInvitation { Email = kAdmin, Role = SharingRole.Editor },
                    },
                    new[] { HistoryMember("amina@example.org", SharingRole.Editor, 5) }
                )
            );
            Assert.That(MakeService().GetRecord(), Is.Null);
        }

        [Test]
        public void File_UsesLowercaseRoleNames()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);

            var json = RobustFile.ReadAllText(
                Path.Combine(_folder.Path, LocalFileCollectionSharingService.kFileName)
            );
            Assert.That(json, Does.Contain("\"role\": \"admin\""));
            Assert.That(json, Does.Contain("\"role\": \"editor\""));
        }

        [Test]
        public void Invite_AddsInvitedMember()
        {
            StartSharingAsRuth();
            _now = _now.AddDays(1);

            Invite(kAdmin, " amina@example.org ", SharingRole.Editor);

            var members = MakeService().GetRecord().Members;
            Assert.That(members.Count, Is.EqualTo(2));
            var amina = members[1];
            Assert.That(amina.Email, Is.EqualTo("amina@example.org"), "should be trimmed");
            Assert.That(amina.Role, Is.EqualTo(SharingRole.Editor));
            Assert.That(amina.InvitedAt, Is.EqualTo(_now));
            Assert.That(amina.InvitedBy, Is.EqualTo(kAdmin));
            Assert.That(amina.LastSeen, Is.Null);
        }

        [Test]
        public void Invite_SomeoneWithAccess_Throws()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);
            Assert.Throws<SharingNotAllowedException>(() =>
                Invite(kAdmin, "AMINA@example.org", SharingRole.Admin)
            );
        }

        [Test]
        public void Invite_ByNonAdmin_Throws()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);
            Assert.Throws<SharingNotAllowedException>(() =>
                Invite("amina@example.org", "sam@example.org", SharingRole.Editor)
            );
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(2));
        }

        [Test]
        public void Invite_NotShared_Throws()
        {
            Assert.Throws<SharingNotAllowedException>(() =>
                Invite(kAdmin, "amina@example.org", SharingRole.Editor)
            );
        }

        [Test]
        public void Invite_Several_AddsThemAll()
        {
            StartSharingAsRuth();
            _service.Invite(
                kAdmin,
                new[]
                {
                    new SharingInvitation { Email = "sam@example.org", Role = SharingRole.Admin },
                    new SharingInvitation
                    {
                        Email = "amina@example.org",
                        Role = SharingRole.Editor,
                    },
                }
            );
            Assert.That(
                MakeService().GetRecord().Members.Select(m => $"{m.Email} {m.Role}"),
                Is.EqualTo(
                    new[] { $"{kAdmin} Admin", "sam@example.org Admin", "amina@example.org Editor" }
                )
            );
        }

        [Test]
        public void Invite_OneBadInBatch_ChangesNothing()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(2));

            // Sam is fine, but Amina already has access, so neither should be added.
            Assert.Throws<SharingNotAllowedException>(() =>
                _service.Invite(
                    kAdmin,
                    new[]
                    {
                        new SharingInvitation
                        {
                            Email = "sam@example.org",
                            Role = SharingRole.Editor,
                        },
                        new SharingInvitation
                        {
                            Email = "amina@example.org",
                            Role = SharingRole.Editor,
                        },
                    }
                )
            );

            Assert.That(MakeService().GetRecord().Members.Count, Is.EqualTo(2));
        }

        [Test]
        public void Invite_SamePersonTwiceInBatch_Throws()
        {
            StartSharingAsRuth();
            Assert.Throws<SharingNotAllowedException>(() =>
                _service.Invite(
                    kAdmin,
                    new[]
                    {
                        new SharingInvitation
                        {
                            Email = "sam@example.org",
                            Role = SharingRole.Editor,
                        },
                        new SharingInvitation
                        {
                            Email = "SAM@example.org",
                            Role = SharingRole.Admin,
                        },
                    }
                )
            );
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(1));
        }

        [Test]
        public void SetRole_ChangesRole()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);

            _service.SetRole(kAdmin, "amina@example.org", SharingRole.Admin);

            Assert.That(MakeService().GetRecord().Members[1].Role, Is.EqualTo(SharingRole.Admin));
        }

        [Test]
        public void SetRole_OwnRole_Throws_EvenWhenThereIsAnotherAdmin()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "sam@example.org", SharingRole.Admin);
            Assert.Throws<SharingNotAllowedException>(() =>
                _service.SetRole(kAdmin, " RUTH@example.org", SharingRole.Editor)
            );
            Assert.That(_service.GetRecord().Members[0].Role, Is.EqualTo(SharingRole.Admin));
        }

        [Test]
        public void SetRole_AnotherAdminMayChangeYourRole()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "sam@example.org", SharingRole.Admin);

            _service.SetRole("sam@example.org", kAdmin, SharingRole.Editor);

            Assert.That(MakeService().GetRecord().Members[0].Role, Is.EqualTo(SharingRole.Editor));
        }

        [Test]
        public void Remove_RemovesMember()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(2));

            _service.Remove(kAdmin, "amina@example.org");

            Assert.That(
                MakeService().GetRecord().Members.Select(m => m.Email),
                Is.EqualTo(new[] { kAdmin })
            );
        }

        [Test]
        public void Remove_Yourself_Throws_EvenWhenThereIsAnotherAdmin()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "sam@example.org", SharingRole.Admin);
            Assert.Throws<SharingNotAllowedException>(() => _service.Remove(kAdmin, kAdmin));
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(2));
        }

        [Test]
        public void Remove_NonMember_Throws()
        {
            StartSharingAsRuth();
            Assert.Throws<SharingNotAllowedException>(() =>
                _service.Remove(kAdmin, "nobody@example.org")
            );
        }

        [Test]
        public void RecordVisit_InvitedMember_GetsLastSeenAndName()
        {
            StartSharingAsRuth();
            Invite(kAdmin, "amina@example.org", SharingRole.Editor);
            var invitedAt = _now;
            Assert.That(_service.GetRecord().Members[1].LastSeen, Is.Null);
            _now = _now.AddDays(3);

            _service.RecordVisit("Amina@example.org", "Amina Yusuf");

            var amina = MakeService().GetRecord().Members[1];
            Assert.That(amina.LastSeen, Is.EqualTo(_now));
            Assert.That(amina.InvitedAt, Is.EqualTo(invitedAt), "a visit is not an invitation");
            Assert.That(amina.Name, Is.EqualTo("Amina Yusuf"));
        }

        [Test]
        public void RecordVisit_HistoryMember_ReplacesHistoryTimeWithNow()
        {
            _service.StartSharing(
                kAdmin,
                "Ruth Nakalema",
                new SharingInvitation[0],
                new[] { HistoryMember("amina@example.org", SharingRole.Editor, 5) }
            );
            Assert.That(_service.GetRecord().Members[1].LastSeen, Is.Not.EqualTo(_now));

            _service.RecordVisit("amina@example.org", "Amina Yusuf");

            Assert.That(MakeService().GetRecord().Members[1].LastSeen, Is.EqualTo(_now));
        }

        [Test]
        public void RecordVisit_BlankName_KeepsExistingName()
        {
            StartSharingAsRuth();
            _service.RecordVisit(kAdmin, " ");
            Assert.That(_service.GetRecord().Members[0].Name, Is.EqualTo("Ruth Nakalema"));
        }

        [Test]
        public void RecordVisit_NonMemberOrNotShared_DoesNothing()
        {
            _service.RecordVisit(kAdmin, "Ruth");
            Assert.That(_service.GetRecord(), Is.Null, "a visit must not share the collection");

            StartSharingAsRuth();
            _service.RecordVisit("stranger@example.org", "Stranger");
            Assert.That(_service.GetRecord().Members.Count, Is.EqualTo(1));
        }
    }
}
