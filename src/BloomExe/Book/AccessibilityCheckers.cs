using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom.Book;
using SIL.Xml;

namespace Bloom.web.controllers
{
	/// <summary>
	/// A set of independent checks for various attributes of a book related to accessibility.
	/// </summary>
	public class AccessibilityCheckers
	{
		/// <summary>
		/// Return an error for every image we find that is missing a description.
		/// </summary>
		/// <returns>returns an enumerator of strings describing any problems it finds</returns>
		public static IEnumerable<string> CheckDescriptionsForAllImages(Book.Book book)
		{
			// Note in BL-6089 we may decide to except placeholder.png from these complaints, if
			// if we are going to trim them out of epub and bloom reader publishing.

			// Note that we intentionally are not dealing with unusual hypothetical situations like where
			// someone might want the language of the description to be something other than language1.
			foreach (XmlElement imageContainer in book.OurHtmlDom.SafeSelectNodes("//div[contains(@class, 'bloom-imageContainer')]"))
			{
				var descriptionElementInTheRightLanguage = imageContainer.SelectSingleNode($@"./div[contains(@class,'bloom-imageDescription')]
													/div[contains(@class,'bloom-editable')
													and @lang='{book.CollectionSettings.Language1Iso639Code}']")
													as XmlElement;
				if (descriptionElementInTheRightLanguage == null || (descriptionElementInTheRightLanguage.InnerText.Trim().Length == 0))
				{
					var page = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(imageContainer);
					var s = L10NSharp.LocalizationManager.GetString("Accessibility.DescriptionsForAllImages.Missing", "Missing image description on page {0}",
						"The {0} is where the page number will be inserted.");
					yield return string.Format(s, page);
				}
			}
		}
	}
}
