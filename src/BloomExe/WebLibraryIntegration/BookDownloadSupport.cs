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

		public BookDownloadSupport()
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
					Language1Iso639Code = "en",
					Language1Name = "English",
					IsSourceCollection = true,
					PathToSettingsFile = pathToSettingsFile
					// All other defaults are fine
				};
				CollectionSettings.CreateNewCollection(settings);
			}

			// Make the OS run Bloom when it sees bloom://somebooktodownload
			RegisterForBloomUrlProtocol();
		}

		/// <summary>
		/// Make sure this instance is registered (at least for this user) and the program to handle bloom:// urls.
		/// See also where these registry entries are made by the wix installer (file Installer.wxs).
		/// </summary>
		private void RegisterForBloomUrlProtocol()
		{
			if (Palaso.PlatformUtilities.Platform.IsLinux)
			{
				// TODO-Linux: no idea what has to happen to register a url handler...probably not this, though.
				// See also where these registry entries are made by the wix installer (file Installer.wxs).
				return;
			}

			if (AlreadyRegistered(Registry.ClassesRoot))
				return;
			var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
			var key = root.CreateSubKey(@"bloom\shell\open\command");
			key.SetValue("", CommandToLaunchBloom);

			key = root.CreateSubKey("bloom");
			key.SetValue("", "BLOOM:URL Protocol");
			key.SetValue("URL Protocol", "");
		}

		private bool AlreadyRegistered(RegistryKey root)
		{
			var key = root.OpenSubKey(@"bloom\shell\open\command");
			if (key == null)
				return false;
			var wanted = CommandToLaunchBloom;
			if (wanted != (key.GetValue("") as string).ToLowerInvariant())
				return false;
			key = root.OpenSubKey("bloom");
			if (key.GetValue("") as string != "BLOOM:URL Protocol")
				return false;
			if (key.GetValue("URL Protocol") as string != "")
				return false;
			return true;
		}

		private string CommandToLaunchBloom
		{
			get { return Application.ExecutablePath.ToLowerInvariant() + " \"%1\""; }
		}
	}
}
