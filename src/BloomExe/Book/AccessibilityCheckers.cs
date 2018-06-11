using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Xml;
using Bloom.Book;
using Bloom.Publish;
using Glob;
using SIL.Extensions;
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
			var messageTemplate = L10NSharp.LocalizationManager.GetString("Accessibility.DescriptionsForAllImages.Missing",
				"Missing image description on page {0}",
				"The {0} is where the page number will be inserted.");

			// Note in BL-6089 we may decide to except placeholder.png from these complaints, if
			// if we are going to trim them out of epub and bloom reader publishing.

			// Note that we intentionally are not dealing with unusual hypothetical situations like where
			// someone might want the language of the description to be something other than language1.
			foreach (XmlElement imageContainer in book.OurHtmlDom.SafeSelectNodes(
				"//div[contains(@class, 'bloom-imageContainer')]"))
			{
				var descriptionElementInTheRightLanguage = imageContainer.SelectSingleNode(
						$@"./div[contains(@class,'bloom-imageDescription')]
													/div[contains(@class,'bloom-editable')
													and @lang='{book.CollectionSettings.Language1Iso639Code}']")
					as XmlElement;
				if (descriptionElementInTheRightLanguage == null ||
				    (descriptionElementInTheRightLanguage.InnerText.Trim().Length == 0))
				{
					var page = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(imageContainer);

					yield return string.Format(messageTemplate, page);
				}
			}
		}


		/// <summary>
		/// Return an error for every image we find that is missing audio on the description.
		/// </summary>
		/// <returns>returns an enumerator of strings describing any problems it finds</returns>
		public static IEnumerable<string> CheckAudioForAllImageDescriptions(Book.Book book)
		{
			yield break;
		}

		/// <summary>
		/// Return an error for every textbox that is missing some text
		/// </summary>
		/// <returns>returns an enumerator of strings describing any problems it finds</returns>
		public static IEnumerable<string> CheckAudioForAllText(Book.Book book)
		{
			var messageTemplate = L10NSharp.LocalizationManager.GetString("Accessibility.AudioForAllText.MissingOnPage",
				"Some text is missing a recording on page {0}",
				"The {0} is where the page number will be inserted.");


			var audioFolderPath = AudioProcessor.GetAudioFolderPath(book.FolderPath);
			// I don't even know if this can happen, but just in case
			if (!Directory.Exists(audioFolderPath))
			{
				yield return String.Format(messageTemplate, "All");
				yield break;
			}

			var audioFolderInfo = new DirectoryInfo(audioFolderPath);
			foreach (XmlElement page in  book.OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				if (PageHasMissingAudio(book, page, audioFolderInfo))
				{
					var pageLabel = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(page);
					yield return String.Format(messageTemplate, pageLabel);
				}
			}
		}

		private static bool PageHasMissingAudio(Book.Book book, XmlElement page, DirectoryInfo audioFolderInfo)
		{
			var elementsInTheRightLanguage = page.SelectNodes(
					$"//div[contains(@class, 'bloom-editable') and @lang='{book.CollectionSettings.Language1Iso639Code}']")
				.Cast<XmlElement>();

			return elementsInTheRightLanguage
				.Any(editable => ElementContainsMissingAudio(editable, audioFolderInfo));
		}

		private static bool ElementContainsMissingAudio(XmlElement element, DirectoryInfo audioFolderInfo)
		{
			foreach (XmlNode child in element.ChildNodes)
			{
				var childElement = child as XmlElement;
				switch (child.NodeType)
				{
					case XmlNodeType.Text:
						// we found some text that was not wrapped in an span.audio-sentence
						// return true if it isn't just whitespace
						if (!String.IsNullOrWhiteSpace(child.InnerText))
							return true;
						// else go on to the sibling of this child
						break;
					case XmlNodeType.Element:
						
						if (childElement.Name == "span" && childElement.GetAttribute("class").Contains("audio-sentence"))
						{
							var id = childElement.GetAttribute("id");
							//Whatever the audio extension, here we assume other parts of Bloom are taking care of that,
							// and just want to see some file with a base name that matches the id
							if (!audioFolderInfo.GlobFiles(id + ".*").Any())
								return true;
							// else go on to the sibling of this child
						}
						else if (child.HasChildNodes)
						{
							if (ElementContainsMissingAudio(childElement, audioFolderInfo)) // recurse down the tree
								return true;
							// else go on to the sibling of this child
						}
						break;
					default:
						break;
				}
			}
			return false;
		}
	}
}
