using System;
using System.Globalization;
using System.Text;
using Bloom.Api;
using Bloom.Collection;
using SIL.IO;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Used by the settings dialog (currently just the EnterpriseSettings tab) and various places that need to know
    /// if Enterprise is enabled or not.
    /// </summary>
    public class SubscriptionSettingsEditorApi
    {
        public const string kApiUrlPart = "settings/";

        private readonly CollectionSettings _collectionSettings;
        private Subscription _subscription;

        public SubscriptionSettingsEditorApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
            _subscription = collectionSettings.Subscription;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Combined endpoint that returns all subscription data
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "Subscription",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        var subscriptionData = new
                        {
                            Code = _subscription.Code ?? "",
                            Tier = _subscription.Tier.ToString(),
                            Summary = GetSummaryHtml(_subscription.Descriptor),
                            Expiration = _subscription.ExpirationDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture
                            ),
                            CodeIntegrity = _subscription.GetIntegrityLabel(),
                            BrandingProjectName = _subscription.Descriptor,
                            HaveBrandingFiles = string.IsNullOrWhiteSpace(_subscription.Descriptor)
                                // We have them in the sense that we have the default logo we show on the back, so "true"
                                ? true
                                : BrandingProject.HaveFilesForBranding(
                                    _subscription.BrandingProjectKey
                                ),
                            EditingBlorgBook = _subscription.EditingBlorgBook,
                        };

                        request.ReplyWithJson(JsonConvert.SerializeObject(subscriptionData));
                    }
                    else
                    {
                        request.Failed(
                            "Only GET method is supported for the Subscription endpoint"
                        );
                    }
                },
                false
            );

            apiHandler.RegisterEnumEndpointHandler(
                kApiUrlPart + "subscriptionTier",
                request => _subscription.Tier,
                null,
                false
            );

            // Existing endpoints kept for backward compatibility
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscriptionCode",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithText(_subscription?.Code ?? "");
                    }
                    else // post
                    {
                        var codeString = request.RequiredPostString();
                        _subscription = new Subscription(codeString);
                        NotifyPendingSubscriptionChange?.Invoke(codeString);
                        request.PostSucceeded();
                    }
                },
                false
            );
        }

        private static string GetSummaryHtml(string descriptor)
        {
            BrandingSettings.ParseSubscriptionDescriptor(
                descriptor,
                out var baseKey,
                out var flavor,
                out var subUnitName
            );
            var summaryFile = BloomFileLocator.GetOptionalBrandingFile(baseKey, "summary.htm");
            if (summaryFile == null)
                return "";

            var html = RobustFile.ReadAllText(summaryFile, Encoding.UTF8);
            return html.Replace("{flavor}", flavor).Replace("SUBUNIT", subUnitName);
        }

        public static Action<string> NotifyPendingSubscriptionChange;
    }
}
