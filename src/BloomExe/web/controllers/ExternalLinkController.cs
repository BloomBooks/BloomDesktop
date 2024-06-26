using Bloom.Api;
using Bloom.ToPalaso;

namespace Bloom.web
{
    /// <summary>
    /// Handles GET requests to open a page, shipped with Bloom in an external browser.
    /// For other web resources, just use http/https and the c# Browser class will intercept
    /// and open the browser.
    ///
    /// For html pages shipped with bloom, use this controller by writing
    /// <a href='/api/externalLink?path=blah/foo.html#fragment=idOfSomeSectionOfFoo'></a>
    /// </summary>
    public class ExternalLinkController
    {
        public static void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Opening a page, better be in UI thread.
            apiHandler.RegisterEndpointHandler("externalLink", HandleExternalLink, true);
            // This class feels like the right place to handle link elements that simply open a link
            // in the external browser.
            apiHandler.RegisterEndpointHandler("link", HandleLink, true);
        }

        /// <summary>
        /// Javascript currently activates this for links that start with http or mailto.
        /// It opens the link in the system default browser.
        /// </summary>
        private static void HandleLink(ApiRequest request)
        {
            var href = request.RequiredPostString();
            ProcessExtra.SafeStartInFront(href);
            request.PostSucceeded();
        }

        /// <summary>
        /// Handles a url matching externalLink with a path parameter and optional fragment parameter.
        /// </summary>
        public static void HandleExternalLink(ApiRequest request)
        {
            var path = request.RequiredParam("path");
            var completeUiLangPath =
                BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, path);
            var cleanUrl = completeUiLangPath.Replace("\\", "/"); // allows jump to file to work

            //we would like to get something like ?path=foo.htm#secondPart but the browser strips off that fragment part
            //so we require that to be written as ?path=foo.htm&fragment=secondPart so it gets to us, then we convert
            //it back to the normal format before sending it to a parser
            var fragment = request.GetParamOrNull("fragment");
            if (!string.IsNullOrEmpty(fragment))
                cleanUrl += "#" + fragment;

            // If we simply provide a path to the file and have a fragment (#xxx), we get file not found exception.
            // If we prepend "file:///", the fragment part of the link (#xxx) is not sent unless we provide the browser path too.
            // This is the same behavior when simply typing a url into the Run command on Windows.
            // Previously, we were attempting to look up the browser path and passing that to SafeStart, but later versions of Windows
            // changed the way to look up the browser path, and we were opening links in Internet Explorer regardless of the default browser. (BL-6952)
            // So now we serve the file using Bloom as the server. This launches in the default browser and makes fragments work.
            var urlToOpenInExternalBrowser =
                $"http://localhost:{BloomServer.portForHttp}/bloom/{cleanUrl}";
            ProcessExtra.SafeStartInFront(urlToOpenInExternalBrowser);
            request.ExternalLinkSucceeded();
        }
    }
}
