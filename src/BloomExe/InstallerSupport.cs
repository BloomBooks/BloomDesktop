using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using Microsoft.Win32;
using Palaso.IO;
using Palaso.PlatformUtilities;
using Palaso.Reporting;
using Squirrel;

namespace Bloom
{
	/// <summary>
	/// Code to work with the Squirrel installer package. This package basically just installs and manages updating
	/// the appropriate files; thus, tasks like updating the registry must be done in the application, which is
	/// invoked with certain command-line arguments to request this. (However, Bloom sets up all the registry
	/// entries every time it is run, to support multi-channel installation with the most recently run version
	/// taking responsibility for opening files and handling downloads.)
	/// </summary>
	static class InstallerSupport
	{
		internal static UpdateVersionTable.UpdateTableLookupResult _updateTableLookupResult;

		internal static void RemoveBloomRegistryEntries()
		{
			RemoveRegistryKey(null, ".BloomPack");
			RemoveRegistryKey(null, ".BloomPackFile");
			RemoveRegistryKey(null, ".BloomCollection");
			RemoveRegistryKey(null, ".BloomCollectionFile");
			RemoveRegistryKey(null, "Bloom.BloomPack");
			RemoveRegistryKey(null, "Bloom.BloomPackFile");
			RemoveRegistryKey(null, "Bloom.BloomCollection");
			RemoveRegistryKey(null, "Bloom.BloomCollectionFile");
			RemoveRegistryKey(null, "bloom");
		}

		internal static void RemoveRegistryKey(string parentName, string keyName)
		{
			var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
			var key = String.IsNullOrEmpty(parentName) ? root : root.OpenSubKey(parentName);
			if (key != null)
			{
				key.DeleteSubKeyTree(keyName, false);
			}
		}


		/// <summary>
		/// Note: this actually has to go out over the web to get the answer, and so it may fail
		/// </summary>
		internal static UpdateVersionTable.UpdateTableLookupResult LookupUrlOfSquirrelUpdate()
		{
			if (_updateTableLookupResult == null)
			{
				_updateTableLookupResult = new UpdateVersionTable().LookupURLOfUpdate();
			}
			return _updateTableLookupResult;
		}

		internal static void HandleSquirrelInstallEvent(string[] args)
		{
			bool firstTime = false;
			var updateUrlResult = LookupUrlOfSquirrelUpdate();
			// Should only be null if we're not online. Not sure how squirrel will handle that,
			// but at least one of these operations is responsible for setting up shortcuts to the program,
			// which we'd LIKE to work offline. Passing it a plausible url, even though it will presumably fail,
			// seems less likely to cause problems than passing null.
			if(string.IsNullOrEmpty(updateUrlResult.URL))
				updateUrlResult.URL = @"https://s3.amazonaws.com/bloomlibrary.org/squirrel";
			if (args[0] == "--squirrel-uninstall")
			{
				RemoveBloomRegistryEntries();
			}
			switch (args[0])
			{
				// args[1] is version number
				case "--squirrel-install": // (first?) installed
				case "--squirrel-updated": // updated to specified version
				case "--squirrel-obsolete": // this version is no longer newest
				case "--squirrel-uninstall": // being uninstalled
					using (var mgr = new UpdateManager(updateUrlResult.URL, Application.ProductName))
					{
						// Note, in most of these scenarios, the app exits after this method
						// completes!
						// We replace two of the usual calls in order to take control of where shortcuts are installed.
						SquirrelAwareApp.HandleEvents(
							onInitialInstall: v => mgr.CreateShortcutsForExecutable(Path.GetFileName(Assembly.GetEntryAssembly().Location),
								StartMenuLocations, args[0] != "--squirrel-install"),
							onAppUpdate: v => mgr.CreateShortcutForThisExe(),
							onAppUninstall: v => mgr.RemoveShortcutsForExecutable(Path.GetFileName(Assembly.GetEntryAssembly().Location), StartMenuLocations),
							onFirstRun: () => firstTime = true,
							arguments: args);
					}
					break;
			}
		}

		private static ShortcutLocation StartMenuLocations
		{
			get { return ShortcutLocation.Desktop | ShortcutLocation.StartMenuPrograms; }
		}

