using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.MiscUI;
using Bloom.web;
using L10NSharp;
using SIL.Reporting;

namespace Bloom.WebLibraryIntegration
{
	public class FirebaseLoginDialog
	{
		// Firebase version of the login dialog. Uses BrowserDialog, since Firebase login is only supported in browsers.

		public static void ShowFirebaseLoginDialog(IBloomWebSocketServer webSocketServer)
		{
			try
			{
				var url = GetLoginDialogUrl();

				// Precondition: we must be on the UI thread for Gecko to work.
				using (var dlg = new BrowserDialog(url))
				{
					dlg.WebSocketServer = webSocketServer;
					dlg.CloseMessage = "close";
					dlg.Width = 400;
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

		private static string GetLoginDialogUrl()
		{
			var firebaseDialogRootPath =
				BloomFileLocator.GetBrowserFile(false, "publish", "LibraryPublish", "loginLoader.html");
			return firebaseDialogRootPath.ToLocalhost() + "?bucket=" + BookTransfer.UploadBucketNameForCurrentEnvironment;
		}

		// We again use the BrowserDialog to give us a GeckoFx component loaded into a window. But here we don't actually show it;
		// we just run some Javascript code to perform the logout.
		public static void FirebaseLogout()
		{
			var url = GetLoginDialogUrl() + "&mode=logout";

			// with the true argument, it will be disposed as soon as soon as the Javascript closes the dialog.
			new BrowserDialog(url, true);
		}

		// We again use the BrowserDialog to give us a GeckoFx component loaded into a window. But here we don't actually show it;
		// if Firebase has remembered user credentials, get a fresh parse token using them.
		public static void FirebaseUpdateToken(Action whenClosed = null)
		{
			var url = GetLoginDialogUrl() + "&mode=getToken";
			// with the true argument, it will be disposed as soon as the Javascript closes the dialog.
			new BrowserDialog(url, true, whenClosed);
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
