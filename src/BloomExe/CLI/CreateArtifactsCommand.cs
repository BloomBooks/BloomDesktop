using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Bloom.Publish.Epub;
using Bloom.web;
using BloomTemp;
using CommandLine;
using L10NSharp;
using SIL.IO;

namespace Bloom.CLI
{
	class CreateArtifactsCommand
	{
		private static ProjectContext _projectContext;

		public static int Handle(CreateArtifactsParameters options)
		{
			Console.Out.WriteLine();

			Program.SetUpErrorHandling();
			try
			{
				using (var applicationContainer = new ApplicationContainer())
				{
					Bloom.Program.RunningNonApplicationMode = true;
					Program.SetUpLocalization(applicationContainer);
					Browser.SetUpXulRunner();
					Browser.XulRunnerShutdown += Program.OnXulRunnerShutdown;
					LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);   // Unclear if this line is needed or not.
					if (DesktopAnalytics.Analytics.AllowTracking)
					{
						throw new ApplicationException("Allow tracking is enabled but we don't want the Harvester to actually send analytics.");
					}

					Program.RunningHarvesterMode = true;
					string collectionFilePath = options.CollectionPath;
					using (_projectContext = applicationContainer.CreateProjectContext(collectionFilePath))
					{
						Bloom.Program.SetProjectContext(_projectContext);

						// Make the .bloomd and /bloomdigital outputs
						CreateArtifacts(options);
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return 1;
			}

			return 0;
		}

		private static void CreateArtifacts(CreateArtifactsParameters parameters)
		{
			string zippedBloomDOutputPath = parameters.BloomDOutputPath;
			string unzippedBloomDigitalOutputPath = parameters.BloomDigitalOutputPath;

			bool isBloomDOrBloomDigitalRequested = !String.IsNullOrEmpty(zippedBloomDOutputPath) || !String.IsNullOrEmpty(unzippedBloomDigitalOutputPath);
			if (isBloomDOrBloomDigitalRequested)
			{
				CreateBloomDigitalArtifacts(parameters.BookPath, parameters.Creator, zippedBloomDOutputPath, unzippedBloomDigitalOutputPath);
			}

			Control control = new Control();
			control.CreateControl();

			using (var countdownEvent = new CountdownEvent(1))
			{
				// Create the ePub in the background. (Some of the ePub work needs to happen off the main thread)
				ThreadPool.QueueUserWorkItem(
					x =>
					{
						CreateEpubArtifact(parameters, control);
						countdownEvent.Signal();    // Decrement by one
					}
				);

				// Wait around until the worker thread is done.
				while (!countdownEvent.IsSet)	// Set = true if the count is down to 0.
				{
					Thread.Sleep(100);
					Application.DoEvents();
				}
			}

			CreateThumbnailArtifact(parameters);
		}

		public static void CreateBloomDigitalArtifacts(string bookPath, string creator, string zippedBloomDOutputPath, string unzippedBloomDigitalOutputPath)
		{
#if DEBUG
			// Useful for allowing debugging of Bloom while running the harvester
			//MessageBox.Show("Attach debugger now");
#endif

			using (var tempBloomD = TempFile.CreateAndGetPathButDontMakeTheFile())
			{
				if (String.IsNullOrEmpty(zippedBloomDOutputPath))
				{
					zippedBloomDOutputPath = tempBloomD.Path;
				}

				BookServer bookServer = _projectContext.BookServer;

				using (var folderForUnzipped = new TemporaryFolder("BloomCreateArtifacts_Unzipped"))
				{
					// Ensure directory exists, just in case.
					Directory.CreateDirectory(Path.GetDirectoryName(zippedBloomDOutputPath));

					// Make the bloomd
					string unzippedPath = Publish.Android.BloomReaderFileMaker.CreateBloomDigitalBook(
					zippedBloomDOutputPath,
					bookPath,
					bookServer,
					System.Drawing.Color.Azure, // TODO: What should this be?
					new Bloom.web.NullWebSocketProgress(),
					folderForUnzipped,
					creator);

					// Currently the zipping process does some things we actually need, like making the cover picture
					// transparent (BL-7437). Eventually we plan to separate the preparation and zipping steps (BL-7445).
					// Until that is done, the most reliable way to get an unzipped BloomD for our preview is to actually
					// unzip the BloomD.
					if (!String.IsNullOrEmpty(unzippedBloomDigitalOutputPath))
					{
						SIL.IO.RobustIO.DeleteDirectory(unzippedBloomDigitalOutputPath, recursive: true);   // In case the folder isn't already empty

						// Ensure directory exists, just in case.
						Directory.CreateDirectory(Path.GetDirectoryName(unzippedBloomDigitalOutputPath));

						ZipFile.ExtractToDirectory(zippedBloomDOutputPath, unzippedBloomDigitalOutputPath);

						RenameBloomDigitalFiles(unzippedBloomDigitalOutputPath);
					}
				}
			}
		}

		// Consumers expect the file to be in index.htm name, not {title}.htm name.
		private static void RenameBloomDigitalFiles(string bookDirectory)
		{
			string originalHtmFilePath = Bloom.Book.BookStorage.FindBookHtmlInFolder(bookDirectory);

			Debug.Assert(RobustFile.Exists(originalHtmFilePath), "Book HTM not found: " + originalHtmFilePath);
			if (RobustFile.Exists(originalHtmFilePath))
			{
				string newHtmFilePath = Path.Combine(bookDirectory, $"index.htm");
				RobustFile.Copy(originalHtmFilePath, newHtmFilePath);
				RobustFile.Delete(originalHtmFilePath);
			}
		}

		/// <summary>
		/// Creates an ePub file at the location specified by parametersr
		/// </summary>
		/// <param name="parameters">BookPath and epubOutputPath should be set.</param>
		/// <param name="control">The epub code needs a control that goes back to the main thread, in order to run some tasks that need to be on the main thread</param>
		public static void CreateEpubArtifact(CreateArtifactsParameters parameters, Control control)
		{
			CreateEpubArtifact(parameters.BookPath, parameters.EpubOutputPath, control);
		}

		public static void CreateEpubArtifact(string downloadBookDir, string epubOutputPath, Control control)
		{
			if (String.IsNullOrEmpty(epubOutputPath))
			{
				return;
			}

			string directoryName = Path.GetDirectoryName(epubOutputPath);
			Directory.CreateDirectory(directoryName);	// Ensures that the directory exists

			BookServer bookServer = _projectContext.BookServer;
			BookThumbNailer thumbNailer = _projectContext.ThumbNailer;
			var maker = new EpubMaker(thumbNailer, bookServer);
			maker.ControlForInvoke = control;

			maker.Book = bookServer.GetBookFromBookInfo(new BookInfo(downloadBookDir, true));
			maker.Unpaginated = true; // so far they all are
			maker.OneAudioPerPage = true; // default used in EpubApi
										  // Enhance: maybe we want book to have image descriptions on page? use reader font sizes?
			
			// Make the epub
			maker.SaveEpub(epubOutputPath, new NullWebSocketProgress());
		}

		public static void CreateThumbnailArtifact(CreateArtifactsParameters parameters)
		{
			if (String.IsNullOrWhiteSpace(parameters.ThumbnailOutputInfoPath))
			{
				return;
			}

			BookServer bookServer = _projectContext.BookServer;
			var book = bookServer.GetBookFromBookInfo(new BookInfo(parameters.BookPath, true));

			var outputPaths = new List<string>();

			int[] requestedHeights = new int[2] { 256, 70 };
			foreach (int height in requestedHeights)
			{
				// A potential "enhancement" is that we could try to re-use the thumbnail generated when making an ePUB.
				//   That could be helpful if creating new thumbnails was a big enough cost
				// The ePub Path looks like this: $"%TEMP%\\ePUB_export\\{i}\\{book.Title}\\thumbnail-{height}.png"
				// (where i is an integer between 0-19. Depends on whether it's been locked or not.)
				//
				// For now, we'll just create the thumbnail every time
				// It makes the code simpler than having fallback logic not to mention the complications of determining which folder the ePub is in, whether it's really up-to-date, etc.
				HtmlThumbNailer.ThumbnailOptions thumbnailOptions = BookThumbNailer.GetCoverThumbnailOptions(height, new Guid());

				BookThumbNailer.CreateThumbnailOfCoverImage(book, thumbnailOptions, null);

				// The thumbnail has now been saved in the book's storage folder.
				// Copy it over to the requested output destination
				string thumbnailPath = Path.Combine(book.FolderPath, thumbnailOptions.FileName);
				if (RobustFile.Exists(thumbnailPath))
				{
					outputPaths.Add(thumbnailPath);
				}
			}

			using (var writer = new StreamWriter(parameters.ThumbnailOutputInfoPath, append: false))
			{
				foreach (var path in outputPaths)
				{
					writer.WriteLine(path);
				}
			}
		}
	}

