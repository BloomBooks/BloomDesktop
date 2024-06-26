using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ErrorReporter;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.WebLibraryIntegration;
using Newtonsoft.Json.Linq;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.Reporting;

namespace Bloom.web.controllers
{
    // ProblemReportApi should be written so that none of the API calls require sync (from BloomApiHandler).
    // The reason is because the ProblemReportApi could be spawned from an API call that requires sync.
    // If the parent API call required sync and the child ProblemReportApi call also required sync, the threads would deadlock
    // until the ProblemReportDialog is closed. (All of these requests to get the strings to populate into the dialog like bookname
    // or diagnosticInfo wouldn't return and the dialog will display their default values like "??" or "Loading..."
    //
    // This class should ideally save all of the values required by the API handlers at the time ShowProblemReport() is called.
    // (That's what the ReportInfo class is here for)
    // This will help with not requiring locking (you only need to worry about whether this class interferes with itself and can ignore the rest of Bloom)
    // It also locks the reported values to their value at the time ShowProblemDialog() was invoked, which is also a nice-to-have.
    internal class ProblemReportApi : IDisposable
    {
        private class ReportInfo
        {
            #region Readonly Properties
            // These fields are readonly to provide assurance that they haven't been changed after the ReportInfo was constructed.
            // (This makes proper threading/locking/synchronization easier to reason about)
            public readonly string HeadingHtml; // What shows at the top of the dialog to indicate the nature of the problem.

            public readonly string DetailedMessage; // usually from Bloom itself
            public readonly Exception Exception;
            public readonly Bloom.Book.Book Book;
            public readonly string BookName;
            public readonly string UserEmail;
            public readonly string UserFirstName;
            public readonly string UserSurname;
            public readonly string ErrorBookFolder;
            #endregion

            // We make a special allowance for ScreenshotTempFile since it uses a more complicated SafeInvoke to be assigned.
            public TempFile ScreenshotTempFile { get; set; }

            public ReportInfo(
                string heading,
                string detailMessage,
                Exception exception,
                Bloom.Book.Book book,
                string bookName,
                string email,
                string firstName,
                string surname,
                bool isHeadingPreEncoded = false
            )
            {
                if (isHeadingPreEncoded)
                {
                    HeadingHtml = heading;
                }
                else
                {
                    HeadingHtml = UrlPathString.CreateFromUnencodedString(heading).HtmlXmlEncoded;
                }

                this.DetailedMessage = detailMessage;
                this.Exception = exception;
                if (exception?.Data != null && exception.Data.Contains("ErrorBookFolder"))
                    this.ErrorBookFolder = exception.Data["ErrorBookFolder"] as string;
                this.Book = book;
                if (this.ErrorBookFolder != null && exception.Data.Contains("ErrorBookName"))
                    this.BookName = exception.Data["ErrorBookName"] as string;
                else
                    this.BookName = bookName;
                this.UserEmail = email;
                this.UserFirstName = firstName;
                this.UserSurname = surname;
            }
        }

        // Assumption: Assumes that only assigned to by ShowProblemReport(), and that only one problem report happens at a time.
        // (The ShowProblemReport() thread should check and set _showingProblemReport before modifying _reportInfo,
        //  thus basically establishing locked access to _reportInfo)
        private static ReportInfo _reportInfo = new ReportInfo("", "", null, null, "", "", "", "");

        private static BookSelection _bookSelection;

        private BloomZipFile _reportZipFile;
        private TempFile _reportZipFileTemp;
        public static string YouTrackProjectKey = "BL"; // this can be used to send to the "Sandbox" for testing: "SB";
        const string kFailureResult = "failed";

        /// <summary>
        /// We want this name "different" enough that it's not likely to be supplied by a user in a book,
        /// since we turn off caching for this file in web/RequestInfo.cs to avoid stale screenshots.
        /// </summary>
        internal const string ScreenshotName = "ProblemReportScreenshot.png";
        public const string kProblemBookJsonName = "BloomProblemBook.json";

        private string CollectionFolder =>
            Path.GetDirectoryName(
                _bookSelection?.CurrentSelection?.StoragePageFolder ?? _reportInfo?.ErrorBookFolder
            );

        public ProblemReportApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        void HandleShowDialog(ApiRequest request)
        {
            var requestData = DynamicJson.Parse(request.RequiredPostJson());
            ShowProblemDialog(
                null,
                null,
                requestData.message,
                ProblemLevel.kUser,
                requestData.shortMessage
            );
            request.PostSucceeded();
        }

        void HandleUnreadableBook(ApiRequest request)
        {
            ShowProblemDialog(
                null,
                null,
                null,
                ProblemLevel.kUser,
                "Bloom could not read this book"
            );
            request.PostSucceeded();
        }

        private static IEnumerable<string> _additionalPathsToInclude; // typically a problem image file

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // This one is an exception: it's not used BY the problem report dialog, but to launch one
            // from Javascript. However, it also must not require the lock, because if it holds it,
            // no calls that need it can run (such as one put forth by the Cancel button).
            apiHandler.RegisterEndpointHandler(
                "problemReport/showDialog",
                HandleShowDialog,
                true,
                false
            );
            // Similarly, but this launches from a button shown in Book.ErrorDom
            apiHandler.RegisterEndpointHandler(
                "problemReport/unreadableBook",
                HandleUnreadableBook,
                true,
                false
            );

            // For the paranoid - We could also have showProblemReport block these handlers while _reportInfo is being populated.
            // I think it's unnecessary since the problem report dialog's URL isn't even set until after the _reportInfo is populated,
            // and we assume that nothing sends problemReport API requests except the problemReportDialog that this class loads.

            // ProblemDialog.tsx uses this endpoint to get the string to show at the top of the main dialog
            apiHandler.RegisterEndpointHandlerUsedByOthers(
                "problemReport/reportHeadingHtml",
                (ApiRequest request) =>
                {
                    request.ReplyWithText(_reportInfo.HeadingHtml ?? "");
                },
                false
            );
            // ProblemDialog.tsx uses this endpoint to get the screenshot image.
            apiHandler.RegisterEndpointHandlerUsedByOthers(
                "problemReport/screenshot",
                (ApiRequest request) =>
                {
                    // Wait until the screenshot is finished.
                    // If not available within the time limit, just continue anyway (so we don't deadlock or anything) and hope for the best.
                    bool isLockTaken = _takingScreenshotLock.Wait(millisecondsTimeout: 5000);

                    if (_reportInfo?.ScreenshotTempFile == null)
                        request.Failed();
                    else
                        request.ReplyWithImage(_reportInfo.ScreenshotTempFile.Path);

                    if (isLockTaken)
                        _takingScreenshotLock.Release();
                },
                true
            );

