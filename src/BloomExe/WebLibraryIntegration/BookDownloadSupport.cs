using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection;
using Microsoft.Win32;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// BookDownloadSupport is used by program.cs at startup to make sure we are ready for download requests from the web.
	/// (It had a larger role at one point, but things have been simplified.)
	/// </summary>
	public class BookDownloadSupport
	{
		private static Thread _serverThread;
		private static bool _shuttingDown;
		public delegate BookDownloadSupport Factory();//autofac uses this
		public const string ArgsPipeName = @"SendBloomArgs";

		public static void EnsureDownloadFolderExists()
		{
			// We need the download folder to exist if we are asked to download a book.
			// We also want it to exist, to show the (planned) link that offers to launch the web site.
			// Another advantage of creating it early is that we don't have to create it in the UI when we want to add
			// a downloaded book to the UI.
			// So, we just make sure it exists here at startup.
			string downloadFolder = BookTransfer.DownloadFolder;
			if (!Directory.Exists(downloadFolder))
			{
				var pathToSettingsFile = CollectionSettings.GetPathForNewSettings(Path.GetDirectoryName(downloadFolder),
					Path.GetFileName(downloadFolder));
				var settings = new NewCollectionSettings()
				{
			
					IsSourceCollection = true,
					PathToSettingsFile = pathToSettingsFile
					// All other defaults are fine
				};
				settings.Language1.Iso639Code = "en";
				settings.Language1.SetName("English", false);
				CollectionSettings.CreateNewCollection(settings);
				ProjectContext.ClearUserInstalledDirectoriesCache();
			}
		}

		/// <summary>
		/// Make sure this instance is registered (at least for this user) as the program to handle bloom:// urls.
		/// If we are installing for all users we can make it in a shared place.
		/// </summary>
		public static void RegisterForBloomUrlProtocol(bool allUsers)
		{
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				// This will be done by the package installer.
				// To manually install it:
				// sudo cp debian/bloom.desktop /usr/share/applications
				// sudo update-desktop-database
				// (and bloom startup wrapper needs to be in the path)
				return;
			}
			var whereToInstall = allUsers ? Registry.LocalMachine : Registry.CurrentUser;
			var root = whereToInstall.CreateSubKey(@"Software\Classes");

			if (AlreadyRegistered(root))
				return;
			var key = root.CreateSubKey(@"bloom\shell\open\command");
			key.SetValue("", CommandToLaunchBloomOnWindows);

			key = root.CreateSubKey("bloom");
			key.SetValue("", "BLOOM:URL Protocol");
			key.SetValue("URL Protocol", "");
		}

		private static bool AlreadyRegistered(RegistryKey root)
		{
			var key = root.OpenSubKey(@"bloom\shell\open\command");
			if (key == null)
				return false;
			var wanted = CommandToLaunchBloomOnWindows;
			if (wanted != (key.GetValue("") as string).ToLowerInvariant())
				return false;
			key = root.OpenSubKey("bloom");
			if (key.GetValue("") as string != "BLOOM:URL Protocol")
				return false;
			if (key.GetValue("URL Protocol") as string != "")
				return false;
			return true;
		}

		private static string CommandToLaunchBloomOnWindows
		{
			get
			{
				//Don't do this: Application.ExecutablePath.ToLowerInvariant()
				//it cause us to have a wrong idea of the case of channels, which
				//leads to urls that AWS S3 rejects when checking for an update. BL-3515
				// The executable path might have spaces in it, for example, if the user's username
				// has spaces.
				return "\"" + Application.ExecutablePath + "\"" + " \"%1\"";
			}
		}
	}
}
