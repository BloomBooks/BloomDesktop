using Bloom.Api;
using Bloom.Registration;
using Newtonsoft.Json;
using System;
using TagLib.Png;

namespace Bloom.web.controllers
{
    /// <summary>
    /// API functions common to various areas of Bloom's HTML UI.
    /// </summary>
    public class RegistrationApi
    {
        public const string kApiUrlPart = "registration/";

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "userInfo", HandleUserInfo, false);

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "tryToSave",
                request =>
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
                },
                false
            );
        }

        private void HandleUserInfo(ApiRequest request)
        {
            var info = RegistrationManager.GetAnalyticsUserInfo();
            var Organization = "";
            var HowUsing = "";
            info.OtherProperties.TryGetValue("Organization", out Organization);
            info.OtherProperties.TryGetValue("HowUsing", out HowUsing);

            var otherPropertiesJsonString =
                $"{{\"Organization\":\"{Organization}\",\"HowUsing\":\"{HowUsing}\"}}";
            var jsonString =
                $"{{\"FirstName\":\"{info.FirstName}\",\"LastName\":\"{info.LastName}\",\"Email\":\"{info.Email}\",\"OtherProperties\":{$"{{\"Organization\":\"{Organization}\",\"HowUsing\":\"{HowUsing}\"}}"},\"UILanguageCode\":\"{info.UILanguageCode}\"}}";

            request.ReplyWithJson(jsonString);
        }
    }
}
