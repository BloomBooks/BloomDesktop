using System;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Local auth provider (`BLOOM_CLOUDTC_AUTH_MODE=local`): signs in against the local GoTrue
    /// instance bundled with the local Supabase stack (the on-this-machine emulation, NOT the
    /// hosted dev/sandbox test cloud). Any email/password is accepted — an unrecognized email is
    /// signed up (auto-confirmed, per server/dev/config.auth.toml.snippet) then signed in — which
    /// is what makes ad-hoc local test identities possible with no seed change. Deliberately tiny
    /// and self-contained: it can be deleted without touching CloudAuth's session core once a
    /// real provider exists.
    /// </summary>
    public class LocalCloudAuthProvider : ICloudAuthProvider
    {
        private readonly CloudEnvironment _environment;
        private RestClient _restClient;

        public LocalCloudAuthProvider(CloudEnvironment environment)
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

            // Unknown email: sign up (auto-confirmed by the local stack's
            // enable_confirmations=false), then the signup response itself is a valid session.
            var signUpResponse = PostAuth("signup", email, password);
            if (signUpResponse.StatusCode == HttpStatusCode.OK)
                return ToSession(signUpResponse);

            throw new CloudAuthException(
                $"Local-mode sign-in failed for {email}: sign-in gave {(int)signInResponse.StatusCode} "
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
                    $"Local-mode token refresh failed: {(int)response.StatusCode} ({response.Content})"
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
                    "Local-mode auth response was not valid JSON: " + response.Content,
                    e
                );
            }

            var accessToken = (string)json["access_token"];
            var refreshToken = (string)json["refresh_token"];
            var expiresIn = (int?)json["expires_in"] ?? 3600;
            var user = json["user"];
            if (string.IsNullOrEmpty(accessToken) || user == null)
                throw new CloudAuthException(
                    "Local-mode auth response was missing access_token/user: " + response.Content
                );

            return new CloudSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                Email = (string)user["email"],
                UserId = (string)user["id"],
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn),
                // The local stack auto-confirms every signup (server/dev/config.auth.toml.snippet),
                // so every local-mode session is, by definition, verified.
                EmailVerified = true,
            };
        }
    }
}
