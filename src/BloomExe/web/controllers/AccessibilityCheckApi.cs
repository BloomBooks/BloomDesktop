using System;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish.AccessibilityChecker;
using SIL.Code;
using SIL.CommandLineProcessing;
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
		private const string kWebsocketId = "a11yChecklist";

		private readonly NavigationIsolator _isolator;
		private readonly BookServer _bookServer;
		public const string kApiUrlPart = "accessibilityCheck/";
		private static string _epubPath;

		public AccessibilityCheckApi(BloomWebSocketServer webSocketServer, BookSelection bookSelection, BookRefreshEvent bookRefreshEvent)
		{
			_webSocketServer = webSocketServer;
			bookSelection.SelectionChanged += (unused1, unused2) => RefreshClient();
			bookRefreshEvent.Subscribe((book) => RefreshClient());
		}
		
		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "setEpubPath", request =>
			{
				_epubPath = request.RequiredPostString();
			}, false);

			
			server.RegisterEndpointHandler(kApiUrlPart + "bookName", request =>
			{
				request.ReplyWithText(request.CurrentBook.TitleBestForUserDisplay);
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "showAccessibilityChecker", request =>
			{
				AccessibilityCheckWindow.StaticShow(RefreshClient);
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
			server.RegisterEndpointHandler(kApiUrlPart + "aceByDaisyReportUrl", request => { MakeAceByDaisyReport(request); }, false);
		}

		private static void MakeAceByDaisyReport(ApiRequest request)
		{
			if (string.IsNullOrEmpty(_epubPath) || !File.Exists((_epubPath)))
			{
				request.ReplyWithHtml("Please save the epub first.");
				return;
			}

			var daisyDirectory = FindAceByDaisyOrTellUser(request);
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
			for (var i = 0; i < 20; i++)
			{
				var randomName = Guid.NewGuid().ToString();
				var reportDirectory = Path.Combine(reportRootDirectory, randomName);

				var arguments = $"ace.js  -o \"{reportDirectory}\" \"{_epubPath}\"";
				const int kSecondsBeforeTimeout = 60;
				var progress = new NullProgress();
				var res = CommandLineRunner.Run("node", arguments, Encoding.UTF8, daisyDirectory, kSecondsBeforeTimeout, progress,
					(dummy) => { });
				if (res.DidTimeOut)
				{
					errorMessage = $"Daisy Ace timed out after {kSecondsBeforeTimeout} seconds.";
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

					if (!haveReportedError) // don't want to put up 50 toasts
					{
						haveReportedError = true;
						NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Ace By Daisy error",
							errorMessage);
					}

					continue; // something went wrong, try again
				}

				// The html client is set to treat a text reply as a url of the report
				request.ReplyWithText("/bloom/" + answerPath);
				return;
			}

			// If we get this far, we give up.
			// The html client is set to treat an html reply as an error message.
			request.ReplyWithHtml(errorMessage);
		}

		private static string FindAceByDaisyOrTellUser(ApiRequest request)
		{
			var instructionsHtml =
				"Please follow the <a href= 'https://inclusivepublishing.org/toolbox/accessibility-checker/getting-started/' >steps under \"Installation\"</a>";

			var whereResult = CommandLineRunner.Run("where", "npm.cmd", Encoding.ASCII, "", 2, new NullProgress());

			if (!whereResult.StandardOutput.Contains("npm.cmd"))
			{
				request.ReplyWithHtml($"Could could not find npm.<br/>{instructionsHtml}");
				return null;
			}

			var fullNpmPath = whereResult.StandardOutput.Split('\n')[0].Trim();
			// note: things like nvm will mess with where the global node_modules lives. The best way seems to be
			// to ask npm:
			var result = CommandLineRunner.Run("npm.cmd", "root -g", Encoding.ASCII, Path.GetDirectoryName(fullNpmPath), 10,
				new NullProgress());

			if (!result.StandardOutput.Contains("node_modules"))
			{
				request.ReplyWithHtml(
					$"Could could not get npm -g root to work. It said {result.StandardOutput} {result.StandardError}.<br/>{instructionsHtml}");
				return null;
			}

			var nodeModulesDirectory = result.StandardOutput.Trim();

			if (!Directory.Exists((nodeModulesDirectory)))
			{
				request.ReplyWithHtml(
					$"Could could not find global node_modules directory at {nodeModulesDirectory}.<br/>{instructionsHtml}");
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
					request.ReplyWithHtml($"Could could not find daisy-ace at {daisyDirectory}.<br/>{instructionsHtml}");
					return null;
				}
			}

			return daisyDirectory;
		}

		public static void SetEpubPath(string previewSrc)
		{
			_epubPath = previewSrc;
		}

		private void RefreshClient()
		{
			_webSocketServer.Send(kWebsocketId, "refresh");
		}
	}
}
