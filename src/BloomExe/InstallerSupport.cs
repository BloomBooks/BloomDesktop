using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Bloom.ToPalaso;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using DesktopAnalytics;
using Microsoft.Win32;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
#if __MonoCS__
using System.Diagnostics;
#else
using Squirrel;
#endif

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
			RemoveRegistryKey(null, ".JoinBloomTC");
			RemoveRegistryKey(null, ".JoinBloomTCFile");
			RemoveRegistryKey(null, ".BloomCollection");
			RemoveRegistryKey(null, ".BloomCollectionFile");
			RemoveRegistryKey(null, "Bloom.BloomPack");
			RemoveRegistryKey(null, "Bloom.BloomPackFile");
			RemoveRegistryKey(null, "Bloom.JoinBloomTC");
			RemoveRegistryKey(null, "Bloom.JoinBloomTCFile");
			RemoveRegistryKey(null, "Bloom.BloomCollection");
			RemoveRegistryKey(null, "Bloom.BloomCollectionFile");
			RemoveRegistryKey(null, "bloom");
		}

		internal static void RemoveRegistryKey(string parentName, string keyName)
		{
			var root = HiveToMakeRegistryKeysIn;
			var key = String.IsNullOrEmpty(parentName) ? root : root.OpenSubKey(parentName);
			if (key != null)
			{
				key.DeleteSubKeyTree(keyName, false);
			}
		}


		/// <summary>
		/// Note: this actually has to go out over the web to get the answer, and so it may fail
		/// </summary>
		internal static UpdateVersionTable.UpdateTableLookupResult LookupUrlOfSquirrelUpdate(bool forceReload = false)
		{
			// If we got an error last time, check again...maybe we were offline and are now connected
			// again. Or perhaps the server was offline and is now back. Also if forceReload is
			// true...in this case the user is asking us to check, we're going to report a Squirrel
			// failure to update, so we need to know if we can get to the internet NOW.
			if (_updateTableLookupResult == null || _updateTableLookupResult.Error != null || forceReload)
			{
				_updateTableLookupResult = new UpdateVersionTable().LookupURLOfUpdate(forceReload);
			}
			return _updateTableLookupResult;
		}

		internal static void HandleSquirrelInstallEvent(string[] args)
		{
#if __MonoCS__
			Debug.Fail("HandleSquirrelInstallEvent should not run on Linux!");	// and the code below doesn't compile on Linux
			return;
#else
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
			if (args[0] == "--squirrel-updated")
			{
				var props = new Dictionary<string, string>();
				if (args.Length > 1)
					props["newVersion"] = args[1];
				props["channel"] = ApplicationUpdateSupport.ChannelName;
				Analytics.Track("Update Version", props);
			}
			string iconPath = null;
			if (args[0] == "--squirrel-install")
			{
				//Using an icon in the root folder fixes the problem of losing the shortcut icon when we
				//upgrade, lose the original, and eventually the windows explorer cache loses it.
				//There was another attempt at fixing this by finding all the shortcuts and updating them, but that didn't work in our testing and this seems simpler and more robust.
				//There may be some other reason for the old approach of pointing at the icon of the app itself (e.g. could be a different icon)?
				var exePath = Application.ExecutablePath;
				var rootAppDirectory = Path.GetDirectoryName(Path.GetDirectoryName(exePath));
				// directory that holds e.g. /3.6/Bloom.exe
				var versionIconPath = Path.ChangeExtension(exePath, "ico"); // where this installation has icon
				iconPath = Path.ChangeExtension(Path.Combine(rootAppDirectory, Path.GetFileName(exePath)), "ico");
				// where we will put a version-independent icon
				try
				{
					if (RobustFile.Exists(versionIconPath))
						RobustFile.Copy(versionIconPath, iconPath, true);
				}
				catch (Exception)
				{
					// ignore...most likely some earlier version of the icon is locked somehow, fairly harmless.
				}
				// Normally this is done on every run of the program, but if we're doing a silent allUsers install,
				// this is our only time running with admin privileges so we can actually make the entries for all users.
				MakeBloomRegistryEntries(args);
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
						// WARNING, in most of these scenarios, the app exits at the end of HandleEvents;
						// thus, the method call does not return and nothing can be done after it!
						// We replace two of the usual calls in order to take control of where shortcuts are installed.
						SquirrelAwareApp.HandleEvents(

							onInitialInstall: v =>
							{
								mgr.CreateShortcutsForExecutable(Path.GetFileName(Assembly.GetEntryAssembly().Location),
									StartMenuLocations,
									false, // not just an update, since this is case initial install
									null, // can provide arguments to pass to Update.exe in shortcut, defaults are OK
									iconPath,
									SharedByAllUsers());
								// Normally we can't do this in our quick silent run as part of install, because of the need to escalate
								// privilege. But if we're being installed for all users we must already be running as admin.
								// We don't need to do an extra restart of Bloom because this install-setup run of Bloom will finish
								// right away anyway. We do this last because we've had some trouble (BL-3342) with timeouts
								// if this install somehow ties up the CPU until Squirrel thinks Bloom is taking too long to do its
								// install-only run.
								if (SharedByAllUsers())
									FontInstaller.InstallFont("AndikaNewBasic", needsRestart: false);
							},
							onAppUpdate: v => HandleAppUpdate(mgr),
							onAppUninstall: v => mgr.RemoveShortcutsForExecutable(Path.GetFileName(Assembly.GetEntryAssembly().Location), StartMenuLocations, SharedByAllUsers()),
							onFirstRun: () => firstTime = true,
							arguments: args);
					}
					break;
			}
