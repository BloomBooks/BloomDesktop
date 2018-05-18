using Bloom.Collection;
using Bloom.Properties;

namespace Bloom.Api
{
	/// <summary>
	/// Provide the web code access to feature control variables.
	/// </summary>
	class FeatureControlApi
	{
		public const string kShowAdvancedFeaturesUrlPart = "featurecontrol/showAdvancedFeatures";
		// I think this will be needed for BL-5862.
		private readonly CollectionSettings _collectionSettings;

		public FeatureControlApi(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kShowAdvancedFeaturesUrlPart, request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText(Settings.Default.ShowExperimentalFeatures.ToString().ToLowerInvariant());
				}
				else // post
				{
					System.Diagnostics.Debug.Fail("We shouldn't ever be using the 'post' version.");
					request.PostSucceeded();
				}
			}, false);

		}
	}
}
