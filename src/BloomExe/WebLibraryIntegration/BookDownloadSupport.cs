using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.Utils;
using Microsoft.Win32;
using SIL.Reporting;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// BookDownloadSupport makes sure we are ready for download requests from the web: at startup
    /// (Program), and before each download (BookDownload).
    /// (It had a larger role at one point, but things have been simplified.)
    /// </summary>
    public class BookDownloadSupport
    {
        public delegate BookDownloadSupport Factory(); //autofac uses this
        public const string ArgsPipeName = @"SendBloomArgs";

        /// <summary>
        /// At startup, make the collection that downloaded books go into, if Windows lets us.
        /// Having it early means the collections list shows it (with its link to Bloom Library)
        /// and we don't have to create it in the UI when we add a downloaded book. Nothing else
        /// needs it, and a download makes sure of it again, explaining if it can't (see
        /// BookDownload.OnDoDownload), so a refusal here is only logged. (BL-16728)
        /// </summary>
        public static void TryEnsureDownloadFolderExists()
        {
            try
            {
                EnsureDownloadFolderExists();
            }
            catch (CollectionFolderNotWritableException e)
            {
                Logger.WriteEvent(
                    $"Could not make the collection for downloaded books in {e.FolderPath}: {e.InnerException.GetType().Name}: {e.InnerException.Message}"
                );
            }
        }

        /// <summary>
        /// Make the collection that downloaded books go into, if it isn't there. Throws
        /// CollectionFolderNotWritableException if Windows won't let Bloom make it.
        /// </summary>
        public static void EnsureDownloadFolderExists()
        {
            string downloadFolder = GetDownloadFolder();
            if (Directory.Exists(downloadFolder))
                return;
            try
            {
                var pathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                    Path.GetDirectoryName(downloadFolder),
                    Path.GetFileName(downloadFolder)
                );
                var settings = new NewCollectionSettings()
                {
                    PathToSettingsFile = pathToSettingsFile,
                    // All other defaults are fine
                };
                settings.Language1.Tag = "en";
                settings.Language1.SetName("English", false);
                CollectionSettings.CreateNewCollection(settings);
                ProjectContext.ClearUserInstalledDirectoriesCache();
            }
            catch (Exception e) when (FileAccessDiagnostics.IsRefusal(e))
            {
                // Saving the new collection's settings, or making a folder above it, can be
                // refused too; callers need only handle one kind of exception.
                throw new CollectionFolderNotWritableException(downloadFolder, e);
            }
        }

        /// <summary>
        /// BookDownload.DownloadFolder, or, if Windows won't let Bloom make the folders above it
        /// (getting DownloadFolder makes them), CollectionFolderNotWritableException naming where
        /// the collection would be.
        /// </summary>
        public static string GetDownloadFolder()
        {
            try
            {
                return BookDownload.DownloadFolder;
            }
            catch (Exception e) when (FileAccessDiagnostics.IsRefusal(e))
            {
                var expected = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SIL",
                    "Bloom",
                    "Collections",
                    BookCollection.DownloadedBooksCollectionNameInEnglish
                );
                throw new CollectionFolderNotWritableException(expected, e);
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
