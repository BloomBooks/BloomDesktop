using Bloom.Api;
using Bloom.Properties;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Used by the Software Updates dialog.
	/// Intended for React components to deal with any system-wide Bloom settings.
	/// </summary>
	public class InstallationSettingsApi
	{
		public const string kApiUrlPart = "system/";

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "autoUpdateValues", HandleAutoUpdate, false);
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
