using System;
using System.IO;
using System.Net;
using System.Text;
using Bloom.Book;
using Bloom.Utils;
using L10NSharp;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.MiscUI
{
    /// <summary>
    /// Thrown when Bloom cannot write in a collection's folder, so the collection cannot be opened
    /// or created there. Callers report it with FolderWriteCheck.ReportProblem.
    /// (BL-16728)
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
    /// This class tests whether Bloom can write in a folder, and tells the user when it can't.
    /// Besides ordinary causes (a read-only folder, a share without write permission), antivirus
    /// folder protection such as Windows Security's Controlled Folder Access can stop it.
    /// </summary>
    public static class FolderWriteCheck
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
        /// Throws CollectionFolderNotWritableException if Bloom cannot write in collectionFolder (it
        /// may be read-only, lack write permission, or be protected by antivirus). Shows nothing
        /// to the user; callers report it with ReportProblem. (BL-16728)
        /// </summary>
        public static void ThrowIfCollectionFolderNotWritable(string collectionFolder)
        {
            var problem = GetWriteProblem(collectionFolder);
            if (problem != null)
                throw new CollectionFolderNotWritableException(collectionFolder, problem);
        }

        /// <summary>
        /// Make folder if it isn't there, then check Bloom can write in it. Throws
        /// CollectionFolderNotWritableException if Windows refuses either. For folders that
        /// collections go into, such as Documents\Bloom when a book is downloaded for editing.
        /// </summary>
        public static void EnsureFolderExistsAndIsWritable(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e) when (FileAccessDiagnostics.IsRefusal(e))
            {
                throw new CollectionFolderNotWritableException(folder, e);
            }
            ThrowIfCollectionFolderNotWritable(folder);
        }

        /// <summary>
        /// Try to create a file in the folder. Returns null if that works (or we are not on
        /// Windows), otherwise the exception we got. Shows nothing to the user.
        /// </summary>
        private static Exception GetWriteProblem(string folder)
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

        /// <summary>
        /// Tell the user that, although the collection in collectionFolder opened, Windows refused
        /// one of the things Bloom does to books (problem, at failedPath), which antivirus that
        /// protects some kinds of files often does (BL-17056). May be called on any thread.
        /// </summary>
        public static void ReportPartlyBlocked(
            Exception problem,
            string collectionFolder,
            string failedPath
        )
        {
            var partlyBlocked = LocalizationManager.GetString(
                "Errors.FolderPartlyBlocked",
                "Bloom opened the collection, but books may fail to save, be renamed or open until this is fixed."
            );
            Report(problem, collectionFolder, failedPath, partlyBlocked);
        }

        private static void ReportProblem(Exception writeProblem, string failingFolder)
        {
            Report(writeProblem, failingFolder, Path.Combine(failingFolder, "probe"), null);
        }

        /// <summary>
        /// Gather and log the evidence for why Bloom couldn't write at diagnosticsPath, then show the
        /// message: the folder, extraParagraph if any, then what the evidence points to.
        /// </summary>
        private static void Report(
            Exception writeProblem,
            string folder,
            string diagnosticsPath,
            string extraParagraph
        )
        {
            var details = FileAccessDiagnostics.Collect(
                diagnosticsPath,
                out var likelyCause,
                out var antivirusSuspected
            );
            Logger.WriteEvent(
                $"Cannot write to {folder}: {writeProblem.GetType().Name}: {writeProblem.Message}{Environment.NewLine}{details}"
            );
            var messageHtml = MakeMessageHtml(
                folder,
                extraParagraph,
                IsPermissionProblem(writeProblem, antivirusSuspected),
                likelyCause
            );
            ShowOnUiThread(messageHtml);
        }

        /// <summary>
        /// The message telling the user Bloom can't write in folder: the folder, extraParagraph if
        /// any, what the evidence points to (permission, or antivirus) with a link to the help page
        /// for that, and the likely cause if we found one.
        /// </summary>
        internal static string MakeMessageHtml(
            string folder,
            string extraParagraph,
            bool isPermissionProblem,
            string likelyCause
        )
        {
            var heading = LocalizationManager.GetString(
                "Errors.DefenderFolderProtectionHeading",
                "This program cannot write to the folder {0}."
            );
            string cause;
            string helpUrl;
            if (isPermissionProblem)
            {
                cause = LocalizationManager.GetString(
                    "Errors.FolderNotWritable",
                    "You may not have permission to change files there, or the folder may be read-only. Choose a folder you can change, or ask whoever manages this computer or network for permission."
                );
                helpUrl = BookStorage.kFileAccessPermissionHelpUrl;
            }
            else
            {
                cause = LocalizationManager.GetString(
                    "Errors.FolderAntivirusProtection",
                    "Antivirus software, such as Avast's Ransomware Shield or Windows Security's Controlled Folder Access, may be stopping Bloom. Tell that program that you trust Bloom."
                );
                helpUrl = FileAccessDiagnostics.kTroubleshootingUrl;
            }
            var html = new StringBuilder();
            html.Append(Paragraph(string.Format(heading, folder)));
            if (extraParagraph != null)
                html.Append(Paragraph(extraParagraph));
            html.Append(Paragraph(cause));
            html.Append($"<p>{BookStorage.GetEncodedSeeWebPageString(helpUrl)}</p>");
            // The likely cause names a product, setting or sync program, which a person helping
            // the user needs, even though it is only in English.
            if (likelyCause != null)
                html.Append(Paragraph(likelyCause));
            return html.ToString();
        }

        private static string Paragraph(string text) => $"<p>{WebUtility.HtmlEncode(text)}</p>";

        /// <summary>
        /// Show messageHtml in a Bloom dialog, so its link works; the caller may be on a background
        /// thread.
        /// </summary>
        private static void ShowOnUiThread(string messageHtml)
        {
            void Show()
            {
                ReactDialog.GetReadyToShowBeforeACollectionIsOpen();
                var closeButton = new MessageBoxButton()
                {
                    Text = LocalizationManager.GetString("Common.Close", "Close"),
                    Id = "close",
                    Default = true,
                };
                // Room for the folder, two or three paragraphs and a link without scrolling.
                // Owned by the Shell or by nothing: at startup the only other form may be the
                // hidden splash screen, and a dialog it owned would have no taskbar button.
                BloomMessageBox.Show(
                    Shell.GetShellOrNull(),
                    messageHtml,
                    new[] { closeButton },
                    logicalWidth: 600,
                    logicalHeight: 380
                );
            }
            var uiForm = Shell.GetShellOrOtherOpenForm();
            if (uiForm != null && uiForm.InvokeRequired)
                uiForm.Invoke((Action)Show);
            else
                Show();
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
