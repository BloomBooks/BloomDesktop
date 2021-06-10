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
			if (String.IsNullOrWhiteSpace(options.Dest))
				options.Dest = UploadDestination.DryRun;
			else
				options.Dest = options.Dest.ToLowerInvariant();
			switch (options.Dest)
			{
				case UploadDestination.DryRun:
				case UploadDestination.Development:
				case UploadDestination.Production:
					break;
				default:
					Console.WriteLine($"Error: if present, upload destination (-d) must be one of {UploadDestination.DryRun}, {UploadDestination.Development}, or {UploadDestination.Production}");
					return 1;
			}
			BookTransfer.Destination = options.Dest;    // must be set before calling SetupErrorHandling() (or BloomParseClient constructor)

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
					switch (options.Dest)
					{
						case UploadDestination.DryRun:
							Console.WriteLine($"\nThe following actions would happen if you set destination to '{(BookTransfer.UseSandboxByDefault ? UploadDestination.Development : UploadDestination.Production)}'.");
							break;
						case UploadDestination.Development:
							Console.WriteLine("\nThe upload will go to dev.bloomlibrary.org.");
							break;
						case UploadDestination.Production:
							Console.WriteLine("\nThe upload will go to bloomlibrary.org.");
							break;
					}
					Console.WriteLine("\nstarting upload");
					transfer.UploadFolder(applicationContainer, options);
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

[Verb("upload", HelpText = "Upload a book or folder of books to bloomlibrary.org.  A folder that contains exactly one .htm file is interpreted as a book and uploaded." +
	"  Other folders are searched recursively for children that appear to be Bloom books.  The parent folder of a Bloom book is searched for a .bloomCollection file" +
	" and, if one is found, the book is treated as part of that collection (e.g., for determining vernacular language).  If no .bloomCollection file is found there," +
	" it uses whatever collection was last open.\n"+
	"When a book is uploaded, that fact is recorded in a local file named BloomBulkUploadLog.txt in the folder given to the upload command.  Books will not be" +
	" uploaded again unless either the reference to the book is removed from that file or that file is itself deleted.  Nothing on the website prevents books from" +
	" being overwritten by being uploaded again."
	)]
public class UploadParameters
{
	[Value(0, MetaName = "path", HelpText = "Specify the path to a folder containing books to upload at some level within.", Required = true)]
	public string Path { get; set; }

	[Option('x', "excludeNarrationAudio", HelpText = "Exclude narration audio files from upload. (The default is to upload audio files.)", Required = false)]
	public bool ExcludeNarrationAudio { get; set; }

	[Option('u', "user", HelpText = "Specify the Bloom Library user for the upload.", Required = false)]
	public string UploadUser { get; set; }

	[Option('p', "password", HelpText = "Specify the password for the given upload user.", Required = false)]
	public string UploadPassword { get; set; }

	[Option('T', "preserveThumbnails", HelpText = "Preserve any existing thumbnail images: don't try to recreate them.", Required = false)]
	public bool PreserveThumbnails { get; set; }

	[Option('d', "destination", HelpText = "If present, this must be one of dry-run, dev, or production. 'dry-run' (the default) will just print out what would happen. 'dev' will upload to dev.bloomlibrary.org (you will need to use an account from there). 'production' will upload to bloomlibrary.org", Required = false)]
	public string Dest { get; set; }
}

/// <summary>
/// Static class containing the list of possible upload destinations.
/// </summary>
/// <remarks>
/// C# doesn't have string enums.  This is a close approximation for our purposes.
/// </remarks>
public static class UploadDestination
{
	public const string DryRun = "dry-run";
	public const string Development = "dev";
	public const string Production = "production";
}
