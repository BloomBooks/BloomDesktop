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
					var metadata = new
					{
						metapicture=  new {type="image", value = "/bloom/"+_bookSelection.CurrentSelection.GetCoverImagePath()},
						name= new { type = "readOnlyText", value = _bookSelection.CurrentSelection.TitleBestForUserDisplay },
						numberOfPages = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLastNumberedPageNumber().ToString() },
						inLanguage =  new { type = "readOnlyText", value = _bookSelection.CurrentSelection.CollectionSettings.Language1Iso639Code },
						License = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLicenseMetadata().License.Url },
						author = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Author },
						typicalAgeRange = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange},
						level = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription },
						subjects = new { type = "subjects", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Subjects },
						hazards = new {type = "hazards", value = ""+_bookSelection.CurrentSelection.BookInfo.MetaData.Hazards },
						a11yFeatures = new { type = "a11yFeatures", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures }
					};
					request.ReplyWithJson((object)metadata);
					break;
				case HttpMethods.Post:
					var json = request.RequiredPostJson();
					var settings = DynamicJson.Parse(json);
					_bookSelection.CurrentSelection.BookInfo.MetaData.Author = settings["author"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange = settings["typicalAgeRange"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription = settings["level"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.Subjects = settings["subjects"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.Hazards = settings["hazards"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures = settings["a11yFeatures"].value.Trim();
					_bookSelection.CurrentSelection.Save();
					request.PostSucceeded();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}
}
