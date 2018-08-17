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
			server.RegisterEndpointHandler("book/controlsEnglish", HandleEnglishRequestForOtherControls, false);
		}

		private static void HandleEnglishRequestForOtherControls(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					var englishStrings = new
					{
						flashingHazard = "Flashing Hazard",
						motionSimulationHazard = "Motion Simulation Hazard",
						soundHazard = "Sound Hazard",
						alternativeText = "Alternative Text",
						signLanguage = "Sign Language",
					};
					request.ReplyWithJson(englishStrings);
					break;
				case HttpMethods.Post:
					throw new ArgumentException("Post method not implemented.");
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void HandleBookMetadata(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					// The spec is here: https://docs.google.com/document/d/e/2PACX-1vREQ7fUXgSE7lGMl9OJkneddkWffO4sDnMG5Vn-IleK35fJSFqnC-6ulK1Ss3eoETCHeLn0wPvcxJOf/pub
					var metadata = new
					{
						metapicture=  new {type="image", value = "/bloom/"+_bookSelection.CurrentSelection.GetCoverImagePath(), english = "Picture"},
						name= new { type = "readOnlyText", value = _bookSelection.CurrentSelection.TitleBestForUserDisplay, english = "Name" },
						numberOfPages = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLastNumberedPageNumber().ToString(), english = "Number of pages" },
						inLanguage =  new { type = "readOnlyText", value = _bookSelection.CurrentSelection.CollectionSettings.Language1Iso639Code, english = "Language" },
						License = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLicenseMetadata().License.Url, english = "License" },
						author = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Author, english = "Author" },
						typicalAgeRange = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange, english = "Typical age range"},
						level = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription, english = "Reading level" },
						subjects = new { type = "subjects", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Subjects, english = "Subjects" },
						hazards = new {type = "hazards", value = ""+_bookSelection.CurrentSelection.BookInfo.MetaData.Hazards, english = "Hazards" },
						a11yFeatures = new { type = "a11yFeatures", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures, english = "Accessibility features" }
					};
					request.ReplyWithJson(metadata);
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
