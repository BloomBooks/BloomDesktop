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
	[Flags]
	enum CreateArtifactsExitCode
	{
		// These flags should all be powers of 2, so they can be bit-or'd together
		// Make sure to update GetErrorsFromExitCode() too
		Success = 0,
		UnhandledException = 1,
		BookHtmlNotFound = 2
	}

	class CreateArtifactsCommand
	{
		private static ProjectContext _projectContext;
		private static Book.Book _book;

		public static List<string> GetErrorsFromExitCode(int exitCode)
		{
			var errors = new List<string>();

			if (exitCode == 0)
				return errors;

			// Check the exit code against bitmask flags
			if ((exitCode & (int)CreateArtifactsExitCode.UnhandledException) != 0)
			{
				errors.Add(CreateArtifactsExitCode.UnhandledException.ToString());
				exitCode &= ~(int)CreateArtifactsExitCode.UnhandledException;
			}

			if ((exitCode & (int)CreateArtifactsExitCode.BookHtmlNotFound) != 0)
			{
				errors.Add(CreateArtifactsExitCode.BookHtmlNotFound.ToString());
				exitCode &= ~(int)CreateArtifactsExitCode.BookHtmlNotFound;
			}

			// Check if:
			// 1) Some error code was found
			// 2) No unknown flags remain
			if (errors.Count == 0 || exitCode != 0)
				errors.Add("Unknown");

			return errors;
		}

		public static int Handle(CreateArtifactsParameters options)
		{
			try
			{
				Console.Out.WriteLine();

				Program.SetUpErrorHandling();

				using (var applicationContainer = new ApplicationContainer())
				{
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
						var exitCode = CreateArtifacts(options);
						return (int)exitCode;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return (int)CreateArtifactsExitCode.UnhandledException;
			}
		}

		private static CreateArtifactsExitCode CreateArtifacts(CreateArtifactsParameters parameters)
		{
			var exitCode = CreateArtifactsExitCode.Success;

			LoadBook(parameters.BookPath);

			string zippedBloomDOutputPath = parameters.BloomDOutputPath;
			string unzippedBloomDigitalOutputPath = parameters.BloomDigitalOutputPath;

			bool isBloomDOrBloomDigitalRequested = !String.IsNullOrEmpty(zippedBloomDOutputPath) || !String.IsNullOrEmpty(unzippedBloomDigitalOutputPath);
			if (isBloomDOrBloomDigitalRequested)
			{
				exitCode |= CreateBloomDigitalArtifacts(parameters.BookPath, parameters.Creator, zippedBloomDOutputPath, unzippedBloomDigitalOutputPath);
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

			return exitCode;
		}

		private static void LoadBook(string bookPath)
		{
			_book = _projectContext.BookServer.GetBookFromBookInfo(new BookInfo(bookPath, true));
		}

		/// <summary>
		/// Creates the .bloomd and bloomdigital folders
		/// </summary>
		private static CreateArtifactsExitCode CreateBloomDigitalArtifacts(string bookPath, string creator, string zippedBloomDOutputPath, string unzippedBloomDigitalOutputPath)
		{
#if DEBUG
			// Useful for allowing debugging of Bloom while running the harvester
			//MessageBox.Show("Attach debugger now");
#endif
			var exitCode = CreateArtifactsExitCode.Success;

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

						exitCode |= RenameBloomDigitalFiles(unzippedBloomDigitalOutputPath);
					}
				}
			}

			return exitCode;
		}

		/// <summary>
		/// Renames the {title}.htm HTM file to index.htm instead
		/// </summary>
		/// <param name="bookDirectory"></param>
		private static CreateArtifactsExitCode RenameBloomDigitalFiles(string bookDirectory)
		{
			string originalHtmFilePath = Bloom.Book.BookStorage.FindBookHtmlInFolder(bookDirectory);

			Debug.Assert(RobustFile.Exists(originalHtmFilePath), "Book HTM not found: " + originalHtmFilePath);
			if (!RobustFile.Exists(originalHtmFilePath))
				return CreateArtifactsExitCode.BookHtmlNotFound;

			string newHtmFilePath = Path.Combine(bookDirectory, $"index.htm");
			RobustFile.Copy(originalHtmFilePath, newHtmFilePath);
			RobustFile.Delete(originalHtmFilePath);
			return CreateArtifactsExitCode.Success;
		}

		/// <summary>
		/// Creates an ePub file at the location specified by parameters
		/// </summary>
		/// <param name="parameters">BookPath and epubOutputPath should be set.</param>
		/// <param name="control">The epub code needs a control that goes back to the main thread, in order to run some tasks that need to be on the main thread</param>
		public static void CreateEpubArtifact(CreateArtifactsParameters parameters, Control control)
		{
			if (String.IsNullOrEmpty(parameters.EpubOutputPath))
			{
				return;
			}

			string directoryName = Path.GetDirectoryName(parameters.EpubOutputPath);
			Directory.CreateDirectory(directoryName);	// Ensures that the directory exists

			BookServer bookServer = _projectContext.BookServer;
			BookThumbNailer thumbNailer = _projectContext.ThumbNailer;
			var maker = new EpubMaker(thumbNailer, bookServer);
			maker.ControlForInvoke = control;

			maker.Book = _book;
			maker.Unpaginated = true; // so far they all are
			maker.OneAudioPerPage = true; // default used in EpubApi
										  // Enhance: maybe we want book to have image descriptions on page? use reader font sizes?
			
			// Make the epub
			maker.SaveEpub(parameters.EpubOutputPath, new NullWebSocketProgress());
		}

		public static void CreateThumbnailArtifact(CreateArtifactsParameters parameters)
		{
			if (String.IsNullOrWhiteSpace(parameters.ThumbnailOutputInfoPath))
			{
				return;
			}

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
				string thumbnailPath = BookThumbNailer.GenerateCoverImageOfRequestedMaxSize(_book, height);

				AppendPathIfExists(thumbnailPath, outputPaths);
			}

			string shareThumbnail = BookThumbNailer.GenerateSocialMediaSharingThumbnail(_book);
			AppendPathIfExists(shareThumbnail, outputPaths);

			using (var writer = new StreamWriter(parameters.ThumbnailOutputInfoPath, append: false))
			{
				foreach (var path in outputPaths)
				{
					writer.WriteLine(path);
				}
			}
		}

		private static void AppendPathIfExists(string path, IList<string> listToAppendTo)
		{
			if (RobustFile.Exists(path))
			{
				listToAppendTo.Add(path);
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
