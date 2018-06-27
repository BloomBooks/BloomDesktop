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
		private readonly EpubMaker.Factory _epubMakerFactory;
		private const string kWebsocketId = "a11yChecklist";

		private readonly NavigationIsolator _isolator;
		private readonly BookServer _bookServer;
		private WebSocketProgress _webSocketProgress;
		public const string kApiUrlPart = "accessibilityCheck/";

		// must match what's in the typescript
		private const string kBookSelectionChanged = "bookSelectionChanged";

		// must match what's in the typescript
		private const string kBookContentsMayHaveChanged = "bookContentsMayHaveChanged";

		// must match what's in the typescript
		private const string kWindowActivated = "a11yChecksWindowActivated"; // REVIEW later... are we going to use this event?


		public AccessibilityCheckApi(BloomWebSocketServer webSocketServer, BookSelection bookSelection,
									BookRefreshEvent bookRefreshEvent, EpubMaker.Factory epubMakerFactory)
		{
			_webSocketServer = webSocketServer;
			_webSocketProgress = new WebSocketProgress(_webSocketServer);
			_epubMakerFactory = epubMakerFactory;
			bookSelection.SelectionChanged += (unused1, unused2) => _webSocketServer.Send(kWebsocketId, kBookSelectionChanged);
			bookRefreshEvent.Subscribe((book) => RefreshClient());
		}
		
		public void RegisterWithServer(EnhancedImageServer server)
		{	
			server.RegisterEndpointHandler(kApiUrlPart + "bookName", request =>
			{
				request.ReplyWithText(request.CurrentBook.TitleBestForUserDisplay);
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "showAccessibilityChecker", request =>
			{
				AccessibilityCheckWindow.StaticShow(()=>_webSocketServer.Send(kWebsocketId, kWindowActivated));
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "descriptionsForAllImages", request =>
			{
				var problems = AccessibilityCheckers.CheckDescriptionsForAllImages(request.CurrentBook);
				var resultClass = problems.Any() ? "failed" : "passed";
				request.ReplyWithJson(new {resultClass = resultClass, problems = problems});
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "audioForAllImageDescriptions", request =>
			{
				var problems = AccessibilityCheckers.CheckAudioForAllImageDescriptions(request.CurrentBook);
				var resultClass = problems.Any() ? "failed" : "passed";
				request.ReplyWithJson(new { resultClass = resultClass, problems = problems });
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "audioForAllText", request =>
			{
				var problems = AccessibilityCheckers.CheckAudioForAllText(request.CurrentBook);
				var resultClass = problems.Any() ? "failed" : "passed";
				request.ReplyWithJson(new { resultClass = resultClass, problems = problems });
			}, false);

			// Just a checkbox that the user ticks to say "yes, I checked this"
			// At this point, we don't have a way to clear that when the book changes.
			server.RegisterBooleanEndpointHandler(kApiUrlPart + "noEssentialInfoByColor",
				request => request.CurrentBook.BookInfo.MetaData.A11y_NoEssentialInfoByColor,
				(request, b) => {
					request.CurrentBook.BookInfo.MetaData.A11y_NoEssentialInfoByColor = b;
					request.CurrentBook.Save();
				},
				false);

			// Just a checkbox that the user ticks to say "yes, I checked this"
			// At this point, we don't have a way to clear that when the book changes.
			server.RegisterBooleanEndpointHandler(kApiUrlPart + "noTextIncludedInAnyImages",
				request => request.CurrentBook.BookInfo.MetaData.A11y_NoTextIncludedInAnyImages,
				(request, b) => {
					request.CurrentBook.BookInfo.MetaData.A11y_NoTextIncludedInAnyImages = b;
					request.CurrentBook.Save();
				},
				false);
			
			//enhance: this might have to become async to work on large books on slow computers
			server.RegisterEndpointHandler(kApiUrlPart + "aceByDaisyReportUrl", request => { MakeAceByDaisyReport(request); },
				true // <-- ui thread needed to make epub for some reason.
					 // This messes with our ability to make progress show up.
					 // Card for fixing the epub maker is BL-6122 
				);
		}

		private void MakeAceByDaisyReport(ApiRequest request)
		{
			if (!UrlLookup.CheckGeneralInternetAvailability(true))
			{
				_webSocketProgress.ErrorWithoutLocalizing("Sorry, you must have an internet connection in order to view the Ace by Daisy report.");
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
			
			var epubPath = MakeEpub(request, reportRootDirectory, _webSocketProgress);
			// Try 3 times. It could be that this is no longer needed, but working on a developer
			// machine isn't proof.
			for (var i = 0; i < 3; i++)
			{
				var randomName = Guid.NewGuid().ToString();
				var reportDirectory = Path.Combine(reportRootDirectory, randomName);

				var arguments = $"ace.js  -o \"{reportDirectory}\" \"{epubPath}\"";
				const int kSecondsBeforeTimeout = 60;
				var progress = new NullProgress();
				_webSocketProgress.MessageWithoutLocalizing("Running Ace by Daisy");
			
				var res = CommandLineRunner.Run("node", arguments, Encoding.UTF8, daisyDirectory, kSecondsBeforeTimeout, progress,
					(dummy) => { });
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

				// The html client is set to treat a text reply as a url of the report
				request.ReplyWithText("/bloom/" + answerPath);
				return;
			}

			// If we get this far, we give up.
			ReportErrorAndFailTheRequest(request, errorMessage);
		}

		private string MakeEpub(ApiRequest request, string parentDirectory, IWebSocketProgress progress)
		{
			var maker = _epubMakerFactory();
			maker.Book = request.CurrentBook;
			var path = Path.Combine(parentDirectory, Guid.NewGuid().ToString() + ".epub");
			maker.SaveEpub(path, progress);
			return path;
		}

		private string FindAceByDaisyOrTellUser(ApiRequest request)
		{
			_webSocketProgress.MessageWithoutLocalizing("Finding Ace by Daisy on this computer...");
			var whereResult = CommandLineRunner.Run("where", "npm.cmd", Encoding.ASCII, "", 2, new NullProgress());
			if (!String.IsNullOrEmpty(whereResult.StandardError))
			{
				_webSocketProgress.ErrorWithoutLocalizing(whereResult.StandardError);
			}
			if (!whereResult.StandardOutput.Contains("npm.cmd"))
			{
				ReportErrorAndFailTheRequest(request, whereResult, "Could could not find npm.");
				return null;
			}

			var fullNpmPath = whereResult.StandardOutput.Split('\n')[0].Trim();
			// note: things like nvm will mess with where the global node_modules lives. The best way seems to be
			// to ask npm:
			var npmFileName = Platform.IsWindows ? "npm.cmd" : "npm";
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

			if (result?.StandardOutput == null || !result.StandardOutput.Contains("node_modules"))
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
					ReportErrorAndFailTheRequest(request, whereResult, $"Could could not find daisy-ace at {daisyDirectory}.");
					return null;
				}
			}
			_webSocketProgress.MessageWithoutLocalizing("Found.");
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
			_webSocketProgress.MessageWithoutLocalizing("Please follow <a href= 'https://inclusivepublishing.org/toolbox/accessibility-checker/getting-started/' >these instructions</a> to install the Ace By Daisy system on this computer.");
			request.Failed();
		}

		private void RefreshClient()
		{
			_webSocketServer.Send(kWebsocketId, kBookContentsMayHaveChanged);
		}
	}
}
