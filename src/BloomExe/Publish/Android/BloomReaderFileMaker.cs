using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Publish.Epub;
using Bloom.web;
using BloomTemp;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Publish.Android
{
	/// <summary>
	/// This class is the beginnings of a separate place to put code for creating .bloomd files.
	/// Much of the logic is still in BookCompressor. Eventually we might move more of it here,
	/// so that making a bloomd actually starts here and calls BookCompressor.
	/// </summary>
	public class BloomReaderFileMaker
	{
		public const string QuestionFileName = "questions.json";

		public static Control ControlForInvoke { get; set; }

		public static void CreateBloomReaderBook(string outputPath, Book.Book book, BookServer bookServer, Color backColor, IWebSocketProgress progress)
		{
			using (var temp = new TemporaryFolder("BloomReaderExport"))
			{
				var modifiedBook = PrepareBookForBloomReader(book, bookServer, temp, backColor, progress);
				// We want at least 256 for Bloom Reader, because the screens have a high pixel density. And (at the moment) we are asking for
				// 64dp in Bloom Reader.

				BookCompressor.MakeSizedThumbnail(modifiedBook, backColor, modifiedBook.FolderPath, 256);

				BookCompressor.CompressDirectory(outputPath, modifiedBook.FolderPath, "", reduceImages: true, omitMetaJson: false, wrapWithFolder: false,
					pathToFileForSha: BookStorage.FindBookHtmlInFolder(book.FolderPath));
			}
		}

		public static Book.Book PrepareBookForBloomReader(Book.Book book, BookServer bookServer, TemporaryFolder temp, Color backColor,
			IWebSocketProgress progress)
		{
			// MakeDeviceXmatterTempBook needs to be able to copy customCollectionStyles.css etc into parent of bookFolderPath
			// And bloom-player expects folder name to match html file name.
			var htmPath = BookStorage.FindBookHtmlInFolder(book.FolderPath);
			var bookFolderPath = Path.Combine(temp.FolderPath, Path.GetFileNameWithoutExtension(htmPath));
			Directory.CreateDirectory(bookFolderPath);
			var modifiedBook = PublishHelper.MakeDeviceXmatterTempBook(book, bookServer, bookFolderPath);

			var jsonPath = Path.Combine(bookFolderPath, QuestionFileName);
			var questionPages = modifiedBook.RawDom.SafeSelectNodes(
				"//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'questions')]");
			var questions = new List<QuestionGroup>();
			foreach (var page in questionPages.Cast<XmlElement>().ToArray())
			{
				ExtractQuestionGroups(page, questions);
				page.ParentNode.RemoveChild(page);
			}
			var builder = new StringBuilder("[");
			foreach (var question in questions)
			{
				if (builder.Length > 1)
					builder.Append(",\n");
				builder.Append(question.GetJson());

			}
			builder.Append("]");
			File.WriteAllText(jsonPath, builder.ToString());

			// Do this after making questions, as they satisfy the criteria for being 'blank'
			using (var helper = new PublishHelper())
			{
				helper.ControlForInvoke = ControlForInvoke;
				helper.RemoveUnwantedContent(modifiedBook.OurHtmlDom, modifiedBook);
			}
			modifiedBook.RemoveBlankPages();

			modifiedBook.SetAnimationDurationsFromAudioDurations();

			modifiedBook.OurHtmlDom.SetMedia("bloomReader");
			EmbedFonts(modifiedBook, progress, new FontFileFinder());

			var bookFile = BookStorage.FindBookHtmlInFolder(modifiedBook.FolderPath);
			StripImgIfWeCannotFindFile(modifiedBook.RawDom, bookFile);
			InsertReaderStylesheet(modifiedBook.RawDom);
			RobustFile.Copy(FileLocationUtilities.GetFileDistributedWithApplication(BloomFileLocator.BrowserRoot,"publish","android","readerStyles.css"),
				Path.Combine(bookFolderPath, "readerStyles.css"));
			ConvertImagesToBackground(modifiedBook.RawDom);

			modifiedBook.Save();

			return modifiedBook;
		}


		private static void StripImgIfWeCannotFindFile(XmlDocument dom, string bookFile)
		{
			var folderPath = Path.GetDirectoryName(bookFile);
			foreach (var imgElt in dom.SafeSelectNodes("//img[@src]").Cast<XmlElement>().ToArray())
			{
				var file = UrlPathString.CreateFromUrlEncodedString(imgElt.Attributes["src"].Value).NotEncoded.Split('?')[0];
				if (!File.Exists(Path.Combine(folderPath, file)))
				{
					imgElt.ParentNode.RemoveChild(imgElt);
				}
			}
		}

		/// <summary>
		/// Find every place in the html file where an img element is nested inside a div with class bloom-imageContainer.
		/// Convert the img into a background image of the image container div.
		/// Specifically, make the following changes:
		/// - Copy any data-x attributes from the img element to the div
		/// - Convert the src attribute of the img to style="background-image:url('...')" (with the same source) on the div
		///    (any pre-existing style attribute on the div is lost)
		/// - Add the class bloom-backgroundImage to the div
		/// - delete the img element
		/// (See oldImg and newImg in unit test CompressBookForDevice_ImgInImgContainer_ConvertedToBackground for an example).
		/// </summary>
		/// <param name="wholeBookHtml"></param>
		/// <returns></returns>
		private static void ConvertImagesToBackground(XmlDocument dom)
		{
			foreach (var imgContainer in dom.SafeSelectNodes("//div[contains(@class, 'bloom-imageContainer')]").Cast<XmlElement>().ToArray())
			{
				var img = imgContainer.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n is XmlElement && n.Name == "img");
				if (img == null || img.Attributes["src"] == null)
					continue;
				// The filename should be already urlencoded since src is a url.
				var src = img.Attributes["src"].Value;
				HtmlDom.SetImageElementUrl(new ElementProxy(imgContainer), UrlPathString.CreateFromUrlEncodedString(src));
				foreach (XmlAttribute attr in img.Attributes)
				{
					if (attr.Name.StartsWith("data-"))
						imgContainer.SetAttribute(attr.Name, attr.Value);
				}
				imgContainer.SetAttribute("class", imgContainer.Attributes["class"].Value + " bloom-backgroundImage");
				imgContainer.RemoveChild(img);
			}
		}

		private static void InsertReaderStylesheet(XmlDocument dom)
		{
			var link = dom.CreateElement("link");
			XmlUtils.GetOrCreateElement(dom, "html", "head").AppendChild(link);
			link.SetAttribute("rel", "stylesheet");
			link.SetAttribute("href", "readerStyles.css");
			link.SetAttribute("type", "text/css");
		}

		/// <summary>
		/// Given a book, typically one in a temporary folder made just for exporting (or testing),
		/// examine the CSS files and determine what fonts should be necessary. (Enhance: we could actually
		/// load the book into a DOM and find out what font IS used for each block.)
		/// Copy the font file for the normal style of that font family from the system font folder,
		/// if permitted; or post a warning in progress if we can't embed it.
		/// Create an extra css file (fonts.css) which tells the book to find the font files for those font families
		/// in the local folder, and insert a link to it into the book.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="progress"></param>
		/// <param name="fontFileFinder">use new FontFinder() for real, or a stub in testing</param>
		public static void EmbedFonts(Book.Book book, IWebSocketProgress progress, IFontFinder fontFileFinder)
		{
			const string defaultFont = "Andika New Basic"; // already in BR, don't need to embed or make rule.
			// The 'false' here says to ignore all but the first font face in CSS's ordered lists of desired font faces.
			// If someone is publishing an Epub, they should have that font showing. For one thing, this makes it easier
			// for us to not embed fonts we don't want/ need.For another, it makes it less likely that an epub will look
			// different or have glyph errors when shown on a machine that does have that primary font.
			var fontsWanted = EpubMaker.GetFontsUsed(book.FolderPath, false).ToList();
			fontsWanted.Remove(defaultFont);
			fontFileFinder.NoteFontsWeCantInstall = true;
			var filesToEmbed = new List<string>();
			foreach (var font in fontsWanted)
			{
				var fontFiles = fontFileFinder.GetFilesForFont(font);
				if (fontFiles.Count() > 0)
				{
					filesToEmbed.AddRange(fontFiles);
					progress.MessageWithParams("CheckFontOK", "{0} is a font name", "Checking {0} font: License OK for embedding.", font);
					// Assumes only one font file per font; if we embed multiple ones will need to enhance this.
					var size = new FileInfo(fontFiles.First()).Length;
					var sizeToReport = (size / 1000000.0).ToString("F1"); // purposely locale-specific; might be e.g. 1,2
					progress.MessageWithColorAndParams("Embedding",
						"{1} is a number with one decimal place, the number of megabytes the font file takes up",
						"blue",
						"Embedding font {0} at a cost of {1} megs",
						font, sizeToReport);
					continue;
				}
				if (fontFileFinder.FontsWeCantInstall.Contains(font))
				{
					progress.ErrorWithParams("LicenseForbids","{0} is a font name", "Checking {0} font: License does not permit embedding.", font);
				}
				else
				{
					progress.ErrorWithParams("NoFontFound", "{0} is a font name", "Checking {0} font: No font found to embed.", font);
				}
				progress.ErrorWithParams("SubstitutingAndika", "{0} and {1} are font names", "Substituting \"{0}\" for \"{1}\"", defaultFont, font);
			}
			foreach (var file in filesToEmbed)
			{
				// Enhance: do we need to worry about problem characters in font file names?
				var dest = Path.Combine(book.FolderPath, Path.GetFileName(file));
				RobustFile.Copy(file, dest);
			}
			// Create the fonts.css file, which tells the browser where to find the fonts for those families.
			var sb = new StringBuilder();
			foreach (var font in fontsWanted)
			{
				var group = fontFileFinder.GetGroupForFont(font);
				if (group != null)
				{
					EpubMaker.AddFontFace(sb, font, "normal", "normal", group.Normal);
				}
				// We don't need (or want) a rule to use Andika instead.
				// The reader typically WILL use Andika, because we have a rule making it the default font
				// for the whole body of the document, and BloomReader always has it available.
				// However, it's possible that although we aren't allowed to embed the desired font,
				// the device actually has it installed. In that case, we want to use it.
			}
			RobustFile.WriteAllText(Path.Combine(book.FolderPath, "fonts.css"), sb.ToString());
			// Tell the document to use the new stylesheet.
			book.OurHtmlDom.AddStyleSheet("fonts.css");
		}

		/// <summary>
		/// Start with a page, which should appear to the user to contain blocks like this,
		/// separated by blank lines:
		/// Question A
		/// answer1
		/// *correct answer2
		/// answer3
		///
		/// Question B
		/// *correct answer1
		/// answer2
		/// answer3
		///
		/// The actual html encoding will vary. Each line may be wrapped as a paragraph, or there might be br-type line breaks.
		/// We want to make json like this for each question:
		/// {"question":"Question", "answers": [{"text":"answer1"}, {"text":"correct answer", "correct":true}, {"text":"answer2"}]},
		/// </summary>
		public static void ExtractQuestionGroups(XmlElement page, List<QuestionGroup> questionGroups)
		{
			foreach (XmlElement source in page.SafeSelectNodes(".//div[contains(@class, 'bloom-editable')]"))
			{
				var lang = source.Attributes["lang"]?.Value??"";
				if (String.IsNullOrEmpty(lang) || lang == "z")
					continue;
				var group = new QuestionGroup() {lang = lang};
				// this looks weird, but it's just driven by the test cases which are in turn collected
				// from various ways of getting the questions on the page (typing, pasting).
				// See BookReaderFileMakerTests.ExtractQuestionGroups_ParsesCorrectly()
				var separators = new[]
				{
					"<br />", "</p>",
					// now add those may not actually show up in firefox, but are in the pre-existing
					// unit tests, presumably with written-by-hand html?
					"</br>", "<p />"
				};
				var lines = source.InnerXml.Split(separators, StringSplitOptions.None);
				var questions = new List<Question>();
				Question question = null;
				var answers = new List<Answer>();
				foreach (var line in lines)
				{
					var cleanLine = line.Replace("<p>", ""); // our split above just looks at the ends of paragraphs, ignores the starts.
					// Similarly, our split above just looks at the ends of brs, ignores the starts
					//(separate start vs. end br elements might not occur in real FF tests, see note above).
					cleanLine = cleanLine.Replace("<br>", "");
					cleanLine = cleanLine.Replace("\u200c", "");
					if (String.IsNullOrWhiteSpace(cleanLine))
					{
						// If we've accumulated an actual question and answers, put it in the output.
						// otherwise, we're probably just dealing with leading white space before the first question.
						if (answers.Any())
						{
							question.answers = answers.ToArray();
							answers.Clear();
							questions.Add(question);
							question = null;
						}
					} else
					{
						var trimLine = cleanLine.Trim();
						if (question == null)
						{
							// If we don't already have a question being built, this first line is the question.
							question = new Question() { question=trimLine};
						}
						else
						{
							// We already got the question, and haven't seen a blank line since,
							// so this is one of its answers.
							var correct = trimLine.StartsWith("*");
							if (correct)
							{
								trimLine = trimLine.Substring(1).Trim();
							}
							answers.Add(new Answer() {text=trimLine, correct = correct});
						}
					}
				}
				if (answers.Any())
				{
					// Save the final question.
					question.answers = answers.ToArray();
					questions.Add(question);
				}
				if (questions.Any())
				{
					// There may well be editable divs, especially automatically generated for active langauges,
					// which don't have any questions. Skip them. But if we got at least one, save it.
					group.questions = questions.ToArray();
					questionGroups.Add(group);
				}
			}
		}
	}
}
