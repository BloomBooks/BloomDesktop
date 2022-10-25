using System;
using System.Threading;
using CommandLine;
using L10NSharp;
using Bloom.Properties;
using Bloom.Book;
using BloomTemp;
using Bloom.Publish.Android;
using Bloom.ToPalaso;
using System.IO;
using SIL.IO;
using Bloom.Api;
using System.Text;
using System.Linq;
using System.Xml;
using SIL.Xml;
using System.Text.RegularExpressions;

namespace Bloom.CLI
{
	[Flags]
	enum SendFontAnalyticsExitCode
	{
		// These flags should all be powers of 2, so they can be bit-or'd together
		// Make sure to update GetErrorsFromExitCode() too
		Success = 0,
		UnhandledException = 1,
		NoFontsFound = 2,
	}

	public class SendFontAnalyticsCommand
	{
		static ProjectContext _projectContext;
		static Book.Book _book;

		public static int Handle(SendFontAnalyticsParameters options)
		{
			Program.SetUpErrorHandling();
			try
			{
				using (var applicationContainer = new ApplicationContainer())
				{
					Program.SetUpLocalization(applicationContainer);
					GeckoFxBrowser.SetUpXulRunner();
					LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);   // Unclear if this line is needed or not.
					Program.RunningHarvesterMode = true;
					using (_projectContext = applicationContainer.CreateProjectContext(options.CollectionPath))
					{
						Program.SetProjectContext(_projectContext);
						CollectFontAnalytics(options);
						if (BloomPubMaker.BloomPubFontsAndLangsUsed != null)
						{
							// Report what we can about fonts and languages for this book.
							// (See https://issues.bloomlibrary.org/youtrack/issue/BL-11512.)
							ReportFontAnalytics(_book, "harvester sendFontAnalytics", options.Testing, !options.NoEpubExists);
							return (int)SendFontAnalyticsExitCode.Success;
						}
					}
					return (int)SendFontAnalyticsExitCode.NoFontsFound;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return (int)SendFontAnalyticsExitCode.UnhandledException;
			}
		}

		/// <summary>
		/// Report font analytics from a command line used by the harvester.
		/// </summary>
		/// <param name="bookId">book's guid</param>
		/// <param name="details">something like "harvester createArtifacts" or "harvester sendFontAnalytics"</param>
		/// <param name="forTesting">not from the production site, so can be ignored by real users</param>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-11512 for original specification.
		/// </remarks>
		public static void ReportFontAnalytics(Book.Book book, string details, bool forTesting, bool epubExists=true)
		{
			var bookId = book.ID;
			var testOnly = forTesting || WebLibraryIntegration.BookUpload.UseSandboxByDefault;
			var version = GetBloomVersionFromBook(book);
			var hasValidEpub = epubExists && WouldEpubBeValidForPublishing(book, version);
			var hasPdf = WasPdfCreated(book, version);
			foreach (var fontName in BloomPubMaker.BloomPubFontsAndLangsUsed.Keys)
			{
				foreach (var lang in BloomPubMaker.BloomPubFontsAndLangsUsed[fontName])
				{
					if (hasValidEpub)
						FontAnalytics.Report("Bloom Library", "2.0", bookId,
							FontAnalytics.FontEventType.PublishEbook, lang, testOnly, fontName, details);
					FontAnalytics.Report("Bloom Library", "2.0", bookId,
						FontAnalytics.FontEventType.PublishWeb, lang, testOnly, fontName, details);
					if (hasPdf)
						FontAnalytics.Report("Bloom Library", "2.0", bookId,
							FontAnalytics.FontEventType.PublishPdf, lang, testOnly, fontName, details);
				}
			}
			// If this method quits before all the reports have actually been sent, then the
			// program exits without some reports being sent.  Squeezing all of this processing
			// into the async/await paradigm without passing that back up to Main() is not worth
			// the code intricacy and programmer time to get it working.  (Believe me, I tried.)
			// Waiting on the flag is the simplest thing that can work, and it does.
			while (FontAnalytics.PendingReports)
			{
				// The timer used by FontAnalytics.Report() to handle queued requests runs on
				// its own thread, so sleeping on this thread works okay.
				Thread.Sleep(1000);
			}
		}

