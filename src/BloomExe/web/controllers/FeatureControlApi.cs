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
		public const string kEnterpriseEnabledUrlPart = "featurecontrol/enterpriseEnabled";

		// I think this will be needed for BL-5862.
		private readonly CollectionSettings _collectionSettings;

		public FeatureControlApi(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
		}

		private bool IsEnterpriseEnabled
		{
			get { return _collectionSettings.HaveEnterpriseFeatures; }
		}

		private static void NoPostAllowed(ApiRequest request)
		{
			System.Diagnostics.Debug.Fail("We shouldn't ever be using the 'post' version.");
			request.PostSucceeded();
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kShowAdvancedFeaturesUrlPart, request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText(Settings.Default.ShowExperimentalFeatures.ToString().ToLowerInvariant());
				}
				else // post
				{
					NoPostAllowed(request);
				}
			}, false);
			apiHandler.RegisterEndpointHandler(kEnterpriseEnabledUrlPart, request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithBoolean(IsEnterpriseEnabled);
				}
				else // post
				{
					NoPostAllowed(request);
				}
			}, true);
		}
	}
}
