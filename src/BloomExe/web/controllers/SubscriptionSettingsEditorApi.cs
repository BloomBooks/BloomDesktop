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
                        var pending = CollectionSettingsApi.PendingSettings;
                        if (pending != null)
                            RecordPendingSubscription(pending, _collectionSettings, _subscription);
                        request.PostSucceeded();
                    }
                },
                false
            );
        }

        /// <summary>
        /// Records the code the user has typed as the session's pending subscription. Only a code
        /// that differs from the collection's own counts as a change (see the Subscription field of
        /// PendingCollectionSettings); going back to the saved code withdraws an earlier edit, or
        /// OK would save a code the user had already undone.
        /// </summary>
        internal static void RecordPendingSubscription(
            PendingCollectionSettings pending,
            CollectionSettings collectionSettings,
            Subscription subscription
        )
        {
            // A pending subscription is itself a reason to restart (see RestartRequired), so
            // setting or clearing it is all it takes; the notice just refreshes the reminder.
            pending.Subscription = collectionSettings.Subscription.IsDifferent(subscription.Code)
                ? subscription
                : null;
            pending.RestartRequiredChanged?.Invoke();
        }
    }
}
