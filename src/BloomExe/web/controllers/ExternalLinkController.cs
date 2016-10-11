using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Api;
using L10NSharp;
using Microsoft.Win32;
using SIL.IO;

namespace Bloom.web
{
	/// <summary>
	/// Handles GET requests to open a page, shipped with Bloom in an external browser.
	/// For other web resources, just use http/https and the c# Browser class will intercept
	/// and open the browser.
	/// 
	/// For html pages shipped with bloom, use this controller by writing
	/// <a href='/api/externalLink/blah/foo.html#fragment=idOfSomeSectionOfFoo'></a>
	/// </summary>
	public class ExternalLinkController
	{
		const string kPrefix = "externalLink";
		public static void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kPrefix+"/.*", ExternalLinkController.HandleRequest);
		}

		/// <summary>
		/// Handles a url starting with api/kPrefix by stripping off that prefix, searching for the file
		/// named in the remainder of the url, and opening it in some browser (passing on any anchor specified).
		/// </summary>
		public static void HandleRequest(ApiRequest request)
		{
			//NB: be careful not to lose case, as at least chrome is case-sensitive with anchors (e.g. #ChoiceOfTopic)
			var localPath = Regex.Replace(request.LocalPath(), "api/"+ kPrefix+"/", "", RegexOptions.IgnoreCase);
			var langCode = LocalizationManager.UILanguageId;
			var completeEnglishPath = FileLocator.GetFileDistributedWithApplication(localPath);
			var completeUiLangPath = completeEnglishPath.Replace("-en.htm", "-" + langCode + ".htm");

			string url;
			if (langCode != "en" && RobustFile.Exists(completeUiLangPath))
				url = completeUiLangPath;
			else
				url = completeEnglishPath;
			var cleanUrl = url.Replace("\\", "/"); // allows jump to file to work


			//we would like to get something like foo.htm#secondPart but the browser strips off that fragment part
			//so we require that to be written as foo.htm?fragment=secondPart so it gets to us, then we convert
			//it back to the normal format before sending it to a parser
			if (!string.IsNullOrEmpty(request.Parameters["fragment"]))
			{
				cleanUrl += "#" + request.Parameters["fragment"];
				request.Parameters.Remove("fragment");
			}

			string browser = string.Empty;
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				// REVIEW: This opens HTML files in the browser. Do we have any non-html
				// files that this code needs to open in the browser? Currently they get
				// opened in whatever application the user has selected for that file type
				// which might well be an editor.
				browser = "xdg-open";
			}
			else
			{
				// If we don't provide the path of the browser, i.e. Process.Start(url + queryPart), we get file not found exception.
				// If we prepend "file:///", the anchor part of the link (#xxx) is not sent unless we provide the browser path too.
				// This is the same behavior when simply typing a url into the Run command on Windows.
				// If we fail to get the browser path for some reason, we still load the page, just without navigating to the anchor.
				string defaultBrowserPath;
				if (TryGetDefaultBrowserPathWindowsOnly(out defaultBrowserPath))
				{
					browser = defaultBrowserPath;
				}
			}

			//Note, we don't currently use this, since this is only used for our own html. I added it for completeness... maybe
			//someday when we are running the BloomLibrary locally for the user, we'll have links that require a query part.
			var queryPart = "";
			if (request.Parameters.Count > 0)
			{
				//reconstruct the query part, this time minus any fragment parameter (which we removed previously, if it was there)
				queryPart = "?" + request.Parameters.AllKeys.Aggregate("", (total, key) => total + key + "=" + request.Parameters.Get(key) + "&");
				queryPart = queryPart.TrimEnd(new[] { '&' });
			}

			if (!string.IsNullOrEmpty(browser))
			{
				try
				{
					Process.Start(browser, "\"file:///" + cleanUrl + queryPart + "\"");
					request.SucceededDoNotNavigate();
					return;
				}
				catch (Exception)
				{
					Debug.Fail("Jumping to browser with anchor failed.");
					// Don't crash Bloom because we can't open an external file.
				}
			}
			// If the above failed, either for lack of default browser or exception, try this:
			Process.Start("\"" + cleanUrl + "\"");

			request.SucceededDoNotNavigate();
		}

		private static bool TryGetDefaultBrowserPathWindowsOnly(out string defaultBrowserPath)
		{
			try
			{
				var key = @"HTTP\shell\open\command";
				using (var registrykey = Registry.ClassesRoot.OpenSubKey(key, false))
					defaultBrowserPath = ((string)registrykey.GetValue(null, null)).Split('"')[1];
				return true;
			}
			catch
			{
				defaultBrowserPath = null;
				return false;
			}
		}
	}
}