            // ProblemDialog.tsx uses this endpoint to get the name of the book.
            apiHandler.RegisterEndpointHandlerUsedByOthers(
                "problemReport/bookName",
                (ApiRequest request) =>
                {
                    request.ReplyWithText(_reportInfo.BookName ?? "??");
                },
                true
            );

            // ProblemDialog.tsx uses this endpoint to get the registered user's email address.
            apiHandler.RegisterEndpointHandlerUsedByOthers(
                "problemReport/emailAddress",
                (ApiRequest request) =>
                {
                    request.ReplyWithText(_reportInfo.UserEmail);
                },
                true
            );

            // PrivacyScreen.tsx uses this endpoint to show the user what info will be included in the report.
            apiHandler.RegisterEndpointHandlerUsedByOthers(
                "problemReport/diagnosticInfo",
                (ApiRequest request) =>
                {
                    var userWantsToIncludeBook = request.RequiredParam("includeBook") == "true";
                    var userInput = request.RequiredParam("userInput");
                    var userEmail = request.RequiredParam("email");
                    request.ReplyWithText(
                        GetDiagnosticInfo(userWantsToIncludeBook, userInput, userEmail)
                    );
                },
                true
            );

            // ProblemDialog.tsx uses this endpoint in its AttemptSubmit method;
            // it expects a response that it will use to show the issue link to the user.
            apiHandler.RegisterEndpointHandlerUsedByOthers(
                "problemReport/submit",
                (ApiRequest request) =>
                {
                    var report = DynamicJson.Parse(request.RequiredPostJson());

                    string issueLink = SubmitToYouTrack(
                        report.kind,
                        report.userInput,
                        report.email,
                        report.includeBook,
                        report.includeScreenshot,
                        null
                    );

                    object linkToNewIssue = new { issueLink };
                    request.ReplyWithJson(linkToNewIssue);
                },
                true
            );
        }

        internal string SubmitToYouTrack(
            string reportKind,
            string userDesc,
            string userEmail,
            bool includeBook,
            bool includeScreenshot,
            IEnumerable<string> additionalPathsToInclude
        )
        {
            var subject =
                reportKind == "User"
                    ? "User Problem"
                    : reportKind == "Fatal"
                        ? "Crash Report"
                        : "Error Report";

            var issueSubmission = new YouTrackIssueSubmitter(YouTrackProjectKey);
            if (
                includeScreenshot
                && _reportInfo?.ScreenshotTempFile != null
                && RobustFile.Exists(_reportInfo.ScreenshotTempFile.Path)
            )
            {
                issueSubmission.AddAttachmentWhenWeHaveAnIssue(_reportInfo.ScreenshotTempFile.Path);
            }
            if (additionalPathsToInclude != null)
            {
                foreach (var path in additionalPathsToInclude)
                {
                    issueSubmission.AddAttachmentWhenWeHaveAnIssue(path);
                }
            }
            string diagnosticInfo = GetDiagnosticInfo(includeBook, userDesc, userEmail);
            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                // remember their email
                SIL.Windows.Forms.Registration.Registration.Default.Email = userEmail;
            }

            string issueId;
            try
            {
                issueId = issueSubmission.SubmitToYouTrack(subject, diagnosticInfo);
            }
            catch (Exception e)
            {
                Debug.Fail(
                    "Submitting problem report to YouTrack failed with '" + e.Message + "'."
                );
                issueId = kFailureResult;
            }

