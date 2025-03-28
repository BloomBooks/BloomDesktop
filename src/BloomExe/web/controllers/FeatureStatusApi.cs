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
    /// Used by the settings dialog and various places that need to know
    /// if our subscription status.
    /// </summary>
    public class FeatureStatusApi
    {
        public const string kApiUrlPart = "features/";

        private readonly CollectionSettings _collectionSettings;
        private Subscription _subscription;

        public FeatureStatusApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
            _subscription = collectionSettings.Subscription;
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
                        var featureName = request.RequiredParam("feature");
                        var featureStatus = FeatureRegistry.Features.Find(
                            f =>
                                f.Feature
                                    .ToString()
                                    .Equals(featureName, StringComparison.OrdinalIgnoreCase)
                        );
                        if (featureStatus == null)
                        {
                            request.Failed($"Feature '{featureName}' not found.");
                            return;
                        }

                        request.ReplyWithJson(JsonConvert.SerializeObject(featureStatus));
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