		/// <summary>
		/// Make the registry entries Bloom requires.
		/// We do this every time a version of Bloom runs, so that if more than one is installed the latest wins.
		/// </summary>
		internal static void MakeBloomRegistryEntries()
		{
			if (Assembly.GetEntryAssembly() == null)
				return; // unit testing.
			// creating this sets some things up so we can download, including relevant registry entries.
			new BookDownloadSupport();
			if (Platform.IsLinux)
			{
				// This will be done by the package installer.
				return;
			}

			var iconDir = FileLocator.GetDirectoryDistributedWithApplication("icons");

			// This is what I (JohnT) think should make Bloom display the right icon for .BloomCollection files.
			EnsureRegistryValue(@".BloomCollection\DefaultIcon", Path.Combine(iconDir, "BloomCollectionIcon.ico"));
			EnsureRegistryValue(@".BloomPack\DefaultIcon", Path.Combine(iconDir, "BloomPack.ico"));

			// These may also be connected with making BloomCollection files display the correct icon.
			// Based on things found in (or done by) the old wix installer.
			EnsureRegistryValue(".BloomCollection", "Bloom.BloomCollectionFile");
			EnsureRegistryValue(".BloomCollectionFile", "Bloom.BloomCollectionFile");
			EnsureRegistryValue("Bloom.BloomCollectionFile", "Bloom Book Collection");
			EnsureRegistryValue(@"Bloom.BloomCollectionFile\DefaultIcon", Path.Combine(iconDir, "BloomCollectionIcon.ico, 0"));

			// I think these help BloomPack files display the correct icon.
			EnsureRegistryValue(".BloomPack", "Bloom.BloomPackFile");
			EnsureRegistryValue("Bloom.BloomPackFile", "Bloom Book Collection");
			EnsureRegistryValue(".BloomPackFile", "Bloom Book Collection");
			EnsureRegistryValue(@"Bloom.BloomPackFile\DefaultIcon", Path.Combine(iconDir, "BloomPack.ico, 0"));
			EnsureRegistryValue(@".BloomPackFile\DefaultIcon", Path.Combine(iconDir, "BloomPack.ico, 0"));
			EnsureRegistryValue(@"SOFTWARE\Classes\Bloom.BloomPack", "Bloom Book Pack", "FriendlyTypeName");

			// This might be part of registering as the executable for various file types?
			// I don't know what does it in wix but it's one of the things the old wix installer created.
			var exe = Assembly.GetExecutingAssembly().Location;
			EnsureRegistryValue(@"bloom\shell\open\command", "\"" + exe + "\" \"%1\"");

			BeTheExecutableFor(".BloomCollection", "BloomCollection file");
			BeTheExecutableFor(".BloomPack", "BloomPack file");
		}

		internal static void BeTheExecutableFor(string extension, string description)
		{
			// e.g.: HKLM\SOFTWARE\Classes\.BloomCollectionFile\Content Type: "application/bloom"
			var fileKey = extension + "File";
			EnsureRegistryValue(fileKey, "application/bloom", "Content Type");
			// e.g.: HKLM\SOFTWARE\Classes\Bloom.BloomCollectionFile\shell\open\: "Open"
			var bloomFileKey = "Bloom" + fileKey;
			EnsureRegistryValue(bloomFileKey + @"\shell\open", "Open");
			// e.g.: HKLM\SOFTWARE\Classes\Bloom.BloomCollectionFile\shell\open\command\: ""C:\Program Files (x86)\Bloom\Bloom.exe" "%1""
			var exe = Assembly.GetExecutingAssembly().Location;
			EnsureRegistryValue(bloomFileKey + @"\shell\open\command", "\"" + exe + "\" \"%1\"");

		}

		internal static void EnsureRegistryValue(string keyName, string value, string name="")
		{
			var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
			var key = root.CreateSubKey(keyName); // may also open an existing key with write permission
			try
			{
				if (key != null)
				{
					var current = (key.GetValue(name) as string);
					if (current != null && current.ToLowerInvariant() == value)
						return; // already set as wanted
				}
				key.SetValue(name, value);

			}
			catch (UnauthorizedAccessException ex)
			{
				// If for some reason we aren't allowed to do it, just don't.
				Logger.WriteEvent("Unable to set registry entry {0}:{1} to {2}: {3}", keyName, name, value, ex.Message);
			}
		}
	}
}
