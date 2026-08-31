using System.IO;
using System.Net;
using System.Threading.Tasks;
using Bloom.Api;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Serves avatar images for a person (by email) from the local server, backed by AvatarCache.
    /// This is deliberately independent of the login/account feature because avatars are broader than
    /// the logged-in user: Team Collection UI asks for teammates' avatars too. Every BloomAvatar in
    /// the front end sources its image from here, so all avatars are cached, offline-capable, and no
    /// longer re-ping remote hosts on each render.
    /// </summary>
    public class AvatarApi
    {
        private readonly AvatarCache _avatarCache;

        // Created by autofac, which creates the one instance and registers it with the server.
        public AvatarApi(AvatarCache avatarCache)
        {
            _avatarCache = avatarCache;
        }

        /// <summary>
        /// Register the avatar endpoint. It does not need the UI thread (pure data), and uses
        /// requiresSync:false so a slow avatar download never holds the global API lock.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterAsyncEndpointHandler(
                "avatar",
                HandleAvatar,
                handleOnUiThread: false,
                requiresSync: false
            );
        }

        /// <summary>
        /// GET /bloom/api/avatar?email=&lt;email&gt; → the avatar image bytes for that email, or 404
        /// when we have nothing (which lets react-avatar fall back to generated initials). The server
        /// normalizes + hashes the email itself, so the front end only has to pass the email.
        /// </summary>
        private async Task HandleAvatar(ApiRequest request)
        {
            var email = request.GetParamOrNull("email");
            if (string.IsNullOrEmpty(email))
            {
                request.Failed(HttpStatusCode.NotFound, "no email supplied");
                return;
            }

            var md5 = AvatarCache.Md5OfEmail(email);
            var result = await _avatarCache.GetAvatarBytesAsync(md5);
            if (result == null)
            {
                // Nothing available (no known photo, no Gravatar, or offline). 404 tells the front end
                // to show initials.
                request.Failed(HttpStatusCode.NotFound, "no avatar");
                return;
            }

            request.ReplyWithStreamContent(
                new MemoryStream(result.Bytes),
                result.ContentType,
                result.Bytes.Length
            );
        }
    }
}
