using Bloom.Api;
using Bloom.Properties;
using Bloom.TeamCollection;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using Moq;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for the "please sign in" invitation that AccountApi offers a Team Collection user who
    /// is not signed in to BloomLibrary.org (BL-16692). The front end asks for this once, as the
    /// collection tab mounts; see AccountApi.HandleSignInInvitationNeeded.
    ///
    /// These tests construct a real BloomLibraryBookApiClient, whose login state lives in
    /// Bloom.Properties.Settings.Default -- the developer's actual persisted configuration -- so we
    /// save the original values in Setup and restore them in TearDown.
    /// </summary>
    [TestFixture]
    public class AccountApiTests
    {
        private string _origWebUserId;
        private string _origLastLoginSessionToken;
        private string _origLastLoginUserId;
        private string _origLastLoginDest;

        [SetUp]
        public void Setup()
        {
            _origWebUserId = Settings.Default.WebUserId;
            _origLastLoginSessionToken = Settings.Default.LastLoginSessionToken;
            _origLastLoginUserId = Settings.Default.LastLoginUserId;
            _origLastLoginDest = Settings.Default.LastLoginDest;
        }

        [TearDown]
        public void TearDown()
        {
            Settings.Default.WebUserId = _origWebUserId;
            Settings.Default.LastLoginSessionToken = _origLastLoginSessionToken;
            Settings.Default.LastLoginUserId = _origLastLoginUserId;
            Settings.Default.LastLoginDest = _origLastLoginDest;
            Settings.Default.Save();
        }

        /// <summary>
        /// Makes an AccountApi whose collection is (or is not) a Team Collection and whose user is
        /// (or is not) signed in. The login data, when wanted, is set BEFORE we construct the API so
        /// that no LoginDataChanged broadcast goes to the websocket server, which is not listening.
        /// </summary>
        private AccountApi MakeApi(bool isTeamCollection, bool loggedIn)
        {
            var client = new BloomLibraryBookApiClient();
            if (loggedIn)
                client.SetLoginData(
                    "someone@example.com",
                    "someUserId",
                    "someSessionToken",
                    "production"
                );
            Assert.That(
                client.LoggedIn,
                Is.EqualTo(loggedIn),
                "test setup failed to set login state"
            );

            var tcManager = new Mock<ITeamCollectionManager>();
            if (isTeamCollection)
            {
                // The API only checks that there IS a current collection, so a mock (which is what
                // TeamCollection's parameterless constructor exists for) tells it all it needs.
                tcManager
                    .Setup(m => m.CurrentCollectionEvenIfDisconnected)
                    .Returns(new Mock<Bloom.TeamCollection.TeamCollection>().Object);
            }

            return new AccountApi(
                client,
                new BloomWebSocketServer(),
                new Bloom.web.AvatarCache(),
                tcManager.Object
            );
        }

        [Test]
        public void TakeSignInInvitation_TeamCollectionAndNotSignedIn_OffersItOnlyOnce()
        {
            var api = MakeApi(isTeamCollection: true, loggedIn: false);

            Assert.That(
                api.TakeSignInInvitation(),
                Is.True,
                "should invite the user the first time"
            );
            Assert.That(
                api.TakeSignInInvitation(),
                Is.False,
                "asking again in the same run should not invite the user again"
            );
        }

        [Test]
        public void TakeSignInInvitation_NotATeamCollection_DoesNotOfferIt()
        {
            var api = MakeApi(isTeamCollection: false, loggedIn: false);

            Assert.That(api.TakeSignInInvitation(), Is.False);
        }

        [Test]
        public void TakeSignInInvitation_AlreadySignedIn_DoesNotOfferIt()
        {
            var api = MakeApi(isTeamCollection: true, loggedIn: true);

            Assert.That(api.TakeSignInInvitation(), Is.False);
        }
    }
}
