using System;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;

namespace Bloom.web.controllers
{
    /// <summary>
    /// App-wide API for the Bloom Library login state. This is the single place the front end goes
    /// to find out whether the user is logged in to Bloom Library, and to log in or out. It is
    /// intentionally independent of any particular screen (e.g. the Publish-to-Web screen): the
    /// underlying login state lives in BloomLibraryBookApiClient, and this class just exposes it over
    /// the API and broadcasts changes via websocket so every screen can stay in sync.
    /// </summary>
    public class AccountApi : IDisposable
    {
        // This goes out with our messages and, on the client side (typescript), messages are filtered
        // down to the context that requested them.
        private const string kWebSocketContext = "account";
        private const string kWebSocketEventId_loginStateChanged = "loginStateChanged";

        private readonly BloomLibraryBookApiClient _client;
        private readonly BloomWebSocketServer _webSocketServer;
        private readonly AvatarCache _avatarCache;

        /// <summary>
        /// Created by autofac, which creates the one instance and registers it with the server.
        /// Subscribes to the BloomLibraryBookApiClient's LoginDataChanged event so that every change
        /// to the login state (however it happens -- interactive login, logout, restoring a saved
        /// login at startup) gets broadcast to the front end.
        /// </summary>
        public AccountApi(
            BloomLibraryBookApiClient client,
            BloomWebSocketServer webSocketServer,
            AvatarCache avatarCache
        )
        {
            _client = client;
            _webSocketServer = webSocketServer;
            _avatarCache = avatarCache;
            _client.LoginDataChanged += OnLoginDataChanged;
        }

        // Fires whenever the login state changes (interactive login, logout, or a restored login at
        // startup); re-broadcasts the current state so every screen stays in sync.
        private void OnLoginDataChanged(object sender, EventArgs e)
        {
            BroadcastLoginState();
        }

        /// <summary>
        /// Register the account/* endpoints: status (GET), login (POST), logout (POST).
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "account/status",
                HandleStatus,
                handleOnUiThread: false
            );
            apiHandler.RegisterEndpointHandler(
                "account/login",
                HandleLogin,
                handleOnUiThread: false
            );
            apiHandler.RegisterEndpointHandler(
                "account/logout",
                HandleLogout,
                handleOnUiThread: false
            );
        }

        // GET account/status: report whether we are logged in, and to whom (the email, or empty).
        private void HandleStatus(ApiRequest request)
        {
            request.ReplyWithJson(new { email = CurrentEmail });
        }

        // POST account/login: launch the external-browser login (the browser calls us back at
        // external/login when it succeeds).
        private void HandleLogin(ApiRequest request)
        {
            BloomLibraryAuthentication.LogIn();
            request.PostSucceeded();
        }

        private void HandleLogout(ApiRequest request)
        {
            // Capture the email BEFORE logging out (Logout clears it) so we can drop this user's known
            // avatar photo from the cache, reverting them to Gravatar. Cached image files may remain;
            // that's harmless.
            var emailBeforeLogout = Settings.Default.WebUserId;
            _client.Logout();
            if (!string.IsNullOrEmpty(emailBeforeLogout))
                _avatarCache.RemoveKnownPhotoUrl(AvatarCache.Md5OfEmail(emailBeforeLogout));
            // The state-change broadcast happens via the LoginDataChanged event handler above.
            request.PostSucceeded();
        }

        // The email of the logged-in user, or empty when not logged in.
        private string CurrentEmail => _client.LoggedIn ? Settings.Default.WebUserId : "";

        /// <summary>
        /// Called once at startup (from ProjectContext, after this API's endpoints are registered) to
        /// restore a previously-saved login, if there is one for the current upload destination.
        /// If a login is restored, the LoginDataChanged event fires and broadcasts the new state to
        /// any UI that is already listening. Afterward, kicks off (fire-and-forget) a background
        /// validation of the restored token, in case it has since been revoked.
        /// </summary>
        public void RestoreSavedLoginIfAny()
        {
            if (_client.TryRestoreSavedLogin(BookUpload.Destination))
            {
                Task.Run(() => _client.ValidateTokenAsync());
            }
        }

        // Send the current login state to the front end over the "account" websocket context so every
        // screen listening for it updates.
        private void BroadcastLoginState()
        {
            dynamic bundle = new DynamicJson();
            bundle.email = CurrentEmail;
            _webSocketServer.SendBundle(
                kWebSocketContext,
                kWebSocketEventId_loginStateChanged,
                bundle
            );
        }

        /// <summary>
        /// Unsubscribe from the client's event so this instance doesn't leak a subscription onto a
        /// singleton that will outlive it (the project scope disposes IDisposable singletons it
        /// created, which is when this runs).
        /// </summary>
        public void Dispose()
        {
            _client.LoginDataChanged -= OnLoginDataChanged;
        }
    }
}
