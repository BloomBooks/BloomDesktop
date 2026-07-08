using System;
using System.Linq;
using System.Net;
using System.Text;
using Bloom.TeamCollection.Cloud;
using Newtonsoft.Json;
using NUnit.Framework;
using RestSharp;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Unit tests for <see cref="FirebaseCloudAuthProvider"/> (task 12's Option A real auth
    /// provider). All HTTP is mocked via the same <see cref="FakeRestExecutor"/>/
    /// <see cref="FakeResponses"/> helpers <see cref="CloudCollectionClientTests"/> uses -- no
    /// live Google/Firebase calls are ever made.
    /// </summary>
    [TestFixture]
    public class FirebaseCloudAuthProviderTests
    {
        private static CloudEnvironment MakeEnvironment(string firebaseApiKey = "test-api-key") =>
            new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_FIREBASE_API_KEY" ? firebaseApiKey : null
            );

        /// <summary>
        /// Builds a JWT-shaped string (header.payload.signature) whose payload is the given
        /// claims, base64url-encoded exactly like a real Firebase ID token. The signature
        /// segment is a meaningless placeholder: FirebaseCloudAuthProvider never verifies it
        /// (see its own doc comment for why), only decodes the payload.
        /// </summary>
        private static string MakeIdToken(object claims)
        {
            string EncodeSegment(object value)
            {
                var json = JsonConvert.SerializeObject(value);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }

            var header = EncodeSegment(new { alg = "RS256", typ = "JWT" });
            var payload = EncodeSegment(claims);
            return $"{header}.{payload}.fake-signature";
        }

        private static object ValidClaims(
            string email = "alice@example.com",
            string sub = "firebase-uid-1",
            bool emailVerified = true,
            long? exp = null
        ) =>
            new
            {
                email,
                email_verified = emailVerified,
                sub,
                exp = exp ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            };

        // ------------------------------------------------------------------
        // AcceptExternalSession (the token-receipt endpoint's entry point)
        // ------------------------------------------------------------------

        [Test]
        public void AcceptExternalSession_ValidToken_ExtractsIdentityFromClaims()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            var idToken = MakeIdToken(ValidClaims());

            var session = provider.AcceptExternalSession(idToken, "a-refresh-token");

            Assert.That(session.Email, Is.EqualTo("alice@example.com"));
            Assert.That(session.UserId, Is.EqualTo("firebase-uid-1"));
            Assert.That(session.EmailVerified, Is.True);
            Assert.That(session.AccessToken, Is.EqualTo(idToken));
            Assert.That(session.RefreshToken, Is.EqualTo("a-refresh-token"));
            Assert.That(session.ExpiresAtUtc, Is.GreaterThan(DateTime.UtcNow));
        }

        [Test]
        public void AcceptExternalSession_UnverifiedEmail_ReportsEmailVerifiedFalse()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            var idToken = MakeIdToken(ValidClaims(emailVerified: false));

            var session = provider.AcceptExternalSession(idToken, "a-refresh-token");

            Assert.That(
                session.EmailVerified,
                Is.False,
                "must reflect the token's own email_verified=false claim, not default to true"
            );
        }

        [TestCase(null)]
        [TestCase("")]
        public void AcceptExternalSession_MissingIdTokenOrRefreshToken_Throws(string missing)
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            var idToken = MakeIdToken(ValidClaims());

            Assert.Throws<CloudAuthException>(() =>
                provider.AcceptExternalSession(missing, "a-refresh-token")
            );
            Assert.Throws<CloudAuthException>(() =>
                provider.AcceptExternalSession(idToken, missing)
            );
        }

        [Test]
        public void AcceptExternalSession_MalformedToken_ThrowsCloudAuthException()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());

            Assert.Throws<CloudAuthException>(() =>
                provider.AcceptExternalSession("not-a-jwt", "a-refresh-token")
            );
        }

        [Test]
        public void AcceptExternalSession_TokenMissingEmailClaim_ThrowsCloudAuthException()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            var idToken = MakeIdToken(new { sub = "firebase-uid-1", email_verified = true });

            var ex = Assert.Throws<CloudAuthException>(() =>
                provider.AcceptExternalSession(idToken, "a-refresh-token")
            );
            Assert.That(ex.Message, Does.Contain("email"));
        }

        [Test]
        public void AcceptExternalSession_NeverCallsNetwork()
        {
            // Sanity check on the design itself: parsing the caller's own freshly-issued idToken
            // is a pure local operation. Wire up an executor that fails the test if it's ever
            // invoked, so a future regression that adds an unwanted network hop is caught here.
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            provider.SetRestExecutorForTests(
                new FakeRestExecutor
                {
                    Handler = req =>
                        throw new InvalidOperationException(
                            "AcceptExternalSession must not make network calls"
                        ),
                }
            );

            Assert.DoesNotThrow(() =>
                provider.AcceptExternalSession(MakeIdToken(ValidClaims()), "a-refresh-token")
            );
        }

        // ------------------------------------------------------------------
        // SignIn: no password flow exists for Firebase/BloomLibrary accounts.
        // ------------------------------------------------------------------

        [Test]
        public void SignIn_AlwaysThrows_NoPasswordFlow()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());

            Assert.Throws<CloudAuthException>(() => provider.SignIn("alice@example.com", "pw"));
        }

        // ------------------------------------------------------------------
        // Refresh: Google's securetoken API (mocked).
        // ------------------------------------------------------------------

        [Test]
        public void Refresh_Success_PostsFormEncodedGrantAndReturnsNewSession()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment("my-firebase-api-key"));
            var executor = new FakeRestExecutor();
            provider.SetRestExecutorForTests(executor);
            var newIdToken = MakeIdToken(ValidClaims(email: "bob@example.com", sub: "uid-bob"));

            executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    JsonConvert.SerializeObject(
                        new
                        {
                            id_token = newIdToken,
                            refresh_token = "new-refresh-token",
                            expires_in = "3600",
                        }
                    )
                );

            var session = provider.Refresh("old-refresh-token");

            Assert.That(session.Email, Is.EqualTo("bob@example.com"));
            Assert.That(session.UserId, Is.EqualTo("uid-bob"));
            Assert.That(session.RefreshToken, Is.EqualTo("new-refresh-token"));
            Assert.That(session.AccessToken, Is.EqualTo(newIdToken));

            Assert.That(executor.RequestsSeen, Has.Count.EqualTo(1));
            var request = executor.RequestsSeen[0];
            var queryParams = request
                .Parameters.Where(p =>
                    p.Type == ParameterType.QueryString
                    || p.Type == ParameterType.QueryStringWithoutEncode
                )
                .ToList();
            Assert.That(
                queryParams,
                Has.Some.Matches<Parameter>(p =>
                    p.Name == "key" && (string)p.Value == "my-firebase-api-key"
                ),
                "the Firebase Web API key must be sent as the 'key' query parameter"
            );
            var bodyParams = request
                .Parameters.Where(p => p.Type == ParameterType.GetOrPost)
                .ToList();
            Assert.That(
                bodyParams,
                Has.Some.Matches<Parameter>(p =>
                    p.Name == "grant_type" && (string)p.Value == "refresh_token"
                )
            );
            Assert.That(
                bodyParams,
                Has.Some.Matches<Parameter>(p =>
                    p.Name == "refresh_token" && (string)p.Value == "old-refresh-token"
                )
            );
        }

        [Test]
        public void Refresh_NonOkResponse_ThrowsCloudAuthException()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            var executor = new FakeRestExecutor
            {
                Handler = req =>
                    FakeResponses.Make(
                        HttpStatusCode.BadRequest,
                        "{\"error\":{\"message\":\"TOKEN_EXPIRED\"}}"
                    ),
            };
            provider.SetRestExecutorForTests(executor);

            Assert.Throws<CloudAuthException>(() => provider.Refresh("expired-refresh-token"));
        }

        [Test]
        public void Refresh_ResponseMissingTokens_ThrowsCloudAuthException()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment());
            var executor = new FakeRestExecutor
            {
                Handler = req => FakeResponses.Make(HttpStatusCode.OK, "{}"),
            };
            provider.SetRestExecutorForTests(executor);

            Assert.Throws<CloudAuthException>(() => provider.Refresh("some-refresh-token"));
        }

        [Test]
        public void Refresh_NoFirebaseApiKeyConfigured_ThrowsWithoutCallingNetwork()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment(firebaseApiKey: ""));
            var executor = new FakeRestExecutor
            {
                Handler = req =>
                    throw new InvalidOperationException(
                        "must not call the network without an API key"
                    ),
            };
            provider.SetRestExecutorForTests(executor);

            Assert.Throws<CloudAuthException>(() => provider.Refresh("some-refresh-token"));
        }

        // ------------------------------------------------------------------
        // End-to-end through CloudAuth's session core: proves the provider's Refresh really
        // does keep a session alive via CloudAuth's generic ~80%-of-TTL proactive-refresh timer
        // (CloudAuthTests already covers that timer mechanism in isolation with a fake
        // provider; this closes the loop with the real FirebaseCloudAuthProvider wired in).
        // ------------------------------------------------------------------

        [Test]
        public void CloudAuth_WithFirebaseProvider_ProactivelyRefreshesNearExpiry()
        {
            var provider = new FirebaseCloudAuthProvider(MakeEnvironment("my-firebase-api-key"));
            var executor = new FakeRestExecutor();
            provider.SetRestExecutorForTests(executor);
            executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    JsonConvert.SerializeObject(
                        new
                        {
                            id_token = MakeIdToken(
                                ValidClaims(
                                    email: "alice@example.com",
                                    exp: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                                )
                            ),
                            refresh_token = "refreshed-refresh-token",
                            expires_in = "3600",
                        }
                    )
                );
            var auth = new CloudAuth(provider, new InMemoryCloudTokenStore());

            // A near-expired (0.2s TTL) ID token so the ~80%-of-TTL proactive-refresh timer
            // fires almost immediately, keeping this test fast.
            var almostExpiredIdToken = MakeIdToken(
                ValidClaims(
                    email: "alice@example.com",
                    exp: DateTimeOffset.UtcNow.AddSeconds(0.2).ToUnixTimeSeconds()
                )
            );
            auth.SignInWithExternalTokens(almostExpiredIdToken, "original-refresh-token");
            Assert.That(auth.CurrentEmail, Is.EqualTo("alice@example.com"));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (executor.RequestsSeen.Count == 0 && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(25);

            Assert.That(
                executor.RequestsSeen,
                Has.Count.GreaterThanOrEqualTo(1),
                "CloudAuth's proactive-refresh timer should have called FirebaseCloudAuthProvider.Refresh on its own"
            );
            Assert.That(auth.IsSignedIn, Is.True, "the refreshed session must still be signed in");
        }

        // ------------------------------------------------------------------
        // CloudAuth.CreateProvider wiring
        // ------------------------------------------------------------------

        [Test]
        public void CreateProvider_CloudAuthMode_ReturnsFirebaseProvider()
        {
            var env = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_AUTH_MODE" ? "cloud" : null
            );

            var provider = CloudAuth.CreateProvider(env);

            Assert.That(provider, Is.InstanceOf<FirebaseCloudAuthProvider>());
        }
    }
}
