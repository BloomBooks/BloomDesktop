using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using L10NSharp;
using SIL.Reporting;
#if !debug
using Bloom.WebLibraryIntegration;
#endif

namespace Bloom.ErrorReporter
{
    /// <summary>
    /// This class is based on LibPalaso's <see cref="ErrorReport"/> class,
    /// but adds methods to call NotifyUserOfProblem with more button customization
    /// </summary>
    public class ErrorReportUtils
    {
        #region Premade Alternate Actions
        internal static void TestAction(string message, Exception error)
        {
            MessageBox.Show("Secondary Action button pressed.");
        }
        #endregion

        #region Fake Test Errors
        /// <summary>
        /// Generates Error Reports for books with specific titles, but only in Debug or Sandbox mode
        /// Facilitates manual testing of error reporting using specific books.
        /// </summary>
        internal static void CheckForFakeTestErrorsIfNotRealUser(string title)
        {
            // A real user is defined as one using a Release build (i.e. not a Debug build) and not using Sandbox mode.
            // Skip these checks for real users, so there's no possibility of them getting spurious error reports
            // from this code (even if the titles required are unlikely real titles)
#if DEBUG
            bool checkAllowed = true;
#else
            bool checkAllowed = BookUpload.UseSandbox;
#endif

            if (checkAllowed)
            {
                // Run on the current thread (Should be the main thread)
                CheckForFakeTestErrors(title);

                //// Use this version to test running off the main thread
                //// (This is just a toy example, don't assume that just because this thread works, your code will never deadlock or anything like that
                //// Note: A slightly more realistic example is to generate these errors on a server worker thread. e.g. in Book.cs::GetPreviewHtmlFileForWholeBook()
                //new Thread(() =>
                //{
                //	CheckForFakeTestErrors(title);
                //}).Start();
            }
        }

        /// <summary>
        /// Generates Error Reports for books with specific titles
        /// Facilitates manual testing of error reporting using specific books.
        /// </summary>
        private static void CheckForFakeTestErrors(string title)
        {
            const string fakeProblemMessage = "Fake problem for development/testing purposes";

            if (title == "Error NotifyUser NoReport")
            {
                // Exercises a path through libPalaso directly (goes thru overloads 1, 2, 4)
                ErrorReport.NotifyUserOfProblem(fakeProblemMessage);
            }
            else if (title == "Error NotifyUser NoReport 2")
            {
                // Exercises a path through libPalaso directly (goes thru overloads 3, 4)
                ErrorReport.NotifyUserOfProblem((Exception)null, fakeProblemMessage);
            }
            else if (title == "Error NotifyUser NoReport 3")
            {
                // Exercises a path where you go through the ErrorReportUtils adapters
                BloomErrorReport.NotifyUserOfProblem(
                    fakeProblemMessage,
                    new NotifyUserOfProblemSettings()
                );
            }
            else if (title == "Error NotifyUser LongMessage")
            {
                var longMessageBuilder = new StringBuilder();
                while (longMessageBuilder.Length < 3000)
                    longMessageBuilder.Append(fakeProblemMessage + " ");

                ErrorReport.NotifyUserOfProblem(longMessageBuilder.ToString());
            }
            else if (title == "Error NotifyUser Report NoRetry")
            {
                // Exercises another path through libPalaso directly (goes thru overloads 3, 4)
                var fakeException = GenerateFakeException();
                ErrorReport.NotifyUserOfProblem(fakeException, fakeProblemMessage);
            }
            else if (title == "Error NotifyUser Report NoRetry 2")
            {
                // Exercises a path where you go through BloomErrorReport
                var fakeException = GenerateFakeException();
                BloomErrorReport.NotifyUserOfProblem(
                    fakeProblemMessage,
                    fakeException,
                    new NotifyUserOfProblemSettings()
                );
            }
            else if (title == "Error NotifyUser Report Retry")
            {
                // Exercises a path where you need to go through BloomErrorReport
                var secondaryButtonLabel = LocalizationManager.GetString(
                    "ErrorReportDialog.Retry",
                    "Retry"
                );
                var fakeException = GenerateFakeException();
                BloomErrorReport.NotifyUserOfProblem(
                    fakeProblemMessage,
                    fakeException,
                    new NotifyUserOfProblemSettings(
                        secondaryButtonLabel,
                        ErrorReportUtils.TestAction
                    ),
                    new ShowAlwaysPolicy()
                );
            }
            else if (title == "Error NotifyUser LegacyInterface")
            {
                // Exercises the legacy 5-argument implementation in libpalaso
                // (follow-up actions are manually invoked by the caller)
                var result = ErrorReport.NotifyUserOfProblem(
                    new ShowAlwaysPolicy(),
                    "CustomReport",
                    ErrorResult.Yes,
                    fakeProblemMessage
                );

                string message =
                    result == ErrorResult.Yes ? "Report button clicked. [Legacy]" : null;
                if (message != null)
                    MessageBox.Show(message);
            }
            else if (title == "Error ReportNonFatalException")
            {
                var fakeException = GenerateFakeException();
                ErrorReport.ReportNonFatalException(fakeException);
            }
            else if (title == "Error ReportNonFatalExceptionWithMessage")
            {
                var fakeException = GenerateFakeException();
                ErrorReport.ReportNonFatalExceptionWithMessage(fakeException, fakeProblemMessage);
            }
            else if (title == "Error ReportNonFatalExceptionWithMessage Scrollbar")
            {
                var longMessageBuilder = new StringBuilder();
                while (longMessageBuilder.Length < 500)
                    longMessageBuilder.AppendLine(fakeProblemMessage);
                var fakeException = GenerateFakeException();
                ErrorReport.ReportNonFatalExceptionWithMessage(
                    fakeException,
                    longMessageBuilder.ToString()
                );
            }
            else if (title == "Error ReportNonFatalMessageWithStackTrace")
            {
                ErrorReport.ReportNonFatalMessageWithStackTrace(fakeProblemMessage);
            }
            else if (title == "Error ReportFatalException")
            {
                var fakeException = GenerateFakeException();
                ErrorReport.ReportFatalException(fakeException);
            }
            else if (title == "Error ReportFatalMessageWithStackTrace")
            {
                ErrorReport.ReportFatalMessageWithStackTrace(fakeProblemMessage);
            }
            else if (title == "Error ReportFatalMessageWithStackTrace Scrollbar")
            {
                var longMessageBuilder = new StringBuilder();
                while (longMessageBuilder.Length < 500)
                    longMessageBuilder.AppendLine(fakeProblemMessage);
                ErrorReport.ReportFatalMessageWithStackTrace(longMessageBuilder.ToString());
            }
        }

        private static Exception GenerateFakeException()
        {
            Exception fakeException;
            // Throwing/catching the exception populates the stack trace
            try
            {
                throw new ApplicationException("Fake exception for development/testing purposes");
            }
            catch (ApplicationException e)
            {
                fakeException = e;
            }

            return fakeException;
        }
        #endregion
    }
}
