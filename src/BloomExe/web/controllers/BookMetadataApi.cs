using System;
using Bloom.Api;
using Bloom.Book;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Exposes values needed by the Book Metadata Dialog via API
	/// </summary>
	public class BookMetadataApi
	{
		private readonly BookSelection _bookSelection;

		public BookMetadataApi(BookSelection bookSelection, PageRefreshEvent pageRefreshEvent)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("book/metadata", HandleBookMetadata, false);
		}

		private void HandleBookMetadata(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					// The spec is here: https://docs.google.com/document/d/e/2PACX-1vREQ7fUXgSE7lGMl9OJkneddkWffO4sDnMG5Vn-IleK35fJSFqnC-6ulK1Ss3eoETCHeLn0wPvcxJOf/pub
					var metadata = new[]
					{
						// Note, the letter casing of these is inconsistent, but it matches the matches spec
						// In the future, we will likely provide new names to show in the UI... perhaps using the localization system.
						new {key = "metapicture", type="image", value = "/bloom/"+_bookSelection.CurrentSelection.GetCoverImagePath()},
						new {key = "Name", type="readOnlyText",value = _bookSelection.CurrentSelection.TitleBestForUserDisplay},
						new {key = "numberOfPages", type="readOnlyText",value = _bookSelection.CurrentSelection.getPageCount().ToString()},
						new {key = "inLanguage", type="readOnlyText",value = _bookSelection.CurrentSelection.CollectionSettings.Language1Iso639Code},
						new {key = "License", type="readOnlyText",value = _bookSelection.CurrentSelection.GetLicenseMetadata().License.Url}
					};
					request.ReplyWithJson((object)metadata);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}
}
