using Bloom.Api;
using Bloom.TeamCollection;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using Moq;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for the "please sign in" invitation that AccountApi offers a Team Collection user who
    /// is not signed in to BloomLibrary.org (BL-16692). The front end asks for this as the
    /// collection tab mounts and reports back once it has shown the dialog; see
    /// AccountApi.HandleSignInInvitationNeeded.
    /// </summary>
    [TestFixture]
    public class AccountApiTests
    {
        /// <summary>
        /// A client that reports being signed in without touching the real login settings (which
        /// live in the developer's persisted configuration). The session token is all
        /// BloomLibraryBookApiClient.LoggedIn looks at.
        /// </summary>
        private class SignedInClient : BloomLibraryBookApiClient
        {
            public SignedInClient()
            {
                _authenticationToken = "someSessionToken";
            }
        }

        /// <summary>
        /// Makes an AccountApi whose collection is (or is not) a Team Collection and whose user is
        /// (or is not) signed in.
        /// </summary>
        private AccountApi MakeApi(bool isTeamCollection, bool loggedIn)
        {
            var client = loggedIn ? new SignedInClient() : new BloomLibraryBookApiClient();
            Assert.That(
                client.LoggedIn,
                Is.EqualTo(loggedIn),
                "test setup failed to set the login state"
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
        public void SignInInvitationNeeded_TeamCollectionAndNotSignedIn_IsTrueUntilTheDialogIsShown()
        {
            var api = MakeApi(isTeamCollection: true, loggedIn: false);

            Assert.That(api.SignInInvitationNeeded, Is.True, "should invite the user");
            Assert.That(
                api.SignInInvitationNeeded,
                Is.True,
                "merely asking must not use up the invitation: the page could reload before the "
                    + "dialog appears"
            );

            api.NoteSignInInvitationShown();

            Assert.That(
                api.SignInInvitationNeeded,
                Is.False,
                "once the user has seen it, this run is done inviting"
            );
        }

        [Test]
        public void SignInInvitationNeeded_NotATeamCollection_IsFalse()
        {
            var api = MakeApi(isTeamCollection: false, loggedIn: false);

            Assert.That(api.SignInInvitationNeeded, Is.False);
        }

        [Test]
        public void SignInInvitationNeeded_AlreadySignedIn_IsFalse()
        {
            var api = MakeApi(isTeamCollection: true, loggedIn: true);

            Assert.That(api.SignInInvitationNeeded, Is.False);
        }
    }
}