	[Verb("createArtifacts", HelpText = "Create artifacts for a book such as .bloomd, unzipped bloom digital, ePub, etc.")]
	public class CreateArtifactsParameters
	{
		[Option("bookPath", HelpText = "Input path in which to find book folder", Required = true)]
		public string BookPath { get; set; }

		[Option("collectionPath", HelpText = "Input path in which to find Bloom collection file for this book", Required = true)]
		public string CollectionPath { get; set; }

		[Option("bloomdOutputPath", HelpText = "Output destination path in which to place bloomd file", Required = false)]
		public string BloomDOutputPath { get; set; }

		[Option("bloomDigitalOutputPath", HelpText = "Output destination path in which to place bloomdigital folder", Required = false)]
		public string BloomDigitalOutputPath { get; set; }

		[Option("epubOutputPath", HelpText = "Output destination path in which to place epub file", Required = false)]
		public string EpubOutputPath { get; set; }

		[Option("thumbnailOutputInfoPath", HelpText = "Output destination path for a text file which contains path information for generated thumbnail files", Required = false)]
		public string ThumbnailOutputInfoPath { get; set; }

		[Option("creator", Required = false, Default = "harvester", HelpText = "The value of the \"creator\" meta tag passed along when creating the bloomdigital.")]
		public string Creator{ get; set; }
	}
}
