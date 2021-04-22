using Bloom.Properties;
using Newtonsoft.Json;

namespace Bloom.Api
{
	/// <summary>
	/// Provide the web code access to various app-wide variables
	/// (i.e. wider than collection settings; related to this Bloom Desktop instance).
	/// </summary>
	public class AppApi
	{
		private const string kAppUrlPrefix = "app/";

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kAppUrlPrefix + "enabledExperimentalFeatures", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText(ExperimentalFeatures.TokensOfEnabledFeatures);
				}
				else // post
				{
					System.Diagnostics.Debug.Fail("We shouldn't ever be using the 'post' version.");
					request.PostSucceeded();
				}
			}, false);
			apiHandler.RegisterEndpointHandler(kAppUrlPrefix + "autoUpdateSoftwareChoice", HandleAutoUpdate, false);
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
