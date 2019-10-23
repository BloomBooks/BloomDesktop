using Bloom.Api;
using Bloom.Edit;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Api for handling requests regarding the edit tab view itself
	/// </summary>
	public class EditingViewApi
	{
		public EditingView View { get; set; }

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("editView/setModalState", HandleSetModalState, true);
		}

		public void HandleSetModalState(ApiRequest request)
		{
			lock (request)
			{
				View.SetModalState(request.RequiredPostBooleanAsJson());
				request.PostSucceeded();
			}
		}
	}
}
