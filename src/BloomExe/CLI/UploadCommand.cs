using System;
using System.Diagnostics;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using CommandLine;
using L10NSharp;

namespace Bloom.CLI
{
	/// <summary>
	/// Uploads a book or folder of books to BloomLibrary
	/// usage:
	///		upload [--excludeNarrationAudio/-x] {path to book or collection directory}
	/// </summary>
	class UploadCommand
	{
		public static bool IsUploading;

		public static int Handle(UploadParameters options)
		{
			bool valid = true;
			if (String.IsNullOrWhiteSpace(options.UploadUser))
				valid = String.IsNullOrWhiteSpace(options.UploadPassword);
			else
				valid = !String.IsNullOrWhiteSpace(options.UploadPassword);
			if (!valid)
			{
				Console.WriteLine("Error: upload -u user and -p password must be used together");
				return 1;
			}
			IsUploading = true;

			// This task will be all the program does. We need to do enough setup so that
			// the upload code can work, then tear it down.
			Program.SetUpErrorHandling();
			try
			{
				using (var applicationContainer = new ApplicationContainer())
				{
					Program.SetUpLocalization(applicationContainer);
					Browser.SetUpXulRunner();
					Browser.XulRunnerShutdown += Program.OnXulRunnerShutdown;
					LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
					var transfer = new BookTransfer(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
						applicationContainer.BookThumbNailer, new BookDownloadStartingEvent());

					// Since Bloom is not a normal console app, when run from a command line, the new command prompt
					// appears at once. The extra newlines here are attempting to separate this from our output.
					Console.WriteLine("\nstarting upload");
					transfer.UploadFolder(options.Path, applicationContainer, options.ExcludeNarrationAudio, options.UploadUser, options.UploadPassword, options.SingleBookshelfLevel, options.PreserveThumbnails);
					Console.WriteLine(("\nupload complete\n"));
				}
				return 0;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return 1;
			}
		}
	}
}

// Used with https://github.com/gsscoder/commandline, which we get via nuget.
// (using the beta of commandline 2.0, as of Bloom 3.8)

[Verb("upload", HelpText = "Upload a book or folder of books to bloomlibrary.org.")]
public class UploadParameters
{
	[Value(0, MetaName = "path", HelpText = "Path to a folder containing books to upload at some level within.  The two directory levels beneath the given folder are used to determine the bookshelf name and possibly the sub-level name.", Required = true)]
	public string Path { get; set; }

	[Option('x', "excludeNarrationAudio", HelpText = "Exclude narration audio files from upload (default is to upload audio files)", Required = false)]
	public bool ExcludeNarrationAudio { get; set; }

	[Option('u', "user", HelpText = "Bloomlibrary user for the upload (default is the local Bloom's most recent upload user)", Required = false)]
	public string UploadUser { get; set; }

	[Option('p', "password", HelpText = "Password for the given upload user (default is the local Bloom's most recent upload password)", Required = false)]
	public string UploadPassword { get; set; }

	[Option('s', "singleBookshelfLevel", HelpText = "Restrict bookshelf name to only the top directory level immediately under the path folder.  (default limit is 2 levels)", Required = false)]
	public bool SingleBookshelfLevel { get; set; }

	[Option('T', "preserveThumbnails", HelpText = "Preserve any existing thumbnail images: don't try to recreate them.", Required = false)]
	public bool PreserveThumbnails { get; set; }
}

