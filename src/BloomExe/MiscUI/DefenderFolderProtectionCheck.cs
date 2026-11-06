using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Properties;
using L10NSharp;
using SIL.IO;
using SIL.PlatformUtilities;

namespace Bloom.MiscUI
{
    /// <summary>
    /// Thrown when Bloom cannot write in a collection's folder, so the collection cannot be opened.
    /// The caller that opens collections reports it (see ReportProblem) and offers the collection
    /// chooser, rather than Bloom exiting. (BL-16728)
    /// </summary>
    public class CollectionFolderNotWritableException : ApplicationException
    {
        /// <summary>
        /// The folder we could not write in.
        /// </summary>
        public string FolderPath { get; }

        /// <summary>
        /// Make one for the folder, with the exception we got trying to write there.
        /// </summary>
        public CollectionFolderNotWritableException(string folderPath, Exception writeProblem)
            : base($"Cannot write to the collection folder {folderPath}", writeProblem)
        {
            FolderPath = folderPath;
        }
    }

    /// <summary>
    /// This class tests whether Bloom can write in a folder. Besides ordinary causes (a read-only
    /// folder, a share without write permission), a Windows Defender setting can stop it.
    /// </summary>
    public static class DefenderFolderProtectionCheck
    {
        /// <summary>
        /// Returns 'true' unless we find we can't run Bloom, in which case it reports the condition and the caller should exit Bloom.
        /// In Oct of 2017, a Windows update to Defender on some machines set Protections on certain
        /// basic folders, like MyDocuments! This resulted in throwing an exception any time Bloom tried
        /// to write out CollectionSettings files!
        /// </summary>
        /// <param name="folderToCheck">An optional folder to test for this problem.</param>
        /// <returns></returns>
        public static bool CanWriteToDirectory(string folderToCheck = null)
        {
            string testPath = !string.IsNullOrEmpty(folderToCheck)
                ? Path.Combine(folderToCheck, TestFileName)
                : GetMruProjectTestPath;
            var problem = GetWriteProblem(Path.GetDirectoryName(testPath));
            if (problem == null)
                return true;
            ReportProblem(problem, Path.GetDirectoryName(testPath));
            return false;
        }

        /// <summary>
        /// Try to create a file in the folder. Returns null if that works (or we are not on
        /// Windows), otherwise the exception we got. Shows nothing to the user.
        /// </summary>
        public static Exception GetWriteProblem(string folder)
        {
            if (!Platform.IsWindows)
            {
                return null;
            }
            var testPath = Path.Combine(folder, TestFileName);
            try
            {
                RobustFile.WriteAllText(testPath, "test contents");
            }
            catch (Exception exc)
            {
                // Creating a previously non-existent file under Defender's Controlled Folder Access just gives a FileNotFoundException, "could not find file".
                return exc;
            }
            finally
            {
                Cleanup(testPath);
            }
            return null;
        }

        // We probably care about MyDocuments if we're about to create a "Bloom" directory (new installation)
        // or a new Collection under "Bloom". But we always care about the actual bloom collection folder, which could be anywhere
        // on disk. Indeed, one work-around is to move your collection to some place that is not under Defender lock down.
        // This property should return the folder that either does or will contain the collection we will be opening.
        private static string GetMruProjectTestPath
        {
            get
            {
                var path = Settings.Default.MruProjects.Latest;
                if (!string.IsNullOrEmpty(path))
                {
                    return Path.Combine(Path.GetDirectoryName(path), TestFileName);
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    TestFileName
                );
            }
        }

        private static string TestFileName
        {
            get { return Path.GetFileName(TempFile.CreateAndGetPathButDontMakeTheFile().Path); }
        }

        /// <summary>
        /// Whether the exception we got writing in a folder means the user lacks permission or the
        /// folder is read-only, as opposed to the FileNotFoundException that Defender's Controlled
        /// Folder Access gives.
        /// </summary>
        public static bool IsPermissionProblem(Exception writeProblem)
        {
            return writeProblem is UnauthorizedAccessException;
        }

        /// <summary>
        /// Tell the user Bloom cannot write in the folder, naming the likely cause.
        /// </summary>
        public static void ReportProblem(Exception writeProblem, string failingFolder)
        {
            var heading = LocalizationManager.GetString(
                "Errors.DefenderFolderProtectionHeading",
                "This program cannot write to the folder {0}."
            );
            string mainMsg;
            if (IsPermissionProblem(writeProblem))
            {
                mainMsg = LocalizationManager.GetString(
                    "Errors.FolderNotWritable",
                    "You may not have permission to change files there, or the folder may be read-only. Move the collection to a folder you can change, such as Documents, or ask whoever manages this computer or network for permission."
                );
            }
            else
            {
                mainMsg = LocalizationManager.GetString(
                    "Errors.DefenderFolderProtection",
                    "This might be caused by Windows Defender \"Controlled Folder Access\" or some other virus protection."
                );
            }
            var msg =
                string.Format(heading, failingFolder)
                + Environment.NewLine
                + Environment.NewLine
                + mainMsg;
            var caption = LocalizationManager.GetString("Common.ProblemTitle", "Bloom Problem");
            MessageBox.Show(msg, caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            if (!IsPermissionProblem(writeProblem))
            {
                SIL.Program.Process.SafeStart(
                    "https://docs.bloomlibrary.org/windows-controlled-folder-access"
                );
            }
        }

        private static void Cleanup(string testPath)
        {
            // try to clean up behind ourselves
            try
            {
                if (RobustFile.Exists(testPath))
                {
                    RobustFile.Delete(testPath);
                }
            }
            catch (Exception)
            {
                // but don't try too hard
            }
        }
    }
}
