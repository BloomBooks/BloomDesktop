using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Publish;
using Glob;

namespace Bloom.web.controllers
{
	/// <summary>
	/// A set of independent checks for various attributes of a book related to accessibility.
	/// </summary>
	public class AccessibilityCheckers
	{
		// this should match the IProblem in checkItem.tsx
		public struct Problem
		{
			public string message;
			public string problemText;
		}
		/// <summary>
		/// Return an error for every image we find that is missing a description.
		/// </summary>
		/// <returns>returns an enumerator of strings describing any problems it finds</returns>
		public static IEnumerable<Problem> CheckDescriptionsForAllImages(Book.Book book)
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
				var visibleElements = imageContainer.SelectSingleNode(
						$@"./div[contains(@class,'bloom-imageDescription')]
								/div[contains(@class,'bloom-editable')
								and @lang='{book.CollectionSettings.Language1Iso639Code}']")
					as XmlElement;
				if (visibleElements == null ||
				    (visibleElements.InnerText.Trim().Length == 0))
				{
					var page = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(imageContainer);

					yield return new Problem() {message=string.Format(messageTemplate, page)};
				}
			}
		}


		/// <summary>
		/// Return an error for every image we find that is missing audio on the description.
		/// </summary>
		/// <returns>returns an enumerator of strings describing any problems it finds</returns>
		public static IEnumerable<Problem> CheckAudioForAllImageDescriptions(Book.Book book)
		{
			var messageTemplate = L10NSharp.LocalizationManager.GetString("Accessibility.AudioForAllImageDescriptions.MissingOnPage",
				"Some text is missing a recording for an image description on page {0}",
				"The {0} is where the page number will be inserted.");

			foreach (var p in InnerCheckAudio(book, messageTemplate, "contains(@class, 'bloom-imageDescription')"))
				yield return p;
		}

		/// <summary>
		/// Return an error for every textbox that is missing some text
		/// </summary>
		/// <returns>returns an enumerator of strings describing any problems it finds</returns>
		public static IEnumerable<Problem> CheckAudioForAllText(Book.Book book)
		{
			var messageTemplate = L10NSharp.LocalizationManager.GetString("Accessibility.AudioForAllText.MissingOnPage",
				"Some text is missing a recording on page {0}",
				"The {0} is where the page number will be inserted.");

			foreach (var p in InnerCheckAudio(book, messageTemplate, "not(contains(@class, 'bloom-imageDescription'))"))
				yield return p;
		}

		private static IEnumerable<Problem> InnerCheckAudio(Book.Book book, string messageTemplate, string translationGroupConstraint)
		{
			var audioFolderPath = AudioProcessor.GetAudioFolderPath(book.FolderPath);

			var audioFolderInfo = new DirectoryInfo(audioFolderPath);
			foreach (XmlElement page in book.OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				var problemText = GetFirstTextOnPageWithMissingAudio(book, page, audioFolderInfo, translationGroupConstraint);
				if (!string.IsNullOrEmpty(problemText))
				{
					var pageLabel = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(page);
					var message = String.Format(messageTemplate, pageLabel);
					yield return new Problem() { message=message, problemText=problemText};
				}
			}
		}

		private static string GetFirstTextOnPageWithMissingAudio(Book.Book book, XmlElement page, DirectoryInfo audioFolderInfo,
			string translationGroupConstraint)
		{
			// NB: we're selecting for bloom-visibility-code-on instead of @lang
			var visibleElements = page.SelectNodes($".//div[contains(@class, 'bloom-translationGroup') " +
					"and not(contains(@class, 'bloom-recording-optional')) " + 
					$"and {translationGroupConstraint}]/div[contains(@class, 'bloom-editable') " +
					"and contains(@class, 'bloom-visibility-code-on')]")
					//$"and @lang='{book.CollectionSettings.Language1Iso639Code}']")
				.Cast<XmlElement>();

			foreach (var editable in visibleElements)
			{
				var problemText = ElementContainsMissingAudio(editable, audioFolderInfo);
				if (!string.IsNullOrEmpty(problemText))
					return problemText;
			}

			return null;
		}

		private static string ElementContainsMissingAudio(XmlElement element, DirectoryInfo audioFolderInfo)
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
							return child.InnerText;// +" (no audio span)";
						// else go on to the sibling of this child
						break;
					case XmlNodeType.Element:
						
						if (HtmlDom.DoesElementAllowAudioSentence(childElement.Name) && childElement.GetAttribute("class").Contains("audio-sentence"))
						{
							var id = childElement.GetAttribute("id");
							//Whatever the audio extension, here we assume other parts of Bloom are taking care of that,
							// and just want to see some file with a base name that matches the id.
							// Note: GlobFiles handles the case of the audioFolder being non-existant just fine.
							if (!Directory.Exists(audioFolderInfo.FullName) ||
								!audioFolderInfo.GlobFiles(id + ".*").Any())
								return childElement.InnerText;
							// else go on to the sibling of this child
						} else if (childElement.Name == "label")
						{
							// ignore
						}
						else if (child.HasChildNodes)
						{
							var problemText = ElementContainsMissingAudio(childElement, audioFolderInfo);
							if (!string.IsNullOrEmpty(problemText)) // recurse down the tree
								return problemText;
							// else go on to the sibling of this child
						}
						break;
					default:
						break;
				}
			}
			return null;
		}
	}
}
