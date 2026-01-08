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
    /// This class tests for a Windows Defender setting that keeps Bloom from functioning.
    /// If the bogus setting is active, Bloom will pop up a message to the user and then exit.
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
            if (!Platform.IsWindows)
            {
                return true;
            }
            string testPath;
            testPath = !string.IsNullOrEmpty(folderToCheck)
                ? Path.Combine(folderToCheck, TestFileName)
                : GetMruProjectTestPath;
            try
            {
                RobustFile.WriteAllText(testPath, "test contents");
            }
            catch (Exception exc)
            {
                // Creating a previously non-existent file under these conditions just gives a FileNotFoundException, "could not find file".
                ReportDefenderProblem(exc, Path.GetDirectoryName(testPath));
                return false;
            }
            finally
            {
                Cleanup(testPath);
            }
            return true;
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

        private static void ReportDefenderProblem(Exception exc, string failingFolder)
        {
            var heading = LocalizationManager.GetString(
                "Errors.DefenderFolderProtectionHeading",
                "This program cannot write to the folder {0}."
            );
            var mainMsg = LocalizationManager.GetString(
                "Errors.DefenderFolderProtection",
                "This might be caused by Windows Defender \"Controlled Folder Access\" or some other virus protection."
            );
            var msg =
                string.Format(heading, failingFolder)
                + Environment.NewLine
                + Environment.NewLine
                + mainMsg;
            var caption = LocalizationManager.GetString("Common.ProblemTitle", "Bloom Problem");
            MessageBox.Show(msg, caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            SIL.Program.Process.SafeStart(
                "https://docs.bloomlibrary.org/windows-controlled-folder-access"
            );
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
