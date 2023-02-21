using Bloom.Api;
using Bloom.Publish.BloomLibrary;

namespace Bloom.web.controllers
{
	/// <summary>
	/// APIs related to the Library (Web) Publish screen.
	/// </summary>
	class LibraryPublishApi
	{
		public static BloomLibraryPublishModel Model { get; set; }

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("libraryPublish/getBookInfo", HandleGetBookInfo, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/setSummary", HandleSetSummary, true);
		}

		private void HandleGetBookInfo(ApiRequest request)
		{
			dynamic bookInfo = new
			{
				title = Model.Title,
				summary = Model.Summary,
				copyright = Model.Copyright,
				licenseType = Model.LicenseType.ToString(),
				licenseToken = Model.LicenseToken,
				licenseRights = Model.LicenseRights
			};
			request.ReplyWithJson(bookInfo);
		}

		private void HandleSetSummary(ApiRequest request)
		{
			Model.Summary = request.GetPostStringOrNull();
			request.PostSucceeded();
		}
	}
}