#endif
		}

#if !__MonoCS__
	    private static void HandleAppUpdate(UpdateManager mgr)
	    {
	        mgr.CreateShortcutForThisExe();

			// See BL-4590 where an upgrade from a clean 3.7.22 to 3.8 either would get no Bloom.exe stub, or get one of 0KB.
			// This is nominally because the installer in 3.7.22 did not use this stub technique... though we don't know why the upgrade
			// process gave us a 0kb attempt at the stub. So we're fixing it here because we need to push this out quickly.
	        var stubPath = Application.ExecutablePath.Replace(".exe", "_ExecutionStub.exe");

			// If the move succeeds, then the stub file won't be in our main bin directory anymore... it will already be moved up to the parent
			// directory. So basically we're just "trying again" here, sigh...
			if (File.Exists(stubPath))
	        {
	            try
	            {
	                // the target is the parent directory, and the file name without the "_ExecutionStub" part of the name.
	                var targetPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath)),
	                    Path.GetFileName(Application.ExecutablePath));
	                RobustFile.Copy(stubPath, targetPath, true);
	            }
	            catch(Exception e)
	            {
	                throw new ApplicationException("Bloom failed to copy the execution stub: "+e.Message, e);
	            }
	        }
	    }
#endif

	    /// <summary>
		/// True if we consider our install to be shared by all users of the computer.
		/// We currently detect this based on being in the Program Files folder.
		/// </summary>
		/// <returns></returns>
		public static bool SharedByAllUsers()
		{
			// Being a 32-bit app, we expect to get installed in Program Files (x86) on a 64-bit system.
			// If we are in fact on a 32-bit system, we will be in plain Program Files...but on such a system that's what this code gets.
			return Application.ExecutablePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
		}

#if !__MonoCS__
		private static ShortcutLocation StartMenuLocations
		{
			get { return ShortcutLocation.Desktop | ShortcutLocation.StartMenuPrograms; }
		}
#endif

		static bool IsFirstTimeInstall(string[] programArgs)
		{
			if (programArgs.Length < 1)
				return false;
			return programArgs[0] == "--squirrel-install";
		}

		private static bool _installInLocalMachine;

		/// <summary>
		/// Make the registry entries Bloom requires.
		/// We do this every time a version of Bloom runs, so that if more than one is installed the latest wins.
		/// </summary>
		internal static void MakeBloomRegistryEntries(string[] programArgs)
		{
			if (Program.RunningUnitTests)
				return; // unit testing.
			// When installed in program files we only do registry entries when we are first installed,
			// thus keeping them consistent for all users, stored in HKLM.
			if (SharedByAllUsers() && !IsFirstTimeInstall(programArgs))
				return;
			_installInLocalMachine = SharedByAllUsers();
			if (Platform.IsLinux)
			{
				// This will be done by the package installer.
				return;
			}

			var iconDir = FileLocationUtilities.GetDirectoryDistributedWithApplication(true, "icons");
			if (iconDir == null)
			{
				// Note: if this happens a lot we'd want to make it localizable. I think that's unlikely, so it may not be worth the
				// burden on localizers.
				var exception =
					new FileNotFoundException(
						"Bloom was not able to find some of its files. The shortcut icon you clicked on may be out of date. Try deleting it and reinstalling Bloom");
				ProblemReportApi.ShowProblemDialog(null, exception, "", "fatal");
				// Not sure these lines are reachable. Just making sure.
				Application.Exit();
				return;
			}

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

			// And similarly for JoinBloomTC, though we haven't made an icon for it yet.
			EnsureRegistryValue(".JoinBloomTC", "Bloom.JoinBloomTCFile");
			EnsureRegistryValue("Bloom.JoinBloomTCFile", "Bloom Team Collection");
			EnsureRegistryValue(".JoinBloomTCFile", "Bloom Team Collection");
			// For now reuse bloom collection icon
			EnsureRegistryValue(@"Bloom.JoinBloomTCFile\DefaultIcon", Path.Combine(iconDir, "BloomCollectionIcon.ico, 0"));
			EnsureRegistryValue(@".JoinBloomTCFile\DefaultIcon", Path.Combine(iconDir, "BloomCollectionIcon.ico, 0"));


			// This might be part of registering as the executable for various file types?
			// I don't know what does it in wix but it's one of the things the old wix installer created.
			var exe = Assembly.GetExecutingAssembly().Location;
			EnsureRegistryValue(@"bloom\shell\open\command", "\"" + exe + "\" \"%1\"");

			BeTheExecutableFor(".BloomCollection", "BloomCollection file");
			BeTheExecutableFor(".BloomPack", "BloomPack file");
			BeTheExecutableFor(".JoinBloomTC", "JoinBloom file");
			// Make the OS run Bloom when it sees bloom://somebooktodownload
			BookDownloadSupport.RegisterForBloomUrlProtocol(_installInLocalMachine);

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
			RegistryKey root = HiveToMakeRegistryKeysIn;

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

		private static RegistryKey HiveToMakeRegistryKeysIn
		{
			get
			{
				if (SharedByAllUsers())
					return Registry.LocalMachine.CreateSubKey(@"Software\Classes");
				else
					return Registry.CurrentUser.CreateSubKey(@"Software\Classes");
			}
		}
	}
}
