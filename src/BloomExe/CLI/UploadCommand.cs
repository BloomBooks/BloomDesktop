using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using CommandLine;
using L10NSharp;

namespace Bloom.CLI
{
	/// <summary>
	/// Uploads a book or folder of books to BloomLibrary
	/// usage:
	///		upload [options] {path to book or collection directory}
	/// </summary>
	class UploadCommand
	{
		public static bool IsUploading;

		public static Task<int> Handle(UploadParameters options)
		{
			try
			{
				Console.OutputEncoding = Encoding.UTF8;
			}
			catch (Exception)
			{
				// swallow. The above throws a handle error when run in Visual Studio
			}

			IsUploading = true;
			// -u user, and <path> are all required, so they must contain strings.
			// -d destination has a default value, so it also must contain a string.
			options.Path = options.Path.TrimEnd(new[] { '/', '\\', System.IO.Path.PathSeparator });	// remove any trailing slashes
			// validate the value for the upload destination.
			options.Dest = options.Dest.ToLowerInvariant();
			switch (options.Dest)
			{
				case UploadDestination.Development:
				case UploadDestination.Production:
					break;
				default:
					Console.WriteLine($"Error: Upload destination (-d) must be one of {UploadDestination.Development} or {UploadDestination.Production}");
					return Task.FromResult(1);
			}
			BookUpload.Destination = options.Dest;    // must be set before calling SetupErrorHandling() (or BloomParseClient constructor)
			BookUpload.IsDryRun = options.DryRun;	  // must be set before calling SetupErrorHandling() (or BloomParseClient constructor)

			// This task will be all the program does. We need to do enough setup so that
			// the upload code can work, then tear it down.
			Program.SetUpErrorHandling();
			try
			{
				using (var applicationContainer = new ApplicationContainer())
				{
					Program.SetUpLocalization(applicationContainer);
					LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
					var singleBookUploader = new BookUpload(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
						applicationContainer.BookThumbNailer);
					var uploader = new BulkUploader(singleBookUploader);


					if (options.DryRun)
					{
						Console.WriteLine($"\nThe following actions would happen if --dry-run were disabled:");
					}

					// Since Bloom is not a normal console app, when run from a command line, the new command prompt
					// appears at once. The extra newlines here are attempting to separate this from our output.
					switch (options.Dest)
					{
						case UploadDestination.Development:
							Console.WriteLine("\nThe upload will go to dev.bloomlibrary.org.");
							break;
						case UploadDestination.Production:
							Console.WriteLine("\nThe upload will go to bloomlibrary.org.");
							break;
					}
					Console.WriteLine("\nStarting upload...");
					uploader.BulkUpload(applicationContainer, options);
					Console.WriteLine(("\nBulk upload complete.\n"));
				}
				return Task.FromResult(0);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return Task.FromResult(1);
			}
		}
	}
}

// Used with https://github.com/gsscoder/commandline, which we get via nuget.

// Note: you only see this help via "bloom --help", not via "bloom upload --help".

[Verb("upload", HelpText = "Upload collections of books to bloomlibrary.org. Cannot be used to upload only a single book. Given a folder that is a collection, this will upload the it. Given a folder that is not a collection, it will search for descendant folders that contain collections.\r\nExample: bloom upload \"c:\\foo\\all my collections\". (Do not use a trailing slash). " +
	" Normally, this command will skip books that have not changed.\r\nIn order to authenticate, you must first log in with the Bloom UI:Publish:Share on the Web, then quit."
	)]
public class UploadParameters
{
	[Value(0, MetaName = "path", HelpText = "Specify the path to a folder containing books to upload at some level within.", Required = true)]
	public string Path { get; set; }

	[Option('e', "excludeMusicAudio", HelpText = "Exclude music (background) audio files from upload.  (The default is to upload music files.)", Required = false)]
	public bool ExcludeMusicAudio { get; set; }

	[Option('u', "user", HelpText = "Specify the email account for the upload. Must match the currently logged in email from Bloom:Publish:Upload (share on the web) screen.", Required = true)]
	public string UploadUser { get; set; }

	[Option('T', "preserveThumbnails", HelpText = "Preserve any existing thumbnail images: don't try to recreate them.", Required = false)]
	public bool PreserveThumbnails { get; set; }

	[Option('d', "destination", HelpText = "'dev' will upload to dev.bloomlibrary.org (you will need to use an account from there). 'production' will upload to bloomlibrary.org", Required = true)]
	public string Dest { get; set; }

	[Option("dry-run", Default=false, HelpText = "If present, performs a dry-run (will just print out what would happen).", Required = false)]
	public bool DryRun { get; set; }

	[Option('F', "force", HelpText = "Force the upload even if existing .lastUploadInfo content indicates that the book has already been uploaded.", Required = false)]
	public bool ForceUpload { get; set; }
}

/// <summary>
/// Static class containing the list of possible upload destinations.
/// </summary>
/// <remarks>
/// C# doesn't have string enums.  This is a close approximation for our purposes.
/// </remarks>
public static class UploadDestination
{
	public const string Development = "dev";
	public const string Production = "production";
}
