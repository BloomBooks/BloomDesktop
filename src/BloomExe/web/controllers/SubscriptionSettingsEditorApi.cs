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

            CollectionSettingsDialog.DialogCancelled += (sender, e) =>
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
                        // The React Collection Settings dialog keeps the code being typed itself
                        // and asks about it with ?code=; it is weighed against the code the
                        // collection has saved. Without it, this describes the WinForms dialog's
                        // pending code.
                        var code = request.GetParamOrNull("code");
                        var subscription =
                            code == null
                                ? _subscription
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

        public static Action<string> NotifyPendingSubscriptionChange;
    }
}
