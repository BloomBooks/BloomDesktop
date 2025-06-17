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
        private Subscription _subscription;
        private BookSelection _bookSelection;

        public FeatureStatusApi(CollectionSettings collectionSettings, BookSelection bookSelection)
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
            _subscription = collectionSettings.Subscription;
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
                            _subscription,
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
                false
            );
        }
    }
}
