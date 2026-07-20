using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
    /// <summary>
    /// Tests for the app-wide login state added to BloomLibraryBookApiClient: SetLoginData,
    /// TryRestoreSavedLogin, and Logout, along with the LoginDataChanged event that lets
    /// other parts of the app (e.g. AccountApi) react to login-state changes.
    ///
    /// These tests read and write Bloom.Properties.Settings.Default, which is the developer's
    /// actual persisted configuration, so we carefully save the original values in Setup and
    /// restore them in TearDown.
    /// </summary>
    [TestFixture]
    public class BloomLibraryBookApiClientLoginTests
    {
        private string _origWebUserId;
        private string _origLastLoginSessionToken;
        private string _origLastLoginUserId;
        private string _origLastLoginDest;

        private BloomLibraryBookApiClient _client;

        [SetUp]
        public void Setup()
        {
            _origWebUserId = Settings.Default.WebUserId;
            _origLastLoginSessionToken = Settings.Default.LastLoginSessionToken;
            _origLastLoginUserId = Settings.Default.LastLoginUserId;
            _origLastLoginDest = Settings.Default.LastLoginDest;

            _client = new BloomLibraryBookApiClient();
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

        [Test]
        public void TryRestoreSavedLogin_NoSavedToken_ReturnsFalseAndStaysLoggedOut()
        {
            // Sanity check / arrange: no saved session token.
            Settings.Default.LastLoginSessionToken = "";
            Settings.Default.LastLoginUserId = "someUserId";
            Settings.Default.LastLoginDest = "production";
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo(string.Empty));

            var result = _client.TryRestoreSavedLogin("production");

            Assert.That(result, Is.False);
            Assert.That(_client.LoggedIn, Is.False);
        }

        [Test]
        public void TryRestoreSavedLogin_DestinationDoesNotMatch_ReturnsFalseAndDoesNotClearSettings()
        {
            // Arrange: a fully saved login, but for a different destination than we'll request.
            Settings.Default.WebUserId = "someone@example.com";
            Settings.Default.LastLoginSessionToken = "savedToken";
            Settings.Default.LastLoginUserId = "savedUserId";
            Settings.Default.LastLoginDest = "sandbox";
            Settings.Default.Save();

            // Sanity check the arrange step actually took effect.
            Assert.That(Settings.Default.LastLoginDest, Is.EqualTo("sandbox"));

            var result = _client.TryRestoreSavedLogin("production");

            Assert.That(result, Is.False);
            Assert.That(_client.LoggedIn, Is.False);

            // A destination mismatch must NOT be treated as an invalid saved login; the settings
            // should be left untouched so a later call with the matching destination can still work.
            Assert.That(Settings.Default.WebUserId, Is.EqualTo("someone@example.com"));
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo("savedToken"));
            Assert.That(Settings.Default.LastLoginUserId, Is.EqualTo("savedUserId"));
            Assert.That(Settings.Default.LastLoginDest, Is.EqualTo("sandbox"));
        }

        [Test]
        public void TryRestoreSavedLogin_MatchingDestinationAndSavedToken_ReturnsTrueAndLogsIn()
        {
            // Arrange: a fully saved login matching the destination we'll request.
            Settings.Default.WebUserId = "someone@example.com";
            Settings.Default.LastLoginSessionToken = "savedToken";
            Settings.Default.LastLoginUserId = "savedUserId";
            Settings.Default.LastLoginDest = "production";
            Settings.Default.Save();

            // Sanity check the arrange step actually took effect, and that we're starting logged out.
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo("savedToken"));
            Assert.That(_client.LoggedIn, Is.False);

            var result = _client.TryRestoreSavedLogin("production");

            Assert.That(result, Is.True);
            Assert.That(_client.LoggedIn, Is.True);
            Assert.That(_client.Account, Is.EqualTo("someone@example.com"));
            Assert.That(_client.UserId, Is.EqualTo("savedUserId"));
        }

        [Test]
        public void Logout_ClearsAllSavedLoginSettingsAndLogsOut()
        {
            // Arrange: log in first, so there's something to log out of.
            _client.SetLoginData("someone@example.com", "savedUserId", "savedToken", "production");

            // Sanity check the arrange step actually logged us in and saved settings.
            Assert.That(_client.LoggedIn, Is.True);
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo("savedToken"));

            // Pass includeFirebaseLogout: false so this doesn't try to open a browser window.
            _client.Logout(includeFirebaseLogout: false);

            Assert.That(_client.LoggedIn, Is.False);
            Assert.That(Settings.Default.WebUserId, Is.EqualTo(string.Empty));
            Assert.That(Settings.Default.LastLoginSessionToken, Is.EqualTo(string.Empty));
            Assert.That(Settings.Default.LastLoginUserId, Is.EqualTo(string.Empty));
            Assert.That(Settings.Default.LastLoginDest, Is.EqualTo(string.Empty));
        }

        [Test]
        public void LoginDataChanged_FiresOnSetLoginDataAndOnLogout()
        {
            var invocationCount = 0;
            _client.LoginDataChanged += (sender, args) => invocationCount++;

            // Sanity check we're starting from zero invocations.
            Assert.That(invocationCount, Is.EqualTo(0));

            _client.SetLoginData("someone@example.com", "savedUserId", "savedToken", "production");
            Assert.That(invocationCount, Is.EqualTo(1));

            // Pass includeFirebaseLogout: false so this doesn't try to open a browser window.
            _client.Logout(includeFirebaseLogout: false);
            Assert.That(invocationCount, Is.EqualTo(2));
        }
    }
}
