using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Used by the settings dialog and various places that need to know
    /// if our subscription status.
    /// </summary>
    public class FeatureStatusApi
    {
        public const string kApiUrlPart = "features/";

        private readonly CollectionSettings _collectionSettings;
        private BookSelection _bookSelection;

        /// <summary>
        /// The collection's current subscription. Read through to CollectionSettings on every use
        /// rather than captured at construction: whatever replaces the collection's Subscription
        /// object (entering a code in the Settings dialog, or the e2e/setBranding test hook) would
        /// otherwise leave this api answering from the subscription Bloom started with.
        /// </summary>
        private Subscription Subscription => _collectionSettings.Subscription;

        public FeatureStatusApi(CollectionSettings collectionSettings, BookSelection bookSelection)
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Combined endpoint that returns all subscription data
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "status",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        var featureName = request.RequiredParam("featureName");
                        var forPublishing = request.GetParamOrNull("forPublishing") == "true";
                        var featureStatus = FeatureStatus.GetFeatureStatus(
                            Subscription,
                            featureName,
                            _bookSelection.CurrentSelection,
                            forPublishing
                        );

                        request.ReplyWithJson(featureStatus.ToJson());
                    }
                    else
                    {
                        request.Failed(
                            "Only GET method is supported for the features/status endpoint"
                        );
                    }
                },
                false,
                requiresSync: false
            );
        }
    }
}
