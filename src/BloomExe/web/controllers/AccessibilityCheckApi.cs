using System;
using System.IO;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish.AccessibilityChecker;
using BloomTemp;
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
		private readonly NavigationIsolator _isolator;
		private readonly BookServer _bookServer;
		public const string kApiUrlPart = "accessibilityCheck/";
		private AccessibilityCheckWindow _accessibilityCheckerWindow = null;
		private static string _epubPath;
		
		public AccessibilityCheckApi(Bloom.Publish.AccessibilityChecker.AccessibilityCheckWindow.Factory createAccessibilityChecker)
		{
			// TODO: call this from autofac setup somehow
			AccessibilityCheckWindow.StaticSetFactory(createAccessibilityChecker);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "setEpubPath", request =>
			{
				_epubPath = request.RequiredPostString();
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "showAccessibilityChecker", request =>
			{
				AccessibilityCheckWindow.StaticShow();
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart+"audioForAllText", request =>
			{
				request.ReplyWithText("not implemented");
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "descriptionsForAllImages", request =>
			{
				request.ReplyWithText("not implemented");
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "audioForAllImageDescriptions", request =>
			{
				request.ReplyWithText("not implemented");
			}, false);

			//todo: this will have to become async to work on large books on slow computers
			server.RegisterEndpointHandler(kApiUrlPart + "aceByDaisyReportUrl", request =>
			{
				if (string.IsNullOrEmpty(_epubPath) || !File.Exists((_epubPath)))
				{
					request.ReplyWithHtml("Please save the epub first.");
					return;
				}
				// while HOME Is available from windows cmd prompt, this fails: var homePath = Environment.GetEnvironmentVariable(("HOME"));
				var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				// not at all clear we're going to ship to users this way, but the following will help testers
				var nodeModulesDirectory = Path.Combine(homePath, "node_modules");
				var instructionsHtml= "Please follow the <a href= 'https://inclusivepublishing.org/toolbox/accessibility-checker/getting-started/' >steps under \"Installation\"</a>";
				if (!Directory.Exists((nodeModulesDirectory)))
				{
					request.ReplyWithHtml($"Could could not find {nodeModulesDirectory}. {instructionsHtml}");
					return;
				}
				// not at all clear we're going to ship to users this way, but the following will help testers
				var daisyCliDirectory = Path.Combine(homePath, "node_modules/@daisy/ace-cli/bin/");
				if (!Directory.Exists((daisyCliDirectory)))
				{
					request.ReplyWithHtml($"Could could not find daisy-ace at {daisyCliDirectory}. {instructionsHtml}");
					return;
				}
				var progress = new NullProgress();
				var runner = new CommandLineRunner();
				var reportDirectory = Path.Combine(System.IO.Path.GetTempPath(), "daisy-ace-report");
				TemporaryFolder.DeleteFolderThatMayBeInUse(reportDirectory);
				var arguments = $"ace.js  -o \"{reportDirectory}\" \"{_epubPath}\"";
				const int kSecondsBeforeTimeout = 60;
				var res = runner.Start("node", arguments, Encoding.UTF8, daisyCliDirectory, kSecondsBeforeTimeout, progress,
					(dummy) => { });
				if (res.DidTimeOut)
				{
					request.ReplyWithHtml($"Daisy Ace timed out after {kSecondsBeforeTimeout} seconds.");
					return;
				}
				var answerPath = Path.Combine(reportDirectory, "report.html");
				request.ReplyWithText("/bloom/"+answerPath);
			}, false);
		}

		public static void SetEpubPath(string previewSrc)
		{
			_epubPath = previewSrc;
		}
	}
}
