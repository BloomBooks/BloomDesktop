using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.MiscUI;
using L10NSharp;
using SIL.Reporting;

namespace Bloom.WebLibraryIntegration
{
	public class FirebaseLoginDialog
	{
		// Firebase version of the login dialog. Uses BrowserDialog, since Firebase login is only supported in browsers.

		public static void ShowFirebaseLoginDialog()
		{
			try
			{
				var firebaseDialogRootPath = BloomFileLocator.GetBrowserFile(false, "publish", "LibraryPublish", "loginLoader.html");
				var url = firebaseDialogRootPath.ToLocalhost() + "?bucket=" + BookTransfer.UploadBucketNameForCurrentEnvironment;

				// Precondition: we must be on the UI thread for Gecko to work.
				using (var dlg = new BrowserDialog(url))
				{
					dlg.Width = 300;
					// This is more than we usually need. But it saves scrolling when doing an email sign-up.
					dlg.Height = 510;
					dlg.ShowDialog();
				}
			}
			catch (Exception ex)
			{
				Logger.WriteError( "*** FirebaseLoginDialog threw an exception", ex);
			}
		}

		// We again use the BrowserDialog to give us a GeckoFx component loaded into a window. But here we don't actually show it;
		// we just run some Javascript code to perform the logout.
		public static void FirebaseLogout()
		{
			var firebaseDialogRootPath = BloomFileLocator.GetBrowserFile(false, "publish", "LibraryPublish", "loginLoader.html");
			var url = firebaseDialogRootPath.ToLocalhost() + "?bucket=" + BookTransfer.UploadBucketNameForCurrentEnvironment + "&mode=logout";

			// with the true argument, it will be disposed as soon as it finishes navigating to the URL.
			new BrowserDialog(url, true);
		}

		// We again use the BrowserDialog to give us a GeckoFx component loaded into a window. But here we don't actually show it;
		// if Firebase has remembered user credentials, get a fresh parse token using them.
		public static void FirebaseUpdateToken()
		{
			var firebaseDialogRootPath = BloomFileLocator.GetBrowserFile(false, "publish", "LibraryPublish", "loginLoader.html");
			var url = firebaseDialogRootPath.ToLocalhost() + "?bucket=" + BookTransfer.UploadBucketNameForCurrentEnvironment + "&mode=getToken";

			// with the true argument, it will be disposed as soon as it finishes navigating to the URL.
			new BrowserDialog(url);
		}

		public static string LoginFailureString
		{
			get
			{
				return LocalizationManager.GetString("PublishTab.Upload.Login.LoginFailed", "Login failed");
			}
		}
	}
}
