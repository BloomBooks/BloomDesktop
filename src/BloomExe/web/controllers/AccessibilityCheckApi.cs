using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish.AccessibilityChecker;
using Bloom.Publish.Epub;
using SIL.CommandLineProcessing;
using SIL.PlatformUtilities;
using SIL.Progress;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Used by two screens:
	/// 1) Epub screen uses it to launch the Accessibility Checks Window
	/// 2) The Accessibility Checks Typescript uses it for... everything.
	/// </summary>
	public class AccessibilityCheckApi
	{
		// Define a socket to signal the client window to refresh
		private readonly BloomWebSocketServer _webSocketServer;
		private PublishEpubApi _epubApi;

		private readonly NavigationIsolator _isolator;
		private readonly BookServer _bookServer;
		private IWebSocketProgress _webSocketProgress;

		public const string kApiUrlPart = "accessibilityCheck/";

		// This goes out with our messages and, on the client side (typescript), messages are filtered
		// down to the context (usualy a screen) that requested them.
		private const string kWebSocketContext = "a11yChecklist"; // must match what is in accsesibilityChecklist.tsx

		// must match what's in the typescript
		private const string kBookSelectionChanged = "bookSelectionChanged";

		// must match what's in the typescript
		private const string kBookContentsMayHaveChanged = "bookContentsMayHaveChanged";

		// must match what's in the typescript
		private const string kWindowActivated = "a11yChecksWindowActivated"; // REVIEW later... are we going to use this event?

		private bool _simulateCataracts;
		private bool _simulateColorBlindness;

		// These options must match the ones used in accessibileImage.tsx
		public enum KindOfColorBlindness
		{
			RedGreen, BlueYellow, Complete
		}

		private KindOfColorBlindness _kindOfColorBlindness;

		public AccessibilityCheckApi(BloomWebSocketServer webSocketServer, BookSelection bookSelection,
			BookRenamedEvent bookRenamedEvent, BookSavedEvent bookSavedEvent, EpubMaker.Factory epubMakerFactory,
			PublishEpubApi epubApi)
		{
			_webSocketServer = webSocketServer;
			var progress = new WebSocketProgress(_webSocketServer, kWebSocketContext);
			_webSocketProgress = progress.WithL10NPrefix("AccessibilityCheck.");
			_epubApi = epubApi;
			bookSelection.SelectionChanged += (unused1, unused2) =>
			{
				_webSocketServer.SendEvent(kWebSocketContext, kBookSelectionChanged);
			};
			// we get this when the book is renamed
			bookRenamedEvent.Subscribe((book) =>
			{
				RefreshClient();
			});
			// we get this when the contents of the page might have changed
			bookSavedEvent.Subscribe((book) =>
			{
				RefreshClient();
			});
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "bookName", request =>
			{
				request.ReplyWithText(request.CurrentBook.TitleBestForUserDisplay);
			}, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "showAccessibilityChecker", request =>
			{
				AccessibilityCheckWindow.StaticShow(()=>_webSocketServer.SendEvent(kWebSocketContext, kWindowActivated));
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "descriptionsForAllImages", request =>
			{
				var problems = AccessibilityCheckers.CheckDescriptionsForAllImages(request.CurrentBook);
				var resultClass = problems.Any() ? "failed" : "passed";
				request.ReplyWithJson(new {resultClass = resultClass, problems = problems});
			}, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "audioForAllImageDescriptions", request =>
			{
				var problems = AccessibilityCheckers.CheckAudioForAllImageDescriptions(request.CurrentBook);
				var resultClass = problems.Any() ? "failed" : "passed";
				request.ReplyWithJson(new { resultClass = resultClass, problems = problems });
			}, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "audioForAllText", request =>
			{
				var problems = AccessibilityCheckers.CheckAudioForAllText(request.CurrentBook);
				var resultClass = problems.Any() ? "failed" : "passed";
				request.ReplyWithJson(new { resultClass = resultClass, problems = problems });
			}, false);

			// Just a checkbox that the user ticks to say "yes, I checked this"
			// At this point, we don't have a way to clear that when the book changes.
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "noEssentialInfoByColor",
				request => request.CurrentBook.BookInfo.MetaData.A11y_NoEssentialInfoByColor,
				(request, b) => {
					request.CurrentBook.BookInfo.MetaData.A11y_NoEssentialInfoByColor = b;
					request.CurrentBook.Save();
				},
				false);

			// Just a checkbox that the user ticks to say "yes, I checked this"
			// At this point, we don't have a way to clear that when the book changes.
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "noTextIncludedInAnyImages",
				request => request.CurrentBook.BookInfo.MetaData.A11y_NoTextIncludedInAnyImages,
				(request, b) => {
					request.CurrentBook.BookInfo.MetaData.A11y_NoTextIncludedInAnyImages = b;
					request.CurrentBook.Save();
				},
				false);

			//enhance: this might have to become async to work on large books on slow computers
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "aceByDaisyReportUrl", request => { MakeAceByDaisyReport(request); },
				false, false
				);

			// A checkbox that the user ticks in the Accessible Image tool to request a preview
			// of how things might look with cataracts.
			// For now this doesn't seem worth persisting, except for the session so it sticks from page to page.
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "cataracts",
				request => _simulateCataracts,
				(request, b) => { _simulateCataracts = b; },
				false);
			// A checkbox that the user ticks in the Accessible Image tool to request a preview
			// of how things might look with color-blindness, and a set of radio buttons
			// for choosing different kinds of color-blindness.
			// For now these doesn't seem worth persisting, except for the session so it sticks from page to page.
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "colorBlindness",
				request => _simulateColorBlindness,
				(request, b) => { _simulateColorBlindness = b; },
				false);
			apiHandler.RegisterEnumEndpointHandler(kApiUrlPart + "kindOfColorBlindness",
				request => _kindOfColorBlindness,
				(request, kind) => _kindOfColorBlindness = kind, false);
		}

		private void MakeAceByDaisyReport(ApiRequest request)
		{
			if (!UrlLookup.CheckGeneralInternetAvailability(true))
			{
				_webSocketProgress.ErrorWithoutLocalizing("Sorry, you must have an internet connection in order to view the Ace by DAISY report.");
				request.Failed();
				return;
			}
			var daisyDirectory = FindAceByDaisyOrTellUser(request); // this should do the request.fail() if needed
			if (string.IsNullOrEmpty(daisyDirectory))
				return;

			var reportRootDirectory = Path.Combine(System.IO.Path.GetTempPath(), "daisy-ace-reports");
			// Do our best at clearing out previous runs.
			// This call is ok if the directory does not exist at all.
			SIL.IO.RobustIO.DeleteDirectoryAndContents(reportRootDirectory);
			// This call is ok if the above failed and it still exists
			Directory.CreateDirectory(reportRootDirectory);

			// was having a problem with some files from previous reports getting locked.
			// so give new folder names if needed
			var haveReportedError = false;
			var errorMessage = "Unknown Error";

			var epubPath = MakeEpub(reportRootDirectory, _webSocketProgress);
			// Try 3 times. It could be that this is no longer needed, but working on a developer
			// machine isn't proof.
			for (var i = 0; i < 3; i++)
				{
					var randomName = Guid.NewGuid().ToString();
					var reportDirectory = Path.Combine(reportRootDirectory, randomName);

					var arguments = $"ace.js --verbose -o \"{reportDirectory}\" \"{epubPath}\"";
					const int kSecondsBeforeTimeout = 60;
					var progress = new NullProgress();
					_webSocketProgress.MessageWithoutLocalizing("Running Ace by DAISY");

					ExecutionResult res = null;
					string ldpath = null;
					try
					{
						// Without this variable switching on Linux, the chrome inside ace finds the
						// wrong version of a library as part of our mozilla code.
						ldpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
						Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
						res = CommandLineRunner.Run("node", arguments, Encoding.UTF8, daisyDirectory, kSecondsBeforeTimeout, progress,
							(dummy) => { });
					}
					finally
					{
						// Restore the variable for our next geckofx browser to find.
						if (!String.IsNullOrEmpty(ldpath))
							Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", ldpath);
					}

					if (res.DidTimeOut)
					{
						errorMessage = $"Daisy Ace timed out after {kSecondsBeforeTimeout} seconds.";
						_webSocketProgress.ErrorWithoutLocalizing(errorMessage);
						continue;
					}

					var answerPath = Path.Combine(reportDirectory, "report.html");
					if (!File.Exists(answerPath))
					{
						// This hasn't been effectively reproduced, but there was a case where this would fail at least
						// half the time on a book, reproducable. That book had 2 pages pointing at placeholder.png,
						// and we were getting an error related to it being locked. So we deduce that ace was trying
						// to copy the file twice, at the same time (normal nodejs code is highly async).
						// Now the problem is not reproducable, but I'm leaving in this code that tried to deal with it.
						errorMessage = $"Exit code{res.ExitCode}{Environment.NewLine}" +
						               $"Standard Error{Environment.NewLine}{res.StandardError}{Environment.NewLine}" +
						               $"Standard Out{res.StandardOutput}";

						_webSocketProgress.ErrorWithoutLocalizing(errorMessage);

						continue; // something went wrong, try again
					}

				// The html client is set to treat a text reply as a url of the report. Make sure it's valid for being a URL.
				// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6197.
				request.ReplyWithText("/bloom/" + answerPath.EscapeCharsForHttp().Replace(Path.DirectorySeparatorChar, '/'));
				return;
			}

			// If we get this far, we give up.
			ReportErrorAndFailTheRequest(request, errorMessage);
		}

		private string MakeEpub(string parentDirectory, IWebSocketProgress progress)
		{
			var settings = new EpubPublishUiSettings();
			_epubApi.GetEpubSettingsForCurrentBook(settings);
			var path = Path.Combine(parentDirectory, Guid.NewGuid().ToString() + ".epub");
			_epubApi.UpdateAndSave(settings, path, true, _webSocketProgress.WithL10NPrefix("PublishTab.Epub."));
			return path;
		}

		private string FindAceByDaisyOrTellUser(ApiRequest request)
		{
			_webSocketProgress.Message("FindingAce", "Finding Ace by DAISY on this computer...");
			var whereProgram = Platform.IsWindows ? "where" : "which";
			var npmFileName = Platform.IsWindows ? "npm.cmd" : "npm";
			var whereResult = CommandLineRunner.Run(whereProgram, npmFileName, Encoding.ASCII, "", 2, new NullProgress());
			if (!String.IsNullOrEmpty(whereResult.StandardError))
			{
				_webSocketProgress.ErrorWithoutLocalizing(whereResult.StandardError);
			}
			if (!whereResult.StandardOutput.Contains(npmFileName))
			{
				ReportErrorAndFailTheRequest(request, whereResult, "Could not find npm.");
				return null;
			}

			var fullNpmPath = whereResult.StandardOutput.Split('\n')[0].Trim();
			// note: things like nvm will mess with where the global node_modules lives. The best way seems to be
			// to ask npm:
			var result = CommandLineRunner.Run(npmFileName, "root -g", Encoding.ASCII, Path.GetDirectoryName(fullNpmPath), 10,
				new NullProgress());

			const string kCoreError = "Could not get \"npm -g root\" to work. Is Node & npm installed and working?";
			if (result == null)
			{
				// I don't think this could happen, but *something* was null for Sue.
				ReportErrorAndFailTheRequest(request, whereResult, $"{kCoreError} CommandLineRunner.Run() returned null.");
				return null;
			}
			if (!string.IsNullOrWhiteSpace(result.StandardError))
			{
				ReportErrorAndFailTheRequest(request, whereResult, $"{kCoreError} <br>StandardError:<br>" + result.StandardError);
				return null;
			}
			if (result.StandardOutput == null)
			{
				ReportErrorAndFailTheRequest(request, whereResult, $"{kCoreError} StandardOutput was null.");
				return null;
			}

			if (!result.StandardOutput.Contains("node_modules"))
			{
				ReportErrorAndFailTheRequest(request, whereResult, kCoreError);
				return null;
			}

			var nodeModulesDirectory = result.StandardOutput.Trim();

			if (!Directory.Exists((nodeModulesDirectory)))
			{
				ReportErrorAndFailTheRequest(request, whereResult, "Could not find global node_modules directory");
				return null;
			}

			// if they installed via npm install -g  @daisy/ace
			var daisyDirectory = Path.Combine(nodeModulesDirectory, "@daisy/ace/bin/");
			if (!Directory.Exists((daisyDirectory)))
			{
				// if they just installed via npm install -g  @daisy/ace-cli
				daisyDirectory = Path.Combine(nodeModulesDirectory, "@daisy/ace-cli/bin/");
				if (!Directory.Exists((daisyDirectory)))
				{
					ReportErrorAndFailTheRequest(request, whereResult, $"Could not find daisy-ace at {daisyDirectory}.");
					return null;
				}
			}
			_webSocketProgress.Message("FoundAce", "Found.");
			return daisyDirectory;
		}

		private void ReportErrorAndFailTheRequest(ApiRequest request, ExecutionResult commandLineResult, string error)
		{
			_webSocketProgress.ErrorWithoutLocalizing(commandLineResult.StandardError);
			_webSocketProgress.ErrorWithoutLocalizing(commandLineResult.StandardOutput);
			ReportErrorAndFailTheRequest(request, error);
		}

		private void ReportErrorAndFailTheRequest(ApiRequest request, string error)
		{
			_webSocketProgress.ErrorWithoutLocalizing(error);
			if (Platform.IsWindows)
			{
				_webSocketProgress.MessageWithoutLocalizing("Please follow <a href= 'https://inclusivepublishing.org/toolbox/accessibility-checker/getting-started/' >these instructions</a> to install the Ace by DAISY system on this computer.");
			}
			else
			{
				var programPath = System.Reflection.Assembly.GetEntryAssembly().ManifestModule.FullyQualifiedName;
				var folder = Path.GetDirectoryName(programPath);
				if (folder.EndsWith("/output/Debug") || folder.EndsWith("/output/Release"))
					folder = "";
				var scriptPath = Path.Combine(folder, "DistFiles", "InstallAce.sh");
				_webSocketProgress.MessageWithoutLocalizing("Please run the "+ scriptPath + " script to install the Ace by DAISY system on this Linux computer.  Do not use sudo to run this script: it already contains any needed sudo commands internally.");
			}
			request.Failed();
		}

		private void RefreshClient()
		{
			_webSocketServer.SendEvent(kWebSocketContext, kBookContentsMayHaveChanged);
		}
	}
}
