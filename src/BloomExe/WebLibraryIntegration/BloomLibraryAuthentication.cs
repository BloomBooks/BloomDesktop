using Bloom.Api;

namespace Bloom.WebLibraryIntegration
{
	public class BloomLibraryAuthentication
	{		
		public static void LogIn()
		{
			SIL.Program.Process.SafeStart(GetUrl());
		}

		public static void Logout()
		{
			SIL.Program.Process.SafeStart(GetUrl() + "&mode=logout");
		}

		private static string GetUrl()
		{
			var host = BookUpload.UseSandbox ? "https://dev.bloomlibrary.org" : "https://bloomlibrary.org";

			// Uncomment for local or alpha testing
			//host = "http://localhost:3000";
			host = BookUpload.UseSandbox ? "https://dev-alpha.bloomlibrary.org" : "https://alpha.bloomlibrary.org";

			return $"{host}/login-for-editor?port={BloomServer.portForHttp}";
		}
	}
}
