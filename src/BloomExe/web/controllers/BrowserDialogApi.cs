using Bloom.Api;
using Bloom.MiscUI;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
	class BrowserDialogApi
	{
		// Important note regarding usage!
		// Consumers that read LastCloseSource should make sure to reset LastCloseSource prior to showing the BrowserDialog.
		// If the user closes the dialog using the WinForms X close button, this class's dialog/close handler will not be invoked.
		static internal string LastCloseSource { get; set; }

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("dialog/close",
				(ApiRequest request) =>
				{
					// Closes the current dialog.
					// Optionally, the caller may provide (in JSON) an object with a "source" field with a string value.  This "source" represents the button/etc that initiated the close action.

					LastCloseSource = null;	// First reset the source, in case of any parsing errors
					var postData = ParseCloseData(request);
					LastCloseSource = postData.source;

					BrowserDialog.CloseDialog();
					request.PostSucceeded();
				}, true);
		}

		// Retrieves and parses the POST data from the close request. 
		private CloseRequestData ParseCloseData(ApiRequest request)
		{
			string json = request.GetPostJsonOrNull();
			return string.IsNullOrEmpty(json)
				? new CloseRequestData()
				: JsonConvert.DeserializeObject<CloseRequestData>(json);
		}

		private class CloseRequestData
		{
			public string source;
		}
	}
}
