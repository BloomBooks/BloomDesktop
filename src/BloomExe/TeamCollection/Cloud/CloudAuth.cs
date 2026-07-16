using System;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// An immutable snapshot of a signed-in session: the bearer token to send, the refresh token
    /// to use when it expires, and who the caller is.
    /// </summary>
    public class CloudSession
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Email { get; set; }
        public string UserId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>
        /// Whether the provider considers this identity's email verified, read from the
        /// provider's own token/claims (never from anything a caller merely asserts). Null
        /// means "this provider doesn't track the concept" (the dev provider's accounts are all
        /// auto-confirmed, so it always sets true -- see DevCloudAuthProvider.ToSession).
        /// </summary>
        public bool? EmailVerified { get; set; }
    }

    /// <summary>
    /// Groundwork for the future `sharing/loginState` API endpoint (the actual endpoint
    /// registration belongs to the UI tasks, which own the shared TeamCollectionApi.cs handler
    /// file): reports enough that the client can decide whether to show a plain dev-mode
    /// email/password form (AuthMode == "dev") instead of a real sign-in browser flow, plus the
    /// current identity so the UI can show who is signed in.
    /// </summary>
    public class CloudLoginState
    {
        public string AuthMode { get; set; }
        public bool SignedIn { get; set; }
        public string Email { get; set; }

        /// <summary>False when signed out or when the current session's provider left it
        /// unknown (see <see cref="CloudSession.EmailVerified"/>). Mirrors what
        /// tc.jwt_email_verified() decides server-side, so the client can show/withhold
        /// approval-dependent UI without waiting on a round trip.</summary>
        public bool EmailVerified { get; set; }
    }

    /// <summary>Thrown by an <see cref="ICloudAuthProvider"/> when sign-in or refresh fails.</summary>
    public class CloudAuthException : ApplicationException
    {
        public CloudAuthException(string message)
            : base(message) { }

        public CloudAuthException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// The mechanics of actually obtaining/renewing a session. <see cref="CloudAuth"/> owns the
    /// provider-agnostic session lifecycle (storage, proactive refresh, sign-out, account-switch
    /// detection) and delegates the actual network exchange to one of these. There are two
    /// implementations: <see cref="DevCloudAuthProvider"/> (local GoTrue) and
    /// <see cref="FirebaseCloudAuthProvider"/> (Option A, decided 8 Jul 2026).
    /// </summary>
    public interface ICloudAuthProvider
    {
        /// <summary>Signs in with an email/password, creating the account first if necessary. Throws CloudAuthException on failure.</summary>
        CloudSession SignIn(string email, string password);

        /// <summary>Exchanges a still-valid refresh token for a new session. Throws CloudAuthException on failure.</summary>
        CloudSession Refresh(string refreshToken);

        /// <summary>
        /// Accepts an externally-obtained ID token + refresh token (e.g. the Firebase tokens
        /// BloomLibrary2's login page forwards to Bloom's token-receipt endpoint -- see
        /// CONTRACTS.md's "Auth (Option A)" section) as a brand-new session. Implementations
        /// MUST derive identity (email/userId/emailVerified/expiry) from the token's own claims,
        /// never from anything the caller separately asserts. Throws CloudAuthException on
        /// failure. Default implementation is "not supported": only a provider that actually
        /// receives externally-minted tokens (the Firebase/cloud provider) needs to override
        /// this, so the dev password-based provider and any test doubles predating this method
        /// don't have to change.
        /// </summary>
        CloudSession AcceptExternalSession(string idToken, string refreshToken) =>
            throw new NotSupportedException(
                $"{GetType().Name} does not accept externally-obtained tokens."
            );
    }

    /// <summary>
    /// Where a signed-in session is remembered between sign-ins (e.g. so a single Bloom instance
    /// can restart without asking the user to sign in again). <see cref="InMemoryCloudTokenStore"/>
    /// is process-lifetime only (used by the dev provider and by tests); the persistent,
    /// DPAPI-backed implementation a real cloud session needs is
    /// <see cref="DpapiCloudTokenStore"/> in CloudTokenStore.cs. Callers that use
    /// InMemoryCloudTokenStore must not assume tokens survive a restart.
    /// </summary>
    public interface ICloudTokenStore
    {
        CloudSession Load();
        void Save(CloudSession session);
        void Clear();
    }

    /// <summary>Process-lifetime-only token store. See <see cref="ICloudTokenStore"/> remarks.</summary>
    public class InMemoryCloudTokenStore : ICloudTokenStore
    {
        private CloudSession _session;

        public CloudSession Load() => _session;

        public void Save(CloudSession session) => _session = session;

        public void Clear() => _session = null;
    }

    /// <summary>
    /// Provider-agnostic session core for Cloud Team Collections: holds the current
    /// <see cref="CloudSession"/>, proactively refreshes it at ~80% of its TTL (and on-demand
    /// when a request comes back 401), detects when a new sign-in switches the active account,
    /// and exposes "who am I" / sign-out. Editing a checked-out book must never block on auth,
    /// so <see cref="GetAccessTokenOrNull"/> is a pure in-memory read — it never makes a network
    /// call; callers that get a 401 back from the server call <see cref="TryRefreshOn401"/> and
    /// abort their operation (surfacing "please sign in") if that also fails.
    /// </summary>
    public class CloudAuth : IDisposable
    {
        private readonly ICloudAuthProvider _provider;
        private readonly ICloudTokenStore _tokenStore;
        private readonly object _lock = new object();
        private CloudSession _session;
        private Timer _refreshTimer;

        // NOTE: this class used to expose AccountSwitched/SignedOut events, but nothing ever
        // subscribed. Account-switch consumers instead key their cached state on the signed-in
        // email (e.g. CloudTeamCollection._membershipsClaimedForEmail), which self-invalidates.

        public CloudAuth(ICloudAuthProvider provider, ICloudTokenStore tokenStore = null)
        {
            _provider = provider;
            _tokenStore = tokenStore ?? new InMemoryCloudTokenStore();
        }

        /// <summary>
        /// The one shared construction sequence for a ready-to-use auth: builds a CloudAuth with
        /// the provider matching <paramref name="environment"/> and immediately establishes a
        /// session where possible (env-override credentials or a stored token — see
        /// <see cref="InitializeAtStartup"/>). Used everywhere a default auth is needed
        /// (TeamCollectionManager.ConnectToCloudCollection, CloudTeamCollection's own default,
        /// SharingApi's process-wide fallback).
        /// </summary>
        public static CloudAuth CreateInitialized(CloudEnvironment environment)
        {
            var auth = new CloudAuth(CreateProvider(environment));
            auth.InitializeAtStartup(environment);
            return auth;
        }

        /// <summary>Builds the provider matching <paramref name="environment"/>'s configured auth mode.</summary>
        public static ICloudAuthProvider CreateProvider(CloudEnvironment environment)
        {
            switch (environment.AuthMode)
            {
                case CloudAuthMode.Cloud:
                    return new FirebaseCloudAuthProvider(environment);
                case CloudAuthMode.Dev:
                default:
                    return new DevCloudAuthProvider(environment);
            }
        }

        public bool IsSignedIn
        {
            get
            {
                lock (_lock)
                    return _session != null;
            }
        }

        /// <summary>The signed-in user's email, or null if not signed in.</summary>
        public string CurrentEmail
        {
            get
            {
                lock (_lock)
                    return _session?.Email;
            }
        }

        /// <summary>The signed-in user's server-assigned id (the JWT `sub` claim), or null.</summary>
        public string CurrentUserId
        {
            get
            {
                lock (_lock)
                    return _session?.UserId;
            }
        }

        /// <summary>See <see cref="CloudSession.EmailVerified"/>; false when signed out or the
        /// provider left it unknown.</summary>
        public bool CurrentEmailVerified
        {
            get
            {
                lock (_lock)
                    return _session?.EmailVerified ?? false;
            }
        }

        /// <summary>
        /// Explicit sign-in (e.g. the user submitted the dev-mode email/password form). Throws
        /// CloudAuthException on failure; the caller decides how to surface that to the user.
        /// </summary>
        public void SignIn(string email, string password)
        {
            var newSession = _provider.SignIn(email, password);
            ApplyNewSession(newSession);
        }

        /// <summary>
        /// Explicit sign-in from an externally-obtained token pair (the Bloom-side half of
        /// BloomLibrary2's login forwarding -- see the token-receipt endpoint in
        /// ExternalApi.cs and CONTRACTS.md's "Auth (Option A)" section). Throws
        /// CloudAuthException on failure (e.g. a malformed token); the caller decides how to
        /// surface that.
        /// </summary>
        public void SignInWithExternalTokens(string idToken, string refreshToken)
        {
            var newSession = _provider.AcceptExternalSession(idToken, refreshToken);
            ApplyNewSession(newSession);
        }

        /// <summary>
        /// Called once at startup to establish a session without blocking on user interaction
        /// where possible. `BLOOM_CLOUDTC_USER`/`BLOOM_CLOUDTC_PASSWORD` (when set) always win
        /// over any stored session — that override, and only that override, is what lets two
        /// Bloom instances on one machine run as two different users. Never throws: a failure
        /// here just leaves the session unset, which is a normal "please sign in" state, not a
        /// crash (editing a checked-out book must never block on auth).
        /// </summary>
        public void InitializeAtStartup(CloudEnvironment environment)
        {
            if (!string.IsNullOrEmpty(environment.DevUser))
            {
                try
                {
                    SignIn(environment.DevUser, environment.DevPassword ?? string.Empty);
                }
                catch (Exception e)
                {
                    Logger.WriteError("CloudAuth: env-override sign-in failed", e);
                }
                return;
            }

            var stored = _tokenStore.Load();
            if (stored?.RefreshToken == null)
                return;

            try
            {
                RefreshWith(stored.RefreshToken);
            }
            catch (Exception e)
            {
                Logger.WriteError("CloudAuth: restoring the stored session failed", e);
                _tokenStore.Clear();
            }
        }

        /// <summary>
        /// A pure in-memory read of the current bearer token (or null if not signed in). This
        /// deliberately never triggers a network call so that nothing on the book-editing path
        /// can block on auth; a stale token simply results in the server returning 401, which
        /// callers handle via <see cref="TryRefreshOn401"/>.
        /// </summary>
        public string GetAccessTokenOrNull()
        {
            lock (_lock)
                return _session?.AccessToken;
        }

        /// <summary>
        /// Called by <see cref="CloudCollectionClient"/> when a request comes back 401. Attempts
        /// one synchronous refresh using the stored refresh token. Returns true if the caller
        /// should retry its request with the (now-refreshed) token; false if there is no way to
        /// recover, in which case the session has been cleared and the caller should abort its
        /// operation and surface "please sign in" rather than retry indefinitely.
        /// </summary>
        public bool TryRefreshOn401()
        {
            string refreshToken;
            lock (_lock)
                refreshToken = _session?.RefreshToken;
            if (refreshToken == null)
                return false;

            try
            {
                RefreshWith(refreshToken);
                return true;
            }
            catch (Exception e)
            {
                Logger.WriteError("CloudAuth: refresh-on-401 failed", e);
                SignOutCore();
                return false;
            }
        }

        /// <summary>Clears the session (locally and in the token store) and cancels the refresh timer.</summary>
        public void SignOut() => SignOutCore();

        /// <summary>
        /// Groundwork data for the `sharing/loginState` endpoint (see <see cref="CloudLoginState"/>).
        /// </summary>
        public CloudLoginState GetLoginState(CloudEnvironment environment) =>
            new CloudLoginState
            {
                AuthMode = environment.AuthMode == CloudAuthMode.Dev ? "dev" : "cloud",
                SignedIn = IsSignedIn,
                Email = CurrentEmail,
                EmailVerified = IsSignedIn && CurrentEmailVerified,
            };

        private void RefreshWith(string refreshToken)
        {
            var newSession = _provider.Refresh(refreshToken);
            ApplyNewSession(newSession);
        }

        private void ApplyNewSession(CloudSession newSession)
        {
            string previousEmail;
            lock (_lock)
            {
                previousEmail = _session?.Email;
                _session = newSession;
            }
            _tokenStore.Save(newSession);
            ScheduleProactiveRefresh(newSession);
        }

        /// <summary>
        /// Arms a one-shot timer that refreshes the session at ~80% of its remaining TTL, well
        /// before the server would reject it — this is what lets a session survive indefinitely
        /// (e.g. the &gt;2h soak test in the task's acceptance criteria) without ever hitting a
        /// user-visible 401 in the common case.
        /// </summary>
        private void ScheduleProactiveRefresh(CloudSession session)
        {
            _refreshTimer?.Dispose();

            var ttl = session.ExpiresAtUtc - DateTime.UtcNow;
            var due = TimeSpan.FromTicks((long)(ttl.Ticks * 0.8));
            if (due < TimeSpan.Zero)
                due = TimeSpan.Zero;

            _refreshTimer = new Timer(
                _ => OnProactiveRefreshDue(),
                null,
                due,
                Timeout.InfiniteTimeSpan
            );
        }

        private void OnProactiveRefreshDue()
        {
            string refreshToken;
            lock (_lock)
                refreshToken = _session?.RefreshToken;
            if (refreshToken == null)
                return;

            try
            {
                RefreshWith(refreshToken);
            }
            catch (Exception e)
            {
                Logger.WriteError("CloudAuth: proactive refresh failed", e);
                SignOutCore();
            }
        }

        private void SignOutCore()
        {
            lock (_lock)
                _session = null;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            _tokenStore.Clear();
        }

        public void Dispose() => _refreshTimer?.Dispose();
    }

    /// <summary>
    /// Dev auth provider (`BLOOM_CLOUDTC_AUTH_MODE=dev`): signs in against the local GoTrue
    /// instance bundled with the local Supabase dev stack. Any email/password is accepted —
    /// an unrecognized email is signed up (auto-confirmed, per server/dev/config.auth.toml.snippet)
    /// then signed in — which is what makes ad-hoc dev identities possible with no seed change.
    /// Deliberately tiny and self-contained: it can be deleted without touching CloudAuth's
    /// session core once a real provider exists.
    /// </summary>
    public class DevCloudAuthProvider : ICloudAuthProvider
    {
        private readonly CloudEnvironment _environment;
        private RestClient _restClient;

        public DevCloudAuthProvider(CloudEnvironment environment)
        {
            _environment = environment;
        }

        private RestClient RestClient =>
            _restClient ?? (_restClient = new RestClient(_environment.SupabaseUrl));

        public CloudSession SignIn(string email, string password)
        {
            var signInResponse = PostAuth("token?grant_type=password", email, password);
            if (signInResponse.StatusCode == HttpStatusCode.OK)
                return ToSession(signInResponse);

            // Unknown email: sign up (auto-confirmed by the dev stack's
            // enable_confirmations=false), then the signup response itself is a valid session.
            var signUpResponse = PostAuth("signup", email, password);
            if (signUpResponse.StatusCode == HttpStatusCode.OK)
                return ToSession(signUpResponse);

            throw new CloudAuthException(
                $"Dev sign-in failed for {email}: sign-in gave {(int)signInResponse.StatusCode} "
                    + $"({signInResponse.Content}); sign-up gave {(int)signUpResponse.StatusCode} "
                    + $"({signUpResponse.Content})"
            );
        }

        public CloudSession Refresh(string refreshToken)
        {
            var request = new RestRequest("auth/v1/token?grant_type=refresh_token", Method.POST);
            request.AddHeader("apikey", _environment.AnonKey);
            request.AddJsonBody(new { refresh_token = refreshToken });
            var response = RestClient.Execute(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new CloudAuthException(
                    $"Dev token refresh failed: {(int)response.StatusCode} ({response.Content})"
                );
            return ToSession(response);
        }

        private IRestResponse PostAuth(string endpoint, string email, string password)
        {
            var request = new RestRequest($"auth/v1/{endpoint}", Method.POST);
            request.AddHeader("apikey", _environment.AnonKey);
            request.AddJsonBody(new { email, password });
            return RestClient.Execute(request);
        }

        private static CloudSession ToSession(IRestResponse response)
        {
            JObject json;
            try
            {
                json = JObject.Parse(response.Content);
            }
            catch (JsonException e)
            {
                throw new CloudAuthException(
                    "Dev auth response was not valid JSON: " + response.Content,
                    e
                );
            }

            var accessToken = (string)json["access_token"];
            var refreshToken = (string)json["refresh_token"];
            var expiresIn = (int?)json["expires_in"] ?? 3600;
            var user = json["user"];
            if (string.IsNullOrEmpty(accessToken) || user == null)
                throw new CloudAuthException(
                    "Dev auth response was missing access_token/user: " + response.Content
                );

            return new CloudSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                Email = (string)user["email"],
                UserId = (string)user["id"],
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn),
                // The dev stack auto-confirms every signup (server/dev/config.auth.toml.snippet),
                // so every dev session is, by definition, verified.
                EmailVerified = true,
            };
        }
    }

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
