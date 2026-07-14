using System.Reflection;
using Bloom.Api;
using Bloom.Collection;
using Bloom.TeamCollection;
using BloomTemp;
using Moq;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// Tests for TeamCollectionManager.CheckConnection(bool allowHardRefusal) (batch item 9,
    /// account-switch behavior): a connection problem flagged IsAccessRefusal must throw
    /// TeamCollectionAccessRefusedException when allowHardRefusal is true (the collection-open
    /// path), but must fall back to the ordinary Disconnected mode -- exactly as before -- for
    /// every other caller (allowHardRefusal false/default), so a membership loss discovered mid-
    /// session never crashes the running app.
    ///
    /// CurrentCollection has a private setter (by design -- nothing outside TeamCollectionManager
    /// itself should replace it), so these tests use reflection to install a scripted fake
    /// TeamCollection, the same way TeamCollection's own "empty constructor... only for mocking
    /// purposes" comment anticipates.
    /// </summary>
    [TestFixture]
    public class TeamCollectionAccountSwitchRefusalTests
    {
        private TemporaryFolder _localCollection;
        private TeamCollectionManager _tcManager;

        [SetUp]
        public void Setup()
        {
            _localCollection = new TemporaryFolder("TeamCollectionAccountSwitchRefusalTests");
            var collectionPath = CollectionSettings.GetSettingsFilePath(
                _localCollection.FolderPath
            );
            // No TeamCollectionLink.txt in this folder, so the constructor's own TC-loading
            // logic is a no-op; CurrentCollection starts null and we install a fake via
            // reflection below.
            _tcManager = new TeamCollectionManager(
                collectionPath,
                new BloomWebSocketServer(),
                null,
                null,
                null,
                null
            );
        }

        [TearDown]
        public void TearDown()
        {
            _localCollection.Dispose();
        }

        private void InstallFakeCollection(Bloom.TeamCollection.TeamCollection fake)
        {
            typeof(TeamCollectionManager)
                .GetProperty(
                    nameof(TeamCollectionManager.CurrentCollection),
                    BindingFlags.Public | BindingFlags.Instance
                )
                .SetValue(_tcManager, fake);
        }

        [Test]
        public void CheckConnection_AllowHardRefusalTrue_AccessRefusalMessage_Throws()
        {
            var fake = new Mock<Bloom.TeamCollection.TeamCollection>();
            fake.Setup(c => c.CheckConnection())
                .Returns(
                    new TeamCollectionMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.Cloud.NotAMemberRefusal",
                        "Bloom cannot open this Team Collection here because {0} is not a member of it. {1}",
                        "bob@dev.local",
                        "Ask an administrator to add you as a member."
                    )
                    {
                        IsAccessRefusal = true,
                    }
                );
            InstallFakeCollection(fake.Object);

            Assert.That(
                () => _tcManager.CheckConnection(allowHardRefusal: true),
                Throws
                    .TypeOf<TeamCollectionAccessRefusedException>()
                    .With.Message.Contains("bob@dev.local")
            );
        }

        [Test]
        public void CheckConnection_AllowHardRefusalFalse_AccessRefusalMessage_FallsBackToDisconnected()
        {
            // Same scripted refusal message, but the DEFAULT (allowHardRefusal: false) caller --
            // used everywhere except the initial collection-open constructor call -- must NOT
            // throw, so a membership loss discovered mid-session just disconnects as before.
            var fake = new Mock<Bloom.TeamCollection.TeamCollection>();
            fake.Setup(c => c.CheckConnection())
                .Returns(
                    new TeamCollectionMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.Cloud.NotAMemberRefusal",
                        "Bloom cannot open this Team Collection here because {0} is not a member of it. {1}",
                        "bob@dev.local",
                        "Ask an administrator to add you as a member."
                    )
                    {
                        IsAccessRefusal = true,
                    }
                );
            InstallFakeCollection(fake.Object);

            bool result = false;
            Assert.That(() => result = _tcManager.CheckConnection(), Throws.Nothing);
            Assert.That(result, Is.False, "the connection check should report failure...");
            Assert.That(
                _tcManager.CurrentCollection,
                Is.Null,
                "...and fall back to Disconnected mode (CurrentCollection cleared), not crash"
            );
        }

        [Test]
        public void CheckConnection_AllowHardRefusalTrue_OrdinaryProblem_FallsBackToDisconnected()
        {
            // A connection problem that is NOT flagged IsAccessRefusal (e.g. NoConnection) must
            // still just disconnect even when allowHardRefusal is true -- only a genuine
            // access-refusal aborts the open.
            var fake = new Mock<Bloom.TeamCollection.TeamCollection>();
            fake.Setup(c => c.CheckConnection())
                .Returns(
                    new TeamCollectionMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.Cloud.NoConnection",
                        "Bloom could not reach the Team Collection server."
                    )
                );
            InstallFakeCollection(fake.Object);

            Assert.That(() => _tcManager.CheckConnection(allowHardRefusal: true), Throws.Nothing);
            Assert.That(_tcManager.CurrentCollection, Is.Null);
        }
    }
}
