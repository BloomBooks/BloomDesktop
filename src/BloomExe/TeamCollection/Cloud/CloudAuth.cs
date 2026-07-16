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
            // Use the DPAPI-backed persistent store (not the in-memory default) so a real Cloud
            // TC session survives a Bloom restart: InitializeAtStartup restores it from disk, and
            // every sign-in/refresh re-saves it. Harmless for the dev env-override path, which
            // re-signs from BLOOM_CLOUDTC_USER before ever consulting the store (see
            // InitializeAtStartup).
            var auth = new CloudAuth(CreateProvider(environment), new DpapiCloudTokenStore());
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
}
