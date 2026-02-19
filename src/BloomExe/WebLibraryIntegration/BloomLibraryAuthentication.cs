using Bloom.Api;
using Bloom.ToPalaso;
using Bloom.web;

namespace Bloom.WebLibraryIntegration
{
    public class BloomLibraryAuthentication
    {
        public static void LogIn()
        {
            ProcessExtra.SafeStartInFront(GetUrl());
        }

        public static void Logout()
        {
            ProcessExtra.SafeStartInFront(GetUrl() + "&mode=logout");
        }

        private static string GetUrl()
        {
            var host = UrlLookup.LookupUrl(UrlType.LibrarySite, null, BookUpload.UseSandbox);

            // Uncomment for local or alpha testing
            //host = "http://localhost:5174";
            //host = BookUpload.UseSandbox ? "https://dev-alpha.bloomlibrary.org" : "https://alpha.bloomlibrary.org";

            return $"{host}/login-for-editor?port={BloomServer.portForHttp}";
        }
    }
}
