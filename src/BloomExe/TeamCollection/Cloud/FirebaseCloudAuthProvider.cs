using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Real (Option A) auth provider (`BLOOM_CLOUDTC_AUTH_MODE=cloud`): the user signs in on the
    /// BloomLibrary-hosted browser page (the same one Bloom already opens for BloomLibrary
    /// account sign-in, see BloomLibraryAuthentication.LogIn/SharingApi.HandleShowSignIn), which
    /// forwards the resulting Firebase ID + refresh tokens to Bloom's token-receipt endpoint
    /// (ExternalApi.cs; CONTRACTS.md's "Auth (Option A)" section documents the exact shape).
    /// There is no password flow -- Firebase already authenticated the user in the browser --
    /// so <see cref="SignIn"/> always throws; the only ways into a session are
    /// <see cref="AcceptExternalSession"/> (the token-receipt endpoint) and <see cref="Refresh"/>
    /// (CloudAuth's proactive-refresh timer / 401 retry, restoring a persisted session, etc.).
    /// Identity (email/userId/emailVerified/expiry) is always read from the token's own claims,
    /// never trusted from a caller -- see <see cref="SessionFromIdToken"/>.
    /// </summary>
    public class FirebaseCloudAuthProvider : ICloudAuthProvider
    {
        // Google's Identity Toolkit "securetoken" REST endpoint. Not a Supabase/GoTrue URL --
        // under Option A, Bloom talks to Firebase directly to keep the session alive; Supabase
        // only ever sees the resulting Firebase ID token as a bearer credential, never mints or
        // refreshes one itself.
        private const string SecureTokenBaseUrl = "https://securetoken.googleapis.com";

        private readonly CloudEnvironment _environment;
        private IRestExecutor _restExecutor;

        public FirebaseCloudAuthProvider(CloudEnvironment environment)
        {
            _environment = environment;
        }

        /// <summary>Test-only seam: lets unit tests substitute a fake <see cref="IRestExecutor"/>
        /// (the same one CloudCollectionClient's tests use) so Refresh's HTTP call can be
        /// exercised without a live network. Production code never needs to call this.</summary>
        internal void SetRestExecutorForTests(IRestExecutor executor) => _restExecutor = executor;

        private IRestExecutor RestExecutor =>
            _restExecutor ?? (_restExecutor = new RestSharpExecutor(SecureTokenBaseUrl));

        public CloudSession SignIn(string email, string password) =>
            throw new CloudAuthException(
                "Cloud Team Collections have no password sign-in; use the BloomLibrary "
                    + "browser sign-in (SharingApi.HandleShowSignIn) instead."
            );

        /// <summary>The Bloom-side half of the token-receipt endpoint: turns a freshly-forwarded
        /// Firebase ID+refresh token pair into a session. See the class doc comment.</summary>
        public CloudSession AcceptExternalSession(string idToken, string refreshToken)
        {
            if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(refreshToken))
                throw new CloudAuthException(
                    "AcceptExternalSession requires both a non-empty idToken and refreshToken."
                );
            return SessionFromIdToken(idToken, refreshToken);
        }

        /// <summary>
        /// Exchanges a refresh token for a new ID token via Google's securetoken API
        /// (https://firebase.google.com/docs/reference/rest/auth#section-refresh-token), the
        /// mechanism CloudAuth's proactive-refresh timer and 401-retry rely on to keep a
        /// long-lived session alive without ever prompting the user again.
        /// </summary>
        public CloudSession Refresh(string refreshToken)
        {
            if (string.IsNullOrEmpty(_environment.FirebaseApiKey))
                throw new CloudAuthException(
                    "BLOOM_CLOUDTC_FIREBASE_API_KEY is not configured; cannot refresh a "
                        + "Cloud Team Collection session."
                );

            var request = new RestRequest(
                $"/v1/token?key={Uri.EscapeDataString(_environment.FirebaseApiKey)}",
                Method.POST
            );
            // Google's securetoken endpoint takes a form-encoded body, unlike the Supabase/
            // GoTrue JSON endpoints DevCloudAuthProvider talks to.
            request.AddParameter("grant_type", "refresh_token");
            request.AddParameter("refresh_token", refreshToken);
            var response = RestExecutor.Execute(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new CloudAuthException(
                    $"Firebase token refresh failed: {(int)response.StatusCode} ({response.Content})"
                );

            JObject json;
            try
            {
                json = JObject.Parse(response.Content);
            }
            catch (JsonException e)
            {
                throw new CloudAuthException(
                    "Firebase token refresh response was not valid JSON: " + response.Content,
                    e
                );
            }

            // The securetoken response's "id_token" is the refreshed JWT carrying the claims
            // SessionFromIdToken reads identity from ("access_token" is documented to carry the
            // same value, kept only as a fallback in case that ever changes).
            var newIdToken = (string)json["id_token"] ?? (string)json["access_token"];
            var newRefreshToken = (string)json["refresh_token"];
            if (string.IsNullOrEmpty(newIdToken) || string.IsNullOrEmpty(newRefreshToken))
                throw new CloudAuthException(
                    "Firebase token refresh response was missing id_token/refresh_token: "
                        + response.Content
                );

            return SessionFromIdToken(newIdToken, newRefreshToken);
        }

        /// <summary>
        /// Builds a CloudSession entirely from the ID token's own claims -- per the class doc
        /// comment, identity is never trusted from a caller. Deliberately does NOT verify the
        /// token's signature: that would require fetching and caching Google's rotating public
        /// certs for no real benefit here, because every actual USE of the resulting
        /// AccessToken is independently verified server-side (Supabase, configured for Firebase
        /// third-party auth, checks the signature on every request) -- a forged/expired token
        /// would simply fail there with a 401, which CloudAuth already treats as "please sign
        /// in". This method only trusts the token enough to populate local, display-only state
        /// (whoami / sign-in status / the emailVerified flag CONTRACTS.md's loginState surfaces).
        /// </summary>
        private static CloudSession SessionFromIdToken(string idToken, string refreshToken)
        {
            JObject claims;
            try
            {
                claims = DecodeJwtPayload(idToken);
            }
            catch (Exception e) when (!(e is CloudAuthException))
            {
                throw new CloudAuthException("Could not parse the Firebase ID token.", e);
            }

            var email = (string)claims["email"];
            var userId = (string)claims["sub"] ?? (string)claims["user_id"];
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(userId))
                throw new CloudAuthException(
                    "Firebase ID token is missing the required 'email'/'sub' claims."
                );

            var expSeconds = (long?)claims["exp"];
            var expiresAtUtc = expSeconds.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(expSeconds.Value).UtcDateTime
                : DateTime.UtcNow.AddHours(1);

            return new CloudSession
            {
                AccessToken = idToken,
                RefreshToken = refreshToken,
                Email = email,
                UserId = userId,
                ExpiresAtUtc = expiresAtUtc,
                // Firebase ID tokens always carry a top-level boolean email_verified claim (the
                // same shape tc.jwt_email_verified() already special-cases in
                // 20260706000001_tc_schema.sql) -- absence would mean a malformed/unexpected
                // token, not "unverified", so this only ever resolves to a real true/false here.
                EmailVerified = (bool?)claims["email_verified"] ?? false,
            };
        }

        /// <summary>Decodes a JWT's middle (payload) segment into its claims. Does not verify
        /// the signature -- see <see cref="SessionFromIdToken"/>'s doc comment for why that's
        /// acceptable here.</summary>
        private static JObject DecodeJwtPayload(string jwt)
        {
            var parts = jwt?.Split('.') ?? Array.Empty<string>();
            if (parts.Length < 2)
                throw new CloudAuthException(
                    "Malformed JWT: expected header.payload.signature, got "
                        + parts.Length
                        + " segment(s)."
                );
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            return JObject.Parse(payloadJson);
        }

        /// <summary>Decodes JWT-flavored base64url (`-`/`_` in place of `+`/`/`, padding
        /// stripped) into raw bytes.</summary>
        private static byte[] Base64UrlDecode(string input)
        {
            var base64 = input.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
