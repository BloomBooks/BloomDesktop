using Bloom.Collection;
using Bloom.Properties;
using Newtonsoft.Json;

namespace Bloom.Api
{
	/// <summary>
	/// Provide the web code access to various system-wide variables.
	/// </summary>
	class SystemControlApi
	{
		private const string kSystemUrlPrefix = "system/";

		// I think this will be needed for BL-5862.
		private readonly CollectionSettings _collectionSettings;

		public SystemControlApi(CollectionSettings collectionSettings)
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
			apiHandler.RegisterEndpointHandler(kSystemUrlPrefix + "showAdvancedFeatures", request =>
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
			apiHandler.RegisterEndpointHandler(kSystemUrlPrefix + "enterpriseEnabled", request =>
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
			apiHandler.RegisterEndpointHandler(kSystemUrlPrefix + "autoUpdateSoftwareChoice", HandleAutoUpdate, false);
		}

		public void HandleAutoUpdate(ApiRequest request)
		{
			if (request.HttpMethod == HttpMethods.Get)
			{
				var json = JsonConvert.SerializeObject(new
				{
					autoUpdate = Settings.Default.AutoUpdate,
					dialogShown = Settings.Default.AutoUpdateDialogShown
				});
				request.ReplyWithJson(json);
			}
			else // post
			{
				var requestData = DynamicJson.Parse(request.RequiredPostJson());
				Settings.Default.AutoUpdateDialogShown = (int)requestData.dialogShown;
				Settings.Default.AutoUpdate = (bool)requestData.autoUpdate;
				request.PostSucceeded();
			}
		}
	}
}
