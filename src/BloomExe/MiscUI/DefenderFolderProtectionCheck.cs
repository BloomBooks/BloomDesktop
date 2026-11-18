using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Utils;
using L10NSharp;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;

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
        /// Returns 'true' unless Bloom can't write in Documents, where a new collection goes, in
        /// which case it reports the condition and the caller should exit Bloom.
        /// </summary>
        public static bool CanWriteToDocuments()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var problem = GetWriteProblem(documents);
            if (problem == null)
                return true;
            ReportProblem(problem, documents);
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

        private static string TestFileName
        {
            get { return Path.GetFileName(TempFile.CreateAndGetPathButDontMakeTheFile().Path); }
        }

        /// <summary>
        /// Whether the exception we got writing in a folder means the user lacks permission or the
        /// folder is read-only, as opposed to virus protection refusing us. antivirusSuspected is
        /// what FileAccessDiagnostics.Collect found (Controlled Folder Access covering the folder,
        /// Avast with the folder under Documents); when it is true we believe it, because antivirus
        /// folder protection also refuses with UnauthorizedAccessException (BL-17056).
        /// </summary>
        public static bool IsPermissionProblem(Exception writeProblem, bool antivirusSuspected)
        {
            return !antivirusSuspected && writeProblem is UnauthorizedAccessException;
        }

        /// <summary>
        /// Tell the user Bloom cannot write in the collection folder the exception names, naming the
        /// likely cause, and log the evidence for whoever helps them.
        /// </summary>
        public static void ReportProblem(CollectionFolderNotWritableException notWritable)
        {
            ReportProblem(notWritable.InnerException, notWritable.FolderPath);
        }

        private static void ReportProblem(Exception writeProblem, string failingFolder)
        {
            var details = FileAccessDiagnostics.Collect(
                Path.Combine(failingFolder, "probe"),
                out var likelyCause,
                out var antivirusSuspected
            );
            Logger.WriteEvent(
                $"Cannot write to {failingFolder}: {writeProblem.GetType().Name}: {writeProblem.Message}{Environment.NewLine}{details}"
            );
            var heading = LocalizationManager.GetString(
                "Errors.DefenderFolderProtectionHeading",
                "This program cannot write to the folder {0}."
            );
            var isPermissionProblem = IsPermissionProblem(writeProblem, antivirusSuspected);
            string mainMsg;
            if (isPermissionProblem)
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
            // The likely cause names a product, setting or sync program, which a person helping
            // the user needs, even though it is only in English.
            if (likelyCause != null)
                mainMsg += Environment.NewLine + Environment.NewLine + likelyCause;
            var msg =
                string.Format(heading, failingFolder)
                + Environment.NewLine
                + Environment.NewLine
                + mainMsg;
            var caption = LocalizationManager.GetString("Common.ProblemTitle", "Bloom Problem");
            MessageBox.Show(msg, caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            SIL.Program.Process.SafeStart(
                isPermissionProblem
                    ? BookStorage.kFileAccessPermissionHelpUrl
                    : FileAccessDiagnostics.kTroubleshootingUrl
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
