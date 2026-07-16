using System;
using System.Collections.Generic;
using System.IO;
using Bloom.TeamCollection.Cloud;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// A scriptable <see cref="ICloudAuthProvider"/> for exercising <see cref="CloudAuth"/>'s
    /// session core without any network access. Each call records what it was asked to do so
    /// tests can assert on call counts, and either returns the next queued session or throws the
    /// next queued exception.
    /// </summary>
    internal class FakeCloudAuthProvider : ICloudAuthProvider
    {
        public int SignInCallCount;
        public int RefreshCallCount;
        public int AcceptExternalSessionCallCount;
        public List<string> RefreshTokensSeen = new List<string>();

        // When set, SignIn returns this session (ignoring the email/password given) unless
        // NextSignInException is also set, in which case the exception wins.
        public Func<string, string, CloudSession> SignInHandler;
        public Func<string, CloudSession> RefreshHandler;
        public Func<string, string, CloudSession> AcceptExternalSessionHandler;

        public CloudSession SignIn(string email, string password)
        {
            SignInCallCount++;
            return SignInHandler(email, password);
        }

        public CloudSession Refresh(string refreshToken)
        {
            RefreshCallCount++;
            RefreshTokensSeen.Add(refreshToken);
            return RefreshHandler(refreshToken);
        }

        public CloudSession AcceptExternalSession(string idToken, string refreshToken)
        {
            AcceptExternalSessionCallCount++;
            return AcceptExternalSessionHandler(idToken, refreshToken);
        }
    }

    /// <summary>Test-only token store that lets a test seed a "previously stored" session.</summary>
    internal class FakeCloudTokenStore : ICloudTokenStore
    {
        public CloudSession Stored;
        public int ClearCallCount;

        public CloudSession Load() => Stored;

        public void Save(CloudSession session) => Stored = session;

        public void Clear()
        {
            ClearCallCount++;
            Stored = null;
        }
    }

    [TestFixture]
    public class CloudAuthTests
    {
        private static CloudSession MakeSession(
            string email,
            string userId = "user-1",
            string accessToken = "access-1",
            string refreshToken = "refresh-1",
            double ttlSeconds = 3600
        ) =>
            new CloudSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                Email = email,
                UserId = userId,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(ttlSeconds),
            };

        [Test]
        public void SignIn_Success_SetsIdentityAndAccessToken()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) => MakeSession(email),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());

            // Sanity check: nothing signed in yet.
            Assert.That(
                auth.IsSignedIn,
                Is.False,
                "should not be signed in before SignIn is called"
            );

            auth.SignIn("alice@dev.local", "BloomDev123!");

            Assert.That(auth.IsSignedIn, Is.True);
            Assert.That(auth.CurrentEmail, Is.EqualTo("alice@dev.local"));
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("access-1"));
            Assert.That(provider.SignInCallCount, Is.EqualTo(1));
        }

        [Test]
        public void SignIn_Failure_LeavesAuthSignedOut()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) =>
                    throw new CloudAuthException("bad credentials"),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());

            Assert.Throws<CloudAuthException>(() => auth.SignIn("bob@dev.local", "wrong"));

            Assert.That(auth.IsSignedIn, Is.False);
            Assert.That(auth.GetAccessTokenOrNull(), Is.Null);
        }

        [Test]
        public void TryRefreshOn401_WithValidRefreshToken_ReplacesSessionAndReturnsTrue()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) =>
                    MakeSession(email, accessToken: "access-1", refreshToken: "refresh-1"),
                RefreshHandler = refreshToken =>
                    MakeSession(
                        "alice@dev.local",
                        accessToken: "access-2",
                        refreshToken: "refresh-2"
                    ),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());
            auth.SignIn("alice@dev.local", "BloomDev123!");

            // Sanity check on the pre-refresh state before we exercise the 401 path.
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("access-1"));

            var refreshed = auth.TryRefreshOn401();

            Assert.That(refreshed, Is.True);
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("access-2"));
            Assert.That(provider.RefreshCallCount, Is.EqualTo(1));
            Assert.That(provider.RefreshTokensSeen, Is.EqualTo(new[] { "refresh-1" }));
        }

        [Test]
        public void TryRefreshOn401_WhenRefreshFails_SignsOutAndReturnsFalse()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) => MakeSession(email),
                RefreshHandler = refreshToken =>
                    throw new CloudAuthException("refresh token expired"),
            };
            var tokenStore = new FakeCloudTokenStore();
            var auth = new CloudAuth(provider, tokenStore);
            auth.SignIn("alice@dev.local", "BloomDev123!");
            Assert.That(
                auth.IsSignedIn,
                Is.True,
                "must be signed in before we can test refresh failure"
            );

            var refreshed = auth.TryRefreshOn401();

            // This is the "refresh failure mid-operation aborts cleanly and surfaces 'please
            // sign in'" acceptance criterion: the caller (e.g. CloudCollectionClient) sees a
            // clean false rather than an exception, and the session is fully cleared so the
            // next access-token read is null (the caller's cue to show "please sign in").
            Assert.That(refreshed, Is.False);
            Assert.That(auth.IsSignedIn, Is.False);
            Assert.That(auth.GetAccessTokenOrNull(), Is.Null);
            Assert.That(tokenStore.ClearCallCount, Is.EqualTo(1));
        }

        [Test]
        public void TryRefreshOn401_WhenNeverSignedIn_ReturnsFalseWithoutCallingProvider()
        {
            var provider = new FakeCloudAuthProvider();
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());

            var refreshed = auth.TryRefreshOn401();

            Assert.That(refreshed, Is.False);
            Assert.That(provider.RefreshCallCount, Is.EqualTo(0));
        }

        [Test]
        public void SignIn_WithDifferentAccount_ReplacesIdentity()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) => MakeSession(email),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());
            auth.SignIn("alice@dev.local", "BloomDev123!");
            // Sanity check the starting identity before switching.
            Assert.That(auth.CurrentEmail, Is.EqualTo("alice@dev.local"));

            auth.SignIn("bob@dev.local", "BloomDev123!");

            Assert.That(auth.CurrentEmail, Is.EqualTo("bob@dev.local"));
        }

        [Test]
        public void InitializeAtStartup_WithEnvOverride_SignsInAsOverrideUserIgnoringStoredToken()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) => MakeSession(email),
                RefreshHandler = refreshToken =>
                    throw new InvalidOperationException(
                        "the stored token must not be used when an env override is present"
                    ),
            };
            var tokenStore = new FakeCloudTokenStore
            {
                // A stored session for a completely different user; the env override must win
                // and this must never be consulted (Refresh throws above if it is).
                Stored = MakeSession("stored-user@dev.local", refreshToken: "stored-refresh"),
            };
            var auth = new CloudAuth(provider, tokenStore);
            var env = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_USER" ? "override@dev.local"
                : name == "BLOOM_CLOUDTC_PASSWORD" ? "pw"
                : null
            );

            auth.InitializeAtStartup(env);

            Assert.That(auth.IsSignedIn, Is.True);
            Assert.That(auth.CurrentEmail, Is.EqualTo("override@dev.local"));
            Assert.That(provider.RefreshCallCount, Is.EqualTo(0));
        }

        [Test]
        public void InitializeAtStartup_WithoutEnvOverride_RestoresFromStoredRefreshToken()
        {
            var provider = new FakeCloudAuthProvider
            {
                RefreshHandler = refreshToken => MakeSession("stored-user@dev.local"),
            };
            var tokenStore = new FakeCloudTokenStore
            {
                Stored = MakeSession("stored-user@dev.local", refreshToken: "stored-refresh"),
            };
            var auth = new CloudAuth(provider, tokenStore);
            var env = new CloudEnvironment(name => null); // no overrides configured

            auth.InitializeAtStartup(env);

            Assert.That(auth.IsSignedIn, Is.True);
            Assert.That(auth.CurrentEmail, Is.EqualTo("stored-user@dev.local"));
            Assert.That(provider.RefreshTokensSeen, Is.EqualTo(new[] { "stored-refresh" }));
        }

        [Test]
        public void InitializeAtStartup_NoOverrideNoStoredSession_LeavesSignedOutWithoutThrowing()
        {
            var provider = new FakeCloudAuthProvider();
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());
            var env = new CloudEnvironment(name => null);

            Assert.DoesNotThrow(() => auth.InitializeAtStartup(env));
            Assert.That(auth.IsSignedIn, Is.False);
        }

        [Test]
        public void RealDpapiTokenStore_SessionSurvivesSimulatedRestart()
        {
            // End-to-end proof (with the REAL DpapiCloudTokenStore, not the fake) that a Cloud TC
            // login survives a Bloom restart: a session persisted by one CloudAuth is restored by a
            // fresh CloudAuth reading the same on-disk store — exactly what CreateInitialized wires
            // up in the app. The dev/fake provider stands in for Firebase, whose refresh follows the
            // identical Load-then-Refresh path.
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                "BloomCloudAuthRestartTest-" + Guid.NewGuid().ToString("N") + ".dat"
            );
            try
            {
                // First run: signing in persists the session (with its refresh token) to disk.
                var firstRun = new CloudAuth(
                    new FakeCloudAuthProvider
                    {
                        SignInHandler = (email, password) =>
                            MakeSession(email, refreshToken: "refresh-A"),
                    },
                    new DpapiCloudTokenStore(tempFile)
                );
                firstRun.SignIn("alice@dev.local", "BloomDev123!");
                Assert.That(
                    firstRun.IsSignedIn,
                    Is.True,
                    "sanity: the first run must be signed in before we simulate a restart"
                );

                // Second run (simulated restart): a brand-new CloudAuth over the SAME store file
                // must restore the session via InitializeAtStartup's Load + refresh.
                var secondRunProvider = new FakeCloudAuthProvider
                {
                    RefreshHandler = refreshToken =>
                        MakeSession("alice@dev.local", refreshToken: refreshToken),
                };
                var secondRun = new CloudAuth(
                    secondRunProvider,
                    new DpapiCloudTokenStore(tempFile)
                );
                Assert.That(
                    secondRun.IsSignedIn,
                    Is.False,
                    "sanity: a fresh CloudAuth is signed out until it restores from the store"
                );

                secondRun.InitializeAtStartup(new CloudEnvironment(name => null));

                Assert.That(
                    secondRun.IsSignedIn,
                    Is.True,
                    "the session must survive the restart via the refresh token saved to disk"
                );
                Assert.That(secondRun.CurrentEmail, Is.EqualTo("alice@dev.local"));
                Assert.That(
                    secondRunProvider.RefreshTokensSeen,
                    Does.Contain("refresh-A"),
                    "the restart must refresh using the refresh token the first run wrote to disk"
                );
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Test]
        public void SignOut_ClearsSessionAndTokenStore()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) => MakeSession(email),
            };
            var tokenStore = new FakeCloudTokenStore();
            var auth = new CloudAuth(provider, tokenStore);
            auth.SignIn("alice@dev.local", "BloomDev123!");
            Assert.That(auth.IsSignedIn, Is.True, "must be signed in before testing sign-out");

            auth.SignOut();

            Assert.That(auth.IsSignedIn, Is.False);
            Assert.That(auth.CurrentEmail, Is.Null);
            Assert.That(tokenStore.ClearCallCount, Is.EqualTo(1));
        }

        [Test]
        public void ProactiveRefreshTimer_FiresBeforeExpiry_ReplacesSessionAutomatically()
        {
            var provider = new FakeCloudAuthProvider
            {
                // A very short TTL so the ~80%-of-TTL proactive-refresh timer fires almost
                // immediately, keeping this test fast without needing to fake the clock.
                SignInHandler = (email, password) =>
                    MakeSession(
                        email,
                        accessToken: "access-1",
                        refreshToken: "refresh-1",
                        ttlSeconds: 0.2
                    ),
                RefreshHandler = refreshToken =>
                    MakeSession(
                        "alice@dev.local",
                        accessToken: "access-2",
                        refreshToken: "refresh-2",
                        ttlSeconds: 3600
                    ),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());
            auth.SignIn("alice@dev.local", "BloomDev123!");
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("access-1"));

            // 80% of 200ms is 160ms; give it generous headroom without making the suite slow.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (provider.RefreshCallCount == 0 && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(25);

            Assert.That(
                provider.RefreshCallCount,
                Is.GreaterThanOrEqualTo(1),
                "the proactive-refresh timer should have fired on its own"
            );
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("access-2"));
        }

        /// <summary>
        /// Live-verifies <see cref="DevCloudAuthProvider"/> against a real local Supabase dev
        /// stack (see server/dev/README.md) — the only thing the mocked tests above cannot
        /// cover, since they substitute <see cref="ICloudAuthProvider"/> entirely. [Explicit]
        /// because it requires `supabase start` to be running; run manually with
        /// `dotnet test --filter FullyQualifiedName~LiveDevProvider`. Exercises exactly the
        /// "two Bloom instances on one machine run as two different users" acceptance
        /// criterion's precondition: two independent CloudAuth instances signing in as two
        /// different dev users concurrently must each hold their own distinct, valid session.
        /// </summary>
        [Test]
        [Explicit("Requires the local Supabase dev stack (`supabase start`) to be running.")]
        public void LiveDevProvider_TwoUsersSignInConcurrently_HoldDistinctSessions()
        {
            var env = CloudEnvironment.FromEnvironment();
            var aliceAuth = new CloudAuth(new DevCloudAuthProvider(env));
            var bobAuth = new CloudAuth(new DevCloudAuthProvider(env));

            aliceAuth.SignIn("alice@dev.local", "BloomDev123!");
            bobAuth.SignIn("bob@dev.local", "BloomDev123!");

            Assert.That(aliceAuth.CurrentEmail, Is.EqualTo("alice@dev.local"));
            Assert.That(bobAuth.CurrentEmail, Is.EqualTo("bob@dev.local"));
            Assert.That(aliceAuth.CurrentUserId, Is.Not.EqualTo(bobAuth.CurrentUserId));
            Assert.That(
                aliceAuth.GetAccessTokenOrNull(),
                Is.Not.EqualTo(bobAuth.GetAccessTokenOrNull())
            );

            // Both sessions must independently survive a refresh (the mechanism the >2h soak
            // test in the task's acceptance criteria relies on), without interfering with each
            // other's identity.
            Assert.That(aliceAuth.TryRefreshOn401(), Is.True);
            Assert.That(bobAuth.TryRefreshOn401(), Is.True);
            Assert.That(aliceAuth.CurrentEmail, Is.EqualTo("alice@dev.local"));
            Assert.That(bobAuth.CurrentEmail, Is.EqualTo("bob@dev.local"));
        }

        [Test]
        public void GetLoginState_ReportsAuthModeAndCurrentIdentity()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) => MakeSession(email),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());
            var env = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_AUTH_MODE" ? "dev" : null
            );

            var loggedOutState = auth.GetLoginState(env);
            Assert.That(loggedOutState.AuthMode, Is.EqualTo("dev"));
            Assert.That(loggedOutState.SignedIn, Is.False);
            Assert.That(loggedOutState.Email, Is.Null);

            auth.SignIn("alice@dev.local", "BloomDev123!");
            var signedInState = auth.GetLoginState(env);

            Assert.That(signedInState.SignedIn, Is.True);
            Assert.That(signedInState.Email, Is.EqualTo("alice@dev.local"));
        }

        [Test]
        public void GetLoginState_CloudMode_ReportsCloudNotReal()
        {
            // Regression guard for the "real" vs "cloud" naming mismatch found while
            // implementing task 12: BloomBrowserUI's sharingApi.ts SharingLoginMode type only
            // ever declared "dev" | "cloud", so the C# side must actually send "cloud".
            var auth = new CloudAuth(new FakeCloudAuthProvider(), new FakeCloudTokenStore());
            var env = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_AUTH_MODE" ? "cloud" : null
            );

            Assert.That(auth.GetLoginState(env).AuthMode, Is.EqualTo("cloud"));
        }

        [Test]
        public void GetLoginState_EmailVerifiedReflectsSessionAndClearsOnSignOut()
        {
            var provider = new FakeCloudAuthProvider
            {
                SignInHandler = (email, password) =>
                {
                    var session = MakeSession(email);
                    session.EmailVerified = false;
                    return session;
                },
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());
            var env = new CloudEnvironment(name => null);

            // Sanity check: signed-out state must never claim a verified email.
            Assert.That(auth.GetLoginState(env).EmailVerified, Is.False);

            auth.SignIn("alice@dev.local", "BloomDev123!");
            Assert.That(
                auth.GetLoginState(env).EmailVerified,
                Is.False,
                "the fake session was built with EmailVerified=false"
            );

            auth.SignOut();
            Assert.That(auth.GetLoginState(env).EmailVerified, Is.False);
        }

        [Test]
        public void SignInWithExternalTokens_Success_SetsIdentityAndAccessToken()
        {
            var provider = new FakeCloudAuthProvider
            {
                AcceptExternalSessionHandler = (idToken, refreshToken) =>
                    new CloudSession
                    {
                        AccessToken = idToken,
                        RefreshToken = refreshToken,
                        Email = "alice@example.com",
                        UserId = "firebase-uid-1",
                        ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                        EmailVerified = true,
                    },
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());

            Assert.That(
                auth.IsSignedIn,
                Is.False,
                "should not be signed in before SignInWithExternalTokens is called"
            );

            auth.SignInWithExternalTokens("fake-id-token", "fake-refresh-token");

            Assert.That(auth.IsSignedIn, Is.True);
            Assert.That(auth.CurrentEmail, Is.EqualTo("alice@example.com"));
            Assert.That(auth.CurrentEmailVerified, Is.True);
            Assert.That(auth.GetAccessTokenOrNull(), Is.EqualTo("fake-id-token"));
            Assert.That(provider.AcceptExternalSessionCallCount, Is.EqualTo(1));
        }

        [Test]
        public void SignInWithExternalTokens_Failure_LeavesAuthSignedOut()
        {
            var provider = new FakeCloudAuthProvider
            {
                AcceptExternalSessionHandler = (idToken, refreshToken) =>
                    throw new CloudAuthException("malformed token"),
            };
            var auth = new CloudAuth(provider, new FakeCloudTokenStore());

            Assert.Throws<CloudAuthException>(() =>
                auth.SignInWithExternalTokens("bad-token", "bad-refresh")
            );

            Assert.That(auth.IsSignedIn, Is.False);
            Assert.That(auth.GetAccessTokenOrNull(), Is.Null);
        }
    }
}