		private static float GetBloomVersionFromBook(Book.Book book)
		{
			// Extract the Bloom version from the node that looks like
			// <meta name="Generator" content="Bloom Version 5.0.0 (apparent build date: 01-Feb-2021)" />
			// This is the most recent version to edit the book, and the version that uploaded it.
			float bloomVersion = -1.0F;
			var generatorNode = book.Storage.Dom.RawDom.SelectSingleNode("//head/meta[@name='Generator']") as XmlElement;
			if (generatorNode != null)
			{
				var generatorContent = generatorNode.GetAttribute("content");
				if (!String.IsNullOrEmpty(generatorContent))
				{
					var version = Regex.Match(generatorContent, "^Bloom Version ([0-9]+\\.[0-9]+)");
					if (version.Success && version.Groups.Count == 2)
						float.TryParse(version.Groups[1].Value, out bloomVersion);
				}
			}
			return bloomVersion;
		}

		private static bool WouldEpubBeValidForPublishing(Book.Book book, float bloomVersion)
		{
			// This logic is copied from BloomHarvester.
			dynamic publishSettings = null;
			string mode = null;
			var settingsPath = Path.Combine(book.FolderPath,"publish-settings.json");
			if (RobustFile.Exists(settingsPath))
			{
				// A valid publish-settings.json file should have been created by BloomHarvester
				// if it didn't already exist.
				try
				{
					var settingsRawText = RobustFile.ReadAllText(settingsPath);
					publishSettings = DynamicJson.Parse(settingsRawText, Encoding.UTF8) as DynamicJson;
				}
				catch
				{
					publishSettings = null;
				}
			}
			mode = publishSettings?.epub?.mode;
			if (String.IsNullOrEmpty(mode))
				mode = bloomVersion < 5.4F ? "flowable" : "fixed";
			if (mode == "fixed")
				return true;
			int goodPages = 0;
			foreach (var div in book.Storage.Dom.SafeSelectNodes("//div[contains(concat(' ', @class, ' '),' numberedPage ')]").Cast<XmlElement>().ToList())
			{
				var imageContainers = div.SafeSelectNodes("div[contains(@class,'marginBox')]//div[contains(@class,'bloom-imageContainer')]");
				if (imageContainers.Count > 1)
					return false;
				// Count any translation group which is not an image description
				var translationGroups = div.SafeSelectNodes("div[contains(@class,'marginBox')]//div[contains(@class,'bloom-translationGroup') and not(contains(@class, 'box-header-off')) and not(contains(@class,'bloom-imageDescription'))]");
				if (translationGroups.Count > 1)
					return false;
				var videos = div.SafeSelectNodes("following-sibling::div[contains(@class,'marginBox')]//video");
				if (videos.Count > 1)
					return false;
				++goodPages;
			}
			return goodPages > 0;
		}

		private static bool WasPdfCreated(Book.Book book, float bloomVersion)
		{
			// Starting with Bloom 5.1, PDFs are no longer created and uploaded if the book
			// contains any video.
			if (bloomVersion < 5.1)
				return true;
			return !BookStorage.GetVideoPathsRelativeToBook(book.RawDom.DocumentElement).Any();
		}

		private static void CollectFontAnalytics(SendFontAnalyticsParameters options)
		{
			using (var stagingFolder = new TemporaryFolder("FontAnalytics"))
			{
				_book = _projectContext.BookServer.GetBookFromBookInfo(new BookInfo(options.BookPath, true,
					_projectContext.TeamCollectionManager.CurrentCollectionEvenIfDisconnected ?? new AlwaysEditSaveContext() as ISaveContext));

				bool isTemplateBook = _book.BookInfo.IsSuitableForMakingShells;
				var settings = AndroidPublishSettings.GetPublishSettingsForBook(_projectContext.BookServer, _book.BookInfo);
				// This method will gather up the desired font analytics as a side-effect.
				BloomPubMaker.PrepareBookForBloomReader(options.BookPath, _projectContext.BookServer, stagingFolder,
					new Bloom.web.NullWebSocketProgress(), isTemplateBook, settings: settings);
			}
		}
	}

	[Verb("sendFontAnalytics", HelpText ="Send font analytics for the given book.")]
	public class SendFontAnalyticsParameters
	{
		[Value(0, MetaName = "bookPath", Required = true, HelpText = "Path to the folder of the book to get font analytics from.")]
		public string BookPath { get; set; }

		[Option("collectionPath", Required = true, HelpText = "Input path of the Bloom collection file for this book")]
		public string CollectionPath { get; set; }

		[Option("testing", Required = false, Default = false, HelpText = "Analytics are being sent for testing, not production")]
		public bool Testing { get; set; }

		[Option("noEpubExists", Required = false, Default = false, HelpText = "Flag that no Epub was created")]
		public bool NoEpubExists { get; set; }
	}
}