            string issueLink;
            if (issueId == kFailureResult)
            {
                var zipPath = MakeEmailableReportFile(
                    includeBook,
                    includeScreenshot,
                    userDesc,
                    diagnosticInfo
                );
                issueLink = kFailureResult + ":" + zipPath;
            }
            else
            {
                issueLink = "https://issues.bloomlibrary.org/youtrack/issue/" + issueId;
                if (includeBook || _additionalPathsToInclude?.Any() == true)
                {
                    try
                    {
                        string zipPath = CreateReportZipFile(issueId, userDesc, includeBook);
                        if (zipPath != null)
                        {
                            // This could be used provided the file is not too large (about 10M as of July 2020),
                            // but it seems simpler to do the same thing every time.
                            //issueSubmission.AttachFileToExistingIssue(issueId, zipPath);
                            var uploadUrl = ProblemBookUploader.UploadBook(
                                BloomS3Client.ProblemBookUploadsBucketName,
                                zipPath,
                                new NullProgress()
                            );
                            issueSubmission.AddCommentToIssue(
                                issueId,
                                "Problem book uploaded to " + uploadUrl
                            );
                        }
                    }
                    catch (Exception error)
                    {
                        Debug.WriteLine(
                            $"Attaching book to new YouTrack issue failed with '{error.Message}'."
                        );
                        var msg =
                            "***Error as ProblemReportApi attempted to upload the zipped book: "
                            + error.Message;
                        userDesc += Environment.NewLine + msg;
                        Logger.WriteEvent(userDesc);
                        // We don't want to change the summary, but currently the YouTrack API requires us to set both together.
                        issueSubmission.AddCommentToIssue(
                            issueId,
                            "Uploading the problem book failed with exception " + error.Message
                        );
                    }
                    finally
                    {
                        _reportZipFileTemp.Detach();
                    }
                }
            }
            return issueLink;
        }

        private string CreateReportZipFile(
            string basename,
            string userDesc,
            bool includeBook = true
        )
        {
            try
            {
                if (_reportZipFileTemp != null)
                    _reportZipFileTemp.Dispose(); // delete any previous report's temp file
                _reportZipFileTemp = TempFile.WithFilenameInTempFolder(
                    basename + (includeBook ? ".BloomProblemBook" : ".zip")
                );
                _reportZipFile = new BloomZipFile(_reportZipFileTemp.Path);

                if (includeBook)
                {
                    _reportZipFile.AddDirectory(
                        _reportInfo.ErrorBookFolder
                            ?? _bookSelection?.CurrentSelection?.StoragePageFolder
                    );
                    if (WantReaderInfo(true))
                    {
                        AddReaderInfo();
                    }
                    AddCollectionSettings();

                    // add a file that will tell Bloom to use this branding regardless of the date
                    dynamic bookJson = new
                    {
                        branding = _bookSelection
                            .CurrentSelection
                            .CollectionSettings
                            .BrandingProjectKey,
                        issueId = basename // the issueID (unless we're doing an email report, in which case it's "ProblemBook")
                    };
                    _reportZipFile.AddTopLevelFileWithText(
                        "BloomProblemBook.json",
                        Newtonsoft.Json.JsonConvert.SerializeObject(bookJson)
                    );
                }

                AddOtherTopLevelFiles();

                _reportZipFile.Save();
                return _reportZipFileTemp.Path;
            }
            catch (Exception error)
            {
                var msg =
                    "***Error as ProblemReportApi attempted to zip up the book: " + error.Message;
                userDesc += Environment.NewLine + msg;
                Logger.WriteEvent(userDesc);
                DisposeOfZipRemnants(true);
                return null;
            }
        }

        private void DisposeOfZipRemnants(bool includeBook)
        {
            // if an error happens in the zipper, the zip file stays locked, so we just leak it
            if (includeBook)
            {
                _reportZipFileTemp.Detach();
            }

            _reportZipFile = null;
        }

        private void AddReaderInfo()
        {
            if (CollectionFolder == null)
                return;
            var filePaths = GetReaderFilePaths(CollectionFolder);
            // If we have any files in the "Allowed Words" or "Sample Texts" folders, store those
            // directories preserving the directory structure rather than individual files at the
            // top level.  See https://issues.bloomlibrary.org/youtrack/issue/BL-10483.
            var subFolderPathsHandled = new List<string>();
            foreach (var filePath in filePaths)
            {
                var subFolderPath = Path.GetDirectoryName(filePath);
                var subFolderName = Path.GetFileName(subFolderPath);
                if (subFolderName == "Allowed Words" || subFolderName == "Sample Texts")
                {
                    if (!subFolderPathsHandled.Contains(subFolderPath))
                    {
                        subFolderPathsHandled.Add(subFolderPath);
                        _reportZipFile.AddDirectory(subFolderPath);
                    }
                }
                else
                {
                    _reportZipFile.AddTopLevelFile(filePath);
                }
            }
        }

        private void AddCollectionSettings()
        {
            if (CollectionFolder == null)
                return;
            var filePaths = GetCollectionFilePaths(CollectionFolder);
            foreach (var filePath in filePaths)
            {
                _reportZipFile.AddTopLevelFile(filePath);
            }
        }

        private void AddOtherTopLevelFiles()
        {
            if (_additionalPathsToInclude != null)
            {
                foreach (var path in _additionalPathsToInclude)
                    _reportZipFile.AddTopLevelFile(path);
            }
        }

        private string MakeEmailableReportFile(
            bool includeBook,
            bool includeScreenshot,
            string userDesc,
            string diagnosticInfo
        )
        {
            try
            {
                var filename = ("Report " + DateTime.UtcNow.ToString("u") + ".zip").Replace(
                    ':',
                    '.'
                );
                filename = filename.SanitizeFilename('#');
                var emailZipPath = Path.Combine(Path.GetTempPath(), filename);
                var emailZipper = new BloomZipFile(emailZipPath);
                using (var file = TempFile.WithFilenameInTempFolder("report.txt"))
                {
                    using (var stream = RobustFile.CreateText(file.Path))
                    {
                        stream.WriteLine(diagnosticInfo);
                        if (includeBook)
                        {
                            stream.WriteLine();
                            stream.WriteLine(
                                "REMEMBER: if the attached zip file appears empty, it may have non-ascii in the file names. Open with 7zip and you should see it."
                            );
                        }
                    }
                    emailZipper.AddTopLevelFile(file.Path);
                }
                if (includeBook || _additionalPathsToInclude?.Any() == true)
                {
                    string bookZipPath = CreateReportZipFile(
                        includeBook ? "ProblemBook" : "ProblemFiles",
                        userDesc,
                        includeBook
                    );
                    if (bookZipPath != null)
                        emailZipper.AddTopLevelFile(bookZipPath);
                }
                if (
                    includeScreenshot
                    && _reportInfo?.ScreenshotTempFile != null
                    && RobustFile.Exists(_reportInfo.ScreenshotTempFile.Path)
                )
                {
                    emailZipper.AddTopLevelFile(_reportInfo.ScreenshotTempFile.Path);
                }
                emailZipper.Save();
                return emailZipPath;
            }
            catch (Exception error)
            {
                var msg =
                    "***Error as ProblemReportApi attempted to zip up error information to email: "
                    + error.Message;
                userDesc += Environment.NewLine + msg;
                Logger.WriteEvent(userDesc);
                return null;
            }
        }

        /// <summary>
        /// Shows a problem dialog. Use to match <code>Action&lt;Exception, string&gt;</code> signature
        /// in ErrorReport.OnShowDetails().
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="detailedMessage"></param>
        public static void ShowProblemDialogForNonFatalException(
            Exception exception,
            string detailedMessage = ""
        )
        {
            ShowProblemDialog(null, exception, detailedMessage, "nonfatal");
        }

        static bool _showingProblemReport;

        // Extra locking object because 1) you can't lock primitives directly, and 2) you shouldn't use the object whose value you'll be reading as the lock object (reads to the object are not blocked)
        static object _showingProblemReportLock = new object();

        static bool IsOwnedForm(Form child, Form parent)
        {
            var test = child;
            while (test != null)
            {
                if (test == parent)
                    return true;
                test = test.Owner;
            }

            return false;
        }

        public static Form GetParentFormForErrorDialogs()
        {
            var active = Form.ActiveForm;
            var shell = Shell.GetShellOrOtherOpenForm();
            if (active == null)
            {
                // no active form. Might be because we are debugging, then typically, shell is the main shell and is good.
                // It's also possible that we're in some startup or shutting-down state where there really
                // is no active form, and probably, shell is null too. In such cases, there is no parent window to
                // try to be in front of, so we just return null.
                return shell;
            }

            if (shell is Shell)
            {
                // normal state, we have a Shell window available.
                if (IsOwnedForm(active, shell))
                {
                    // We can safely use the active form and still be in front of the shell, since the shell
                    // (directly or indirectly) is the owner of active (or simply IS the active form)
                    return active;
                }
                else
                {
                    // This is an unfortunate situation. We should launch dialogs with an appropriate owner,
                    // so the dialog stays in front of its owner (and the shell) and starts on the same screen.
                    // Perhaps the ActiveForm is something quite unexpected, like the Toast window.
                    // It might be something that doesn't have a taskbar entry, so if the shell somehow later
                    // gets in front of the current active form, there will be no easy way to recover, especially
                    // once we launch another modal dialog and it gets behind the Shell (BL-12412).
                    // So, if we're in a debug build, we will stop and encourage the programmer to give the
                    // window an owner.
                    // Otherwise, it seems safest to return the Shell. At least that can be selected from the
                    // task bar, and then if it is the owner of the error dialog, then the dialog should be
                    // in front of it.
                    Debug.Fail("Dialogs should have owners!");
                    return shell;
                }
            }
            else
            {
                // No Shell window active, maybe we're in startup or shutdown? May as well go with
                // the most likely form, or anything we can find if there isn't an active one.
                return active ?? shell;
            }
        }

        // ENHANCE: Reduce duplication in HtmlErrorReporter and ProblemReportApi code. Some of the ProblemReportApi code can move to HtmlErrorReporter code.

        // ENHANCE: I think levelOfProblem would benefit from being required and being an enum.

        /// <summary>
        /// Try to show the react problem dialog, but show fallback dialogs if appropriate.
        /// Currently called by ShowProblemDialog and OneDriveUtils.CheckForAndHandleOneDriveExceptions, for the logic they share
        /// </summary>
        public static void ShowProblemReactDialogWithFallbacks(
            Action showFallbackDialogAction,
            dynamic reactDialogProps,
            string reactDialogHeader,
            IEnumerable<string> additionalPathsToInclude,
            Control control,
            Exception exception,
            int height = 616
        )
        {
            SafeInvoke.InvokeIfPossible(
                "Show Problem Dialog",
                control,
                false,
                () =>
                {
                    // Uses a browser ReactDialog (if possible) to show the problem report
                    try
                    {
                        // We call CloseSplashScreen() above too, where it might help in some cases, but
                        // this one, while apparently redundant might be wise to keep since closing the splash screen
                        // needs to be done on the UI thread.
                        StartupScreenManager.CloseSplashScreen();

                        if (!BloomServer.ServerIsListening)
                        {
                            // We can't use the react dialog!
                            showFallbackDialogAction();
                            return;
                        }

                        // Precondition: we must be on the UI thread for Gecko to work.
                        using (
                            var dlg = new ReactDialog(
                                "problemReportBundle",
                                reactDialogProps,
                                reactDialogHeader
                            )
                        )
                        {
                            _additionalPathsToInclude = additionalPathsToInclude;
                            dlg.FormBorderStyle = FormBorderStyle.FixedToolWindow; // Allows the window to be dragged around
                            dlg.ControlBox = true; // Add controls like the X button back to the top bar
                            dlg.Text = ""; // Remove the title from the WinForms top bar

                            dlg.Width = 731;
                            dlg.Height = height;

                            // ShowDialog will cause this thread to be blocked (because it spins up a modal) until the dialog is closed.
                            BloomServer._theOneInstance.RegisterThreadBlocking();
                            try
                            {
                                // Keep dialog on top of program window if possible.  See https://issues.bloomlibrary.org/youtrack/issue/BL-10292.
                                dlg.ShowDialog(GetParentFormForErrorDialogs());
                            }
                            finally
                            {
                                BloomServer._theOneInstance.RegisterThreadUnblocked();
                                _additionalPathsToInclude = null;
                            }
                        }
                    }
                    catch (Exception problemReportException)
                    {
                        Logger.WriteError(
                            "*** ProblemReportApi threw an exception trying to display",
                            problemReportException
                        );
                        // At this point our problem reporter has failed for some reason, so we want the old WinForms handler
                        // to report both the original error for which we tried to open our dialog and this new one where
                        // the dialog itself failed.
                        // In order to do that, we create a new exception with the original exception (if there was one) as the
                        // inner exception. We include the message of the exception we just caught. Then we call the
                        // old WinForms fatal exception report directly.
                        // In any case, both of the errors will be logged by now.
                        var message =
                            "Bloom's error reporting failed: " + problemReportException.Message;

                        // Fallback to Winforms in case of trouble getting the browser up
                        var fallbackReporter = new WinFormsErrorReporter();
                        // ENHANCE?: If reporting a non-fatal problem failed, why is the program required to abort? It might be able to handle other tasks successfully
                        fallbackReporter.ReportFatalException(
                            new ApplicationException(message, exception ?? problemReportException)
                        );
                    }
                }
            );
        }

        /// <summary>
        /// Shows a problem dialog.
        /// </summary>
        /// <param name="controlForScreenshotting"></param>
        /// <param name="exception"></param>
        /// <param name="detailedMessage"></param>
        /// <param name="levelOfProblem">"user", "nonfatal", or "fatal"</param>
        /// <param name="additionalPathsToInclude"></param>
        public static void ShowProblemDialog(
            Control controlForScreenshotting,
            Exception exception,
            string detailedMessage = "",
            string levelOfProblem = "user",
            string shortUserLevelMessage = "",
            bool isShortMessagePreEncoded = false,
            string[] additionalPathsToInclude = null
        )
        {
            // Before we do anything that might be "risky", put the problem in the log.
            LogProblem(exception, detailedMessage, levelOfProblem);
            if (Program.RunningHarvesterMode)
            {
                Console.WriteLine(
                    levelOfProblem
                        + " Problem Detected: "
                        + shortUserLevelMessage
                        + "  "
                        + detailedMessage
                        + "  "
                        + exception
                );
                return;
            }
            StartupScreenManager.CloseSplashScreen(); // if it's still up, it'll be on top of the dialog

            lock (_showingProblemReportLock)
            {
                if (_showingProblemReport)
                {
                    // If a problem is reported when already reporting a problem, that could
                    // be an unbounded recursion that freezes the program and prevents the original
                    // problem from being reported.  So minimally report the recursive problem and stop
                    // the recursion in its tracks.
                    //
                    // Alternatively, can happen if multiple async BloomAPI calls go out and return errors.
                    // It's probably not helpful to have multiple problem report dialogs at the same time
                    // in this case either (even if there are theoretically a finite (not infinite) number of them)
                    const string msg =
                        "MULTIPLE CALLS to ShowProblemDialog. Suppressing the subsequent calls";
                    Console.Write(msg);
                    Logger.WriteEvent(msg);
                    return; // Abort
                }

                _showingProblemReport = true;
            }

            string filePath = FileException.GetFilePathIfPresent(exception);
            // FileException is a Bloom exception to capture the filepath. We want to report the inner, original exception.
            Exception originalException = FileException.UnwrapIfFileException(exception);

            // We have a better UI for this problem
            // Note that this will trigger whether it's a plain 'ol System.IO.PathTooLongException, or our own enhanced subclass, Bloom.Utiles.PathTooLongException
            if (exception is System.IO.PathTooLongException)
            {
                Utils.LongPathAware.ReportLongPath((System.IO.PathTooLongException)exception);
                return;
            }

            if (
                OneDriveUtils.CheckForAndHandleOneDriveExceptions(
                    exception,
                    filePath,
                    levelOfProblem
                )
            )
            {
                lock (_showingProblemReportLock)
                {
                    _showingProblemReport = false;
                }
                return;
            }

            GatherReportInfoExceptScreenshot(
                originalException,
                detailedMessage,
                shortUserLevelMessage,
                isShortMessagePreEncoded
            );

            if (controlForScreenshotting == null)
                controlForScreenshotting = Shell.GetShellOrOtherOpenForm();
            if (controlForScreenshotting == null) // still possible if we come from a "Details" button
                controlForScreenshotting = FatalExceptionHandler.ControlOnUIThread;
            ResetScreenshotFile();
            // Originally, we used SafeInvoke for both the screenshot and the new dialog display. SafeInvoke was great
            // for trying to get a screenshot, but having the actual dialog inside
            // of it was causing problems for handling any errors in showing the dialog.
            // Now we use SafeInvoke only inside of this extracted method.
            TryGetScreenshot(controlForScreenshotting);

            var showFallbackDialogAction = new Action(() =>
            {
                HtmlErrorReporter.ShowFallbackProblemDialog(
                    levelOfProblem,
                    originalException,
                    detailedMessage,
                    shortUserLevelMessage,
                    isShortMessagePreEncoded
                );
            });

            if (BloomServer._theOneInstance == null)
            {
                // We got an error really early, before we can use HTML dialogs. Report using the old dialog.
                // Hopefully we're still on the one main thread.
                showFallbackDialogAction();
                return;
            }

            var reactDialogProps = new { level = levelOfProblem };
            try
            { // We want to show the problem dialog with the screenshot if we can, but if we can't, we want to show the fallback dialog.
                ShowProblemReactDialogWithFallbacks(
                    showFallbackDialogAction,
                    reactDialogProps,
                    "Problem Report",
                    additionalPathsToInclude,
                    controlForScreenshotting,
                    originalException
                );
            }
            finally
            {
                lock (_showingProblemReportLock)
                {
                    _showingProblemReport = false;
                }
            }
        }

        /// <summary>
        /// Sends a problem report directly (without bringing up the UI dialog).
        /// This may be useful when the user has selected "Report" after receiving a Notify, especially if we can't bring up the HTML-based UI.
        /// </summary>
        /// <param name="levelOfProblem">One of the values of ProblemLevel. e.g. fatal, nonfatal, user, notify</param>
        /// <param name="exception">Optional - the exception to report. </param>
        /// <param name="shortUserLevelMessage">Optional. Short Description. If provided, must be the raw, literal, unencoded text... No using HTML to apply formatting</param>
        /// <param name="detailedMessage">Optional. Additional Description.</param>
        /// <param name="additionalPathsToInclude">Optional. If provided, the paths in this IEnumerable will be attached to the issue</param>
        public void SendReportWithoutUI(
            string levelOfProblem,
            Exception exception,
            string shortUserLevelMessage,
            string detailedMessage,
            IEnumerable<string> additionalPathsToInclude
        )
        {
            // Before we do anything that might be "risky", put the problem in the log.
            LogProblem(exception, detailedMessage, levelOfProblem);

            // Acquire the lock (even though we're not technically SHOWING a problem report dialog)
            // so that there's no interference with the member variables
            lock (_showingProblemReportLock)
            {
                if (_showingProblemReport)
                {
                    // Prevent multiple calls, in case of unbounded recursion
                    const string msg =
                        "MULTIPLE CALLS to ShowProblemDialog. Suppressing the subsequent calls";
                    Console.Write(msg);
                    Logger.WriteEvent(msg);
                    return; // Abort
                }

                _showingProblemReport = true;
            }

            string issueLink;
            try
            {
                GatherReportInfoExceptScreenshot(
                    exception,
                    detailedMessage,
                    shortUserLevelMessage,
                    false
                );

                // NOTE: Taking screenshots not supported in this mode (yet)

                // NOTE: kFailureResult may be returned (if submitting the issue failed).
                issueLink = SubmitToYouTrack(
                    levelOfProblem,
                    "",
                    SIL.Windows.Forms.Registration.Registration.Default.Email,
                    false,
                    false,
                    additionalPathsToInclude
                );
            }
            catch (Exception)
            {
                issueLink = kFailureResult;
            }
            finally
            {
                _showingProblemReport = false;
            }

            string message = issueLink.StartsWith(kFailureResult)
                ? "Failed to report issue. Please email Bloom team manually."
                : "Successfully reported issue: " + issueLink;

            // NOTE: This call should ideally be invoked after _showingProblemReport is back to false,
            // so that the resources will be available to this call.
            ErrorReport.NotifyUserOfProblem(message);
        }

        /// <summary>
        /// Instantiates _reportInfo with the relevant info from the reported problem.
        /// However, _screenshotTempFile will not be instantiated by this function,
        /// since that its more involved and involves getting the UI control / calling Invoke.
        /// </summary>
        internal static void GatherReportInfoExceptScreenshot(
            Exception exception,
            string detailedMessage,
            string shortUserLevelMessage,
            bool isShortMessagePreEncoded
        )
        {
            var heading = shortUserLevelMessage;
            var isHeadingPreEncoded = isShortMessagePreEncoded;

            if (string.IsNullOrEmpty(heading))
            {
                heading = detailedMessage;
                isHeadingPreEncoded = false;
            }
            if (string.IsNullOrEmpty(heading) && exception != null)
            {
                heading = exception.Message;
                isHeadingPreEncoded = false;
            }

            var book = _bookSelection?.CurrentSelection;
            var bestBookName = book?.NameBestForUserDisplay;

            var userEmail = SIL.Windows.Forms.Registration.Registration.Default.Email;
            var userFirstName = SIL.Windows.Forms.Registration.Registration.Default.FirstName;
            var userSurname = SIL.Windows.Forms.Registration.Registration.Default.Surname;

            _reportInfo = new ReportInfo(
                heading,
                detailedMessage,
                exception,
                book,
                bestBookName,
                userEmail,
                userFirstName,
                userSurname,
                isHeadingPreEncoded
            );
        }

        // This lock uses a SemaphoreSlim instead of a Monitor, because a Monitor only works for one thread.
        // Even though the acquisition of the lock and release of the lock can be on the same thread,
        // it doesn't seem safe to assume that this is always the case. So we use a locking mechanism that works across threads.
        private static SemaphoreSlim _takingScreenshotLock = new SemaphoreSlim(1, 1);

        private static void TryGetScreenshot(Control controlForScreenshotting)
        {
            if (controlForScreenshotting == null)
            {
                Logger.WriteEvent(
                    "Bloom was unable to create a screenshot as no active form could be found"
                );
                return;
            }
            _takingScreenshotLock.Wait(); // Acquire the lock

            try
            {
                SafeInvoke.Invoke(
                    "Screen Shot",
                    controlForScreenshotting,
                    false,
                    true,
                    () =>
                    {
                        try
                        {
                            // I got tired of landing here in the debugger so I'm avoiding the exception
                            if (controlForScreenshotting.Bounds.Width == 0)
                            {
                                ResetScreenshotFile();
                            }
                            else
                            {
#if !__MonoCS__
                                var scaledBounds =
                                    WindowsMonitorScaling.GetRectangleFromControlScaledToMonitorResolution(
                                        controlForScreenshotting
                                    );
#else
                                var scaledBounds = controlForScreenshotting.Bounds;
#endif
                                var screenshot = new Bitmap(
                                    scaledBounds.Width,
                                    scaledBounds.Height
                                );
                                using (var g = Graphics.FromImage(screenshot))
                                {
                                    if (controlForScreenshotting.Parent == null)
                                        g.CopyFromScreen(
                                            scaledBounds.Left,
                                            scaledBounds.Top,
                                            0,
                                            0,
                                            scaledBounds.Size
                                        ); // bounds already in screen coords
                                    else
                                        g.CopyFromScreen(
                                            controlForScreenshotting.PointToScreen(
                                                new Point(scaledBounds.Left, scaledBounds.Top)
                                            ),
                                            Point.Empty,
                                            scaledBounds.Size
                                        );
                                }

                                _reportInfo.ScreenshotTempFile = TempFile.WithFilename(
                                    ScreenshotName
                                );
                                SIL.IO.RobustImageIO.SaveImage(
                                    screenshot,
                                    _reportInfo.ScreenshotTempFile.Path,
                                    ImageFormat.Png
                                );
                            }
                        }
                        catch (Exception e)
                        {
                            ResetScreenshotFile();
                            Logger.WriteError("Bloom was unable to create a screenshot.", e);
                        }
                        finally
                        {
                            // Release lock (Unblock others)
                            if (_takingScreenshotLock.CurrentCount == 0)
                                _takingScreenshotLock.Release();
                        }
                    }
                );
            }
            catch (Exception error)
            {
                // Release lock (Unblock others)
                if (_takingScreenshotLock.CurrentCount == 0)
                    _takingScreenshotLock.Release();

                Debug.Fail("This error would be swallowed in release version: " + error.Message);
                SIL.Reporting.Logger.WriteEvent("**** " + error.Message);
            }
        }

        internal static void LogProblem(
            Exception exception,
            string detailedMessage,
            string levelOfProblem
        )
        {
            var sb = new StringBuilder();
            sb.AppendLine("*** ProblemReportApi is about to report:");
            if (exception != null)
                sb.AppendLineFormat("    exception = {0}", exception.ToString());
            if (!string.IsNullOrWhiteSpace(detailedMessage))
                sb.AppendLineFormat("   detailed message = {0}", detailedMessage);
            sb.AppendLineFormat("    level of problem = {0}", levelOfProblem);
            var msg = sb.ToString();
            Logger.WriteEvent(msg);
        }

        private static void ResetScreenshotFile()
        {
            _reportInfo.ScreenshotTempFile?.Dispose();
            _reportInfo.ScreenshotTempFile = null;
        }

        private string GetDiagnosticInfo(bool includeBook, string userDescription, string userEmail)
        {
            var bldr = new StringBuilder();

            bldr.AppendLine("### Problem Description");
            bldr.AppendLine(userDescription);
            bldr.AppendLine();

            GetInformationAboutUser(bldr, userEmail);
            GetExceptionInformation(bldr);
            GetStandardErrorReportingProperties(bldr, true);
            GetBookHistoryEvents(bldr);
            GetAdditionalBloomEnvironmentInfo(bldr);
            GetAdditionalFileInfo(bldr, includeBook);
            return bldr.ToString();
        }

        private static string GetDomainlessEmail(string rawEmail)
        {
            var atIndex = rawEmail.IndexOf("@");
            return atIndex < 0 ? rawEmail : rawEmail.Substring(0, atIndex);
        }

        private static void GetBookHistoryEvents(StringBuilder bldr)
        {
            var book = _reportInfo.Book;
            if (string.IsNullOrEmpty(book?.FolderPath) || book.BookInfo == null)
                return;
            try
            {
                var events = CollectionHistory.GetBookEvents(book.BookInfo);
                if (events.Count == 0)
                    return;

                bldr.AppendLine();
                bldr.AppendLine("#### History of Book Events");
                bldr.AppendLine("<details>");

                // Unfortunately, the collapsible <details> section doesn't allow normal markdown styling.
                // So we need <br>s here (and html styling below).
                void AddLine(StringBuilder sb, string text = "")
                {
                    sb.AppendLine(text + "<br>");
                }
                for (var i = 0; i < events.Count; i++)
                {
                    var historyEvent = events[i];
                    var time = historyEvent.When.ToString("G", CultureInfo.InvariantCulture);
                    var version = historyEvent.BloomVersion ?? "{unknown version}";
                    var email = GetDomainlessEmail(historyEvent.UserId);
                    AddLine(bldr, $"<b>{i + 1}  {historyEvent.Type}</b>");
                    AddLine(
                        bldr,
                        $"  Time: {time}UTC, with Bloom {version}, User: {historyEvent.UserName} ({email})"
                    );
                    AddLine(bldr, $"  Book with Title: {historyEvent.Title}");
                    if (!string.IsNullOrEmpty(historyEvent.Message))
                        AddLine(bldr, $"  {historyEvent.Message}");
                }

                bldr.AppendLine("</details>");
                bldr.AppendLine();
            }
            catch (Exception e)
            {
                Console.WriteLine("GetBookHistoryEvents says: " + e);
                bldr.AppendLine("GetBookHistoryEvents says: " + e.Message);
            }
        }

        private static void GetExceptionInformation(StringBuilder bldr)
        {
            if (
                _reportInfo.Exception == null
                && string.IsNullOrWhiteSpace(_reportInfo.DetailedMessage)
            )
                return;
            if (_reportInfo.Exception != null)
            {
                Exception dummy = null;
                bldr.AppendLine();
                bldr.AppendLine("#### Exception Details");
                if (!string.IsNullOrWhiteSpace(_reportInfo.DetailedMessage))
                {
                    bldr.AppendLine(_reportInfo.DetailedMessage);
                    bldr.AppendLine();
                }
                bldr.AppendLine("```stacktrace");
                bldr.Append(
                    ExceptionHelper.GetHiearchicalExceptionInfo(_reportInfo.Exception, ref dummy)
                );
                bldr.AppendLine("```");
            }
            else
            {
                // No exception, but we do have a detailed message from Bloom. This may not actually ever occur.
                bldr.AppendLine();
                bldr.AppendLine("#### Detailed message");
                bldr.AppendLine(_reportInfo.DetailedMessage);
            }
        }

        public static string GetObfuscatedEmail(string userEmail = "")
        {
            var email = string.IsNullOrWhiteSpace(userEmail) ? _reportInfo?.UserEmail : userEmail;
            string obfuscatedEmail;
            try
            {
                var m = new MailAddress(email);
                // note: we have code in YouTrack that de-obfuscates this particular format, so don't mess with it
                obfuscatedEmail = string.Format("{1} {0}", m.User, m.Host).Replace(".", "/");
            }
            catch (Exception)
            {
                obfuscatedEmail = email; // ah well, it's not valid anyhow, so no need to obfuscate (other code may not let the user get this far anyhow)
            }
            return obfuscatedEmail;
        }

        private static void GetInformationAboutUser(StringBuilder bldr, string userEmail)
        {
            var firstName = _reportInfo.UserFirstName;
            var lastName = _reportInfo.UserSurname;
            var errorReportFrom = GetStandardUserInfo(userEmail, firstName, lastName);
            bldr.AppendLine(errorReportFrom);
        }

        /// <summary>
        /// Generates a standard representation of the user information we want to have in an error
        /// report submitted to YouTrack. It's important that the fields be arranged (and obfusticated)
        /// exactly like this, because we have code running on the YouTrack system that will recognize
        /// data in this format and extract the unobfusticated email and the reporter name.
        /// (I'm not sure exactly what details must not be changed, so don't change anything without
        /// checking and testing.)
        /// </summary>
        public static string GetStandardUserInfo(
            string userEmail,
            string firstName,
            string lastName
        )
        {
            var nameString = GetNameString(firstName, lastName);
            var obfuscatedEmail = GetObfuscatedEmail(userEmail);
            var emailString = string.IsNullOrWhiteSpace(obfuscatedEmail)
                ? string.Empty
                : " (" + obfuscatedEmail + ")";
            return "Error Report from "
                + nameString
                + emailString
                + " on "
                + DateTime.UtcNow.ToUniversalTime()
                + " UTC";
        }

        private static object GetNameString(string firstName, string lastName)
        {
            return !string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName)
                ? lastName + ", " + firstName
                : string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName)
                    ? "unknown"
                    : (lastName + firstName).Trim();
        }

        private static void GetStandardErrorReportingProperties(StringBuilder bldr, bool appendLog)
        {
            const string Version = "Version";
            bldr.AppendLine("#### Error Reporting Properties");
            foreach (string label in ErrorReport.Properties.Keys)
            {
                if (label == Version)
                    bldr.Append("**"); // Version is the most important of these; bring it out a bit
                bldr.Append(label);
                bldr.Append(": ");
                bldr.Append(ErrorReport.Properties[label]);
                bldr.AppendLine(label == Version ? "**" : "");
            }

            bldr.AppendLine("WebView2: " + WebView2Browser.GetWebView2Version());

            if (appendLog || Logger.Singleton == null)
            {
                bldr.AppendLine();
                bldr.AppendLine("#### Log");
                bldr.AppendLine("```stacktrace");
                bldr.AppendLine();
                try
                {
                    bldr.Append(Logger.LogText);
                }
                catch (Exception err)
                {
                    // We have more than one report of dying while logging an exception.
                    bldr.AppendLine("****Could not read from log: " + err.Message);
                }
                bldr.AppendLine("```");
            }
        }

        private void GetAdditionalBloomEnvironmentInfo(StringBuilder bldr)
        {
            var book = _reportInfo.Book;
            var projectName = book?.CollectionSettings.CollectionName;
            bldr.AppendLine("#### Additional User Environment Information");
            if (_reportInfo.ErrorBookFolder != null)
            {
                bldr.AppendLine(
                    "Folder of book that could not be selected: " + _reportInfo.ErrorBookFolder
                );
                bldr.AppendLine(
                    "Title of book that could not be selected: " + _reportInfo.BookName
                );
                if (book == null)
                {
                    AppendTimeZone(bldr); // only setting that doesn't depend on book
                }
                else
                {
                    bldr.AppendLine(
                        "**** Book information below is from the previously selected book, not the one that could not be selected. ****"
                    );
                    bldr.AppendLine("**** Collection information should be valid. ****");
                }
            }
            if (book == null)
            {
                bldr.AppendLine("No Book was selected.");
                return;
            }
            try
            {
                bldr.AppendLine("Collection name: " + projectName);
                var sizeOrient = book.GetLayout().SizeAndOrientation;
                bldr.AppendLine("Page Size/Orientation: " + sizeOrient);
            }
            catch (Exception)
            {
                bldr.AppendLine("GetLayout() or SizeAndOrientation threw an exception.");
            }
            var settings = book.CollectionSettings;
            if (settings == null)
            {
                // paranoia, shouldn't happen
                bldr.AppendLine("Book's CollectionSettings was null.");
                return;
            }
            bldr.AppendLine("Collection name: " + settings.CollectionName);
            bldr.AppendLine("xMatter pack name: " + settings.XMatterPackName);
            // TODO: rethink how to display language information if we expand the languages available.
            AppendWritingSystem(book.BookData.Language1, "Language1", bldr);
            AppendWritingSystem(book.BookData.Language2, "Language2", bldr);
            AppendWritingSystem(book.BookData.Language3, "Language3", bldr);
            AppendWritingSystem(book.BookData.SignLanguage, "SignLanguage", bldr);
            AppendWritingSystem(book.BookData.MetadataLanguage1, "MetadataLanguage1", bldr);
            var enterpriseStatus = settings.GetEnterpriseStatus(false).ToString();
            var branding = settings.BrandingProjectKey;
            bldr.AppendLine();
            bldr.AppendLine("Enterprise status: " + enterpriseStatus);
            bldr.AppendLine(
                "Branding: " + (string.IsNullOrEmpty(branding) ? "None found" : branding)
            );
            AppendTimeZone(bldr);
        }

        private static void AppendTimeZone(StringBuilder bldr)
        {
            var tzName = TimeZone.CurrentTimeZone.IsDaylightSavingTime(DateTime.Now)
                ? TimeZone.CurrentTimeZone.DaylightName
                : TimeZone.CurrentTimeZone.StandardName;
            var tzOffset = TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now);
            var tzFormatString = (tzOffset < TimeSpan.Zero ? "\\-" : "") + "hh\\:mm";
            bldr.AppendLine(
                "User timezone: UTC" + tzOffset.ToString(tzFormatString) + "  (" + tzName + ")"
            );
        }

        void AppendWritingSystem(WritingSystem ws, string label, StringBuilder bldr)
        {
            if (string.IsNullOrEmpty(ws?.Tag))
            {
                bldr.AppendLine("No " + label + " defined");
            }
            else
            {
                bldr.AppendLine(
                    label
                        + " -> lang: '"
                        + ws.Tag
                        + "',  font: "
                        + ws.FontName
                        + (ws.IsRightToLeft ? " RTL" : string.Empty)
                );
            }
        }

        private void GetAdditionalFileInfo(StringBuilder bldr, bool includeBook)
        {
            var book = _reportInfo.Book;
            if (string.IsNullOrEmpty(book?.FolderPath) && _additionalPathsToInclude?.Any() != true)
                return;
            bldr.AppendLine();
            bldr.AppendLine("#### Additional Files Bundled With Book");
            if (!string.IsNullOrEmpty(book?.FolderPath))
            {
                var collectionFolder = Path.GetDirectoryName(book.FolderPath);
                if (collectionFolder != null)
                {
                    if (WantReaderInfo(includeBook))
                    {
                        var listOfReaderFiles = GetReaderFilePaths(collectionFolder);
                        ListFiles(listOfReaderFiles, bldr);
                    }
                    var listOfCollectionFiles = GetCollectionFilePaths(collectionFolder);
                    ListFiles(listOfCollectionFiles, bldr);
                }
            }
            if (_additionalPathsToInclude?.Any() == true)
                ListFiles(_additionalPathsToInclude, bldr);
        }

        private static IEnumerable<string> GetReaderFilePaths(string collectionFolder)
        {
            var result = Directory.GetFiles(collectionFolder, "ReaderTools*-*.json").ToList();
            ListFolderContents(Path.Combine(collectionFolder, "Allowed Words"), result);
            ListFolderContents(Path.Combine(collectionFolder, "Sample Texts"), result);
            return result;
        }

        private static IEnumerable<string> GetCollectionFilePaths(string collectionFolder)
        {
            var result = Directory.GetFiles(collectionFolder, "*CollectionStyles.css").ToList();
            result.AddRange(Directory.GetFiles(collectionFolder, "*.bloomCollection"));
            return result;
        }

        private static void ListFiles(IEnumerable<string> filePaths, StringBuilder bldr)
        {
            foreach (var filePath in filePaths)
                bldr.AppendLine(filePath);
        }

        private static bool WantReaderInfo(bool includeBook)
        {
            var book = _reportInfo.Book;

            if (book == null || !includeBook)
                return false;
            foreach (var tool in book.BookInfo.Tools)
            {
                if (tool.ToolId == "decodableReader" || tool.ToolId == "leveledReader")
                    return true;
            }
            return false;
        }

        private static void ListFolderContents(string folder, List<string> listOfFilePaths)
        {
            if (!Directory.Exists(folder))
                return;
            listOfFilePaths.AddRange(Directory.GetFiles(folder));
            // Probably overkill, but if there are subfolders, they will be zipped up with the book.
            foreach (var sub in Directory.GetDirectories(folder))
                ListFolderContents(sub, listOfFilePaths);
        }

        public static string UnpackProblemBook(string path)
        {
            // Unzip it into a temp folder.
            // We decided it is ok that these temp folders will accumulate on dev machines and never get cleaned up.
            var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var fileNameWIthoutExtension = Path.GetFileNameWithoutExtension(path);
            var collectionFolder = Path.Combine(tempFolder, fileNameWIthoutExtension);
            ZipUtils.ExpandZip(path, collectionFolder);
            // get the path to the file ending in ".bloomCollection" in tempFolder
            var collectionPath = Directory
                .GetFiles(collectionFolder, "*.bloomCollection")
                .FirstOrDefault();

            // rename the collection to match the issueId, this makes it easier for the dev to know what's what.
            var problemReportSettingsPath = Path.Combine(collectionFolder, kProblemBookJsonName);
            if (RobustFile.Exists(problemReportSettingsPath))
            {
                var editSettings = JObject.Parse(RobustFile.ReadAllText(problemReportSettingsPath));

                // BloomProblemReport.json's have an issueId that will be better for us to use as a collection name
                if (editSettings.TryGetValue("issueId", out JToken issueId))
                {
                    var newCollectionPath = Path.Combine(
                        collectionFolder,
                        issueId.Value<string>() + ".bloomCollection"
                    );
                    RobustFile.Move(collectionPath, newCollectionPath);
                    return newCollectionPath;
                }
            }
            return collectionPath;
        }

        public void Dispose()
        {
            _reportInfo.ScreenshotTempFile?.Dispose();
            _reportZipFile = null;
            _reportZipFileTemp?.Dispose();
        }
    }
}
