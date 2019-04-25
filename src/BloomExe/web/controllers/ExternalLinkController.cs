using System.Text.RegularExpressions;
using Bloom.Api;

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
		public static void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// Opening a page, better be in UI thread.
			apiHandler.RegisterEndpointHandler(kPrefix+"/.*", HandleRequest, true);
		}

		/// <summary>
		/// Handles a url starting with api/kPrefix by stripping off that prefix, searching for the file
		/// named in the remainder of the url, and opening it in some browser (passing on any anchor specified).
		/// </summary>
		public static void HandleRequest(ApiRequest request)
		{
			//NB: be careful not to lose case, as at least chrome is case-sensitive with anchors (e.g. #ChoiceOfTopic)
			var localPath = Regex.Replace(request.LocalPath(), "api/"+ kPrefix+"/", "", RegexOptions.IgnoreCase);
			var completeUiLangPath = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, localPath);
			var cleanUrl = completeUiLangPath.Replace("\\", "/"); // allows jump to file to work


			//we would like to get something like foo.htm#secondPart but the browser strips off that fragment part
			//so we require that to be written as foo.htm?fragment=secondPart so it gets to us, then we convert
			//it back to the normal format before sending it to a parser
			if (!string.IsNullOrEmpty(request.Parameters["fragment"]))
			{
				cleanUrl += "#" + request.Parameters["fragment"];
				request.Parameters.Remove("fragment");
			}

			// If we simply provide a path to the file and have a fragment (#xxx), we get file not found exception.
			// If we prepend "file:///", the fragment part of the link (#xxx) is not sent unless we provide the browser path too.
			// This is the same behavior when simply typing a url into the Run command on Windows.
			// Previously, we were attempting to look up the browser path and passing that to SafeStart, but later versions of Windows
			// changed the way to look up the browser path, and we were opening links in Internet Explorer regardless of the default browser. (BL-6952)
			// So now we serve the file using Bloom as the server. This launches in the default browser and makes fragments work.
			var urlToOpenInExternalBrowser = $"http://localhost:{BloomServer.portForHttp}/bloom/{cleanUrl}";
			SIL.Program.Process.SafeStart(urlToOpenInExternalBrowser);
			request.ExternalLinkSucceeded();
		}
	}
}
