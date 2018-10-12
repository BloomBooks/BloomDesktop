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
		public static int Handle(UploadParameters options)
		{
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
					transfer.UploadFolder(options.Path, applicationContainer, options.ExcludeNarrationAudio);
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
	[Value(0, MetaName = "path", HelpText = "Path to the book or folder, determined automatically.", Required = true)]
	public string Path { get; set; }

	[Option('x', "excludeNarrationAudio", HelpText = "Option excludes narration audio files from upload", Required = false)]
	public bool ExcludeNarrationAudio { get; set; }
}
