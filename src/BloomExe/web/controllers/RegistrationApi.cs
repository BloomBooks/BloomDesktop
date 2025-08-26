using Bloom.Api;
using Bloom.Registration;

namespace Bloom.web.controllers
{
    /// <summary>
    /// API functions for managing user registration in Bloom's HTML UI.
    /// </summary>
    public class RegistrationApi
    {
        public const string kApiUrlPart = "registration/";

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "userInfo", HandleUserInfo, false);
        }

        private void HandleUserInfo(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
            {
                var info = RegistrationManager.GetAnalyticsUserInfo();
                info.OtherProperties.TryGetValue("Organization", out string organization);
                info.OtherProperties.TryGetValue("HowUsing", out string usingFor);

                request.ReplyWithJson(
                    new
                    {
                        firstName = info.FirstName,
                        surname = info.LastName,
                        email = info.Email,
                        organization,
                        usingFor,
                        hadEmailAlready = !string.IsNullOrWhiteSpace(info.Email),
                    }
                );
            }
            else // post
            {
                var info = DynamicJson.Parse(request.RequiredPostJson());
                RegistrationManager.SaveAndSendIfPossible(
                    info.firstName,
                    info.surname,
                    info.email,
                    info.organization,
                    info.usingFor,
                    info.hadEmailAlready
                );

                request.PostSucceeded();
            }
        }
    }
}
