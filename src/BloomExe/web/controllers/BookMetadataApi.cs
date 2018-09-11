using System;
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

		public BookMetadataApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			bool requiresSync = false; // Lets us open the dialog while the epub preview is being generated
			apiHandler.RegisterEndpointHandler("book/metadata", HandleBookMetadata, false, requiresSync);
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
							translatedLabel = LocalizationManager.GetString("BookMetadata.metapicture", "Picture")},
						name = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.TitleBestForUserDisplay,
							translatedLabel = LocalizationManager.GetString("BookMetadata.name", "Name") },
						numberOfPages = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLastNumberedPageNumber().ToString(),
							translatedLabel = LocalizationManager.GetString("BookMetadata.numberOfPages", "Number of pages") },
						inLanguage =  new { type = "readOnlyText", value = _bookSelection.CurrentSelection.CollectionSettings.Language1Iso639Code,
							translatedLabel = LocalizationManager.GetString("BookMetadata.inLanguage", "Language") },
						License = new { type = "readOnlyText", value = _bookSelection.CurrentSelection.GetLicenseMetadata().License.Url,
							translatedLabel = LocalizationManager.GetString("BookMetadata.License", "License") },
						author = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Author,
							translatedLabel = LocalizationManager.GetString("BookMetadata.author", "Author") },
						typicalAgeRange = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange,
							translatedLabel = LocalizationManager.GetString("BookMetadata.typicalAgeRange", "Typical age range") },
						level = new { type = "editableText", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription,
							translatedLabel = LocalizationManager.GetString("BookMetadata.level", "Reading level") },
						subjects = new { type = "subjects", value = _bookSelection.CurrentSelection.BookInfo.MetaData.Subjects,
							translatedLabel = LocalizationManager.GetString("BookMetadata.subjects", "Subjects") },
						hazards = new {type = "hazards", value = ""+_bookSelection.CurrentSelection.BookInfo.MetaData.Hazards,
							translatedLabel = LocalizationManager.GetString("BookMetadata.hazards", "Hazards") },
						a11yFeatures = new { type = "a11yFeatures", value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures,
							translatedLabel = LocalizationManager.GetString("BookMetadata.a11yFeatures", "Accessibility features") }
					};
					var translatedStringPairs = new
					{
						flashingHazard = LocalizationManager.GetString("BookMetadata.flashingHazard", "Flashing Hazard"),
						motionSimulationHazard = LocalizationManager.GetString("BookMetadata.motionSimulationHazard", "Motion Simulation Hazard"),
						soundHazard = LocalizationManager.GetString("BookMetadata.soundHazard", "Sound Hazard"),
						alternativeText = LocalizationManager.GetString("BookMetadata.alternativeText", "Alternative Text"),
						signLanguage = LocalizationManager.GetString("BookMetadata.signLanguage", "Sign Language"),
					};
					var blob = new
					{
						metadata,
						translatedStringPairs,
					};
					request.ReplyWithJson(blob);
					break;
				case HttpMethods.Post:
					var json = request.RequiredPostJson();
					var settings = DynamicJson.Parse(json);
					_bookSelection.CurrentSelection.BookInfo.MetaData.Author = settings["author"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange = settings["typicalAgeRange"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription = settings["level"].value.Trim();
					_bookSelection.CurrentSelection.BookInfo.MetaData.Subjects = settings["subjects"].value;
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
