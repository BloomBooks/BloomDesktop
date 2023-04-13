using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using CommandLine;
using L10NSharp;

namespace Bloom.CLI
{
	/// <summary>
	/// This command downloads the specified book to the specified destination.
	/// See DownloadBookOptions for the expected options.
	/// </summary>
	class DownloadBookCommand
	{
		public static Task<int> HandleSilentDownload(DownloadBookOptions options)
		{
			// This task will be all the program does. We need to do enough setup so that
			// the download code can work, then tear it down.
			Program.SetUpErrorHandling();
			try
			{
				using (var applicationContainer = new ApplicationContainer())
				{
					Program.SetUpLocalization(applicationContainer);
					LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
					var downloader = new BookDownload(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
						 new BookDownloadStartingEvent()); /*not hooked to anything*/;
					// Since Bloom is not a normal console app, when run from a command line, the new command prompt
					// appears at once. The extra newlines here are attempting to separate this from our output.
					Console.WriteLine("\nstarting download");
					downloader.HandleDownloadWithoutProgress(options.Url, options.DestinationPath);
					Console.WriteLine(("\ndownload complete\n"));
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
// (using the beta of commandline 2.0, as of Bloom 3.8)

[Verb("download", HelpText = "Download a book to a specified location")]
public class DownloadBookOptions
{
	[Option("url", HelpText = "url of folder on S3, e.g., https://s3.amazonaws.com/BloomLibraryBooks/someone@example.com/0a2745dd-ca98-47ea-8ba4-2cabc67022e5", Required = true)]
	public string Url { get; set; }

	[Option("dest", HelpText = "destination path in which to place book folder (excludes book title)", Required = true)]
	public string DestinationPath { get; set; }
}
