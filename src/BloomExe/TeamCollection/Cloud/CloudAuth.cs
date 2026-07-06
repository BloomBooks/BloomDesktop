using System;
using System.Net;
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
    }

    /// <summary>Raised by <see cref="CloudAuth.AccountSwitched"/> when a new sign-in changes the identity.</summary>
    public class CloudAccountSwitchedEventArgs : EventArgs
    {
        public string PreviousEmail { get; }
        public string NewEmail { get; }

        public CloudAccountSwitchedEventArgs(string previousEmail, string newEmail)
        {
            PreviousEmail = previousEmail;
            NewEmail = newEmail;
        }
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
    /// <see cref="RealCloudAuthProvider"/> (a stub until the Option A/B/C decision lands).
    /// </summary>
    public interface ICloudAuthProvider
    {
        /// <summary>Signs in with an email/password, creating the account first if necessary. Throws CloudAuthException on failure.</summary>
        CloudSession SignIn(string email, string password);

        /// <summary>Exchanges a still-valid refresh token for a new session. Throws CloudAuthException on failure.</summary>
        CloudSession Refresh(string refreshToken);
    }

    /// <summary>
    /// Where a signed-in session is remembered between sign-ins (e.g. so a single Bloom instance
    /// can restart without asking the user to sign in again). The default
    /// <see cref="InMemoryCloudTokenStore"/> is process-lifetime only; a persistent
    /// implementation (Settings-backed or OS credential store) is a follow-up, not required for
    /// this skeleton, and callers must not assume tokens survive a restart.
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

        /// <summary>Fired when a sign-in replaces a previously-active, different account.</summary>
        public event EventHandler<CloudAccountSwitchedEventArgs> AccountSwitched;

        /// <summary>Fired whenever the session is cleared, whether by explicit sign-out or a failed refresh.</summary>
        public event EventHandler SignedOut;

        public CloudAuth(ICloudAuthProvider provider, ICloudTokenStore tokenStore = null)
        {
            _provider = provider;
            _tokenStore = tokenStore ?? new InMemoryCloudTokenStore();
        }

        /// <summary>Builds the provider matching <paramref name="environment"/>'s configured auth mode.</summary>
        public static ICloudAuthProvider CreateProvider(CloudEnvironment environment)
        {
            switch (environment.AuthMode)
            {
                case CloudAuthMode.Real:
                    return new RealCloudAuthProvider();
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
                SignOutCore(raiseEvent: true);
                return false;
            }
        }

        /// <summary>Clears the session (locally and in the token store) and cancels the refresh timer.</summary>
        public void SignOut() => SignOutCore(raiseEvent: true);

        /// <summary>
        /// Groundwork data for the `sharing/loginState` endpoint (see <see cref="CloudLoginState"/>).
        /// </summary>
        public CloudLoginState GetLoginState(CloudEnvironment environment) =>
            new CloudLoginState
            {
                AuthMode = environment.AuthMode == CloudAuthMode.Dev ? "dev" : "real",
                SignedIn = IsSignedIn,
                Email = CurrentEmail,
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

            if (
                previousEmail != null
                && !string.Equals(
                    previousEmail,
                    newSession.Email,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AccountSwitched?.Invoke(
                    this,
                    new CloudAccountSwitchedEventArgs(previousEmail, newSession.Email)
                );
            }
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
                SignOutCore(raiseEvent: true);
            }
        }

        private void SignOutCore(bool raiseEvent)
        {
            lock (_lock)
                _session = null;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            _tokenStore.Clear();
            if (raiseEvent)
                SignedOut?.Invoke(this, EventArgs.Empty);
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
            };
        }
    }

    /// <summary>
    /// Real auth provider (`BLOOM_CLOUDTC_AUTH_MODE=real`): a placeholder for the BloomLibrary/
    /// Firebase sign-in that the pending Option A/B/C decision (see the design doc) will select.
    /// The `external/login` payload hook (ExternalApi.LoginSuccessful) already exists from the
    /// BloomLibrary integration and is expected to feed this provider once implemented; until
    /// then it is simply not available, and callers should treat that as "please sign in" rather
    /// than a crash.
    /// </summary>
    public class RealCloudAuthProvider : ICloudAuthProvider
    {
        public CloudSession SignIn(string email, string password) =>
            throw new CloudAuthException(
                "Real Cloud Team Collection sign-in is not yet available (Option A/B/C decision pending)."
            );

        public CloudSession Refresh(string refreshToken) =>
            throw new CloudAuthException(
                "Real Cloud Team Collection sign-in is not yet available (Option A/B/C decision pending)."
            );
    }
}
