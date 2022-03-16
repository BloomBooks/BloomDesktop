﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.FontProcessing;
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
	public static class BloomPubMaker
	{
		public const string kQuestionFileName = "questions.json";
		public const string BRExportFolder = "BloomReaderExport";
		internal const string kDistributionFileName = ".distribution";
		internal const string kDistributionBloomDirect = "bloom-direct";
		internal const string kDistributionBloomWeb = "bloom-web";
		internal const string kCreatorBloom = "bloom";
		internal const string kCreatorHarvester = "harvester";

		public static Control ControlForInvoke { get; set; }

		public static void CreateBloomPub(BookInfo bookInfo, AndroidPublishSettings settings, string outputFolder, BookServer bookServer, IWebSocketProgress progress )
		{
			var outputPath = Path.Combine(outputFolder, Path.GetFileName(bookInfo.FolderPath) + BookCompressor.BloomPubExtensionWithDot);
			BloomPubMaker.CreateBloomPub(outputPath, bookInfo.FolderPath, bookServer, progress, bookInfo.IsSuitableForMakingShells, settings: settings);
		}
		public static void CreateBloomPub(string outputPath, Book.Book book, BookServer bookServer, IWebSocketProgress progress, AndroidPublishSettings settings = null)
		{
			CreateBloomPub(outputPath, book.FolderPath, bookServer, progress, book.IsTemplateBook, settings:settings);
		}

		// Create a BloomReader book while also creating the temporary folder for it (according to the specified parameter) and disposing of it
		public static void CreateBloomPub(string outputPath, string bookFolderPath, BookServer bookServer,
	
			IWebSocketProgress progress, bool isTemplateBook, string tempFolderName = BRExportFolder,
			string creator = kCreatorBloom, AndroidPublishSettings settings = null)
		{
			using (var temp = new TemporaryFolder(tempFolderName))
			{
				CreateBloomPub(outputPath, bookFolderPath, bookServer, progress, temp, creator, isTemplateBook, settings);
			}
		}

		/// <summary>
		/// Create a Bloom Digital book (the zipped .bloomd file) as used by BloomReader (and Bloom Library etc)
		/// </summary>
		/// <param name="outputPath">The path to create the zipped .bloomd output file at</param>
		/// <param name="bookFolderPath">The path to the input book</param>
		/// <param name="bookServer"></param>
		/// <param name="backColor"></param>
		/// <param name="progress"></param>
		/// <param name="tempFolder">A temporary folder. This function will not dispose of it when done</param>
		/// <param name="creator">value for &lt;meta name="creator" content="..."/&gt; (defaults to "bloom")</param>
		/// <param name="isTemplateBook"></param>
		/// <param name="settings"></param>
		/// <returns>Path to the unzipped .bloomd</returns>
		public static string CreateBloomPub(string outputPath, string bookFolderPath, BookServer bookServer,
			IWebSocketProgress progress, TemporaryFolder tempFolder, string creator = kCreatorBloom, bool isTemplateBook=false,
			AndroidPublishSettings settings = null)
		{
			var modifiedBook = PrepareBookForBloomReader(bookFolderPath, bookServer, tempFolder, progress, isTemplateBook, creator, settings);
			// We want at least 256 for Bloom Reader, because the screens have a high pixel density. And (at the moment) we are asking for
			// 64dp in Bloom Reader.

			BookCompressor.MakeSizedThumbnail(modifiedBook, modifiedBook.FolderPath, 256);

			BookCompressor.CompressBookDirectory(outputPath, modifiedBook.FolderPath, "", reduceImages: true, omitMetaJson: false, wrapWithFolder: false,
				pathToFileForSha: BookStorage.FindBookHtmlInFolder(bookFolderPath));

			return modifiedBook.FolderPath;
		}

		public static Book.Book PrepareBookForBloomReader(string bookFolderPath, BookServer bookServer,
			TemporaryFolder temp,
			IWebSocketProgress progress, bool isTemplateBook,
			string creator = kCreatorBloom,
			AndroidPublishSettings settings = null)
		{
			// MakeDeviceXmatterTempBook needs to be able to copy customCollectionStyles.css etc into parent of bookFolderPath
			// And bloom-player expects folder name to match html file name.
			var htmPath = BookStorage.FindBookHtmlInFolder(bookFolderPath);
			var tentativeBookFolderPath = Path.Combine(temp.FolderPath,
				// Windows directory names cannot have trailing periods, but FileNameWithoutExtension can have these.  (BH-6097)
				BookStorage.SanitizeNameForFileSystem(Path.GetFileNameWithoutExtension(htmPath)));
			Directory.CreateDirectory(tentativeBookFolderPath);
			var modifiedBook = PublishHelper.MakeDeviceXmatterTempBook(bookFolderPath, bookServer,
				tentativeBookFolderPath, isTemplateBook);

			// Although usually tentativeBookFolderPath and modifiedBook.FolderPath are the same, there are some exceptions
			// In the process of bringing a book up-to-date (called by MakeDeviceXmatterTempBook), the folder path may change.
			// For example, it could change if the original folder path contains punctuation marks now deemed dangerous.
			//    The book will be moved to the sanitized version of the file name instead.
			// It can also happen if we end up picking a different version of the title (i.e. in a different language)
			//    than the one written to the .htm file.
			string modifiedBookFolderPath = modifiedBook.FolderPath;

			if (modifiedBook.CollectionSettings.HaveEnterpriseFeatures)
				ProcessQuizzes(modifiedBookFolderPath, modifiedBook.RawDom);

			// Right here, let's maintain the history of what the BloomdVersion signifies to a reader.
			// Version 1 (as opposed to no BloomdVersion field): the bookFeatures property may be
			// used to report features analytics (with earlier bloomd's, the reader must use its own logic)
			modifiedBook.Storage.BookInfo.MetaData.BloomdVersion = 1;

			modifiedBook.Storage.BookInfo.UpdateOneSingletonTag("distribution", settings?.DistributionTag);
			if (!string.IsNullOrEmpty(settings?.BookshelfTag))
			{
				modifiedBook.Storage.BookInfo.UpdateOneSingletonTag("bookshelf", settings.BookshelfTag);
			}


			if (settings?.LanguagesToInclude != null)
				PublishModel.RemoveUnwantedLanguageData(modifiedBook.OurHtmlDom, settings.LanguagesToInclude, modifiedBook.BookData.MetadataLanguage1IsoCode);
			else if (Program.RunningHarvesterMode && modifiedBook.OurHtmlDom.SelectSingleNode(BookStorage.ComicalXpath) != null)
			{
				// This indicates that we are harvesting a book with comic speech bubbles or other overlays (Overlay Tool).
				// For books with overlays, we only publish a single language. It's not currently feasible to
				// allow the reader to switch language in a book with overlays, because typically that requires
				// adjusting the positions of the overlays, and we don't yet support having more than one
				// set of overlay locations in a single book. See BL-7912 for some ideas on how we might
				// eventually improve this. In the meantime, switching language would have bad effects,
				// and if you can't switch language, there's no point in the book containing more than one.
				var languagesToInclude = new string[1] { modifiedBook.BookData.Language1.Iso639Code };
				PublishModel.RemoveUnwantedLanguageData(modifiedBook.OurHtmlDom, languagesToInclude, modifiedBook.BookData.MetadataLanguage1IsoCode);
			}

			// Do this after processing interactive pages, as they can satisfy the criteria for being 'blank'
			HashSet<string> fontsUsed = null;
			using (var helper = new PublishHelper())
			{
				helper.ControlForInvoke = ControlForInvoke;
				ISet<string> warningMessages = new HashSet<string>();
				helper.RemoveUnwantedContent(modifiedBook.OurHtmlDom, modifiedBook, false, warningMessages);
				PublishHelper.SendBatchedWarningMessagesToProgress(warningMessages, progress);
				fontsUsed = helper.FontsUsed;
			}
			if (!modifiedBook.IsTemplateBook)
				modifiedBook.RemoveBlankPages(settings?.LanguagesToInclude);

			// See https://issues.bloomlibrary.org/youtrack/issue/BL-6835.
			RemoveInvisibleImageElements(modifiedBook);
			modifiedBook.Storage.CleanupUnusedSupportFiles(/*isForPublish:*/true, settings?.AudioLanguagesToExclude);
			if (!modifiedBook.IsTemplateBook && RobustFile.Exists(Path.Combine(modifiedBookFolderPath, "placeHolder.png")))
				RobustFile.Delete(Path.Combine(modifiedBookFolderPath, "placeHolder.png"));
			modifiedBook.RemoveObsoleteAudioMarkup();

			// We want these to run after RemoveUnwantedContent() so that the metadata will more accurately reflect
			// the subset of contents that are included in the .bloomd
			// Note that we generally want to disable features here, but not enable them, especially while
			// running harvester!  See https://issues.bloomlibrary.org/youtrack/issue/BL-8995.
			var enableBlind = modifiedBook.BookInfo.MetaData.Feature_Blind || !Program.RunningHarvesterMode;
			// BloomReader and BloomPlayer are not using the SignLanguage feature, and it's misleading to
			// assume the existence of videos implies sign language.  There is a separate "Video" feature
			// now that gets set automatically.  (Automated setting of the Blind feature is imperfect, but
			// more meaningful than trying to automate sign language just based on one video existing.)
			var enableSignLanguage = modifiedBook.BookInfo.MetaData.Feature_SignLanguage;
			modifiedBook.UpdateMetadataFeatures(
				isBlindEnabled: enableBlind,
				isSignLanguageEnabled: enableSignLanguage,
				isTalkingBookEnabled: true, // talkingBook is only ever set automatically as far as I can tell.
				allowedLanguages: null // allow all because we've already filtered out the unwanted ones from the dom above.
				);	

			modifiedBook.SetAnimationDurationsFromAudioDurations();

			modifiedBook.OurHtmlDom.SetMedia("bloomReader");
			modifiedBook.OurHtmlDom.AddOrReplaceMetaElement("bloom-digital-creator", creator);
			EmbedFonts(modifiedBook, progress, fontsUsed, FontFileFinder.GetInstance(Program.RunningUnitTests));

			var bookFile = BookStorage.FindBookHtmlInFolder(modifiedBook.FolderPath);
			StripImgIfWeCannotFindFile(modifiedBook.RawDom, bookFile);
			StripContentEditableAndTabIndex(modifiedBook.RawDom);
			InsertReaderStylesheet(modifiedBook.RawDom);
			RobustFile.Copy(FileLocationUtilities.GetFileDistributedWithApplication(BloomFileLocator.BrowserRoot,"publish","ReaderPublish","readerStyles.css"),
				Path.Combine(modifiedBookFolderPath, "readerStyles.css"));
			ConvertImagesToBackground(modifiedBook.RawDom);

			AddDistributionFile(modifiedBookFolderPath, creator, settings);

			modifiedBook.Save();

			return modifiedBook;
		}

		/// <summary>
		/// Add a `.distribution` file to the zip which will be reported on for analytics from Bloom Reader.
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-8875.
		/// </summary>
		private static void AddDistributionFile(string bookFolder, string creator, AndroidPublishSettings settings=null)
		{
			string distributionValue;
			switch (creator)
			{
				case kCreatorHarvester:
					distributionValue = kDistributionBloomWeb;
					break;
				case kCreatorBloom:
					distributionValue = kDistributionBloomDirect;
					if(settings!=null && !string.IsNullOrEmpty(settings.DistributionTag))
						distributionValue = settings.DistributionTag;
					break;
				default: throw new ArgumentException("Unknown creator", creator);
			}
			RobustFile.WriteAllText(Path.Combine(bookFolder, kDistributionFileName), distributionValue);
		}

		private static void ProcessQuizzes(string bookFolderPath, XmlDocument bookDom)
		{
			var jsonPath = Path.Combine(bookFolderPath, kQuestionFileName);
			var questionPages = bookDom.SafeSelectNodes(
				"//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'questions')]");
			var questions = new List<QuestionGroup>();
			foreach (var page in questionPages.Cast<XmlElement>().ToArray())
			{
				ExtractQuestionGroups(page, questions);
				page.ParentNode.RemoveChild(page);
			}

			var quizPages = bookDom.SafeSelectNodes(
				"//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'simple-comprehension-quiz')]");
			foreach (var page in quizPages.Cast<XmlElement>().ToArray())
				AddQuizQuestionGroup(page, questions);
			var builder = new StringBuilder("[");
			foreach (var question in questions)
			{
				if (builder.Length > 1)
					builder.Append(",\n");
				builder.Append(question.GetJson());
			}

			builder.Append("]");
			File.WriteAllText(jsonPath, builder.ToString());
		}

		// Given a page built using the new simple-comprehension-quiz template, generate JSON to produce the same
		// effect (more-or-less) in BloomReader 1.x. These pages are NOT deleted like the old question pages,
		// so we need to mark the JSON onlyForBloomReader1 to prevent BR2 from duplicating them.
		private static void AddQuizQuestionGroup(XmlElement page, List<QuestionGroup> questions)
		{
			var questionElts =
				page.SafeSelectNodes(
					".//div[contains(@class, 'bloom-editable') and contains(@class, 'bloom-content1') and contains(@class, 'QuizQuestion-style')]");
			var answerElts =
				page.SafeSelectNodes(
					".//div[contains(@class, 'bloom-editable') and contains(@class, 'bloom-content1') and contains(@class, 'QuizAnswer-style')]")
				.Cast<XmlElement>()
				.Where(a => !string.IsNullOrWhiteSpace(a.InnerText));
			if (questionElts.Count == 0 || !answerElts.Any())
			{
				return;
			}

			var questionElt = questionElts[0];
			if (string.IsNullOrWhiteSpace(questionElt.InnerText))
				return;
			var lang = questionElt.Attributes["lang"]?.Value ?? "";
			if (string.IsNullOrEmpty(lang))
				return; // paranoia, bloom-editable without lang should not be content1

			var group = new QuestionGroup() { lang = lang, onlyForBloomReader1 = true};
			var question = new Question()
			{
				question = questionElt.InnerText.Trim(),
				answers = answerElts.Select(a => new Answer()
				{
					text= a.InnerText.Trim(),
					correct = ((a.ParentNode?.ParentNode as XmlElement)?.Attributes["class"]?.Value ??"").Contains("correct-answer")
				}).ToArray()
			};
			group.questions = new [] {question};

			questions.Add(group);
		}


		private static void StripImgIfWeCannotFindFile(XmlDocument dom, string bookFile)
		{
			var folderPath = Path.GetDirectoryName(bookFile);
			foreach (var imgElt in dom.SafeSelectNodes("//img[@src]").Cast<XmlElement>().ToArray())
			{
				var file = UrlPathString.CreateFromUrlEncodedString(imgElt.Attributes["src"].Value).PathOnly.NotEncoded;
				if (!File.Exists(Path.Combine(folderPath, file)))
				{
					imgElt.ParentNode.RemoveChild(imgElt);
				}
			}
		}

		private static void StripContentEditableAndTabIndex(XmlDocument dom)
		{
			foreach (var editableElt in dom.SafeSelectNodes("//div[@contenteditable]").Cast<XmlElement>())
				editableElt.RemoveAttribute("contenteditable");

			// For some reason Bloom adds tabindex="0" to bloom-editables and for some reason we remove them
			// when we go to publish (neither reason are clear to me; "saving space" for the latter?). In any case,
			// we need to keep the tabindex on translationGroups so we preserve audio playback order.
			const string tabindexXpath =
				"//div[@tabindex and not(contains(concat(' ', @class, ' '), ' bloom-translationGroup '))]";
			foreach (var tabIndexDiv in dom.SafeSelectNodes(tabindexXpath).Cast<XmlElement>())
				tabIndexDiv.RemoveAttribute("tabindex");
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
		/// Remove image elements that are invisible due to the book's layout orientation.
		/// </summary>
		/// <remarks>
		/// This code is temporary for Version4.5.  Version4.6 extensively refactors the
		/// electronic publishing code to combine ePUB and BloomReader preparation as much
		/// as possible.
		/// </remarks>
		private static void RemoveInvisibleImageElements(Bloom.Book.Book book)
		{
			var isLandscape = book.GetLayout().SizeAndOrientation.IsLandScape;
			foreach (var img in book.RawDom.SafeSelectNodes("//img").Cast<XmlElement>().ToArray())
			{
				var src = img.Attributes["src"]?.Value;
				if (string.IsNullOrEmpty(src))
					continue;
				var classes = img.Attributes["class"]?.Value;
				if (string.IsNullOrEmpty(classes))
					continue;
				if (isLandscape && classes.Contains("portraitOnly") ||
					!isLandscape && classes.Contains("landscapeOnly"))
				{
					// Remove this img element since it shouldn't be displayed.
					img.ParentNode.RemoveChild(img);
				}
			}
		}

		/// <summary>
		/// Given a book, typically one in a temporary folder made just for exporting (or testing),
		/// and given the set of fonts found while creating that book and removing hidden elements,
		/// find the files needed for those fonts.
		/// Copy the font file for the normal style of that font family from the system font folder,
		/// if permitted; or post a warning in progress if we can't embed it.
		/// Create an extra css file (fonts.css) which tells the book to find the font files for those font families
		/// in the local folder, and insert a link to it into the book.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="progress"></param>
		/// <param name="fontFileFinder">use new FontFinder() for real, or a stub in testing</param>
		public static void EmbedFonts(Book.Book book, IWebSocketProgress progress, HashSet<string> fontsWanted, IFontFinder fontFileFinder)
		{
			const string defaultFont = "Andika New Basic"; // already in BR, don't need to embed or make rule.
			fontsWanted.Remove(defaultFont);
			fontFileFinder.NoteFontsWeCantInstall = true;
			var filesToEmbed = new List<string>();
			var badFonts = new HashSet<string>();
			foreach (var font in fontsWanted)
			{
				var fontFiles = fontFileFinder.GetFilesForFont(font);
				var badFileType = fontFiles.Count() > 0 && !FontMetadata.fontFileTypesBloomKnows.Contains(Path.GetExtension(fontFiles.First()).ToLowerInvariant());
				if (fontFiles.Count() > 0 && !badFileType)
				{
					filesToEmbed.AddRange(fontFiles);
					progress.MessageWithParams("PublishTab.Android.File.Progress.CheckFontOK", "{0} is a font name", "Checking {0} font: License OK for embedding.", ProgressKind.Progress, font);
					// Assumes only one font file per font; if we embed multiple ones will need to enhance this.
					var size = new FileInfo(fontFiles.First()).Length;
					var sizeToReport = (size / 1000000.0).ToString("F1"); // purposely locale-specific; might be e.g. 1,2
					progress.MessageWithParams("PublishTab.Android.File.Progress.Embedding",
						"{1} is a number with one decimal place, the number of megabytes the font file takes up",
						"Embedding font {0} at a cost of {1} megs",
						ProgressKind.Note,
						font, sizeToReport);
					continue;
				}
				if (badFileType)
				{
					progress.MessageWithParams("IncompatibleFontFileFormat", "{0} is a font name", "This book has text in a font named \"{0}\". Bloom cannot use a font in this font's file format.", ProgressKind.Error, font);
				}
				else if (fontFileFinder.FontsWeCantInstall.Contains(font))
				{
					//progress.Error("Common.Warning", "Warning");
					progress.MessageWithParams("LicenseForbids","{0} is a font name", "This book has text in a font named \"{0}\". The license for \"{0}\" does not permit Bloom to embed the font in the book.",ProgressKind.Error, font);
				}
				else
				{
					progress.MessageWithParams("NoFontFound", "{0} is a font name", "This book has text in a font named \"{0}\", but Bloom could not find that font on this computer.", ProgressKind.Error, font);
				}
				progress.MessageWithParams("SubstitutingAndika", "{0} is a font name", "Bloom will substitute \"{0}\" instead.", ProgressKind.Error, defaultFont, font);
				badFonts.Add(font);	// need to prevent the bad/missing font from showing up in fonts.css and elsewhere
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
				if (badFonts.Contains(font))
					continue;
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
			// Repair defaultLangStyles.css and other places in the output book if needed.
			if (badFonts.Any())
				FixCssReferencesForBadFonts(book, defaultFont, badFonts);
		}

		private static void FixCssReferencesForBadFonts(Book.Book book, string defaultFont, HashSet<string> badFonts)
		{
			// Note that the font may be referred to in defaultLangStyles.css, in customCollectionStyles.css, or in a style defined in the HTML.
			var defaultLangStyles = Path.Combine(book.FolderPath, "defaultLangStyles.css");
			if (RobustFile.Exists(defaultLangStyles))
			{
				var cssTextOrig = RobustFile.ReadAllText(defaultLangStyles);
				var cssText = cssTextOrig;
				foreach (var font in badFonts)
				{
					var cssRegex = new System.Text.RegularExpressions.Regex($"font-family:\\s*'?{font}'?;");
					cssText = cssRegex.Replace(cssText, $"font-family: '{defaultFont}';");
				}
				if (cssText != cssTextOrig)
					RobustFile.WriteAllText(defaultLangStyles, cssText);
			}
			var customCollectionStyles = Path.Combine(book.FolderPath, "customCollectionStyles.css");
			if (RobustFile.Exists(customCollectionStyles))
			{
				var cssTextOrig = RobustFile.ReadAllText(customCollectionStyles);
				var cssText = cssTextOrig;
				foreach (var font in badFonts)
				{
					var cssRegex = new System.Text.RegularExpressions.Regex($"font-family:\\s*'?{font}'?;");
					cssText = cssRegex.Replace(cssText, $"font-family: '{defaultFont}';");
				}
				if (cssText != cssTextOrig)
					RobustFile.WriteAllText(customCollectionStyles, cssText);
			}
			// Now for styles defined in the dom...
			var userStylesNode = book.OurHtmlDom.SelectSingleNode("//head/style[@type='text/css' and @title='userModifiedStyles']");
			if (userStylesNode != null && !String.IsNullOrEmpty(userStylesNode.InnerXml) && userStylesNode.InnerXml.Contains("font-family:"))
			{
				var cssTextOrig = userStylesNode.InnerXml;  // InnerXml needed to preserve CDATA markup
				var cssText = cssTextOrig;
				foreach (var font in badFonts)
				{
					var cssRegex = new System.Text.RegularExpressions.Regex($"font-family:\\s*{font}\\s*!\\s*important;");
					cssText = cssRegex.Replace(cssText, $"font-family: {defaultFont} !important;");
				}
				if (cssText != cssTextOrig)
					userStylesNode.InnerXml = cssText;
			}
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
