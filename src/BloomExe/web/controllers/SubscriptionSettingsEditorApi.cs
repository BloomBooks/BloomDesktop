using System;
using System.Globalization;
using System.Text;
using Bloom.Api;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Used by the settings dialog and various places that need to know
    /// if our subscription status.
    /// </summary>
    public class SubscriptionSettingsEditorApi
    {
        public const string kApiUrlPart = "settings/";

        private readonly CollectionSettings _collectionSettings;

        public SubscriptionSettingsEditorApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Combined endpoint that returns all subscription data
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscription",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        // This describes the collection's subscription, or, given ?code=, the one
                        // that code stands for (a Settings dialog asks about the code being typed).
                        var code = request.GetParamOrNull("code");
                        var subscription =
                            code == null
                                ? _collectionSettings.Subscription
                                : CollectionSettingsUpdater.GetChangedSubscription(
                                    _collectionSettings,
                                    code
                                ) ?? _collectionSettings.Subscription;
                        var subscriptionData = new
                        {
                            Code = subscription.Code ?? "",
                            Tier = subscription.Tier.ToString(),
                            Summary = BrandingSettings.GetSummaryHtml(subscription.Descriptor),
                            Expiration = subscription.ExpirationDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture
                            ),
                            CodeIntegrity = subscription.GetIntegrityLabel(),
                            SubscriptionDescriptor = subscription.Descriptor,
                            MissingBrandingFiles = (
                                subscription.Tier == SubscriptionTier.Enterprise
                                && !BrandingProject.HaveFilesForBranding(subscription.BrandingKey)
                            ),
                            EditingBlorgBook = subscription.EditingBlorgBook,
                        };

                        request.ReplyWithJson(JsonConvert.SerializeObject(subscriptionData));
                    }
                    else
                    {
                        request.Failed(
                            "Only GET method is supported for the 'subscription' endpoint"
                        );
                    }
                },
                false
            );

            apiHandler.RegisterEnumEndpointHandler(
                kApiUrlPart + "subscriptionTier",
                request => _collectionSettings.Subscription.Tier,
                null,
                false
            );
        }
    }
}
