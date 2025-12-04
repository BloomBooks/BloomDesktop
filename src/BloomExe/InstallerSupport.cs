using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using DesktopAnalytics;
using Microsoft.Win32;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using Velopack.Logging;
#if __MonoCS__
using System.Diagnostics;
#else
using Velopack;
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
            RemoveRegistryKey(null, ".BloomProblemBook");
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
        internal static UpdateVersionTable.UpdateTableLookupResult LookupUrlOfVelopackUpdate(
            bool forceReload = false
        )
        {
            // If we got an error last time, check again...maybe we were offline and are now connected
            // again. Or perhaps the server was offline and is now back. Also if forceReload is
            // true...in this case the user is asking us to check, we're going to report a Squirrel
            // failure to update, so we need to know if we can get to the internet NOW.
            if (
                _updateTableLookupResult == null
                || _updateTableLookupResult.Error != null
                || forceReload
            )
            {
                _updateTableLookupResult = new UpdateVersionTable().LookupURLOfUpdate(forceReload);
            }
            return _updateTableLookupResult;
        }

        class logger : IVelopackLogger
        {
            public void Log(VelopackLogLevel logLevel, string message, Exception exception)
            {
                if (exception != null)
                {
                    Logger.WriteError("Velopack error: " + message, exception);
                    return;
                }
                Logger.WriteEvent("Velopack: " + message);
            }
        }

        /// <summary>
        /// Mainly to handle the various special startups that Velopack executes when Bloom is installed,
        /// updated, or uninstalled. However, Velopack wants this to be called on every startup.
        /// One thing that won't work otherwise is looking for updates.
        /// </summary>
        /// <returns>false if there is a problem, and Bloom should not continue to start up.</returns>
        internal static bool HandleVelopackStartup(string[] commandLineArgs)
        {
            var log = new logger();
            VelopackApp
                .Build()
                .SetLogger(log)
                .OnBeforeUninstallFastCallback(
                    (v) =>
                    {
                        RemoveBloomRegistryEntries();
                    }
                )
                .OnAfterInstallFastCallback(v =>
                {
                    // If we implement all-users install, we should make the registry entries here.
                    //MakeBloomRegistryEntries(args); // add an argument or something to tell it to go ahead even though installed for all users.
                })
                .OnFirstRun(
                    (v) => {
                        // Nothing to do for now (we get a message some other way about first install).
                    }
                )
                .OnAfterUpdateFastCallback(v =>
                {
                    var props = new Dictionary<string, string>
                    {
                        ["newVersion"] = v.ToString(),
                        ["channel"] = ApplicationUpdateSupport.ChannelName,
                    };
                    Analytics.Track("Update Version", props);
                })
                .Run();
            if (commandLineArgs.Length == 0)
                return !CheckForBadInstall();
            return true;
        }

        // returns true if there's a problem
        static bool CheckForBadInstall()
        {
            // If we are running from a messed-up install resulting from an imperfect update from a version installed by Squirrel,
            // this should clean things up.
            // Velopack always puts our EXE in a directory called "current" under one that contains "Update.exe".
            // If Update.exe is present, then it's fair to assume this is an installed build.
            // In that case, if we're not in a "current" directory, we need run Update.exe to clean up the mess.
            var location = Assembly.GetExecutingAssembly().Location;
            var bloomDir = Path.GetDirectoryName(location);
            var mainInstallDir = Path.GetDirectoryName(bloomDir);
            var updateExePath = Path.Combine(mainInstallDir, "Update.exe");
            if (
                RobustFile.Exists(updateExePath)
                && Path.GetFileName(bloomDir).ToLowerInvariant() != "current"
            )
            {
                // We can tell Update.exe to wait for this process to finish before it continues
                // with tasks that may involve replacing or moving our exe. The "start" argument tells it
                // to complete any updates that are in progress (which will be an incomplete update from
                // Squirrel, that happens because our update logic for Squirrel did not use Update.exe to
                // restart the app, as was apparently expected).
                var processId = Process.GetCurrentProcess().Id;
                var args = "start --waitPid " + processId;

                Process.Start(updateExePath, args);
                // Program.main() will exit so Update.exe can finish the install and then restart us.
                return true;
            }

            return false;
        }

        /// <summary>
        /// True if we consider our install to be shared by all users of the computer.
        /// We currently detect this based on being in the Program Files folder.
        /// </summary>
        /// <returns></returns>
        public static bool SharedByAllUsers()
        {
            // Being a 32-bit app, we expect to get installed in Program Files (x86) on a 64-bit system.
            // If we are in fact on a 32-bit system, we will be in plain Program Files...but on such a system that's what this code gets.
            return Application.ExecutablePath.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            );
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
            // If we support allUsers installs, we will need to make an exception here when called during installation.
            if (SharedByAllUsers())
                return;
            _installInLocalMachine = SharedByAllUsers();
            if (Platform.IsLinux)
            {
                // This will be done by the package installer.
                return;
            }

            var iconDir = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                true,
                "icons"
            );
            if (iconDir == null)
            {
                // Note: if this happens a lot we'd want to make it localizable. I think that's unlikely, so it may not be worth the
                // burden on localizers.
                var exception = new FileNotFoundException(
                    "Bloom was not able to find some of its files. The shortcut icon you clicked on may be out of date. Try deleting it and reinstalling Bloom"
                );
                ProblemReportApi.ShowProblemDialog(null, exception, "", "fatal");
                // Not sure these lines are reachable. Just making sure.
                ProgramExit.Exit();
                return;
            }

            // BloomCollection icon
            CreateIconRegistrySettings(
                "BloomCollection",
                iconDir,
                "BloomCollectionIcon.ico",
                "Bloom Book Collection"
            );
            CreateIconRegistrySettings(
                "BloomProblemBook",
                iconDir,
                "BloomProblemBook.ico",
                "Bloom Problem Book"
            );
            // BloomPack icon
            CreateIconRegistrySettings(
                "BloomPack",
                iconDir,
                "BloomPack.ico",
                "Bloom Book Pack",
                "FriendlyTypeName"
            );
            // JoinBloomTC icon
            CreateIconRegistrySettings(
                "JoinBloomTC",
                iconDir,
                "JoinBloomTC.ico",
                "Join Bloom Team Collection"
            );

            // This might be part of registering as the executable for various file types?
            // I don't know what does it in wix but it's one of the things the old wix installer created.
            var exe = Assembly.GetExecutingAssembly().Location;
            EnsureRegistryValue(@"bloom\shell\open\command", "\"" + exe + "\" \"%1\"");

            BeTheExecutableFor(".BloomCollection", "BloomCollection file");
            BeTheExecutableFor(".BloomPack", "BloomPack file");
            BeTheExecutableFor(".BloomProblemBook", "Bloom Problem Book file");
            BeTheExecutableFor(".JoinBloomTC", "JoinBloom file");
            // Make the OS run Bloom when it sees bloom://somebooktodownload
            BookDownloadSupport.RegisterForBloomUrlProtocol(_installInLocalMachine);
        }

        private static void CreateIconRegistrySettings(
            string extension,
            string iconDir,
            string iconFileName,
            string description,
            string softwareClassesName = null
        )
        {
            // This is what I (JohnT) think should make Bloom display the right icon for .{extension} files.
            EnsureRegistryValue($@".{extension}\DefaultIcon", Path.Combine(iconDir, iconFileName));

            // These may also be connected with making files display the correct icon.
            // Based on things found in (or done by) the old wix installer.
            EnsureRegistryValue($".{extension}", $"Bloom.{extension}File");
            EnsureRegistryValue($".{extension}File", $"Bloom.{extension}File");
            EnsureRegistryValue($"Bloom.{extension}File", description);
            EnsureRegistryValue(
                $@"Bloom.{extension}File\DefaultIcon",
                Path.Combine(iconDir, $"{iconFileName}, 0")
            );
            EnsureRegistryValue(
                $@".{extension}File\DefaultIcon",
                Path.Combine(iconDir, $"{iconFileName}, 0")
            );

            if (softwareClassesName != null)
                EnsureRegistryValue(
                    $@"SOFTWARE\Classes\Bloom.{extension}",
                    description,
                    softwareClassesName
                );
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
            // With Velopack and .net 8, most ways of getting the exe path give a path to Bloom.dll,
            // which does not work for this purpose. For example, Assembly.GetExecutingAssembly().Location,
            // Assembly.GetEntryAssembly().Location, and Environment.GetCommandLineArgs()[0]
            // all yield the path to Bloom.dll. This AI suggestion seems to work.
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            EnsureRegistryValue(bloomFileKey + @"\shell\open\command", "\"" + exe + "\" \"%1\"");
        }

        internal static void EnsureRegistryValue(string keyName, string value, string name = "")
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
                Logger.WriteEvent(
                    "Unable to set registry entry {0}:{1} to {2}: {3}",
                    keyName,
                    name,
                    value,
                    ex.Message
                );
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
