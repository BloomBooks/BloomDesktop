using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.ErrorReporter;
using L10NSharp;
using Sentry;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Utils
{
    /// <summary>
    /// When a collection opens, check in the background that Windows lets Bloom create, change,
    /// rename and delete files in the collection's folder, and tell the user quietly if it does not.
    /// Antivirus folder protection (Avast's Ransomware Shield, Windows Security's Controlled Folder
    /// Access...) often refuses exactly these operations, which later shows up as books that can't
    /// be renamed, saved or even opened (BL-17056). Testing the operations themselves, rather than
    /// looking for particular products, means we complain only when something really is blocking us.
    /// Also finds the antivirus summary for problem reports while we're in the background anyway.
    /// </summary>
    public static class FileAccessStartupCheck
    {
        // Names a probe folder inside the collection folder. Starts with a period, so the collection's
        // book list skips it; see also IsProbeFolder.
        public const string kProbeFolderPrefix = ".bloom-access-check-";

        // The kinds of files Bloom most often changes in a book. Folder protection programs often
        // protect only some file types, so we try each.
        private static readonly string[] s_probeExtensions = { ".htm", ".png", ".mp4" };

        /// <summary>
        /// True if path (a file or folder) is, or was made from, a probe folder of this check.
        /// Code that walks a collection's subfolders must leave these alone: one Windows refused to
        /// let us delete may be left behind, holding a probe .htm that makes it look like a book.
        /// </summary>
        public static bool IsProbeFolder(string path)
        {
            return Path.GetFileName(path).StartsWith(kProbeFolderPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Start the check for this collection folder on a background thread and return at once.
        /// </summary>
        public static void StartInBackground(string collectionFolder)
        {
            Task.Run(() => Check(collectionFolder));
        }

        private static void Check(string collectionFolder)
        {
            var antivirus = FileAccessDiagnostics.AntivirusSummary;
            Logger.WriteEvent("Antivirus: " + antivirus);
            SentrySdk.ConfigureScope(scope => scope.SetExtra("antivirus", antivirus));

            // Controlled Folder Access can be checked from its settings alone, which saves trying
            // writes it would refuse (and Windows popping up its own "blocked" notice about them).
            // Its protection covers what is inside a protected folder, so ask about a file there.
            var fileInCollection = Path.Combine(collectionFolder, "book.htm");
            if (ControlledFolderAccessWouldBlock(fileInCollection))
            {
                ReportBlocked(
                    collectionFolder,
                    fileInCollection,
                    "Controlled Folder Access protects this folder and does not list Bloom as allowed"
                );
                return;
            }

            var problem = ProbeFolder(collectionFolder, out var failedPath);
            if (problem == null)
                return;
            ReportBlocked(
                collectionFolder,
                failedPath,
                $"{problem.GetType().Name}: {problem.Message}"
            );
        }

        /// <summary>
        /// True if Windows Security's Controlled Folder Access is on and enforced, protects path,
        /// and does not list Bloom among its allowed apps. Its setting can stay on while another
        /// antivirus has made Microsoft Defender step aside, and then nothing enforces it, so we also
        /// require Defender to be the one with real-time protection on. Never throws.
        /// </summary>
        private static bool ControlledFolderAccessWouldBlock(string path)
        {
            // Check the setting first: it is usually off.
            if (
                !FileAccessDiagnostics.ControlledFolderAccessCoversFile(
                    FileAccessDiagnostics.ReadControlledFolderAccessSetting(),
                    path,
                    FileAccessDiagnostics.GetControlledFolderAccessProtectedFolders(),
                    FileAccessDiagnostics.IsAllowedByControlledFolderAccess()
                )
            )
                return false;
            return FileAccessDiagnostics
                .GetActiveAntivirusNames()
                .Any(name => name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Create, overwrite, rename and delete a few small files in a new subfolder of folder, then
        /// rename and delete the subfolder: the things Bloom does to books. Returns null if everything
        /// worked, else the exception from the first step Windows refused, with failedPath set to the
        /// file or folder it was working on. Removes any probe folder an earlier run could not delete.
        /// </summary>
        public static Exception ProbeFolder(string folder, out string failedPath)
        {
            RemoveLeftoverProbeFolders(folder);
            var probeFolder = Path.Combine(
                folder,
                kProbeFolderPrefix + Guid.NewGuid().ToString("N").Substring(0, 8)
            );
            // What we are working on, so a failure can say where it happened.
            var current = probeFolder;
            try
            {
                Directory.CreateDirectory(probeFolder);
                foreach (var extension in s_probeExtensions)
                {
                    current = Path.Combine(probeFolder, "probe" + extension);
                    RobustFile.WriteAllText(current, "Bloom checks that it can change files here.");
                    // Some protection allows new files but refuses changes to existing ones.
                    RobustFile.WriteAllText(
                        current,
                        "Bloom checks that it can replace files here."
                    );
                    var renamedPath = Path.Combine(probeFolder, "renamed" + extension);
                    RobustFile.Move(current, renamedPath);
                    current = renamedPath;
                    RobustFile.Delete(renamedPath);
                }
                // Renaming a book's folder to match its title is what failed most often in BL-17056.
                current = probeFolder;
                var renamedFolder = probeFolder + "-renamed";
                RobustIO.MoveDirectory(probeFolder, renamedFolder);
                probeFolder = current = renamedFolder;
                RobustIO.DeleteDirectory(probeFolder, false);
                failedPath = null;
                return null;
            }
            catch (Exception e) when (FileAccessDiagnostics.IsRefusal(e))
            {
                // Windows may let us delete it later, so we leave no clutter behind; else the next
                // check will try again.
                TryDeleteProbeFolder(probeFolder);
                failedPath = current;
                return e;
            }
        }

        private static void RemoveLeftoverProbeFolders(string folder)
        {
            foreach (var leftover in Directory.GetDirectories(folder, kProbeFolderPrefix + "*"))
                TryDeleteProbeFolder(leftover);
        }

        private static void TryDeleteProbeFolder(string probeFolder)
        {
            if (!Directory.Exists(probeFolder))
                return;
            try
            {
                RobustIO.DeleteDirectory(probeFolder, true);
            }
            catch (Exception e) when (FileAccessDiagnostics.IsRefusal(e))
            {
                Logger.WriteEvent(
                    $"Could not remove file access check folder {probeFolder}: {e.Message}"
                );
            }
        }

        /// <summary>
        /// Log the evidence about why Bloom can't change files in collectionFolder and show the user
        /// a toast that leads to an explanation. problem says what went wrong, at failedPath.
        /// </summary>
        private static void ReportBlocked(
            string collectionFolder,
            string failedPath,
            string problem
        )
        {
            var details = FileAccessDiagnostics.Collect(failedPath, out var likelyCause);
            Logger.WriteEvent(
                $"File access check of {collectionFolder} failed at {failedPath}: {problem}{Environment.NewLine}{details}"
            );

            var shortMsg = LocalizationManager.GetString(
                "Errors.FileAccessCheck.Blocked",
                "Windows is not letting Bloom change files in this collection."
            );
            var longerMsgTemplate = LocalizationManager.GetString(
                "Errors.FileAccessCheck.BlockedLonger",
                "Bloom checked that it can create, change, rename and delete files in the folder \"{0}\", and Windows refused. This will cause problems, such as books that can't be renamed or opened. The usual cause is antivirus software that protects folders, such as Avast's Ransomware Shield or Windows Security's Controlled Folder Access. Tell that program that you trust Bloom. For help, see \"{1}\"."
            );
            var longerMsg = string.Format(
                longerMsgTemplate,
                collectionFolder,
                FileAccessDiagnostics.TroubleshootingLinkHtml()
            );
            // The likely cause names a product and setting, which a person helping the user needs,
            // even though it is only in English.
            if (likelyCause != null)
                longerMsg += "<br><br>" + likelyCause;
            BloomErrorReport.NotifyUserUnobtrusively(shortMsg, longerMsg);
        }
    }
}
