using Bloom.Api;
using Bloom.MiscUI;

namespace Bloom.web.controllers
{
	class BrowserDialogApi
	{
		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("dialog/close",
				(ApiRequest request) =>
				{
					BrowserDialog.CurrentDialog?.Close();
					request.PostSucceeded();
				}, true);
		}
	}
}
