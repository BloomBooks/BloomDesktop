using System;
using System.Collections.Generic;
using Bloom.Api;
using Bloom.Book;
using L10NSharp;

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
			server.RegisterEndpointHandler("book/controlsTranslation", HandleTranslationRequestForOtherControls, false);
		}

		private static void HandleTranslationRequestForOtherControls(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					var translatedStringPairs = new
					{
						flashingHazard = GetTranslation("flashingHazard", "Flashing Hazard"),
						motionSimulationHazard = GetTranslation("motionSimulationHazard", "Motion Simulation Hazard"),
						soundHazard = GetTranslation("soundHazard", "Sound Hazard"),
						alternativeText = GetTranslation("alternativeText", "Alternative Text"),
						signLanguage = GetTranslation("signLanguage", "Sign Language"),
					};
					request.ReplyWithJson(translatedStringPairs);
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
						metapicture =  new {type="image", value = "/bloom/"+_bookSelection.CurrentSelection.GetCoverImagePath(),
							translatedLabel = GetTranslation("metapicture", "Picture")},
						name = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.TitleBestForUserDisplay,
							translatedLabel = GetTranslation("name", "Name") },
						numberOfPages = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLastNumberedPageNumber().ToString(),
							translatedLabel = GetTranslation("numberOfPages", "Number of pages") },
						inLanguage =  new { type = "readOnlyText", value = _bookSelection.CurrentSelection.CollectionSettings.Language1Iso639Code,
							translatedLabel = GetTranslation("inLanguage", "Language") },
						License = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLicenseMetadata().License.Url,
							translatedLabel = GetTranslation("License", "License") },
						author = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Author,
							translatedLabel = GetTranslation("author", "Author") },
						typicalAgeRange = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange,
							translatedLabel = GetTranslation("typicalAgeRange", "Typical age range") },
						level = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription,
							translatedLabel = GetTranslation("level", "Reading level") },
						subjects = new { type = "subjects", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Subjects,
							translatedLabel = GetTranslation("subjects", "Subjects") },
						hazards = new {type = "hazards", value = ""+_bookSelection.CurrentSelection.BookInfo.MetaData.Hazards,
							translatedLabel = GetTranslation("hazards", "Hazards") },
						a11yFeatures = new { type = "a11yFeatures", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures,
							translatedLabel = GetTranslation("a11yFeatures", "Accessibility features") }
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

		private static string GetTranslation(string key, string english)
		{
			return LocalizationManager.GetString("BookMetadata." + key, english);
		}
	}
}
