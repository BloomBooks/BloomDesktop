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
        private Subscription _subscription;

        public SubscriptionSettingsEditorApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
            _subscription = collectionSettings.Subscription;

            CollectionSettingsApi.EditingCancelled += (sender, e) =>
            {
                _subscription = collectionSettings.Subscription;
            };
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
                        var subscriptionData = new
                        {
                            Code = _subscription.Code ?? "",
                            Tier = _subscription.Tier.ToString(),
                            Summary = BrandingSettings.GetSummaryHtml(_subscription.Descriptor),
                            Expiration = _subscription.ExpirationDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture
                            ),
                            CodeIntegrity = _subscription.GetIntegrityLabel(),
                            SubscriptionDescriptor = _subscription.Descriptor,
                            MissingBrandingFiles = (
                                _subscription.Tier == SubscriptionTier.Enterprise
                                && !BrandingProject.HaveFilesForBranding(_subscription.BrandingKey)
                            ),
                            EditingBlorgBook = _subscription.EditingBlorgBook,
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
                        // Only a code that differs from the collection's own counts as a pending
                        // change; see the Subscription field of PendingCollectionSettings.
                        var pending = CollectionSettingsApi.PendingSettings;
                        if (
                            pending != null
                            && _collectionSettings.Subscription.IsDifferent(codeString)
                        )
                        {
                            pending.Subscription = _subscription;
                            pending.ChangeThatRequiresRestart();
                        }
                        request.PostSucceeded();
                    }
                },
                false
            );
        }
    }
}
